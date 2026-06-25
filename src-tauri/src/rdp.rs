use std::process::Command;
use std::collections::HashMap;
use std::sync::Mutex;
use std::path::PathBuf;
use windows_sys::Win32::Foundation::{HWND, LPARAM};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    EnumWindows, SetParent, SetWindowLongW,
    GetWindowLongW, GWL_STYLE, WS_CHILD, WS_POPUP, WS_CAPTION, WS_THICKFRAME,
    GetClassNameW, GetParent, GetWindowTextW,
    SetWindowPos, SWP_SHOWWINDOW, SWP_FRAMECHANGED,
};
use tauri::Manager;

type BOOL = i32;
const TRUE: BOOL = 1;
const FALSE: BOOL = 0;

#[derive(Clone, Copy, Debug)]
pub struct SendHwnd(pub HWND);

unsafe impl Send for SendHwnd {}
unsafe impl Sync for SendHwnd {}

pub struct RdpSession {
    pub child: std::process::Child,
    pub hwnd: Option<SendHwnd>,
    pub rdp_file_path: PathBuf,
    pub target_host: String,
}

pub struct RdpState {
    pub sessions: Mutex<HashMap<String, RdpSession>>,
}

impl RdpState {
    pub fn new() -> Self {
        Self {
            sessions: Mutex::new(HashMap::new()),
        }
    }
}

struct EnumData {
    target_host: String,
    hwnd: HWND,
}

unsafe extern "system" fn enum_windows_callback(hwnd: HWND, lparam: LPARAM) -> BOOL {
    let data = &mut *(lparam as *mut EnumData);
    let mut class_name = [0u16; 256];
    let len = GetClassNameW(hwnd, class_name.as_mut_ptr(), 256);
    if len > 0 {
        let class_str = String::from_utf16_lossy(&class_name[..len as usize]);
        if class_str.contains("TscShellContainerClass") {
            let parent = GetParent(hwnd);
            if parent == 0 as HWND {
                // Check if the window title contains the host name
                let mut title = [0u16; 512];
                let title_len = GetWindowTextW(hwnd, title.as_mut_ptr(), 512);
                let title_str = if title_len > 0 {
                    String::from_utf16_lossy(&title[..title_len as usize]).to_lowercase()
                } else {
                    String::new()
                };

                let target = data.target_host.to_lowercase();
                if title_str.contains(&target) {
                    data.hwnd = hwnd;
                    return FALSE; // Exact match, stop enumeration!
                }

                // If fallback is not set yet, set it
                if data.hwnd == 0 as HWND {
                    data.hwnd = hwnd;
                }
            }
        }
    }
    TRUE // Continue enumeration
}

/// Launches the native Windows Remote Desktop client (mstsc) pointing to the specified host and port.
pub fn launch_rdp_session(
    host: &str,
    port: u32,
    fullscreen: bool,
    username: Option<&str>,
    password: Option<&str>,
) -> Result<(), String> {
    let connection_string = if port == 3389 {
        host.to_string()
    } else {
        format!("{}:{}", host, port)
    };

    if let (Some(user), Some(pass)) = (username, password) {
        let target = format!("TERMSRV/{}", host);
        let status = Command::new("cmdkey")
            .args(&[&format!("/generic:{}", target), &format!("/user:{}", user), &format!("/pass:{}", pass)])
            .status()
            .map_err(|e| format!("Failed to execute cmdkey: {}", e))?;

        if !status.success() {
            println!("Warning: cmdkey failed with status {}", status);
        }
    }

    let mut args = vec![format!("/v:{}", connection_string)];
    
    if fullscreen {
        args.push("/f".to_string());
    }

    Command::new("mstsc")
        .args(&args)
        .spawn()
        .map(|_| ())
        .map_err(|e| format!("Failed to spawn mstsc process: {}", e))
}

