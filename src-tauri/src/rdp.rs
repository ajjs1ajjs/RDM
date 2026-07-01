use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Mutex;
use std::process::Command;
use windows::Win32::Foundation::{HWND, LPARAM, BOOL, WPARAM};
use tauri::{Manager, Emitter};

#[derive(Clone, Copy, Debug)]
pub struct SendHwnd(pub HWND);
unsafe impl Send for SendHwnd {}
unsafe impl Sync for SendHwnd {}

pub struct RdpSession {
    pub pid: u32,
    pub hwnd: Option<SendHwnd>,
    pub rdp_file_path: PathBuf,
    pub target_host: String,
    pub server_id: Option<String>,
    pub x: i32,
    pub y: i32,
    pub width: i32,
    pub height: i32,
}

pub struct RdpState {
    pub sessions: Mutex<HashMap<String, RdpSession>>,
}

impl RdpState {
    pub fn new() -> Self {
        Self { sessions: Mutex::new(HashMap::new()) }
    }
}

// ============================================================
// Win32 API declarations
// ============================================================

fn log_debug(app_data_dir: &std::path::Path, message: &str) {
    let log_file = app_data_dir.join("rdp_debug.log");
    let line = format!("{}\n", message);
    let _ = std::fs::OpenOptions::new()
        .create(true).append(true)
        .open(log_file)
        .and_then(|mut f| std::io::Write::write_all(&mut f, line.as_bytes()));
}

unsafe fn set_dpi_hosting_behavior_mixed(app_data_dir: &std::path::Path) {
    let user32_dll = "user32.dll\0";
    let set_behavior_fn = "SetThreadDpiHostingBehavior\0";
    
    let h_module = GetModuleHandleA(user32_dll.as_ptr());
    if h_module != 0 {
        let addr = GetProcAddress(h_module, set_behavior_fn.as_ptr());
        if !addr.is_null() {
            let func: unsafe extern "system" fn(i32) -> i32 = std::mem::transmute(addr);
            let prev = func(1); // DPI_HOSTING_BEHAVIOR_MIXED = 1
            log_debug(app_dir_fallback(app_data_dir), &format!("SetThreadDpiHostingBehavior(1) called. Previous behavior: {}", prev));
        } else {
            log_debug(app_dir_fallback(app_data_dir), "SetThreadDpiHostingBehavior is not supported on this OS version");
        }
    } else {
        log_debug(app_dir_fallback(app_data_dir), "Failed to load user32.dll for DPI scaling setup");
    }
}

fn app_dir_fallback(p: &std::path::Path) -> &std::path::Path {
    p
}


struct WebViewFinder {
    webview_hwnd: HWND,
    app_data_dir: std::path::PathBuf,
}

unsafe extern "system" fn enum_child_callback(hwnd: HWND, lparam: LPARAM) -> BOOL {
    let finder = &mut *(lparam.0 as *mut WebViewFinder);
    let mut class_name = [0u16; 256];
    let class_len = GetClassNameW(hwnd, class_name.as_mut_ptr(), 256);
    if class_len > 0 {
        let class_str = String::from_utf16_lossy(&class_name[..class_len as usize]);
        let mut rect = RECT { left: 0, top: 0, right: 0, bottom: 0 };
        let _ = GetWindowRect(hwnd, &mut rect);
        log_debug(&finder.app_data_dir, &format!("enum_child_callback: Found child window HWND {:?} Class: {}, Rect: {:?}", hwnd, class_str, rect));
        if class_str.contains("Chrome_WidgetWin_0") && finder.webview_hwnd.0.is_null() {
            finder.webview_hwnd = hwnd;
        }
    }
    true.into() // Continue enumeration to log all children
}

