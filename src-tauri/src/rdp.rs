use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, mpsc};
use std::net::TcpStream;
use std::time::Duration;
use tauri::{Manager, Emitter};
use serde::Serialize;
use base64::Engine;
// No image crate imports needed since we use raw RGBA pixels

// Import rdp-rs-2 types
use rdp::core::client::Connector;
use rdp::core::event::{RdpEvent, PointerEvent, PointerButton, KeyboardEvent};

pub struct RdpSession {
    pub target_host: String,
    pub server_id: Option<String>,
    pub width: i32,
    pub height: i32,
    pub running: Arc<AtomicBool>,
    pub tx_event: std::sync::mpsc::Sender<RdpEvent>,
}

#[derive(Clone, Serialize)]
pub struct RdpFramePayload {
    pub session_id: String,
    pub data: String,
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

fn log_debug(app_data_dir: &std::path::Path, message: &str) {
    let log_file = app_data_dir.join("rdp_debug.log");
    let line = format!("{}\n", message);
    let _ = std::fs::OpenOptions::new()
        .create(true).append(true)
        .open(log_file)
        .and_then(|mut f| std::io::Write::write_all(&mut f, line.as_bytes()));
}

fn vk_to_scancode(vk: u16) -> u16 {
    match vk {
        0x08 => 0x0E, // Backspace
        0x09 => 0x0F, // Tab
        0x0D => 0x1C, // Enter / Return
        0x10 | 0xA0 | 0xA1 => 0x2A, // Shift (Left shift)
        0x11 | 0xA2 | 0xA3 => 0x1D, // Control (Left control)
        0x12 | 0xA4 | 0xA5 => 0x38, // Alt (Left menu)
        0x14 => 0x3A, // Caps Lock
        0x1B => 0x01, // Escape
        0x20 => 0x39, // Space
        0x21 => 0x49, // Page Up
        0x22 => 0x51, // Page Down
        0x23 => 0x4F, // End
        0x24 => 0x47, // Home
        0x25 => 0x4B, // Left Arrow
        0x26 => 0x48, // Up Arrow
        0x27 => 0x4D, // Right Arrow
        0x28 => 0x50, // Down Arrow
        0x2D => 0x52, // Insert
        0x2E => 0x53, // Delete
        
        // Number keys
        0x30 => 0x0B, // 0
        0x31 => 0x02, // 1
        0x32 => 0x03, // 2
        0x33 => 0x04, // 3
        0x34 => 0x05, // 4
        0x35 => 0x06, // 5
        0x36 => 0x07, // 6
        0x37 => 0x08, // 7
        0x38 => 0x09, // 8
        0x39 => 0x0A, // 9
        
        // Letter keys (A-Z)
        0x41 => 0x1E, // A
        0x42 => 0x30, // B
        0x43 => 0x2E, // C
        0x44 => 0x20, // D
        0x45 => 0x12, // E
        0x46 => 0x21, // F
        0x47 => 0x22, // G
        0x48 => 0x23, // H
        0x49 => 0x17, // I
        0x4A => 0x24, // J
        0x4B => 0x25, // K
        0x4C => 0x26, // L
        0x4D => 0x32, // M
        0x4E => 0x31, // N
        0x50 => 0x19, // P
        0x4F => 0x18, // O
        0x51 => 0x10, // Q
        0x52 => 0x13, // R
        0x53 => 0x1F, // S
        0x54 => 0x14, // T
        0x55 => 0x16, // U
        0x56 => 0x2F, // V
        0x57 => 0x11, // W
        0x58 => 0x2D, // X
        0x59 => 0x15, // Y
        0x5A => 0x2C, // Z
        
        // F keys
        0x70 => 0x3B, // F1
        0x71 => 0x3C, // F2
        0x72 => 0x3D, // F3
        0x73 => 0x3E, // F4
        0x74 => 0x3F, // F5
        0x75 => 0x40, // F6
        0x76 => 0x41, // F7
        0x77 => 0x42, // F8
        0x78 => 0x43, // F9
        0x79 => 0x44, // F10
        0x7A => 0x57, // F11
        0x7B => 0x58, // F12
        
        // Punctuation / symbols
        0xBA => 0x27, // Semicolon
        0xBB => 0x0D, // Equal
        0xBC => 0x33, // Comma
        0xBD => 0x0C, // Minus
        0xBE => 0x34, // Period
        0xBF => 0x35, // Slash
        0xC0 => 0x29, // Backquote
        0xDB => 0x1A, // Left Bracket
        0xDC => 0x2B, // Backslash
        0xDD => 0x1B, // Right Bracket
        0xDE => 0x28, // Quote
        
        _ => 0,
    }
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
    _parent_hwnd: windows::Win32::Foundation::HWND,
    width: i32,
    height: i32,
    _device_pixel_ratio: f64,
    _app_data_dir: PathBuf,
    app: tauri::AppHandle,
    server_id: Option<String>,
    _rdp_clipboard: bool,
    _rdp_drives: bool,
    _rdp_printers: bool,
    _rdp_smart_sizing: bool,
    _rdp_audio: u32,
    _rdp_smartcards: bool,
    _rdp_webauthn: bool,
) -> Result<(), String> {
    let port = if port == 0 { 3389 } else { port };
    let host_addr = format!("{}:{}", host, port);

    let username_str = username.unwrap_or("").to_string();
    let password_str = password.unwrap_or("").to_string();
    let hostname = host.to_string();

    let (tx_event, rx_event) = std::sync::mpsc::channel::<RdpEvent>();
    let running = Arc::new(AtomicBool::new(true));

    {
        let state = app.state::<RdpState>();
        let mut sessions = state.sessions.lock().map_err(|e| e.to_string())?;
        sessions.insert(
            session_id.clone(),
            RdpSession {
                target_host: host.to_string(),
                server_id: server_id.clone(),
                width,
                height,
                running: running.clone(),
                tx_event: tx_event.clone(),
            },
        );
    }

    let app_clone = app.clone();
    let session_id_clone = session_id.clone();
    let running_clone = running.clone();

    std::thread::spawn(move || {
        let app_data_dir = app_clone.path().app_data_dir().unwrap_or_default();
        log_debug(&app_data_dir, &format!("Connecting pure Rust RDP client to {}", host_addr));

        // Parse domain from username if present (e.g. "DOMAIN\user")
        let mut domain_str = String::new();
        let mut username_clean = username_str.clone();
        if let Some(pos) = username_clean.find('\\') {
            domain_str = username_clean[..pos].to_string();
            username_clean = username_clean[pos + 1..].to_string();
        }

        // Determine NLA preference: try NLA first if we have credentials
        let prefer_nla = !username_clean.is_empty();
        log_debug(&app_data_dir, &format!("RDP client options: domain='{}', username='{}', prefer_nla={}", domain_str, username_clean, prefer_nla));

        // Retry loop: try 4 combinations (NLA on/off × cert check on/off)
        // to handle servers with self-signed certs or non-standard NLA config
        let mut phase = 0u32;
        let (mut client, socket_control) = loop {
            let (use_nla, check_cert) = match phase {
                0 => (prefer_nla, true),
                1 => (prefer_nla, false),
                2 => (!prefer_nla, true),
                3 => (!prefer_nla, false),
                _ => {
                    log_debug(&app_data_dir, "Embedded RDP failed after all retries, auto-launching external mstsc.exe...");
                    let _ = app_clone.emit("rdp-closed", &session_id_clone);

                    let rdp_sessions_dir = app_data_dir.join("rdp_sessions");
                    let _ = std::fs::create_dir_all(&rdp_sessions_dir);
                    let file_name = format!("session_auto-{}-{}.rdp", server_id.clone().unwrap_or_default(), uuid::Uuid::new_v4());
                    let rdp_file_path = rdp_sessions_dir.join(file_name);
                    let user_line = if username_str.is_empty() { String::new() } else { format!("username:s:{}\r\n", username_str) };
                    let rdp_content = format!(
                        "full address:s:{}\r\n{}\
                         screen mode id:i:1\r\nsmart sizing:i:1\r\n\
                         dynamic resolution:i:1\r\nredirectclipboard:i:1\r\n\
                         authentication level:i:0\r\ndisplayconnectionbar:i:1\r\n",
                        host_addr, user_line
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
                    let _ = std::fs::write(&rdp_file_path, rdp_content_bytes);
                    let _ = std::process::Command::new("mstsc")
                        .arg(rdp_file_path.to_string_lossy().to_string())
                        .spawn();
                    return;
                }
            };
            phase += 1;

            let tcp = match TcpStream::connect(&host_addr) {
                Ok(s) => s,
                Err(e) => {
                    log_debug(&app_data_dir, &format!("Failed to connect TCP: {:?}", e));
                    continue;
                }
            };
            let _ = tcp.set_read_timeout(Some(Duration::from_secs(10)));
            let _ = tcp.set_write_timeout(Some(Duration::from_secs(10)));

            let socket_control = match tcp.try_clone() {
                Ok(s) => s,
                Err(e) => {
                    log_debug(&app_data_dir, &format!("Failed to clone socket: {:?}", e));
                    continue;
                }
            };

            log_debug(&app_data_dir, &format!("Attempting RDP with use_nla={}, check_cert={}", use_nla, check_cert));
            let mut connector = Connector::new()
                .screen(width as u16, height as u16)
                .credentials(domain_str.clone(), username_clean.clone(), password_str.clone())
                .check_certificate(check_cert)
                .use_nla(use_nla)
                .hostname(hostname.clone());

            // Run connect on a separate thread with a 20-second timeout
            // to avoid hangs with SChannel + danger_accept_invalid_certs
            let (tx_connect, rx_connect) = mpsc::channel();
            std::thread::spawn(move || {
                let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    connector.connect(tcp)
                }));
                let _ = tx_connect.send(match result {
                    Ok(Ok(c)) => Ok(c),
                    Ok(Err(e)) => Err(e),
                    Err(panic_payload) => {
                        let msg = if let Some(s) = panic_payload.downcast_ref::<&str>() {
                            s.to_string()
                        } else if let Some(s) = panic_payload.downcast_ref::<String>() {
                            s.clone()
                        } else {
                            "unknown panic".to_string()
                        };
                        Err(rdp::model::error::Error::RdpError(rdp::model::error::RdpError::new(
                            rdp::model::error::RdpErrorKind::ProtocolNegFailure,
                            &format!("Panic: {}", msg)
                        )))
                    }
                });
            });

            match rx_connect.recv_timeout(Duration::from_secs(20)) {
                Ok(Ok(c)) => break (c, socket_control),
                Ok(Err(e)) => {
                    log_debug(&app_data_dir, &format!("RDP connection with use_nla={}, check_cert={} failed: {:?}", use_nla, check_cert, e));
                    continue;
                }
                Err(mpsc::RecvTimeoutError::Timeout) => {
                    log_debug(&app_data_dir, &format!("RDP connection with use_nla={}, check_cert={} timed out after 20s", use_nla, check_cert));
                    continue;
                }
                Err(mpsc::RecvTimeoutError::Disconnected) => {
                    log_debug(&app_data_dir, &format!("RDP connection with use_nla={}, check_cert={} thread panicked", use_nla, check_cert));
                    continue;
                }
            }
        };

        log_debug(&app_data_dir, "RDP Handshake completed successfully!");

        if let Err(e) = socket_control.set_read_timeout(Some(Duration::from_millis(1))) {
            log_debug(&app_data_dir, &format!("Failed to set socket read timeout: {:?}", e));
        }

        // Spawn a background thread to handle JPEG compression and Tauri emission.
        // This offloads heavy work from the RDP network read thread, avoiding TCP backlog.
        let (tx_frame, rx_frame) = mpsc::channel::<(usize, usize, i32, i32, Vec<u8>)>();
        let app_worker = app_clone.clone();
        let session_id_worker = session_id_clone.clone();
        let running_worker = running_clone.clone();
        
        std::thread::spawn(move || {
            while running_worker.load(Ordering::SeqCst) {
                match rx_frame.recv_timeout(Duration::from_millis(100)) {
                    Ok((rect_w, rect_h, dest_left, dest_top, mut decompressed_data)) => {
                        // Ensure opaque alpha; BGRA→RGBA conversion is done on frontend via canvas
                        let count = (rect_w * rect_h * 4).min(decompressed_data.len());
                        let mut i = 3;
                        while i < count {
                            decompressed_data[i] = 255;
                            i += 4;
                        }

                        let encoded = base64::engine::general_purpose::STANDARD.encode(&decompressed_data);
                        let payload = RdpFramePayload {
                            session_id: session_id_worker.clone(),
                            data: encoded,
                            x: dest_left,
                            y: dest_top,
                            width: rect_w as i32,
                            height: rect_h as i32,
                        };
                        let _ = app_worker.emit("rdp-frame", &payload);
                    }
                    Err(mpsc::RecvTimeoutError::Timeout) => {}
                    Err(mpsc::RecvTimeoutError::Disconnected) => break,
                }
            }
        });

        while running_clone.load(Ordering::SeqCst) {
            // A. Process outgoing input events
            while let Ok(event) = rx_event.try_recv() {
                if let Err(e) = client.write(event) {
                    log_debug(&app_data_dir, &format!("Failed to write event to RDP client: {:?}", e));
                }
            }

            // B. Read incoming display updates
            let read_ok = client.read(|event| {
                match event {
                    RdpEvent::Bitmap(bitmap) => {
                        let rect_w = bitmap.width as usize;
                        let rect_h = bitmap.height as usize;
                        let dest_left = bitmap.dest_left as i32;
                        let dest_top = bitmap.dest_top as i32;

                        if rect_w == 0 || rect_h == 0 {
                            return;
                        }

                        if let Ok(decompressed_data) = bitmap.decompress() {
                            let _ = tx_frame.send((rect_w, rect_h, dest_left, dest_top, decompressed_data));
                        }
                    }
                    _ => {}
                }
            });

            // C. Handle error/timeout
            let mut did_timeout = false;
            match read_ok {
                Ok(_) => {}
                Err(rdp::model::error::Error::Io(ref e)) 
                    if e.kind() == std::io::ErrorKind::WouldBlock || e.kind() == std::io::ErrorKind::TimedOut => {
                    did_timeout = true;
                }
                Err(e) => {
                    log_debug(&app_data_dir, &format!("Error in client read loop: {:?}", e));
                    break;
                }
            }
 
            if did_timeout {
                std::thread::sleep(Duration::from_millis(1));
            }
        }

        log_debug(&app_data_dir, &format!("Cleaning up session {}", session_id_clone));
        {
            let state = app_clone.state::<RdpState>();
            if let Ok(mut sessions) = state.sessions.lock() {
                sessions.remove(&session_id_clone);
            };
        }

        if let Some(ref srv_id) = server_id {
            if let Some(db_state) = app_clone.try_state::<crate::DbState>() {
                if let Ok(conn) = db_state.conn.lock() {
                    let hist = crate::db::ConnectionHistory {
                        id: uuid::Uuid::new_v4().to_string(),
                        server_id: srv_id.clone(),
                        timestamp: String::new(),
                        status: "disconnected".to_string(),
                        log: "Embedded pure Rust RDP session disconnected".to_string(),
                    };
                    let _ = crate::db::add_history(&conn, &hist);
                }
            }
        }

        let _ = app_clone.emit("rdp-closed", &session_id_clone);
    });

    Ok(())
}

