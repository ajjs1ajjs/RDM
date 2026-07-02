use portable_pty::{native_pty_system, CommandBuilder, PtySize};
use std::io::Read;
use std::time::{Duration, Instant};
use std::path::PathBuf;

pub struct TempKeyGuard {
    pub path: Option<PathBuf>,
}

impl Drop for TempKeyGuard {
    fn drop(&mut self) {
        if let Some(ref path) = self.path {
            if path.exists() {
                // Overwrite with zeros before deletion
                if let Ok(mut f) = std::fs::File::create(path) {
                    use std::io::Write;
                    let _ = f.write_all(&[0u8; 4096]);
                }
                let _ = std::fs::remove_file(path);
            }
        }
    }
}

pub fn run_ssh_command_sync(
    app_data_dir: PathBuf,
    cmd_name: &str,
    args: &[String],
    password: Option<&str>,
    private_key: Option<&str>,
) -> Result<String, String> {
    // Clean up any stale temp keys from previous crashes
    let keys_dir = app_data_dir.join("temp_keys");
    let _ = std::fs::remove_dir_all(&keys_dir);
    let mut _key_guard = None;
    let mut actual_args = vec![];
    
    // We add common ssh options to prevent hanging on prompts
    if cmd_name == "ssh" || cmd_name == "scp" {
        let known_hosts = app_data_dir.join("known_hosts");
        actual_args.push("-o".to_string());
        actual_args.push("StrictHostKeyChecking=accept-new".to_string());
        actual_args.push("-o".to_string());
        actual_args.push(format!("UserKnownHostsFile={}", known_hosts.display()));
        actual_args.push("-o".to_string());
        actual_args.push("BatchMode=no".to_string());
    }

    if let Some(key_content) = private_key {
        let keys_dir = app_data_dir.join("temp_keys");
        std::fs::create_dir_all(&keys_dir).map_err(|e| format!("Failed to create temp key dir: {}", e))?;
        
        let key_file = keys_dir.join(format!("sftp_key_{}", uuid::Uuid::new_v4()));
        std::fs::write(&key_file, key_content).map_err(|e| format!("Failed to write private key: {}", e))?;
        
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(&key_file, std::fs::Permissions::from_mode(0o600))
                .map_err(|e| format!("Failed to set permissions on key file: {}", e))?;
        }

        actual_args.push("-i".to_string());
        actual_args.push(key_file.to_string_lossy().to_string());
        _key_guard = Some(TempKeyGuard { path: Some(key_file.clone()) });
    }

    actual_args.extend(args.iter().cloned());

    let pty_system = native_pty_system();
    let pair = pty_system
        .openpty(PtySize {
            rows: 24,
            cols: 80,
            pixel_width: 0,
            pixel_height: 0,
        })
        .map_err(|e| format!("Failed to open PTY: {}", e))?;

    let mut cmd = CommandBuilder::new(cmd_name);
    cmd.args(&actual_args);

    let mut child = pair.slave.spawn_command(cmd)
        .map_err(|e| format!("Failed to spawn process: {}", e))?;

    let mut reader = pair.master.try_clone_reader()
        .map_err(|e| format!("Failed to clone PTY reader: {}", e))?;
    
    let mut writer = pair.master.take_writer()
        .map_err(|e| format!("Failed to take PTY writer: {}", e))?;

    let mut output = String::new();
    let mut password_sent = false;
    let mut buf = [0u8; 1024];

    let start = Instant::now();
    let timeout = Duration::from_secs(30);

    // Use a separate thread to read from PTY so we don't block forever if it hangs
    let (tx, rx) = std::sync::mpsc::channel();
    std::thread::spawn(move || {
        loop {
            match reader.read(&mut buf) {
                Ok(n) if n > 0 => {
                    let chunk = String::from_utf8_lossy(&buf[..n]).to_string();
                    if tx.send(chunk).is_err() {
                        break;
                    }
                }
                _ => break,
            }
        }
    });

    loop {
        if start.elapsed() > timeout {
            let _ = child.kill();
            return Err("Command timed out".to_string());
        }

        if let Ok(chunk) = rx.recv_timeout(Duration::from_millis(50)) {
            output.push_str(&chunk);
            
            if !password_sent {
                let lower_out = output.to_lowercase();
                if lower_out.contains("password:") || lower_out.contains("passphrase") {
                    if let Some(pw) = password {
                        let _ = writer.write_all(format!("{}\n", pw).as_bytes());
                        password_sent = true;
                    }
                }
            }
        }

        if let Ok(Some(status)) = child.try_wait() {
            // Drain remaining output
            while let Ok(chunk) = rx.try_recv() {
                output.push_str(&chunk);
            }
            if status.success() {
                return Ok(output);
            } else {
                return Err(format!("Command failed with exit code: {:?}. Output: {}", status.exit_code(), output));
            }
        }
    }
}
