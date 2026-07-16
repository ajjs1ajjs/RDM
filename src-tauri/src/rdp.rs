use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Mutex;
use std::time::Duration;
use tauri::{Emitter, Manager};

use windows::Win32::Foundation::{BOOL, HWND, LPARAM};
use windows::Win32::UI::WindowsAndMessaging::{
    EnumChildWindows, EnumWindows, GetClassNameW, GetWindowLongPtrW, GetWindowLongW,
    GetWindowThreadProcessId, IsWindow, MoveWindow, SetWindowLongPtrW, SetWindowLongW,
    SetWindowPos, ShowWindow, GWLP_HWNDPARENT, GWL_STYLE, HWND_NOTOPMOST, SWP_NOACTIVATE,
    SWP_NOMOVE, SWP_NOSIZE, SWP_SHOWWINDOW, SW_HIDE, SW_SHOW, WS_BORDER, WS_CAPTION,
    WS_CLIPCHILDREN, WS_CLIPSIBLINGS, WS_SYSMENU, WS_THICKFRAME,
};

struct OwnerData(isize);

#[derive(Clone, Copy)]
struct ChildResizeData {
    width: i32,
    height: i32,
}

unsafe extern "system" fn resize_child_fill(hwnd: HWND, lparam: LPARAM) -> BOOL {
    let data = &*(lparam.0 as *const ChildResizeData);
    let _ = MoveWindow(hwnd, 0, 0, data.width, data.height, true);
    BOOL(1)
}

unsafe extern "system" fn enum_owned_hwnd_top(hwnd: HWND, lparam: LPARAM) -> BOOL {
    let data = &mut *(lparam.0 as *mut OwnerData);
    let owner_hwnd = data.0 as isize;
    let owner = GetWindowLongPtrW(hwnd, GWLP_HWNDPARENT);
    if owner == owner_hwnd {
        let _ = SetWindowPos(
            hwnd,
            HWND_NOTOPMOST,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
        );
    }
    BOOL(1)
}

pub struct RdpSession {
    pub target_host: String,
    pub server_id: Option<String>,
    pub mstsc_hwnd: isize,
    pub pid: u32,
    pub visible: bool,
}

pub struct RdpState {
    pub sessions: Mutex<HashMap<String, RdpSession>>,
}

impl Drop for RdpState {
    fn drop(&mut self) {
        // Kill orphaned mstsc/RdpHost processes when app exits
        let _ = std::process::Command::new("taskkill")
            .args(&["/f", "/im", "mstsc.exe"])
            .output();
        let _ = std::process::Command::new("taskkill")
            .args(&["/f", "/im", "RdpHost.exe"])
            .output();
    }
}

impl RdpState {
    pub fn new() -> Self {
        Self {
            sessions: Mutex::new(HashMap::new()),
        }
    }
}

fn log_debug(app_data_dir: &std::path::Path, message: &str) {
    let log_file = app_data_dir.join("rdp_debug.log");
    let line = format!("{}\n", message);
    let _ = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(log_file)
        .and_then(|mut f| std::io::Write::write_all(&mut f, line.as_bytes()));
}

#[derive(Clone, Copy)]
struct EnumData {
    pid: u32,
    hwnd: Option<HWND>,
}

unsafe extern "system" fn enum_windows_callback(hwnd: HWND, lparam: LPARAM) -> BOOL {
    let data = &mut *(lparam.0 as *mut EnumData);
    let mut pid: u32 = 0;
    let tid = GetWindowThreadProcessId(hwnd, Some(&mut pid));
    if tid != 0 && pid == data.pid {
        let mut class_name = [0u16; 256];
        let len = GetClassNameW(hwnd, &mut class_name);
        if len > 0 {
            let class_str = String::from_utf16_lossy(&class_name[..len as usize]);
            if class_str == "TscShellContainerClass"
                || class_str.contains("FreeRDP")
                || class_str.contains("wfreerdp")
            {
                data.hwnd = Some(hwnd);
                return BOOL(0);
            }
        }
    }
    BOOL(1)
}