/// Launches an embedded RDP session inside a parent Tauri window
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
    app_data_dir: PathBuf,
    app: tauri::AppHandle,
) -> Result<(), String> {
    let connection_string = if port == 3389 {
        host.to_string()
    } else {
        format!("{}:{}", host, port)
    };

    // Register credentials using cmdkey if provided
    if let (Some(user), Some(pass)) = (username, password) {
        let target = format!("TERMSRV/{}", host);
        let _ = Command::new("cmdkey")
            .args(&[&format!("/generic:{}", target), &format!("/user:{}", user), &format!("/pass:{}", pass)])
            .status();
    }

    // Write a temporary .rdp file to force embedded mode settings
    let rdp_dir = app_data_dir.join("rdp_sessions");
    std::fs::create_dir_all(&rdp_dir).map_err(|e| format!("Failed to create rdp dir: {}", e))?;
    
    let rdp_file_path = rdp_dir.join(format!("session_{}.rdp", session_id));
    
    let user_line = username.map(|u| format!("username:s:{}\r\n", u)).unwrap_or_default();
    
    let rdp_content = format!(
        "full address:s:{}\r\n\
         {}\
         screen mode id:i:1\r\n\
         desktop width:i:{}\r\n\
         desktop height:i:{}\r\n\
         smart sizing:i:1\r\n\
         authentication level:i:0\r\n\
         displayconnectionbar:i:0\r\n",
        connection_string,
        user_line,
        width,
        height
    );

    std::fs::write(&rdp_file_path, rdp_content)
        .map_err(|e| format!("Failed to write temporary RDP file: {}", e))?;

    // Spawn mstsc pointing to the custom .rdp file
    let child = Command::new("mstsc")
        .arg(rdp_file_path.to_string_lossy().to_string())
        .spawn()
        .map_err(|e| format!("Failed to launch mstsc: {}", e))?;

    // Store session state as None for HWND initially
    {
        let state = app.state::<RdpState>();
        let mut sessions = state.sessions.lock().unwrap();
        sessions.insert(
            session_id.clone(),
            RdpSession {
                child,
                hwnd: None,
                rdp_file_path,
                target_host: host.to_string(),
            },
        );
    }

    // Spawn background thread to find and reparent the window
    let app_clone = app.clone();
    let session_id_clone = session_id.clone();
    let host_clone = host.to_string();
    let parent_hwnd_isize = parent_hwnd as isize;

    std::thread::spawn(move || {
        let state = app_clone.state::<RdpState>();
        let mut found_hwnd = 0 as HWND;

        // Loop for up to 60 seconds (600 * 100ms)
        for _ in 0..600 {
            // Check if session was deleted in the meantime (e.g. user closed tab)
            {
                let sessions = state.sessions.lock().unwrap();
                if !sessions.contains_key(&session_id_clone) {
                    return; // Exit thread
                }
            }

            let mut data = EnumData {
                target_host: host_clone.clone(),
                hwnd: 0 as HWND,
            };
            unsafe {
                EnumWindows(Some(enum_windows_callback), &mut data as *mut _ as LPARAM);
            }

            if data.hwnd != 0 as HWND {
                found_hwnd = data.hwnd;
                break;
            }

            std::thread::sleep(std::time::Duration::from_millis(100));
        }

        if found_hwnd != 0 as HWND {
            // Wait 150ms for window handles to stabilize
            std::thread::sleep(std::time::Duration::from_millis(150));

            unsafe {
                // Clear styles and inject WS_CHILD
                let style = GetWindowLongW(found_hwnd, GWL_STYLE);
                let new_style = (style & !(WS_POPUP as i32 | WS_CAPTION as i32 | WS_THICKFRAME as i32)) | WS_CHILD as i32;
                SetWindowLongW(found_hwnd, GWL_STYLE, new_style);
                
                // Reparent
                let parent_hwnd = parent_hwnd_isize as HWND;
                SetParent(found_hwnd, parent_hwnd);
                
                // Apply layout update and repaint
                SetWindowPos(
                    found_hwnd,
                    0 as HWND,
                    x,
                    y,
                    width,
                    height,
                    SWP_FRAMECHANGED | SWP_SHOWWINDOW,
                );
            }

            // Update session with final HWND
            let mut sessions = state.sessions.lock().unwrap();
            if let Some(session) = sessions.get_mut(&session_id_clone) {
                session.hwnd = Some(SendHwnd(found_hwnd));
            }
        }
    });

    Ok(())
}

/// Resizes the embedded RDP window to match the new container coordinates
pub fn resize_rdp_embedded(
    session_id: &str,
    x: i32,
    y: i32,
    width: i32,
    height: i32,
    state: &RdpState,
) -> Result<(), String> {
    let sessions = state.sessions.lock().unwrap();
    if let Some(session) = sessions.get(session_id) {
        if let Some(hwnd) = session.hwnd {
            unsafe {
                SetWindowPos(
                    hwnd.0,
                    0 as HWND,
                    x,
                    y,
                    width,
                    height,
                    SWP_SHOWWINDOW | SWP_FRAMECHANGED,
                );
            }
        }
        Ok(())
    } else {
        Err("RDP Session not found".to_string())
    }
}

/// Closes the RDP session process and deletes the temporary files
pub fn disconnect_rdp_embedded(
    session_id: &str,
    state: &RdpState,
) -> Result<(), String> {
    let mut sessions = state.sessions.lock().unwrap();
    if let Some(session) = sessions.remove(session_id) {
        // Terminate the process
        let mut child = session.child;
        let _ = child.kill();
        
        // Remove credentials from credential manager if needed
        let target = format!("TERMSRV/{}", session.target_host);
        let _ = Command::new("cmdkey")
            .arg(format!("/delete:{}", target))
            .status();

        // Clean up temp RDP file
        let _ = std::fs::remove_file(session.rdp_file_path);
    }
    Ok(())
}