unsafe fn get_webview_hwnd(parent_hwnd: HWND, app_data_dir: &std::path::Path) -> HWND {
    log_debug(app_data_dir, &format!("get_webview_hwnd: Starting search under parent HWND {:?}", parent_hwnd));
    let mut finder = WebViewFinder {
        webview_hwnd: HWND(std::ptr::null_mut()),
        app_data_dir: app_data_dir.to_path_buf(),
    };
    let _ = EnumChildWindows(
        parent_hwnd,
        enum_child_callback,
        LPARAM(&mut finder as *mut _ as isize),
    );
    if finder.webview_hwnd.0.is_null() {
        log_debug(app_data_dir, "get_webview_hwnd: Chrome_WidgetWin_0 not found, falling back to parent");
        parent_hwnd
    } else {
        log_debug(app_data_dir, &format!("get_webview_hwnd: Found Chrome_WidgetWin_0 at {:?}", finder.webview_hwnd));
        finder.webview_hwnd
    }
}


extern "system" {
    fn PostMessageW(
        hWnd: HWND,
        Msg: u32,
        wParam: WPARAM,
        lParam: LPARAM,
    ) -> BOOL;
    fn SetWindowPos(
        hWnd: HWND,
        hWndInsertAfter: HWND,
        X: i32,
        Y: i32,
        cx: i32,
        cy: i32,
        uFlags: u32,
    ) -> BOOL;
    fn SetParent(hWndChild: HWND, hWndNewParent: HWND) -> HWND;
    fn GetClassNameW(hWnd: HWND, lpClassName: *mut u16, nMaxCount: i32) -> i32;
    fn EnumWindows(lpEnumFunc: unsafe extern "system" fn(HWND, LPARAM) -> BOOL, lParam: LPARAM) -> BOOL;
    fn GetWindowLongW(hWnd: HWND, nIndex: i32) -> i32;
    fn SetWindowLongW(hWnd: HWND, nIndex: i32, dwNewLong: i32) -> i32;
    fn OpenProcess(dwDesiredAccess: u32, bInheritHandle: bool, dwProcessId: u32) -> isize;
    fn TerminateProcess(hProcess: isize, uExitCode: u32) -> BOOL;
    fn CloseHandle(hObject: isize) -> BOOL;
    fn GetLastError() -> u32;
    fn GetModuleHandleA(lpModuleName: *const u8) -> isize;
    fn GetProcAddress(hModule: isize, lpProcName: *const u8) -> *const std::ffi::c_void;
    fn GetParent(hWnd: HWND) -> HWND;
    fn IsWindow(hWnd: HWND) -> BOOL;
    fn GetWindowThreadProcessId(hWnd: HWND, lpdwProcessId: *mut u32) -> u32;
    fn EnumChildWindows(hwndParent: HWND, lpEnumFunc: unsafe extern "system" fn(HWND, LPARAM) -> BOOL, lParam: LPARAM) -> BOOL;
    fn GetWindowRect(hWnd: HWND, lpRect: *mut RECT) -> BOOL;
    fn GetDpiForWindow(hwnd: HWND) -> u32;
}

#[repr(C)]
#[derive(Debug, Copy, Clone)]
pub struct RECT {
    pub left: i32,
    pub top: i32,
    pub right: i32,
    pub bottom: i32,
}

fn get_window_scale_factor(hwnd: HWND, app_data_dir: &std::path::Path) -> f64 {
    let dpi = unsafe { GetDpiForWindow(hwnd) };
    let factor = dpi as f64 / 96.0;
    log_debug(app_data_dir, &format!("get_window_scale_factor: DPI={}, factor={}", dpi, factor));
    if factor > 0.0 { factor } else { 1.0 }
}

const GWL_STYLE: i32 = -16;
const WS_POPUP: i32 = 0x80000000u32 as i32;
const WS_CAPTION: i32 = 0x00C00000;
const WS_THICKFRAME: i32 = 0x00040000;
const WS_CHILD: i32 = 0x40000000;

const WM_CLOSE: u32 = 0x0010;
const PROCESS_TERMINATE: u32 = 0x0001;