fn find_mstsc_hwnd(pid: u32) -> Option<HWND> {
    let mut data = EnumData { pid, hwnd: None };
    unsafe {
        let _ = EnumWindows(
            Some(enum_windows_callback),
            LPARAM(&mut data as *mut EnumData as isize),
        );
    }
    data.hwnd
}

#[repr(C)]
struct WinCredential {
    flags: u32,
    typ: u32,
    target_name: *const u16,
    comment: *const u16,
    last_written: i64,
    credential_blob_size: u32,
    credential_blob: *const u8,
    persist: u32,
    attribute_count: u32,
    attributes: *const std::ffi::c_void,
    target_alias: *const u16,
    user_name: *const u16,
}

#[link(name = "advapi32")]
extern "system" {
    fn CredWriteW(credential: *const WinCredential, flags: u32) -> BOOL;
    fn CredDeleteW(target_name: *const u16, typ: u32, flags: u32) -> BOOL;
}

const CRED_TYPE_GENERIC: u32 = 1;
const CRED_PERSIST_LOCAL_MACHINE: u32 = 2;

fn store_rdp_credential_secure(host: &str, username: &str, password: &str) {
    let target_name: Vec<u16> = format!("TERMSRV/{}\0", host).encode_utf16().collect();
    let user_name: Vec<u16> = format!("{}\0", username).encode_utf16().collect();
    // mstsc expects password as UTF-16LE null-terminated in the credential blob
    let password_utf16: Vec<u16> = format!("{}\0", password).encode_utf16().collect();
    let password_bytes: Vec<u8> = password_utf16
        .iter()
        .flat_map(|c| c.to_le_bytes())
        .collect();
    let blob_size = password_bytes.len() as u32;

    let mut cred = WinCredential {
        flags: 0,
        typ: CRED_TYPE_GENERIC,
        target_name: target_name.as_ptr(),
        comment: std::ptr::null(),
        last_written: 0,
        credential_blob_size: blob_size,
        credential_blob: password_bytes.as_ptr(),
        persist: CRED_PERSIST_LOCAL_MACHINE,
        attribute_count: 0,
        attributes: std::ptr::null(),
        target_alias: std::ptr::null(),
        user_name: user_name.as_ptr(),
    };

    unsafe {
        let _ = CredWriteW(&mut cred, 0);
    }
}

fn delete_rdp_credential_secure(host: &str) {
    let target_name: Vec<u16> = format!("TERMSRV/{}", host).encode_utf16().collect();
    unsafe {
        let _ = CredDeleteW(target_name.as_ptr(), CRED_TYPE_GENERIC, 0);
    }
}

