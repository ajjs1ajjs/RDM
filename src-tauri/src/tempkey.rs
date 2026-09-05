use std::path::PathBuf;

/// Zeroizes a temporary SSH key file before deleting it, so key material does
/// not linger on disk. Shared by the SSH and SFTP backends.
pub struct TempKeyGuard {
    pub path: Option<PathBuf>,
}

impl Drop for TempKeyGuard {
    fn drop(&mut self) {
        if let Some(ref path) = self.path {
            if path.exists() {
                if let Ok(mut f) = std::fs::File::create(path) {
                    use std::io::Write;
                    let _ = f.write_all(&[0u8; 4096]);
                }
                let _ = std::fs::remove_file(path);
            }
        }
    }
}
