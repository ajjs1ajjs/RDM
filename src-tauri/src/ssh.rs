use portable_pty::{native_pty_system, CommandBuilder, PtySize, MasterPty};
use std::collections::HashMap;
use std::io::{Read, Write};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;
use tauri::{AppHandle, Emitter, Manager};
use serde::{Serialize, Deserialize};
use std::path::PathBuf;

#[derive(Clone, Serialize)]
pub struct SshOutputPayload {
    pub session_id: String,
    pub data: String,
}

pub struct SshSession {
    pub writer: Arc<Mutex<Box<dyn std::io::Write + Send>>>,
    pub master: Arc<Mutex<Box<dyn MasterPty + Send>>>,
    pub temp_key_path: Option<PathBuf>,
}

pub struct SshState {
    pub sessions: Mutex<HashMap<String, SshSession>>,
}

impl SshState {
    pub fn new() -> Self {
        Self {
            sessions: Mutex::new(HashMap::new()),
        }
    }
}

/// Connects to an SSH server using portable-pty and system ssh binary
pub fn connect_ssh(
    app: AppHandle,
    session_id: String,
    host: &str,
    port: u16,
    username: &str,
    password: Option<&str>,
    private_key: Option<&str>,
    passphrase: Option<&str>,
    cols: u32,
    rows: u32,
) -> Result<(), String> {
    let mut temp_key_path = None;
    let mut args = vec![
        "-o".to_string(), "StrictHostKeyChecking=no".to_string(),
        "-o".to_string(), "UserKnownHostsFile=/dev/null".to_string(),
        "-p".to_string(), port.to_string(),
    ];

    // If private key is provided, write to secure temp file
    if let Some(key_content) = private_key {
        let app_dir = app.path().app_data_dir().unwrap();
        let keys_dir = app_dir.join("temp_keys");
        std::fs::create_dir_all(&keys_dir).map_err(|e| format!("Failed to create temp key dir: {}", e))?;
        
        let key_file = keys_dir.join(format!("key_{}", session_id));
        std::fs::write(&key_file, key_content).map_err(|e| format!("Failed to write private key: {}", e))?;
        
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(&key_file, std::fs::Permissions::from_mode(0o600))
                .map_err(|e| format!("Failed to set permissions on key file: {}", e))?;
        }

        args.push("-i".to_string());
        args.push(key_file.to_string_lossy().to_string());
        temp_key_path = Some(key_file);
    }

    args.push(format!("{}@{}", username, host));

    // Open PTY
    let pty_system = native_pty_system();
    let pair = pty_system
        .openpty(PtySize {
            rows: rows as u16,
            cols: cols as u16,
            pixel_width: 0,
            pixel_height: 0,
        })
        .map_err(|e| format!("Failed to open PTY: {}", e))?;

    // Build SSH command (assumes ssh is in system path)
    let mut cmd = CommandBuilder::new("ssh");
    cmd.args(&args);

    // Spawn the SSH client process into the PTY
    let mut child = pair.slave.spawn_command(cmd)
        .map_err(|e| format!("Failed to spawn SSH process: {} (Is ssh installed?)", e))?;

    // Setup I/O
    let mut reader = pair.master.try_clone_reader()
        .map_err(|e| format!("Failed to clone PTY reader: {}", e))?;
    
    let writer = pair.master.take_writer()
        .map_err(|e| format!("Failed to take PTY writer: {}", e))?;

    let writer_arc = Arc::new(Mutex::new(writer));
    let writer_clone = Arc::clone(&writer_arc);
    let master_arc = Arc::new(Mutex::new(pair.master));

    let session_id_clone = session_id.clone();
    let password_clone = password.map(|s| s.to_string());
    let passphrase_clone = passphrase.map(|s| s.to_string());
    let app_clone = app.clone();
    let temp_key_path_clone = temp_key_path.clone();

    // Spawn reader thread
    thread::spawn(move || {
        let mut buf = [0u8; 8192];
        let mut password_sent = false;
        let mut passphrase_sent = false;
        let mut output_accumulated = String::new();

        loop {
            // Check if process has exited
            if let Ok(Some(_)) = child.try_wait() {
                break;
            }

            match reader.read(&mut buf) {
                Ok(0) => break, // EOF
                Ok(n) => {
                    let text = String::from_utf8_lossy(&buf[..n]).to_string();
                    
                    // Auto-fill password / passphrase if prompt is detected
                    if !password_sent || !passphrase_sent {
                        output_accumulated.push_str(&text);
                        let lower = output_accumulated.to_lowercase();
                        
                        if let Some(ref pass) = password_clone {
                            if !password_sent && (lower.contains("password:") || lower.contains("password for")) {
                                thread::sleep(Duration::from_millis(150)); // Wait for ssh to stabilize prompt
                                let mut wr = writer_clone.lock().unwrap();
                                let _ = write!(wr, "{}\n", pass);
                                password_sent = true;
                                output_accumulated.clear();
                            }
                        }

                        if let Some(ref phrase) = passphrase_clone {
                            if !passphrase_sent && (lower.contains("passphrase") || lower.contains("enter passphrase")) {
                                thread::sleep(Duration::from_millis(150));
                                let mut wr = writer_clone.lock().unwrap();
                                let _ = write!(wr, "{}\n", phrase);
                                passphrase_sent = true;
                                output_accumulated.clear();
                            }
                        }

                        if output_accumulated.len() > 1000 {
                            output_accumulated.drain(..500);
                        }
                    }

                    // Stream output to frontend
                    let payload = SshOutputPayload {
                        session_id: session_id_clone.clone(),
                        data: text,
                    };
                    let _ = app_clone.emit("ssh-output", &payload);
                }
                Err(_) => break, // Read error (e.g. process terminated)
            }
        }

        // Clean up
        let _ = app_clone.emit("ssh-closed", &session_id_clone);
        
        if let Some(path) = temp_key_path_clone {
            let _ = std::fs::remove_file(path);
        }

        if let Some(state) = app_clone.try_state::<SshState>() {
            let mut sessions = state.sessions.lock().unwrap();
            sessions.remove(&session_id_clone);
        }
    });

    // Store in global state
    if let Some(state) = app.try_state::<SshState>() {
        let mut sessions = state.sessions.lock().unwrap();
        sessions.insert(
            session_id,
            SshSession {
                writer: writer_arc,
                master: master_arc,
                temp_key_path,
            },
        );
    }

    Ok(())
}