/// Launches an external mstsc.exe RDP session
pub fn launch_rdp_session(
    host: &str,
    port: u32,
    fullscreen: bool,
    username: Option<&str>,
    _password: Option<&str>,
    app_data_dir: PathBuf,
    server_id: Option<String>,
    rdp_clipboard: bool,
    rdp_drives: bool,
    rdp_printers: bool,
    rdp_smart_sizing: bool,
    rdp_audio: u32,
    rdp_smartcards: bool,
    rdp_webauthn: bool,
    rdp_multimon: bool,
) -> Result<(), String> {
    let connection_string = if port == 3389 || port == 0 {
        host.to_string()
    } else {
        format!("{}:{}", host, port)
    };

    let rdp_sessions_dir = app_data_dir.join("rdp_sessions");
    let _ = std::fs::create_dir_all(&rdp_sessions_dir);

    let file_name = format!(
        "session_ext-{}-{}.rdp",
        server_id.unwrap_or_default(),
        uuid::Uuid::new_v4()
    );
    let rdp_file_path = rdp_sessions_dir.join(file_name);

    let user_line = if let Some(user) = username {
        format!("username:s:{}\r\n", user)
    } else {
        String::new()
    };

    let screen_mode = if fullscreen { 2 } else { 1 };
    let multimon_line = if rdp_multimon {
        "use multimon:i:1\r\n"
    } else {
        ""
    };

    let smart_sizing_val = if rdp_smart_sizing { 1 } else { 0 };
    let redirect_clipboard = if rdp_clipboard { 1 } else { 0 };
    let redirect_drives = if rdp_drives { 1 } else { 0 };
    let redirect_printers = if rdp_printers { 1 } else { 0 };
    let redirect_smartcards = if rdp_smartcards { 1 } else { 0 };
    let redirect_webauthn = if rdp_webauthn { 1 } else { 0 };
    let audio_val = match rdp_audio {
        0 => 0,
        1 => 1,
        2 => 2,
        _ => 0,
    };

    let has_creds = _password.is_some();
    let auth_level = if has_creds { 2 } else { 0 };
    let credssp = if has_creds { 1 } else { 0 };

    let rdp_content = format!(
        "full address:s:{0}\r\n\
         {1}\
         screen mode id:i:{2}\r\n\
         {3}\
         smart sizing:i:{4}\r\n\
         dynamic resolution:i:1\r\n\
         redirectclipboard:i:{5}\r\n\
         redirectdrives:i:{6}\r\n\
         redirectprinters:i:{7}\r\n\
         audiomode:i:{8}\r\n\
         redirectsmartcards:i:{9}\r\n\
         enablewebauthn:i:{10}\r\n\
         authentication level:i:{11}\r\n\
         enablecredsspsupport:i:{12}\r\n\
         displayconnectionbar:i:1\r\n",
        connection_string,
        user_line,
        screen_mode,
        multimon_line,
        smart_sizing_val,
        redirect_clipboard,
        redirect_drives,
        redirect_printers,
        audio_val,
        redirect_smartcards,
        redirect_webauthn,
        auth_level,
        credssp,
    );

    let rdp_content_utf16: Vec<u16> = std::iter::once(0xFEFF)
        .chain(rdp_content.encode_utf16())
        .collect();

    let rdp_content_bytes: &[u8] = unsafe {
        std::slice::from_raw_parts(
            rdp_content_utf16.as_ptr() as *const u8,
            rdp_content_utf16.len() * 2,
        )
    };

    std::fs::write(&rdp_file_path, rdp_content_bytes)
        .map_err(|e| format!("Failed to write external RDP file: {}", e))?;

    std::process::Command::new("mstsc")
        .arg(rdp_file_path.to_string_lossy().to_string())
        .spawn()
        .map(|_| ())
        .map_err(|e| format!("Failed to spawn mstsc process: {}", e))
}

