use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Mutex;
use std::time::Duration;
use tauri::{Manager, Emitter};

use windows::Win32::Foundation::{HWND, LPARAM, BOOL};
use windows::Win32::UI::WindowsAndMessaging::{
    EnumWindows, EnumChildWindows, GetWindowThreadProcessId, GetClassNameW,
    SetWindowLongW, SetWindowLongPtrW, GetWindowLongW, ShowWindow, MoveWindow, SetWindowPos,
    IsWindow, GWL_STYLE, GWLP_HWNDPARENT, SetForegroundWindow,
    WS_POPUP, WS_CAPTION, WS_THICKFRAME, WS_BORDER, WS_SYSMENU,
    WS_CLIPSIBLINGS, WS_CLIPCHILDREN,
    SW_SHOW, SW_HIDE, SWP_NOMOVE, SWP_NOSIZE, SWP_NOACTIVATE, SWP_SHOWWINDOW, HWND_TOP,
};

struct ChildResizeData {
    width: i32,
    height: i32,
}

unsafe extern "system" fn resize_child_fill(child: HWND, lparam: LPARAM) -> BOOL {
    let data = &*(lparam.0 as *const ChildResizeData);
    let _ = MoveWindow(child, 0, 0, data.width, data.height, BOOL(1));
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

impl RdpState {
    pub fn new() -> Self {
        Self { sessions: Mutex::new(HashMap::new()) }
    }
}

fn log_debug(app_data_dir: &std::path::Path, message: &str) {
    let log_file = app_data_dir.join("rdp_debug.log");
    let line = format!("{}\n", message);
    let _ = std::fs::OpenOptions::new()
        .create(true).append(true)
        .open(log_file)
        .and_then(|mut f| std::io::Write::write_all(&mut f, line.as_bytes()));
}

// Win32 EnumWindows helper structures
struct EnumData {
    pid: u32,
    hwnd: Option<HWND>,
}

unsafe extern "system" fn enum_windows_callback(hwnd: HWND, lparam: LPARAM) -> BOOL {
    let data = &mut *(lparam.0 as *mut EnumData);
    let mut window_pid = 0u32;
    GetWindowThreadProcessId(hwnd, Some(&mut window_pid));
    if window_pid == data.pid {
        let mut class_name = [0u16; 256];
        let len = GetClassNameW(hwnd, &mut class_name);
        if len > 0 {
            let class_str = String::from_utf16_lossy(&class_name[..len as usize]);
            if class_str == "TscShellContainerClass" || class_str.contains("FreeRDP") || class_str.contains("wfreerdp") {
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
        let _ = EnumWindows(Some(enum_windows_callback), LPARAM(&mut data as *mut EnumData as isize));
    }
    data.hwnd
}







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

    let file_name = format!("session_ext-{}-{}.rdp", server_id.unwrap_or_default(), uuid::Uuid::new_v4());
    let rdp_file_path = rdp_sessions_dir.join(file_name);

    let user_line = if let Some(user) = username {
        format!("username:s:{}\r\n", user)
    } else {
        String::new()
    };

    let screen_mode = if fullscreen { 2 } else { 1 };
    let multimon_line = if rdp_multimon { "use multimon:i:1\r\n" } else { "" };
    
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

    let rdp_content = format!(
        "full address:s:{}\r\n\
         {}\
         screen mode id:i:{}\r\n\
         {}\
         smart sizing:i:{}\r\n\
         dynamic resolution:i:1\r\n\
         redirectclipboard:i:{}\r\n\
         redirectdrives:i:{}\r\n\
         redirectprinters:i:{}\r\n\
         audiomode:i:{}\r\n\
         redirectsmartcards:i:{}\r\n\
         enablewebauthn:i:{}\r\n\
         authentication level:i:0\r\n\
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
        redirect_webauthn
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
        .args(&["add", "HKCU\\Software\\Microsoft\\Terminal Server Client", "/v", "AuthenticationLevelOverride", "/t", "REG_DWORD", "/d", "0", "/f"])
        .spawn()
        .and_then(|mut c| c.wait());

    // Store RDP credentials via cmdkey if password is supplied
    if let (Some(user), Some(pass)) = (username, password) {
        let target = format!("TERMSRV/{}", host);
        log_debug(&app_data_dir, &format!("Storing credential for {}", target));
        let _ = std::process::Command::new("cmdkey")
            .args(&["/generic:", &target, "/user:", user, "/pass:", pass])
            .spawn()
            .and_then(|mut c| c.wait());
    }

    // 1. Compute screen coordinates BEFORE creating RDP file (so winposstr is accurate)
    let (screen_x, screen_y) = unsafe {
        use windows::Win32::Graphics::Gdi::ClientToScreen;
        use windows::Win32::Foundation::POINT;
        let mut pt = POINT { x: x_phys, y: y_phys };
        let _ = ClientToScreen(parent_hwnd, &mut pt);
        (pt.x, pt.y)
    };

    // 2. Create RDP file with correct winposstr at screen coords
    let rdp_sessions_dir = app_data_dir.join("rdp_sessions");
    let _ = std::fs::create_dir_all(&rdp_sessions_dir);

    let file_name = format!("session_emb-{}.rdp", uuid::Uuid::new_v4());
    let rdp_file_path = rdp_sessions_dir.join(file_name);

    let user_line = username.map(|u| format!("username:s:{}\r\n", u)).unwrap_or_default();
    let smart_sizing_val = if rdp_smart_sizing { 1 } else { 0 };
    let redirect_clipboard = if rdp_clipboard { 1 } else { 0 };
    let redirect_drives = if rdp_drives { 1 } else { 0 };
    let redirect_printers = if rdp_printers { 1 } else { 0 };
    let redirect_smartcards = if rdp_smartcards { 1 } else { 0 };
    let redirect_webauthn = if rdp_webauthn { 1 } else { 0 };
    let audio_val = match rdp_audio { 0 => 0, 1 => 1, 2 => 2, _ => 0 };
    let win_right = screen_x + width_phys;
    let win_bottom = screen_y + height_phys;

    let rdp_content = format!(
        "full address:s:{}\r\n\
         {}\
         screen mode id:i:1\r\n\
         smart sizing:i:{}\r\n\
         dynamic resolution:i:1\r\n\
         redirectclipboard:i:{}\r\n\
         redirectdrives:i:{}\r\n\
         redirectprinters:i:{}\r\n\
         audiomode:i:{}\r\n\
         redirectsmartcards:i:{}\r\n\
         enablewebauthn:i:{}\r\n\
         authentication level:i:0\r\n\
         serverauth:i:0\r\n\
         enablecredsspsupport:i:{}\r\n\
         displayconnectionbar:i:0\r\n\
         prompt for credentials:i:0\r\n\
         promptcredentialonce:i:0\r\n\
         disableconnectionsharing:i:1\r\n\
         autoreconnection enabled:i:1\r\n\
         connection type:i:2\r\n\
         desktopwidth:i:{}\r\n\
         desktopheight:i:{}\r\n\
         session bpp:i:32\r\n\
         winposstr:s:0,1,{},{},{},{}\r\n",
        connection_string,
        user_line,
        smart_sizing_val,
        redirect_clipboard,
        redirect_drives,
        redirect_printers,
        audio_val,
        redirect_smartcards,
        redirect_webauthn,
        if password.is_some() { 1 } else { 0 },
        width_phys, height_phys,
        screen_x, screen_y, win_right, win_bottom,
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
    log_debug(&app_data_dir, &format!("RDP file created with winposstr {} {} {} {}", screen_x, screen_y, win_right, win_bottom));

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
                log_debug(&app_data_dir, &format!("mstsc exited prematurely with code {:?}", status.code()));
                return Err(format!("mstsc exited prematurely (code {:?})", status.code()));
            }
            _ => {}
        }
        if let Some(hwnd) = find_mstsc_hwnd(pid) {
            mstsc_hwnd = Some(hwnd);
            log_debug(&app_data_dir, &format!("Found mstsc window HWND={:?}", hwnd));
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
        let _ = SetWindowPos(hwnd, HWND_TOP, screen_x, screen_y, width_phys, height_phys,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        log_debug(&app_data_dir, &format!("Styled/positioned mstsc @ ({},{}) {}x{}", screen_x, screen_y, width_phys, height_phys));

        // Resize all child windows of mstsc to fill the parent
        let resize_data = ChildResizeData { width: width_phys, height: height_phys };
        let _ = EnumChildWindows(hwnd, Some(resize_child_fill), LPARAM(&resize_data as *const _ as isize));
    }

    // 5. Store session
    let state = app.state::<RdpState>();
    {
        let mut sessions = state.sessions.lock().map_err(|e| e.to_string())?;
        sessions.insert(session_id.clone(), RdpSession {
            target_host: host.to_string(),
            server_id,
            mstsc_hwnd: hwnd.0 as isize,
            pid,
            visible: true,
        });
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
            let should_be_visible = app_clone.state::<RdpState>().sessions.lock().ok()
                .and_then(|s| s.get(&sid_clone).map(|sess| sess.visible))
                .unwrap_or(false);
            unsafe {
                if should_be_visible {
                    // Restore from minimized/hidden state after app restore
                    ShowWindow(thread_hwnd, SW_SHOW);
                    let _ = SetWindowPos(thread_hwnd, HWND_TOP, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                } else {
                    // Force off-screen so it never blocks other windows
                    let _ = SetWindowPos(thread_hwnd, HWND_TOP, -32000, -32000, 0, 0,
                        SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
        }
        // Cleanup stored credential
        let _ = std::process::Command::new("cmdkey")
            .args(&["/delete:", &format!("TERMSRV/{}", host_clone)])
            .spawn()
            .and_then(|mut c| c.wait());
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
                ShowWindow(hwnd, SW_HIDE);
                session.visible = false;
                // Move off-screen so even if mstsc re-shows, it's hidden
                let _ = SetWindowPos(hwnd, HWND_TOP, -32000, -32000, 0, 0,
                    SWP_NOSIZE | SWP_NOACTIVATE);
            } else {
                let main_window = app.get_webview_window("main").ok_or_else(|| "Main window not found".to_string())?;
                let parent_hwnd = HWND(main_window.hwnd().map_err(|e| e.to_string())?.0 as *mut _);

                use windows::Win32::Graphics::Gdi::ClientToScreen;
                use windows::Win32::Foundation::POINT;

                let x_phys = (x as f64 * device_pixel_ratio).round() as i32;
                let y_phys = (y as f64 * device_pixel_ratio).round() as i32;
                let width_phys = (width as f64 * device_pixel_ratio).round() as i32;
                let height_phys = (height as f64 * device_pixel_ratio).round() as i32;

                let mut pt = POINT { x: x_phys, y: y_phys };
                let _ = ClientToScreen(parent_hwnd, &mut pt);

                ShowWindow(hwnd, SW_SHOW);
                session.visible = true;
                let _ = SetWindowPos(hwnd, HWND_TOP, pt.x, pt.y, width_phys, height_phys,
                    SWP_NOACTIVATE | SWP_SHOWWINDOW);
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
    let app_data_dir = app.path().app_data_dir().unwrap_or_default();
    log_debug(&app_data_dir, &format!("disconnect_rdp_embedded called for session {}", session_id));

    let session = {
        let mut sessions = state.sessions.lock().map_err(|e| e.to_string())?;
        sessions.remove(session_id)
    };

    if let Some(sess) = session {
        let mut window_pid = 0u32;
        unsafe {
            GetWindowThreadProcessId(HWND(sess.mstsc_hwnd as *mut _), Some(&mut window_pid));
        }

        if window_pid != 0 {
            let _ = std::process::Command::new("taskkill")
                .args(&["/F", "/PID", &window_pid.to_string()])
                .spawn()
                .and_then(|mut c| c.wait());
        }

        let _ = std::process::Command::new("taskkill")
            .args(&["/F", "/PID", &sess.pid.to_string()])
            .spawn()
            .and_then(|mut c| c.wait());
    }

    Ok(())
}

// Deprecated empty handlers to avoid breaking Tauri registration signatures
pub fn send_rdp_mouse(
    _session_id: &str,
    _x: i32,
    _y: i32,
    _button: &str,
    _action: &str,
    _wheel_delta: i32,
    _state: &RdpState,
) -> Result<(), String> {
    Ok(())
}

pub fn send_rdp_key(
    _session_id: &str,
    _vk: u16,
    _key_up: bool,
    _state: &RdpState,
) -> Result<(), String> {
    Ok(())
}