const SWP_SHOWWINDOW: u32 = 0x0040;
const SWP_HIDEWINDOW: u32 = 0x0080;
const SWP_FRAMECHANGED: u32 = 0x0020;
const SWP_NOACTIVATE: u32 = 0x0010;
const SWP_NOMOVE: u32 = 0x0002;
const SWP_NOSIZE: u32 = 0x0001;
const SWP_NOZORDER: u32 = 0x0004;

struct EnumData {
    target_pid: u32,
    hwnd: HWND,
}

fn resize_all_children(parent_hwnd: HWND, width: i32, height: i32) {
    struct ResizeData {
        width: i32,
        height: i32,
    }
    
    unsafe extern "system" fn resize_child_callback(hwnd: HWND, lparam: LPARAM) -> BOOL {
        let data = &*(lparam.0 as *const ResizeData);
        let _ = SetWindowPos(
            hwnd,
            HWND(std::ptr::null_mut()),
            0,
            0,
            data.width,
            data.height,
            SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED,
        );
        BOOL(1)
    }

    let mut data = ResizeData { width, height };
    unsafe {
        let _ = EnumChildWindows(
            parent_hwnd,
            resize_child_callback,
            LPARAM(&mut data as *mut _ as isize),
        );
    }
}

unsafe extern "system" fn enum_windows_callback(hwnd: HWND, lparam: LPARAM) -> BOOL {
    let data = &mut *(lparam.0 as *mut EnumData);
    
    // Check window class
    let mut class_name = [0u16; 256];
    let class_len = GetClassNameW(hwnd, class_name.as_mut_ptr(), 256);
    if class_len > 0 {
        let class_str = String::from_utf16_lossy(&class_name[..class_len as usize]);
        // mstsc window class is "TscShellContainerClass"
        if class_str == "TscShellContainerClass" {
            let mut window_pid = 0u32;
            let _ = GetWindowThreadProcessId(hwnd, &mut window_pid as *mut u32);
            if window_pid == data.target_pid {
                data.hwnd = hwnd;
                return false.into(); // Found exact match, stop enumeration
            }
        }
    }
    true.into() // Continue enumeration
}