/// Launches an embedded (reparented) mstsc.exe RDP session
pub fn launch_rdp_embedded(
    session_id: String,
    host: &str,
    port: u32,
    username: Option<&str>,
    password: Option<&str>,
    parent_hwnd: windows::Win32::Foundation::HWND,
    x: i32,
    y: i32,
    width: i32,
    height: i32,
    device_pixel_ratio: f64,
    app_data_dir: PathBuf,
    app: tauri::AppHandle,
    server_id: Option<String>,
    rdp_clipboard: bool,
    rdp_drives: bool,
    rdp_printers: bool,
    rdp_smart_sizing: bool,
    rdp_audio: u32,
    rdp_smartcards: bool,
    rdp_webauthn: bool,
) -> Result<(), String> {
    let port = if port == 0 { 3389 } else { port };
    let connection_string = if port == 3389 {
        host.to_string()
    } else {
        format!("{}:{}", host, port)
    };

    let x_phys = (x as f64 * device_pixel_ratio).round() as i32;
    let y_phys = (y as f64 * device_pixel_ratio).round() as i32;
    let width_phys = (width as f64 * device_pixel_ratio).round() as i32;
    let height_phys = (height as f64 * device_pixel_ratio).round() as i32;

    // 0. Bypass all RDP certificate warnings via registry
    let _ = std::process::Command::new("reg")
        .args(&[
            "add",
            "HKCU\\Software\\Microsoft\\Terminal Server Client",
            "/v",
            "AuthenticationLevelOverride",
            "/t",
            "REG_DWORD",
            "/d",
            "0",
            "/f",
        ])
        .spawn()
        .and_then(|mut c| c.wait());

    // Store RDP credentials via Windows Credential Manager API (secure, no cmdline exposure)
    if let (Some(user), Some(pass)) = (username, password) {
        log_debug(
            &app_data_dir,
            &format!("Storing credential for TERMSRV/{}", host),
        );
        store_rdp_credential_secure(host, user, pass);
    }

    // 1. Compute screen coordinates BEFORE creating RDP file (so winposstr is accurate)
    let (screen_x, screen_y) = unsafe {
        use windows::Win32::Foundation::POINT;
        use windows::Win32::Graphics::Gdi::ClientToScreen;
        let mut pt = POINT {
            x: x_phys,
            y: y_phys,
        };
        let _ = ClientToScreen(parent_hwnd, &mut pt);
        (pt.x, pt.y)
    };

    // 2. Create RDP file with correct winposstr at screen coords
    let rdp_sessions_dir = app_data_dir.join("rdp_sessions");
    let _ = std::fs::create_dir_all(&rdp_sessions_dir);

    let file_name = format!("session_emb-{}.rdp", uuid::Uuid::new_v4());
    let rdp_file_path = rdp_sessions_dir.join(file_name);

    let user_line = username
        .map(|u| format!("username:s:{}\r\n", u))
        .unwrap_or_default();
    let smart_sizing_val = if rdp_smart_sizing { 1 } else { 0 };
    let redirect_clipboard = if rdp_clipboard { 1 } else { 0 };
    let redirect_drives = if rdp_drives { 1 } else { 0 };
    let redirect_printers = if rdp_printers { 1 } else { 0 };
    let redirect_smartcards = if rdp_smartcards { 1 } else { 0 };
    let redirect_webauthn = if rdp_webauthn { 1 } else { 0 };
    let audio_val = match rdp_audio {
        0 => 0,
        1 => 1,
        2 => 2,
        _ => 0,
    };
    let win_right = screen_x + width_phys;
    let win_bottom = screen_y + height_phys;

    let has_creds = password.is_some();
    let auth_level = if has_creds { 2 } else { 0 };
    let server_auth = if has_creds { 1 } else { 0 };
    let credssp = if has_creds { 1 } else { 0 };

    let rdp_content = format!(
        "full address:s:{0}\r\n\
         {1}\
         screen mode id:i:1\r\n\
         smart sizing:i:{2}\r\n\
         dynamic resolution:i:1\r\n\
         redirectclipboard:i:{3}\r\n\
         redirectdrives:i:{4}\r\n\
         redirectprinters:i:{5}\r\n\
         audiomode:i:{6}\r\n\
         redirectsmartcards:i:{7}\r\n\
         enablewebauthn:i:{8}\r\n\
         authentication level:i:{9}\r\n\
         serverauth:i:{10}\r\n\
         enablecredsspsupport:i:{11}\r\n\
         displayconnectionbar:i:0\r\n\
         prompt for credentials:i:0\r\n\
         promptcredentialonce:i:0\r\n\
         disableconnectionsharing:i:1\r\n\
         autoreconnection enabled:i:1\r\n\
         connection type:i:2\r\n\
         desktopwidth:i:{12}\r\n\
         desktopheight:i:{13}\r\n\
         session bpp:i:32\r\n\
         winposstr:s:0,1,{14},{15},{16},{17}\r\n",
        connection_string,
        user_line,
        smart_sizing_val,
        redirect_clipboard,
        redirect_drives,
        redirect_printers,
        audio_val,
        redirect_smartcards,
        redirect_webauthn,
        auth_level,
        server_auth,
        credssp,
        width_phys,
        height_phys,
        screen_x,
        screen_y,
        win_right,
        win_bottom,
    );

    let rdp_content_utf16: Vec<u16> = std::iter::once(0xFEFF)
        .chain(rdp_content.encode_utf16())
        .collect();
    let rdp_content_bytes: &[u8] = unsafe {
        std::slice::from_raw_parts(
            rdp_content_utf16.as_ptr() as *const u8,
            rdp_content_utf16.len() * 2,
        )
    };
    std::fs::write(&rdp_file_path, rdp_content_bytes)
        .map_err(|e| format!("Failed to write RDP file: {}", e))?;

    let fp = rdp_file_path.to_string_lossy().to_string();
    log_debug(
        &app_data_dir,
        &format!(
            "RDP file created with winposstr {} {} {} {}",
            screen_x, screen_y, win_right, win_bottom
        ),
    );

    // 3. Launch mstsc.exe (creates its window at correct screen coords via winposstr)
    let mut child = std::process::Command::new("mstsc")
        .arg(&fp)
        .spawn()
        .map_err(|e| format!("Failed to spawn mstsc: {}", e))?;

    let pid = child.id();
    log_debug(&app_data_dir, &format!("mstsc spawned PID {}", pid));

    // 4. Poll for the mstsc window
    let mut mstsc_hwnd = None;
    for _ in 0..200 {
        std::thread::sleep(Duration::from_millis(50));
        match child.try_wait() {
            Ok(Some(status)) => {
                log_debug(
                    &app_data_dir,
                    &format!("mstsc exited prematurely with code {:?}", status.code()),
                );
                return Err(format!(
                    "mstsc exited prematurely (code {:?})",
                    status.code()
                ));
            }
            _ => {}
        }
        if let Some(hwnd) = find_mstsc_hwnd(pid) {
            mstsc_hwnd = Some(hwnd);
            log_debug(
                &app_data_dir,
                &format!("Found mstsc window HWND={:?}", hwnd),
            );
            break;
        }
    }

    let hwnd = match mstsc_hwnd {
        Some(h) => h,
        None => {
            let _ = child.kill();
            return Err("Failed to find mstsc window within 10s timeout".to_string());
        }
    };

    // 5. Style mstsc: remove border chrome, keep as independent top-level window, reinforce position
    unsafe {
        let mut style = GetWindowLongW(hwnd, GWL_STYLE) as u32;
        style &= !(WS_CAPTION.0 | WS_THICKFRAME.0 | WS_BORDER.0 | WS_SYSMENU.0);
        style |= WS_CLIPSIBLINGS.0 | WS_CLIPCHILDREN.0;
        SetWindowLongW(hwnd, GWL_STYLE, style as i32);

        // Set owner so mstsc follows app minimize/restore (visible flag prevents SSH interference)
        let _ = SetWindowLongPtrW(hwnd, GWLP_HWNDPARENT, parent_hwnd.0 as isize);

        // Position at screen coords
        let _ = SetWindowPos(
            hwnd,
            HWND_NOTOPMOST,
            screen_x,
            screen_y,
            width_phys,
            height_phys,
            SWP_NOACTIVATE | SWP_SHOWWINDOW,
        );

        log_debug(
            &app_data_dir,
            &format!(
                "Styled/positioned mstsc @ ({},{}) {}x{}",
                screen_x, screen_y, width_phys, height_phys
            ),
        );

        // Resize all child windows of mstsc to fill the parent
        let resize_data = ChildResizeData {
            width: width_phys,
            height: height_phys,
        };
        let _ = EnumChildWindows(
            hwnd,
            Some(resize_child_fill),
            LPARAM(&resize_data as *const _ as isize),
        );
    }

    // 5. Store session
    let state = app.state::<RdpState>();
    {
        let mut sessions = state.sessions.lock().map_err(|e| e.to_string())?;
        sessions.insert(
            session_id.clone(),
            RdpSession {
                target_host: host.to_string(),
                server_id,
                mstsc_hwnd: hwnd.0 as isize,
                pid,
                visible: true,
            },
        );
    }

    // 6. Monitor thread (keeps mstsc on top only while visible flag is true)
    let app_clone = app.clone();
    let session_id_clone = session_id.clone();
    let hwnd_raw = hwnd.0 as usize;
    let host_clone = host.to_string();
    let sid_clone = session_id.clone();
    std::thread::spawn(move || {
        let thread_hwnd = HWND(hwnd_raw as *mut _);
        loop {
            std::thread::sleep(Duration::from_millis(200));
            unsafe {
                if !IsWindow(thread_hwnd).as_bool() {
                    break;
                }
            }
            // Check the visible flag (set by resize_rdp_embedded)
            let should_be_visible = app_clone
                .state::<RdpState>()
                .sessions
                .lock()
                .ok()
                .and_then(|s| s.get(&sid_clone).map(|sess| sess.visible))
                .unwrap_or(false);
            unsafe {
                if should_be_visible {
                    let _ = ShowWindow(thread_hwnd, SW_SHOW);
                    let _ = SetWindowPos(
                        thread_hwnd,
                        HWND_NOTOPMOST,
                        0,
                        0,
                        0,
                        0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
                    );
                    // Bring all dialogs owned by mstsc to front
                    let mut od = OwnerData(thread_hwnd.0 as isize);
                    let _ = EnumWindows(
                        Some(enum_owned_hwnd_top),
                        LPARAM(&mut od as *mut _ as isize),
                    );
                } else {
                    let _ = SetWindowPos(
                        thread_hwnd,
                        HWND_NOTOPMOST,
                        -32000,
                        -32000,
                        0,
                        0,
                        SWP_NOSIZE | SWP_NOACTIVATE,
                    );
                }
            }
        }
        // Cleanup stored credential via Windows Credential Manager
        delete_rdp_credential_secure(&host_clone);
        if let Ok(mut sessions) = app_clone.state::<RdpState>().sessions.lock() {
            sessions.remove(&session_id_clone);
        }
        let _ = app_clone.emit("rdp-closed", &session_id_clone);
    });

    Ok(())
}

