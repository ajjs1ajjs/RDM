use portable_pty::{native_pty_system, CommandBuilder, PtySize};
use std::io::Read;
use std::time::{Duration, Instant};
use std::path::PathBuf;

pub use crate::tempkey::TempKeyGuard;

/// Validates hostname per RFC 1123 / RFC 952
fn validate_hostname(host: &str) -> Result<(), String> {
    if host.is_empty() || host.len() > 253 {
        return Err("Invalid hostname: length".into());
    }
    for label in host.split('.') {
        if label.is_empty() || label.len() > 63 {
            return Err("Invalid hostname: label length".into());
        }
        if !label.chars().all(|c| c.is_ascii_alphanumeric() || c == '-') {
            return Err("Invalid hostname: invalid character".into());
        }
        if label.starts_with('-') || label.ends_with('-') {
            return Err("Invalid hostname: hyphen position".into());
        }
    }
    Ok(())
}

/// Validates username per POSIX (conservative subset)
fn validate_username(user: &str) -> Result<(), String> {
    if user.is_empty() || user.len() > 32 {
        return Err("Invalid username: length".into());
    }
    if !user.chars().all(|c| c.is_ascii_alphanumeric() || matches!(c, '_' | '-' | '.')) {
        return Err("Invalid username: invalid character".into());
    }
    Ok(())
}

pub fn run_ssh_command_sync(
    app_data_dir: PathBuf,
    cmd_name: &str,
    args: &[String],
    password: Option<&str>,
    private_key: Option<&str>,
    passphrase: Option<&str>,
) -> Result<String, String> {
    // args[0] should be "user@host" - validate host and username parts
    if let Some(first_arg) = args.first() {
        if let Some(at_pos) = first_arg.find('@') {
            let username = &first_arg[..at_pos];
            let host = &first_arg[at_pos + 1..];
            validate_username(username)?;
            validate_hostname(host)?;
        }
    }

    let keys_dir = app_data_dir.join("temp_keys");
    let mut _key_guard = None;
    let mut actual_args = vec![];

    if cmd_name == "ssh" || cmd_name == "scp" {
        let known_hosts = app_data_dir.join("known_hosts");
        actual_args.push("-o".to_string());
        actual_args.push("StrictHostKeyChecking=ask".to_string());
        actual_args.push("-o".to_string());
        actual_args.push(format!("UserKnownHostsFile={}", known_hosts.display()));
        actual_args.push("-o".to_string());
        actual_args.push("BatchMode=no".to_string());
    }

    if let Some(key_content) = private_key {
        std::fs::create_dir_all(&keys_dir).map_err(|e| format!("Failed to create temp key dir: {}", e))?;

        let key_file = keys_dir.join(format!("sftp_key_{}", uuid::Uuid::new_v4()));
        std::fs::write(&key_file, key_content).map_err(|e| format!("Failed to write private key: {}", e))?;

        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(&key_file, std::fs::Permissions::from_mode(0o600))
                .map_err(|e| format!("Failed to set permissions on key file: {}", e))?;
        }
        #[cfg(windows)]
        {
            let _ = std::process::Command::new("icacls")
                .args(&[
                    key_file.to_string_lossy().as_ref(),
                    "/inheritance:r",
                    "/grant:r",
                    &format!("{}:(R,W)", std::env::var("USERNAME").unwrap_or_default()),
                ])
                .output();
        }

        actual_args.push("-i".to_string());
        actual_args.push(key_file.to_string_lossy().to_string());
        _key_guard = Some(TempKeyGuard { path: Some(key_file.clone()) });
    }

    if password.is_some() || passphrase.is_some() {
        actual_args.push("-tt".to_string());
    }

    actual_args.extend(args.iter().cloned());

    let pty_system = native_pty_system();
    let pair = pty_system
        .openpty(PtySize { rows: 24, cols: 80, pixel_width: 0, pixel_height: 0 })
        .map_err(|e| format!("Failed to open PTY: {}", e))?;

    let mut cmd = CommandBuilder::new(cmd_name);
    cmd.args(&actual_args);

    if let Ok(home) = std::env::var("USERPROFILE").or_else(|_| std::env::var("HOME")) {
        cmd.env("HOME", &home);
    }

    let mut child = pair.slave.spawn_command(cmd)
        .map_err(|e| format!("Failed to spawn process: {}", e))?;

    let mut reader = pair.master.try_clone_reader()
        .map_err(|e| format!("Failed to clone PTY reader: {}", e))?;

    let mut writer = pair.master.take_writer()
        .map_err(|e| format!("Failed to take PTY writer: {}", e))?;

    let mut output = String::new();
    let mut password_sent = false;
    let mut passphrase_sent = false;
    let mut buf = [0u8; 1024];

    let start = Instant::now();
    let timeout = Duration::from_secs(30);

    let (tx, rx) = std::sync::mpsc::channel();
    std::thread::spawn(move || {
        loop {
            match reader.read(&mut buf) {
                Ok(n) if n > 0 => {
                    let chunk = String::from_utf8_lossy(&buf[..n]).to_string();
                    if tx.send(chunk).is_err() { break; }
                }
                _ => break,
            }
        }
    });

    // RES-001 / PERF-001: bounded output + guaranteed child kill on timeout.
    const MAX_OUTPUT_BYTES: usize = 512 * 1024;
    loop {
        if start.elapsed() > timeout {
            let _ = child.kill();
            drop(writer);
            return Err("Command timed out after 30s".to_string());
        }

        if let Ok(chunk) = rx.recv_timeout(Duration::from_millis(50)) {
            if output.len() + chunk.len() > MAX_OUTPUT_BYTES {
                let _ = child.kill();
                return Err("Command output exceeded 512KB limit".to_string());
            }
            output.push_str(&chunk);
            // Only scan the tail for prompts to keep this O(1) per chunk.
            let tail_start = output.len().saturating_sub(2000);
            let lower_out = output[tail_start..].to_lowercase();

            if !password_sent {
                if let Some(pw) = password {
                    if lower_out.contains("password:") || lower_out.contains("'s password:") || lower_out.contains("пароль:") {
                        let _ = writer.write_all(format!("{}\n", pw).as_bytes());
                        password_sent = true;
                    }
                }
            }

            if !passphrase_sent {
                if let Some(pp) = passphrase {
                    if lower_out.contains("passphrase:") || lower_out.contains("enter passphrase") {
                        let _ = writer.write_all(format!("{}\n", pp).as_bytes());
                        passphrase_sent = true;
                    }
                }
            }
        }

        if let Ok(Some(status)) = child.try_wait() {
            while let Ok(chunk) = rx.try_recv() {
                if output.len() + chunk.len() > MAX_OUTPUT_BYTES {
                    return Err("Command output exceeded 512KB limit".to_string());
                }
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sftp_validation_rejects_injection() {
        assert!(validate_hostname("host -oProxyCommand=x").is_err());
        assert!(validate_username("user;id").is_err());
        assert!(validate_hostname("ok.example.com").is_ok());
        assert!(validate_username("deploy").is_ok());
    }
}