/// Writes keypresses or commands to the active PTY session.
pub fn write_ssh_input(
    state: &SshState,
    session_id: &str,
    data: &str,
) -> Result<(), String> {
    let sessions = state.sessions.lock().unwrap();
    let session = sessions
        .get(session_id)
        .ok_or_else(|| format!("SSH Session not found: {}", session_id))?;

    let mut writer = session.writer.lock().unwrap();
    writer.write_all(data.as_bytes())
        .map_err(|e| format!("Failed to write to SSH session: {}", e))?;
    writer.flush()
        .map_err(|e| format!("Failed to flush SSH session: {}", e))
}

/// Resizes the PTY terminal window.
pub fn resize_ssh_pty(
    state: &SshState,
    session_id: &str,
    cols: u32,
    rows: u32,
) -> Result<(), String> {
    let sessions = state.sessions.lock().unwrap();
    let session = sessions
        .get(session_id)
        .ok_or_else(|| format!("SSH Session not found: {}", session_id))?;

    let master = session.master.lock().unwrap();
    master.resize(PtySize {
        rows: rows as u16,
        cols: cols as u16,
        pixel_width: 0,
        pixel_height: 0,
    })
    .map_err(|e| format!("Failed to resize PTY: {}", e))
}

/// Disconnects the SSH session.
pub fn disconnect_ssh_session(
    state: &SshState,
    session_id: &str,
) -> Result<(), String> {
    let mut sessions = state.sessions.lock().unwrap();
    if let Some(session) = sessions.remove(session_id) {
        // Drop the master pty handle, which automatically kills the spawned child process on Windows/Unix
        drop(session.master);
        drop(session.writer);
        if let Some(path) = session.temp_key_path {
            let _ = std::fs::remove_file(path);
        }
    }
    Ok(())
}