pub fn resize_rdp_embedded(
    session_id: &str,
    x: i32,
    y: i32,
    width: i32,
    height: i32,
    device_pixel_ratio: f64,
    app: &tauri::AppHandle,
    state: &RdpState,
) -> Result<(), String> {
    let mut sessions = state.sessions.lock().map_err(|e| e.to_string())?;
    if let Some(session) = sessions.get_mut(session_id) {
        let hwnd = HWND(session.mstsc_hwnd as *mut _);
        unsafe {
            if width <= 0 || height <= 0 {
                let _ = ShowWindow(hwnd, SW_HIDE);
                session.visible = false;
                // Move off-screen so even if mstsc re-shows, it's hidden
                let _ = SetWindowPos(
                    hwnd,
                    HWND_NOTOPMOST,
                    -32000,
                    -32000,
                    0,
                    0,
                    SWP_NOSIZE | SWP_NOACTIVATE,
                );
            } else {
                let main_window = app
                    .get_webview_window("main")
                    .ok_or_else(|| "Main window not found".to_string())?;
                let parent_hwnd = HWND(main_window.hwnd().map_err(|e| e.to_string())?.0 as *mut _);

                use windows::Win32::Foundation::POINT;
                use windows::Win32::Graphics::Gdi::ClientToScreen;

                let x_phys = (x as f64 * device_pixel_ratio).round() as i32;
                let y_phys = (y as f64 * device_pixel_ratio).round() as i32;
                let width_phys = (width as f64 * device_pixel_ratio).round() as i32;
                let height_phys = (height as f64 * device_pixel_ratio).round() as i32;

                let mut pt = POINT {
                    x: x_phys,
                    y: y_phys,
                };
                let _ = ClientToScreen(parent_hwnd, &mut pt);

                let _ = ShowWindow(hwnd, SW_SHOW);
                session.visible = true;
                let _ = SetWindowPos(
                    hwnd,
                    HWND_NOTOPMOST,
                    pt.x,
                    pt.y,
                    width_phys,
                    height_phys,
                    SWP_NOACTIVATE | SWP_SHOWWINDOW,
                );
            }
        }
    }
    Ok(())
}