pub fn resize_rdp_embedded(
    session_id: &str,
    width: i32,
    height: i32,
    _device_pixel_ratio: f64,
    app: &tauri::AppHandle,
    _state: &RdpState,
) -> Result<(), String> {
    let app_data_dir = app.path().app_data_dir().unwrap_or_default();
    log_debug(&app_data_dir, &format!("resize_rdp_embedded called for {} to {}x{} (ignored)", session_id, width, height));
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
    let tx_event = {
        let sessions = state.sessions.lock().map_err(|e| e.to_string())?;
        sessions.get(session_id)
            .ok_or_else(|| "RDP session not found".to_string())?
            .tx_event
            .clone()
    };

    let p_button = match button {
        "left" => PointerButton::Left,
        "right" => PointerButton::Right,
        "middle" => PointerButton::Middle,
        _ => PointerButton::None,
    };

    let down = match action {
        "down" => true,
        "up" => false,
        _ => false,
    };

    let event = RdpEvent::Pointer(PointerEvent {
        x: x as u16,
        y: y as u16,
        button: p_button,
        down,
    });

    let _ = tx_event.send(event);
    Ok(())
}

pub fn send_rdp_key(session_id: &str, vk: u16, key_up: bool, state: &RdpState) -> Result<(), String> {
    let tx_event = {
        let sessions = state.sessions.lock().map_err(|e| e.to_string())?;
        sessions.get(session_id)
            .ok_or_else(|| "RDP session not found".to_string())?
            .tx_event
            .clone()
    };

    let scancode = vk_to_scancode(vk);
    if scancode != 0 {
        let event = RdpEvent::Key(KeyboardEvent {
            code: scancode,
            down: !key_up,
        });
        let _ = tx_event.send(event);
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

    let mut sessions = state.sessions.lock().map_err(|e| e.to_string())?;
    if let Some(session) = sessions.remove(session_id) {
        session.running.store(false, Ordering::SeqCst);
    }
    Ok(())
}