pub fn launch_rdp_session(
    host: &str,
    port: u32,
    fullscreen: bool,
    username: Option<&str>,
    password: Option<&str>,
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
    let connection_string = if port == 3389 {
        host.to_string()
    } else {
        format!("{}:{}", host, port)
    };

    if let (Some(user), Some(pass)) = (username, password) {
        let target = format!("TERMSRV/{}", host);
        let _ = Command::new("cmdkey")
            .args(&[&format!("/generic:{}", target), &format!("/user:{}", user), &format!("/pass:{}", pass)])
            .status();

        if port != 3389 {
            let target2 = format!("TERMSRV/{}:{}", host, port);
            let _ = Command::new("cmdkey")
                .args(&[&format!("/generic:{}", target2), &format!("/user:{}", user), &format!("/pass:{}", pass)])
                .status();
        }
    }

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
    parent_hwnd: HWND,
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
    let connection_string = if port == 3389 {
        host.to_string()
    } else {
        format!("{}:{}", host, port)
    };

    // Frontend sends CSS pixel coordinates — we scale using real OS DPI from the parent window
    let scale_factor = get_window_scale_factor(parent_hwnd, &app_data_dir);
    let physical_x = (x as f64 * scale_factor) as i32;
    let physical_y = (y as f64 * scale_factor) as i32;
    let physical_width = (width as f64 * scale_factor) as i32;
    let physical_height = (height as f64 * scale_factor) as i32;
    log_debug(&app_data_dir, &format!("launch_rdp_embedded: css=({},{},{}x{}) dpr={} scale={} phys=({},{},{}x{})", x, y, width, height, device_pixel_ratio, scale_factor, physical_x, physical_y, physical_width, physical_height));

    // Register credentials using cmdkey
    if let (Some(user), Some(pass)) = (username, password) {
        let target = format!("TERMSRV/{}", host);
        let _ = Command::new("cmdkey")
            .args(&[&format!("/generic:{}", target), &format!("/user:{}", user), &format!("/pass:{}", pass)])
            .status();

        if port != 3389 {
            let target2 = format!("TERMSRV/{}:{}", host, port);
            let _ = Command::new("cmdkey")
                .args(&[&format!("/generic:{}", target2), &format!("/user:{}", user), &format!("/pass:{}", pass)])
                .status();
        }
    }

    // Create session temp directory
    let rdp_sessions_dir = app_data_dir.join("rdp_sessions");
    let _ = std::fs::create_dir_all(&rdp_sessions_dir);

    // Unique filename for this session
    let file_name = format!("session_rdp-{}-{}.rdp", session_id, uuid::Uuid::new_v4());
    let rdp_file_path = rdp_sessions_dir.join(file_name);

    let user_line = if let Some(user) = username {
        format!("username:s:{}\r\n", user)
    } else {
        String::new()
    };

    let smart_sizing_val = if rdp_smart_sizing { 1 } else { 0 };
    let redirect_clipboard = if rdp_clipboard { 1 } else { 0 };
    let redirect_drives = if rdp_drives { 1 } else { 0 };
    let redirect_printers = if rdp_printers { 1 } else { 0 };
    let redirect_smartcards = if rdp_smartcards { 1 } else { 0 };
    let redirect_webauthn = if rdp_webauthn { 1 } else { 0 };

    let audio_val = match rdp_audio {
        0 => 0, // Play on this computer
        1 => 1, // Do not play
        2 => 2, // Play on remote computer
        _ => 0,
    };

    // Use exact physical dimensions for desktop resolution — smart sizing handles scaling
    let desktop_w = if physical_width > 0 { physical_width } else { 800 };
    let desktop_h = if physical_height > 0 { physical_height } else { 600 };

    let rdp_content = format!(
        "full address:s:{}\r\n\
         {}\
         screen mode id:i:1\r\n\
         desktopwidth:i:{}\r\n\
         desktopheight:i:{}\r\n\
         smart sizing:i:{}\r\n\
         winposstr:s:0,1,-32000,-32000,-31000,-30000\r\n\
         redirectclipboard:i:{}\r\n\
         redirectdrives:i:{}\r\n\
         redirectprinters:i:{}\r\n\
         audiomode:i:{}\r\n\
         redirectsmartcards:i:{}\r\n\
         enablewebauthn:i:{}\r\n\
         authentication level:i:0\r\n\
         displayconnectionbar:i:0\r\n",
        connection_string,
        user_line,
        desktop_w,
        desktop_h,
        smart_sizing_val,
        redirect_clipboard,
        redirect_drives,
        redirect_printers,
        audio_val,
        redirect_smartcards,
        redirect_webauthn
    );

    // MUST write RDP file as UTF-16 LE with BOM for mstsc to accept it on all systems
    let rdp_content_utf16: Vec<u16> = std::iter::once(0xFEFF) // UTF-16 LE BOM
        .chain(rdp_content.encode_utf16())
        .collect();
    
    let rdp_content_bytes: &[u8] = unsafe {
        std::slice::from_raw_parts(
            rdp_content_utf16.as_ptr() as *const u8,
            rdp_content_utf16.len() * 2,
        )
    };

    std::fs::write(&rdp_file_path, rdp_content_bytes)
        .map_err(|e| format!("Failed to write temporary RDP file: {}", e))?;

    // Spawn mstsc pointing to the custom .rdp file
    let mut child = Command::new("mstsc")
        .arg(rdp_file_path.to_string_lossy().to_string())
        .spawn()
        .map_err(|e| format!("Failed to launch mstsc: {}", e))?;

    let pid = child.id();
    let app_clone_wait = app.clone();
    let session_id_clone_wait = session_id.clone();
    std::thread::spawn(move || {
        let _ = child.wait();
        
        let app_data_dir = app_clone_wait.path().app_data_dir().unwrap_or_default();
        log_debug(&app_data_dir, &format!("RDP process for session {} exited.", session_id_clone_wait));
        
        // Wait 15 seconds to give the search loop time to find the window
        std::thread::sleep(std::time::Duration::from_secs(15));
        
        let state = app_clone_wait.state::<RdpState>();
        let mut sessions = state.sessions.lock().unwrap();
        
        let should_cleanup = if let Some(session) = sessions.get(&session_id_clone_wait) {
            let is_none = session.hwnd.is_none();
            log_debug(&app_data_dir, &format!("wait thread check: session found, hwnd.is_none = {}", is_none));
            is_none
        } else {
            log_debug(&app_data_dir, "wait thread check: session not found in active sessions map");
            false
        };
        
        if should_cleanup {
            log_debug(&app_data_dir, &format!("wait thread executing cleanup for session {}", session_id_clone_wait));
            if let Some(session) = sessions.remove(&session_id_clone_wait) {
                let target = format!("TERMSRV/{}", session.target_host);
                let _ = Command::new("cmdkey")
                    .arg(format!("/delete:{}", target))
                    .status();
                let _ = std::fs::remove_file(session.rdp_file_path);
            }
            let _ = app_clone_wait.emit("rdp-closed", &session_id_clone_wait);
        }
    });

    // Store session state initially with hwnd = None
    {
        let state = app.state::<RdpState>();
        let mut sessions = state.sessions.lock().unwrap();
        sessions.insert(
            session_id.clone(),
            RdpSession {
                pid,
                hwnd: None,
                rdp_file_path,
                target_host: host.to_string(),
                server_id,
                x: physical_x,
                y: physical_y,
                width: physical_width,
                height: physical_height,
            },
        );
    }

    // Spawn background thread to find and reparent the window
    let app_clone = app.clone();
    let session_id_clone = session_id.clone();
    let parent_hwnd_isize = parent_hwnd.0 as isize;

    std::thread::spawn(move || {
        let state = app_clone.state::<RdpState>();
        let mut found_hwnd = HWND(std::ptr::null_mut());

        // Loop for up to 60 seconds (600 * 100ms)
        for _ in 0..600 {
            // Check if session was deleted in the meantime
            {
                let sessions = state.sessions.lock().unwrap();
                if !sessions.contains_key(&session_id_clone) {
                    return; // Exit thread
                }
            }

            let mut data = EnumData {
                target_pid: pid,
                hwnd: HWND(std::ptr::null_mut()),
            };
            unsafe {
                let _ = EnumWindows(enum_windows_callback, LPARAM(&mut data as *mut _ as isize));
            }

            if !data.hwnd.0.is_null() {
                found_hwnd = data.hwnd;
                break;
            }

            std::thread::sleep(std::time::Duration::from_millis(100));
        }

        if !found_hwnd.0.is_null() {
            // Wait 50ms for window handles to stabilize
            std::thread::sleep(std::time::Duration::from_millis(50));

            let found_hwnd_val = found_hwnd.0 as isize;
            let app_clone2 = app_clone.clone();
            let session_id_clone2 = session_id_clone.clone();
            let app_data_dir_clone = app_clone.path().app_data_dir().unwrap_or_default();
            let _ = app_clone.run_on_main_thread(move || {
                let hwnd = HWND(found_hwnd_val as *mut std::ffi::c_void);
                log_debug(&app_data_dir_clone, &format!("Window found: {:?}. Starting reparenting...", hwnd));
                
                let state = app_clone2.state::<RdpState>();
                let mut sessions = state.sessions.lock().unwrap();
                let (current_x, current_y, current_width, current_height) = if let Some(session) = sessions.get_mut(&session_id_clone2) {
                    session.hwnd = Some(SendHwnd(hwnd));
                    (session.x, session.y, session.width, session.height)
                } else {
                    (physical_x, physical_y, physical_width, physical_height)
                };
                drop(sessions);

                unsafe {
                    set_dpi_hosting_behavior_mixed(&app_data_dir_clone);

                    // Clear styles and inject WS_CHILD (clear WS_THICKFRAME to avoid borders)
                    let style = GetWindowLongW(hwnd, GWL_STYLE);
                    let new_style = (style & !(WS_POPUP | WS_CAPTION | WS_THICKFRAME)) | WS_CHILD;
                    let set_style_res = SetWindowLongW(hwnd, GWL_STYLE, new_style);
                    log_debug(&app_data_dir_clone, &format!("Style updated. Old style: 0x{:X}, New style: 0x{:X}, Result: {}", style, new_style, set_style_res));
                    
                    // Reparent
                    let parent_hwnd = HWND(parent_hwnd_isize as *mut std::ffi::c_void);
                    let target_parent = get_webview_hwnd(parent_hwnd, &app_data_dir_clone);
                    let prev_parent = SetParent(hwnd, target_parent);
                    let err = GetLastError();
                    let actual_parent = GetParent(hwnd);
                    log_debug(&app_data_dir_clone, &format!("SetParent called. Target Parent: {:?}, Previous Parent: {:?}, Actual Parent after call: {:?}, GetLastError: {}", target_parent, prev_parent, actual_parent, err));
                    
                    // Apply layout update and repaint — no SWP_FRAMECHANGED to avoid mstsc recalculating chrome
                    let set_pos_res = SetWindowPos(
                        hwnd,
                        HWND(std::ptr::null_mut()),
                        current_x,
                        current_y,
                        current_width,
                        current_height,
                        SWP_NOZORDER | SWP_SHOWWINDOW,
                    );
                    log_debug(&app_data_dir_clone, &format!("SetWindowPos called at coordinates ({}, {}), size: ({}x{}). Result: {}", current_x, current_y, current_width, current_height, set_pos_res.0));
                    
                    // Recursively resize all nested children of the RDP window container
                    resize_all_children(hwnd, current_width, current_height);
                }

                // Spawn lightweight background watchdog (60s @ 500ms) - only checks window validity
                let app_clone3 = app_clone2.clone();
                let session_id_clone3 = session_id_clone2.clone();
                let app_data_dir_clone3 = app_data_dir_clone.clone();
                std::thread::spawn(move || {
                    let state = app_clone3.state::<RdpState>();
                    // Run for 60 seconds (120 * 500ms) to catch early window failures
                    for _ in 0..120 {
                        std::thread::sleep(std::time::Duration::from_millis(500));

                        let sessions = state.sessions.lock().unwrap();
                        let hwnd_val = if let Some(session) = sessions.get(&session_id_clone3) {
                            session.hwnd.map(|h| h.0.0 as isize)
                        } else {
                            None
                        };
                        drop(sessions);

                        let check = hwnd_val;
                        if let Some(hwnd_val) = check {
                            let app_clone4 = app_clone3.clone();
                            let session_id_clone4 = session_id_clone3.clone();
                            let app_data_dir_clone4 = app_data_dir_clone3.clone();
                            let _ = app_clone3.run_on_main_thread(move || {
                                unsafe {
                                let hwnd = HWND(hwnd_val as *mut std::ffi::c_void);
                                if IsWindow(hwnd).0 == 0 {
                                    log_debug(&app_data_dir_clone4, "Watchdog: window handle invalid, cleaning up.");
                                    let state = app_clone4.state::<RdpState>();
                                    let _ = disconnect_rdp_embedded(&session_id_clone4, &state, &app_clone4);
                                }
                                }
                            });
                        } else {
                            break;
                        }
                    }
                    log_debug(&app_data_dir_clone3, "--- RDP watchdog thread finished ---");
                });
            });
        }
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
    let app_data_dir = app.path().app_data_dir().unwrap_or_default();
    let mut sessions = state.sessions.lock().unwrap();
    if let Some(session) = sessions.get_mut(session_id) {
        // Compute scale factor from actual window DPI, with frontend dpr as fallback
        let scale = session.hwnd.map_or(device_pixel_ratio, |h| get_window_scale_factor(h.0, &app_data_dir));
        let phys_x = (x as f64 * scale) as i32;
        let phys_y = (y as f64 * scale) as i32;
        let phys_w = (width as f64 * scale) as i32;
        let phys_h = (height as f64 * scale) as i32;
        log_debug(&app_data_dir, &format!("resize_rdp_embedded: css=({},{},{}x{}) dpr={} scale={} phys=({},{},{}x{})", x, y, width, height, device_pixel_ratio, scale, phys_x, phys_y, phys_w, phys_h));
        session.x = phys_x;
        session.y = phys_y;
        session.width = phys_w;
        session.height = phys_h;
        if let Some(hwnd) = session.hwnd {
            unsafe {
                let flags = if phys_w == 0 || phys_h == 0 {
                    SWP_HIDEWINDOW | SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER
                } else {
                    SWP_SHOWWINDOW | SWP_NOZORDER
                };

                let _ = SetWindowPos(
                    hwnd.0,
                    HWND(std::ptr::null_mut()),
                    phys_x,
                    phys_y,
                    phys_w,
                    phys_h,
                    flags,
                );
                
                if phys_w > 0 && phys_h > 0 {
                    resize_all_children(hwnd.0, phys_w, phys_h);
                }
            }
        }
        Ok(())
    } else {
        log_debug(&app_data_dir, &format!("resize_rdp_embedded: Session {} not found in active state", session_id));
        Err("RDP Session not found".to_string())
    }
}

pub fn disconnect_rdp_embedded(
    session_id: &str,
    state: &RdpState,
    app: &tauri::AppHandle,
) -> Result<(), String> {
    let app_data_dir = app.path().app_data_dir().unwrap_or_default();
    log_debug(&app_data_dir, &format!("disconnect_rdp_embedded called for session {}", session_id));

    let mut sessions = state.sessions.lock().unwrap();
    if let Some(session) = sessions.remove(session_id) {
        // Send WM_CLOSE to window
        if let Some(hwnd) = session.hwnd {
            unsafe {
                let _ = PostMessageW(
                    hwnd.0,
                    WM_CLOSE,
                    WPARAM(0),
                    LPARAM(0),
                );
            }
        }

        // Wait 300ms and terminate process if still running
        let pid = session.pid;
        std::thread::spawn(move || {
            std::thread::sleep(std::time::Duration::from_millis(300));
            // Kill child using Win32 TerminateProcess
            unsafe {
                let h_process = OpenProcess(PROCESS_TERMINATE, false, pid);
                if h_process != 0 {
                    let _ = TerminateProcess(h_process, 0);
                    let _ = CloseHandle(h_process);
                }
            }
        });

        if let Some(ref srv_id) = session.server_id {
            if let Some(db_state) = app.try_state::<crate::DbState>() {
                let conn = db_state.conn.lock().unwrap();
                let hist = crate::db::ConnectionHistory {
                    id: uuid::Uuid::new_v4().to_string(),
                    server_id: srv_id.clone(),
                    timestamp: String::new(),
                    status: "disconnected".to_string(),
                    log: "Embedded RDP session disconnected".to_string(),
                };
                let _ = crate::db::add_history(&conn, &hist);
            }
        }
        
        // Clean up temporary RDP file
        let _ = std::fs::remove_file(session.rdp_file_path);

        // Emit closed event to frontend
        let _ = app.emit("rdp-closed", session_id);
    }
    Ok(())
}