pub fn disconnect_rdp_embedded(
    session_id: &str,
    state: &RdpState,
    app: &tauri::AppHandle,
) -> Result<(), String> {
    use windows::Win32::Foundation::{LPARAM, WPARAM};
    use windows::Win32::UI::WindowsAndMessaging::{PostMessageW, WM_CLOSE};

    let mut sessions = state.sessions.lock().map_err(|e| e.to_string())?;
    if let Some(session) = sessions.remove(session_id) {
        let hwnd = HWND(session.mstsc_hwnd as *mut _);
        unsafe {
            // Send WM_CLOSE to gracefully close the mstsc window
            let _ = PostMessageW(hwnd, WM_CLOSE, WPARAM(0), LPARAM(0));
        }
    }
    Ok(())
}

pub fn send_rdp_mouse(
    session_id: &str,
    x: i32,
    y: i32,
    button: &str,
    action: &str,
    _wheel_delta: i32,
    state: &RdpState,
) -> Result<(), String> {
    use windows::Win32::Foundation::{LPARAM, WPARAM};
    use windows::Win32::UI::WindowsAndMessaging::{
        PostMessageW, WM_LBUTTONDOWN, WM_LBUTTONUP, WM_MOUSEMOVE, WM_RBUTTONDOWN, WM_RBUTTONUP,
    };

    let sessions = state.sessions.lock().map_err(|e| e.to_string())?;
    if let Some(session) = sessions.get(session_id) {
        let hwnd = HWND(session.mstsc_hwnd as *mut _);
        let lparam = LPARAM((((y as u32) << 16) | ((x as u32) & 0xFFFF)) as isize);
        unsafe {
            match (button, action) {
                ("left", "down") => {
                    let _ = PostMessageW(hwnd, WM_LBUTTONDOWN, WPARAM(1), lparam);
                }
                ("left", "up") => {
                    let _ = PostMessageW(hwnd, WM_LBUTTONUP, WPARAM(0), lparam);
                }
                ("right", "down") => {
                    let _ = PostMessageW(hwnd, WM_RBUTTONDOWN, WPARAM(1), lparam);
                }
                ("right", "up") => {
                    let _ = PostMessageW(hwnd, WM_RBUTTONUP, WPARAM(0), lparam);
                }
                ("move", _) => {
                    let _ = PostMessageW(hwnd, WM_MOUSEMOVE, WPARAM(0), lparam);
                }
                _ => {}
            }
        }
    }
    Ok(())
}

pub fn send_rdp_key(
    session_id: &str,
    vk: u16,
    key_up: bool,
    state: &RdpState,
) -> Result<(), String> {
    use windows::Win32::Foundation::{LPARAM, WPARAM};
    use windows::Win32::UI::WindowsAndMessaging::{PostMessageW, WM_KEYDOWN, WM_KEYUP};

    let sessions = state.sessions.lock().map_err(|e| e.to_string())?;
    if let Some(session) = sessions.get(session_id) {
        let hwnd = HWND(session.mstsc_hwnd as *mut _);
        let msg = if key_up { WM_KEYUP } else { WM_KEYDOWN };
        unsafe {
            let _ = PostMessageW(hwnd, msg, WPARAM(vk as usize), LPARAM(0));
        }
    }
    Ok(())
}
