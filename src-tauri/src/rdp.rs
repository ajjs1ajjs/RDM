use std::process::Command;

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

    // If username and password are provided, register credentials with Windows Credential Manager via cmdkey
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

    // Launch mstsc as a detached process
    Command::new("mstsc")
        .args(&args)
        .spawn()
        .map(|_| ())
        .map_err(|e| format!("Failed to spawn mstsc process: {}", e))
}
