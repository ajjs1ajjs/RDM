use std::process::Command;

/// Launches the native Windows Remote Desktop client (mstsc) pointing to the specified host and port.
pub fn launch_rdp_session(host: &str, port: u32, fullscreen: bool) -> Result<(), String> {
    let connection_string = if port == 3389 {
        host.to_string()
    } else {
        format!("{}:{}", host, port)
    };

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
