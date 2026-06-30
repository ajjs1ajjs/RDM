mod crypto;
mod db;
mod rdp;
mod ssh;
mod sftp;

pub use windows_core;

use std::sync::Mutex;
use tauri::{AppHandle, Manager, State};
use uuid::Uuid;
use rand::{thread_rng, RngCore};
use serde::Deserialize;

// State definitions
pub struct DbState {
    pub conn: Mutex<rusqlite::Connection>,
}

pub struct SessionState {
    pub kek: Mutex<Option<[u8; 32]>>,
}

impl SessionState {
    pub fn new() -> Self {
        Self {
            kek: Mutex::new(None),
        }
    }
}

// Commands implementation
#[tauri::command]
fn is_vault_setup(db: State<'_, DbState>) -> Result<bool, String> {
    let conn = db.conn.lock().unwrap();
    let sentinel = db::get_setting(&conn, "sentinel")?;
    Ok(sentinel.is_some())
}

#[tauri::command]
fn get_setting(key: String, db: State<'_, DbState>) -> Result<Option<String>, String> {
    let conn = db.conn.lock().unwrap();
    db::get_setting(&conn, &key)
}

#[tauri::command]
fn set_setting(key: String, value: String, db: State<'_, DbState>) -> Result<(), String> {
    let conn = db.conn.lock().unwrap();
    db::set_setting(&conn, &key, &value)
}

#[tauri::command]
fn is_vault_unlocked(state: State<'_, SessionState>) -> Result<bool, String> {
    let kek = state.kek.lock().unwrap();
    Ok(kek.is_some())
}

fn setup_master_password_impl(
    conn: &rusqlite::Connection,
    session_kek_mutex: &Mutex<Option<[u8; 32]>>,
    password: &str,
) -> Result<(), String> {
    // Check if already setup
    if db::get_setting(conn, "sentinel")?.is_some() {
        return Err("Vault is already initialized".to_string());
    }

    // Generate random 16-byte salt
    let mut salt = [0u8; 16];
    thread_rng().fill_bytes(&mut salt);
    let salt_hex = hex::encode(salt);

    // Derive Key Encryption Key (KEK)
    let kek = crypto::derive_key(password, &salt)?;

    // Encrypt authentication sentinel
    let sentinel_plaintext = "rdm-auth-sentinel";
    let encrypted_sentinel = crypto::encrypt_secret(&kek, sentinel_plaintext)?;
    let sentinel_json = serde_json::to_string(&encrypted_sentinel)
        .map_err(|e| format!("Failed to serialize sentinel: {}", e))?;

    // Store salt and sentinel in db
    db::set_setting(conn, "salt", &salt_hex)?;
    db::set_setting(conn, "sentinel", &sentinel_json)?;

    // Store KEK in session state
    let mut session_kek = session_kek_mutex.lock().unwrap();
    *session_kek = Some(kek);

    Ok(())
}

fn unlock_vault_impl(
    conn: &rusqlite::Connection,
    session_kek_mutex: &Mutex<Option<[u8; 32]>>,
    password: &str,
) -> Result<bool, String> {
    let salt_hex = db::get_setting(conn, "salt")?
        .ok_or_else(|| "Vault has not been initialized yet".to_string())?;
    
    let sentinel_json = db::get_setting(conn, "sentinel")?
        .ok_or_else(|| "Vault sentinel not found".to_string())?;

    let salt = hex::decode(&salt_hex)
        .map_err(|e| format!("Invalid salt encoding: {}", e))?;

    let kek = crypto::derive_key(password, &salt)?;

    let encrypted_sentinel: crypto::EncryptedData = serde_json::from_str(&sentinel_json)
        .map_err(|e| format!("Failed to parse sentinel data: {}", e))?;

    // Try to decrypt sentinel
    match crypto::decrypt_secret(&kek, &encrypted_sentinel) {
        Ok(decrypted) if decrypted == "rdm-auth-sentinel" => {
            // Save KEK in session state
            let mut session_kek = session_kek_mutex.lock().unwrap();
            *session_kek = Some(kek);
            Ok(true)
        }
        _ => Ok(false), // Incorrect password
    }
}

#[tauri::command]
fn setup_master_password(
    password: String,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let conn = db.conn.lock().unwrap();
    setup_master_password_impl(&conn, &state.kek, &password)
}

#[tauri::command]
fn unlock_vault(
    password: String,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<bool, String> {
    let conn = db.conn.lock().unwrap();
    unlock_vault_impl(&conn, &state.kek, &password)
}

#[tauri::command]
fn lock_vault(state: State<'_, SessionState>) -> Result<(), String> {
    let mut session_kek = state.kek.lock().unwrap();
    if let Some(mut key) = session_kek.take() {
        // Zero out the key in memory
        key.iter_mut().for_each(|x| *x = 0);
    }
    Ok(())
}

// Credentials commands
#[tauri::command]
fn get_credentials(
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<Vec<db::Credential>, String> {
    // Require vault to be unlocked
    let kek = state.kek.lock().unwrap();
    if kek.is_none() {
        return Err("Vault is locked".to_string());
    }

    let conn = db.conn.lock().unwrap();
    db::get_credentials(&conn)
}

#[tauri::command]
fn add_credential(
    name: String,
    cred_type: String,
    username: String,
    secret: String,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let kek_guard = state.kek.lock().unwrap();
    let kek = kek_guard.ok_or_else(|| "Vault is locked".to_string())?;

    let encrypted = crypto::encrypt_secret(&kek, &secret)?;
    let encrypted_json = serde_json::to_string(&encrypted)
        .map_err(|e| format!("Failed to serialize credential: {}", e))?;

    let cred = db::Credential {
        id: Uuid::new_v4().to_string(),
        name,
        r#type: cred_type,
        username,
        encrypted_secret: encrypted_json,
        created_at: String::new(),
        updated_at: String::new(),
    };

    let conn = db.conn.lock().unwrap();
    db::add_credential(&conn, &cred)
}

#[tauri::command]
fn update_credential(
    id: String,
    name: String,
    cred_type: String,
    username: String,
    secret: Option<String>,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let kek_guard = state.kek.lock().unwrap();
    let kek = kek_guard.ok_or_else(|| "Vault is locked".to_string())?;

    let conn = db.conn.lock().unwrap();

    let encrypted_json = if let Some(sec_val) = secret {
        let encrypted = crypto::encrypt_secret(&kek, &sec_val)?;
        serde_json::to_string(&encrypted)
            .map_err(|e| format!("Failed to serialize credential: {}", e))?
    } else {
        // Keep existing secret
        let list = db::get_credentials(&conn)?;
        let existing = list.iter().find(|c| c.id == id)
            .ok_or_else(|| "Credential not found".to_string())?;
        existing.encrypted_secret.clone()
    };

    let cred = db::Credential {
        id,
        name,
        r#type: cred_type,
        username,
        encrypted_secret: encrypted_json,
        created_at: String::new(),
        updated_at: String::new(),
    };

    db::update_credential(&conn, &cred)
}

#[tauri::command]
fn delete_credential(
    id: String,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let kek = state.kek.lock().unwrap();
    if kek.is_none() {
        return Err("Vault is locked".to_string());
    }

    let conn = db.conn.lock().unwrap();
    db::delete_credential(&conn, &id)
}

#[tauri::command]
fn decrypt_credential_secret(
    id: String,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<String, String> {
    let kek_guard = state.kek.lock().unwrap();
    let kek = kek_guard.ok_or_else(|| "Vault is locked".to_string())?;

    let conn = db.conn.lock().unwrap();
    let list = db::get_credentials(&conn)?;
    let cred = list.iter().find(|c| c.id == id)
        .ok_or_else(|| "Credential not found".to_string())?;

    let encrypted: crypto::EncryptedData = serde_json::from_str(&cred.encrypted_secret)
        .map_err(|e| format!("Failed to parse credential secret: {}", e))?;

    crypto::decrypt_secret(&kek, &encrypted)
        .map_err(|_| "Decryption error. Since the vault login password was disabled, previously saved credentials cannot be decrypted. Please edit the linked credential in the Vault and enter the password again. (Помилка розшифрування. Оскільки пароль на вхід був вимкнений, старі збережені облікові дані не можуть бути розшифровані. Будь ласка, відредагуйте пов'язаний запис у сейфі та введіть пароль заново.)".to_string())
}

#[tauri::command]
fn decrypt_server_password(
    id: String,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<String, String> {
    let kek_guard = state.kek.lock().unwrap();
    let kek = kek_guard.ok_or_else(|| "Vault is locked".to_string())?;

    let conn = db.conn.lock().unwrap();
    let list = db::get_servers(&conn)?;
    let srv = list.iter().find(|s| s.id == id)
        .ok_or_else(|| "Server not found".to_string())?;

    let encrypted_json = srv.encrypted_password.as_ref()
        .ok_or_else(|| "No manual password configured".to_string())?;

    let encrypted: crypto::EncryptedData = serde_json::from_str(encrypted_json)
        .map_err(|e| format!("Failed to parse encrypted password: {}", e))?;

    crypto::decrypt_secret(&kek, &encrypted)
        .map_err(|_| "Decryption error. Since the vault login password was disabled, previously saved passwords cannot be decrypted. Please edit this connection and enter the password again. (Помилка розшифрування. Оскільки пароль на вхід був вимкнений, старі збережені паролі не можуть бути розшифровані. Будь ласка, відредагуйте це підключення та введіть пароль заново.)".to_string())
}

// Servers commands
#[tauri::command]
fn get_servers(db: State<'_, DbState>) -> Result<Vec<db::Server>, String> {
    let conn = db.conn.lock().unwrap();
    db::get_servers(&conn)
}

#[tauri::command]
fn add_server(
    name: String,
    hostname: String,
    ip: String,
    port: u32,
    protocol: String,
    os: String,
    folder_path: String,
    tags: String,
        description: String,
    credential_id: Option<String>,
    username: Option<String>,
    password: Option<String>,
    rdp_clipboard: Option<i32>,
    rdp_drives: Option<i32>,
    rdp_printers: Option<i32>,
    rdp_smart_sizing: Option<i32>,
    rdp_audio: Option<i32>,
    rdp_smartcards: Option<i32>,
    rdp_webauthn: Option<i32>,
    rdp_fullscreen: Option<i32>,
    rdp_multimon: Option<i32>,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let mut encrypted_password = None;

    if let Some(ref pass_val) = password {
        if !pass_val.is_empty() {
            let kek_guard = state.kek.lock().unwrap();
            let kek = kek_guard.ok_or_else(|| "Vault is locked. Cannot store custom password.".to_string())?;
            let encrypted = crypto::encrypt_secret(&kek, pass_val)?;
            let encrypted_json = serde_json::to_string(&encrypted)
                .map_err(|e| format!("Failed to serialize manual password: {}", e))?;
            encrypted_password = Some(encrypted_json);
        }
    }

    let srv = db::Server {
        id: Uuid::new_v4().to_string(),
        name,
        hostname,
        ip,
        port,
        protocol,
        os,
        folder_path,
        tags,
        description,
        credential_id,
        username,
        encrypted_password,
        created_at: String::new(),
        updated_at: String::new(),
        rdp_clipboard,
        rdp_drives,
        rdp_printers,
        rdp_smart_sizing,
        rdp_audio,
        rdp_smartcards,
        rdp_webauthn,
        rdp_fullscreen,
        rdp_multimon,
    };

    let conn = db.conn.lock().unwrap();
    db::add_server(&conn, &srv)
}

#[tauri::command]
fn update_server(
    id: String,
    name: String,
    hostname: String,
    ip: String,
    port: u32,
    protocol: String,
    os: String,
    folder_path: String,
    tags: String,
    description: String,
    credential_id: Option<String>,
    username: Option<String>,
    password: Option<String>,
    rdp_clipboard: Option<i32>,
    rdp_drives: Option<i32>,
    rdp_printers: Option<i32>,
    rdp_smart_sizing: Option<i32>,
    rdp_audio: Option<i32>,
    rdp_smartcards: Option<i32>,
    rdp_webauthn: Option<i32>,
    rdp_fullscreen: Option<i32>,
    rdp_multimon: Option<i32>,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let conn = db.conn.lock().unwrap();
    let mut encrypted_password = None;

    if let Some(ref pass_val) = password {
        if !pass_val.is_empty() {
            if pass_val == "__UNCHANGED__" {
                let list = db::get_servers(&conn)?;
                let existing = list.iter().find(|s| s.id == id)
                    .ok_or_else(|| "Server not found".to_string())?;
                encrypted_password = existing.encrypted_password.clone();
            } else {
                let kek_guard = state.kek.lock().unwrap();
                let kek = kek_guard.ok_or_else(|| "Vault is locked. Cannot store custom password.".to_string())?;
                let encrypted = crypto::encrypt_secret(&kek, pass_val)?;
                let encrypted_json = serde_json::to_string(&encrypted)
                    .map_err(|e| format!("Failed to serialize manual password: {}", e))?;
                encrypted_password = Some(encrypted_json);
            }
        }
    }

    let srv = db::Server {
        id,
        name,
        hostname,
        ip,
        port,
        protocol,
        os,
        folder_path,
        tags,
        description,
        credential_id,
        username,
        encrypted_password,
        created_at: String::new(),
        updated_at: String::new(),
        rdp_clipboard,
        rdp_drives,
        rdp_printers,
        rdp_smart_sizing,
        rdp_audio,
        rdp_smartcards,
        rdp_webauthn,
        rdp_fullscreen,
        rdp_multimon,
    };

    db::update_server(&conn, &srv)
}

#[tauri::command]
fn delete_server(id: String, db: State<'_, DbState>) -> Result<(), String> {
    let conn = db.conn.lock().unwrap();
    db::delete_server(&conn, &id)
}

// Connection history commands
#[tauri::command]
fn get_connection_history(
    server_id: String,
    db: State<'_, DbState>,
) -> Result<Vec<db::ConnectionHistory>, String> {
    let conn = db.conn.lock().unwrap();
    db::get_history(&conn, &server_id)
}

#[tauri::command]
fn add_connection_history(
    server_id: String,
    status: String,
    log: String,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let conn = db.conn.lock().unwrap();
    let hist = db::ConnectionHistory {
        id: Uuid::new_v4().to_string(),
        server_id,
        timestamp: String::new(),
        status,
        log,
    };
    db::add_history(&conn, &hist)
}

// Connect commands
#[tauri::command]
fn connect_ssh(
    session_id: String,
    host: String,
    port: u16,
    username: String,
    credential_id: Option<String>,
    server_id: Option<String>,
    cols: u32,
    rows: u32,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let mut decrypted_password = None;
    let mut decrypted_key = None;
    let mut passphrase = None;
    let mut final_username = username;

    let conn = db.conn.lock().unwrap();

    // Check if server has manual credentials first
    if let Some(ref srv_id) = server_id {
        let list = db::get_servers(&conn)?;
        if let Some(srv) = list.iter().find(|s| s.id == *srv_id) {
            if let Some(ref manual_user) = srv.username {
                if !manual_user.is_empty() {
                    final_username = manual_user.clone();
                }
            }
            if let Some(ref encrypted_pass_json) = srv.encrypted_password {
                if !encrypted_pass_json.is_empty() {
                    let kek_guard = state.kek.lock().unwrap();
                    let kek = kek_guard.ok_or_else(|| "Vault is locked. Cannot decrypt manual password.".to_string())?;
                    let encrypted: crypto::EncryptedData = serde_json::from_str(encrypted_pass_json)
                        .map_err(|e| format!("Failed to parse manual password: {}", e))?;
                    let decrypted = crypto::decrypt_secret(&kek, &encrypted)
                        .map_err(|_| "Decryption error. Since the vault login password was disabled, previously saved passwords cannot be decrypted. Please edit this connection and enter the password again. (Помилка розшифрування. Оскільки пароль на вхід був вимкнений, старі збережені паролі не можуть бути розшифровані. Будь ласка, відредагуйте це підключення та введіть пароль заново.)".to_string())?;
                    decrypted_password = Some(decrypted);
                }
            }
        }
    }

    // Fallback to linked credential if manual password was not found
    if decrypted_password.is_none() {
        if let Some(cred_id) = credential_id {
            let kek_guard = state.kek.lock().unwrap();
            let kek = kek_guard.ok_or_else(|| "Vault is locked. Cannot retrieve credentials.".to_string())?;

            let list = db::get_credentials(&conn)?;
            let cred = list.iter().find(|c| c.id == cred_id)
                .ok_or_else(|| "Credential not found".to_string())?;

            let encrypted: crypto::EncryptedData = serde_json::from_str(&cred.encrypted_secret)
                .map_err(|e| format!("Failed to parse credential secret: {}", e))?;

            let decrypted = crypto::decrypt_secret(&kek, &encrypted)
                .map_err(|_| "Decryption error. Since the vault login password was disabled, previously saved credentials cannot be decrypted. Please edit the linked credential in the Vault and enter the password/key again. (Помилка розшифрування. Оскільки пароль на вхід був вимкнений, старі збережені облікові дані не можуть бути розшифровані. Будь ласка, відредагуйте пов'язаний запис у сейфі та введіть пароль/ключ заново.)".to_string())?;
            
            if cred.r#type == "password" {
                decrypted_password = Some(decrypted);
            } else if cred.r#type == "ssh_key" {
                if decrypted.starts_with('{') {
                    #[derive(Deserialize)]
                    struct KeyDetails {
                        key: String,
                        passphrase: Option<String>,
                    }
                    if let Ok(details) = serde_json::from_str::<KeyDetails>(&decrypted) {
                        decrypted_key = Some(details.key);
                        passphrase = details.passphrase;
                    } else {
                        decrypted_key = Some(decrypted);
                    }
                } else {
                    decrypted_key = Some(decrypted);
                }
            }
        }
    }

    let password_ref = decrypted_password.as_deref();
    let key_ref = decrypted_key.as_deref();
    let passphrase_ref = passphrase.as_deref();

    let res = ssh::connect_ssh(
        app,
        session_id,
        &host,
        port,
        &final_username,
        password_ref,
        key_ref,
        passphrase_ref,
        cols,
        rows,
        server_id.clone(),
    );

    if let Some(ref srv_id) = server_id {
        let hist = db::ConnectionHistory {
            id: Uuid::new_v4().to_string(),
            server_id: srv_id.clone(),
            timestamp: String::new(),
            status: if res.is_ok() { "connected".to_string() } else { "failed".to_string() },
            log: if res.is_ok() {
                format!("SSH session initiated to {}:{}", host, port)
            } else {
                format!("Failed to initiate SSH session: {}", res.as_ref().err().unwrap())
            },
        };
        let _ = db::add_history(&conn, &hist);
    }

    res
}

#[tauri::command]
fn write_ssh_input(
    session_id: String,
    data: String,
    ssh_state: State<'_, ssh::SshState>,
) -> Result<(), String> {
    ssh::write_ssh_input(&ssh_state, &session_id, &data)
}

#[tauri::command]
fn resize_ssh_pty(
    session_id: String,
    cols: u32,
    rows: u32,
    ssh_state: State<'_, ssh::SshState>,
) -> Result<(), String> {
    ssh::resize_ssh_pty(&ssh_state, &session_id, cols, rows)
}

#[tauri::command]
fn disconnect_ssh(
    session_id: String,
    ssh_state: State<'_, ssh::SshState>,
) -> Result<(), String> {
    ssh::disconnect_ssh_session(&ssh_state, &session_id)
}

#[tauri::command]
fn connect_rdp(
    host: String,
    port: u32,
    fullscreen: bool,
    credential_id: Option<String>,
    server_id: Option<String>,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let mut decrypted_password = None;
    let mut username = None;
    let mut rdp_multimon = false;
    let mut rdp_clipboard = true;
    let mut rdp_drives = false;
    let mut rdp_printers = false;
    let mut rdp_smart_sizing = true;
    let mut rdp_audio = 0;
    let mut rdp_smartcards = false;
    let mut rdp_webauthn = false;

    let conn = db.conn.lock().unwrap();

    // Check if server has manual credentials first
    if let Some(ref srv_id) = server_id {
        let list = db::get_servers(&conn)?;
        if let Some(srv) = list.iter().find(|s| s.id == *srv_id) {
            rdp_multimon = srv.rdp_multimon.unwrap_or(0) != 0;
            rdp_clipboard = srv.rdp_clipboard.unwrap_or(1) != 0;
            rdp_drives = srv.rdp_drives.unwrap_or(0) != 0;
            rdp_printers = srv.rdp_printers.unwrap_or(0) != 0;
            rdp_smart_sizing = srv.rdp_smart_sizing.unwrap_or(1) != 0;
            rdp_audio = srv.rdp_audio.unwrap_or(0) as u32;
            rdp_smartcards = srv.rdp_smartcards.unwrap_or(0) != 0;
            rdp_webauthn = srv.rdp_webauthn.unwrap_or(0) != 0;

            if let Some(ref manual_user) = srv.username {
                if !manual_user.is_empty() {
                    username = Some(manual_user.clone());
                }
            }
            if let Some(ref encrypted_pass_json) = srv.encrypted_password {
                if !encrypted_pass_json.is_empty() {
                    let kek_guard = state.kek.lock().unwrap();
                    let kek = kek_guard.ok_or_else(|| "Vault is locked. Cannot decrypt manual password.".to_string())?;
                    let encrypted: crypto::EncryptedData = serde_json::from_str(encrypted_pass_json)
                        .map_err(|e| format!("Failed to parse manual password: {}", e))?;
                    let decrypted = crypto::decrypt_secret(&kek, &encrypted)
                        .map_err(|_| "Decryption error. Since the vault login password was disabled, previously saved passwords cannot be decrypted. Please edit this connection and enter the password again. (Помилка розшифрування. Оскільки пароль на вхід був вимкнений, старі збережені passwords не можуть бути розшифровані. Будь ласка, відредагуйте це підключення та введіть пароль заново.)".to_string())?;
                    decrypted_password = Some(decrypted);
                }
            }
        }
    }

    // Fallback to linked credential if password was not found from manual config
    if decrypted_password.is_none() {
        if let Some(cred_id) = credential_id {
            let kek_guard = state.kek.lock().unwrap();
            let kek = kek_guard.ok_or_else(|| "Vault is locked. Cannot retrieve credentials.".to_string())?;

            let list = db::get_credentials(&conn)?;
            let cred = list.iter().find(|c| c.id == cred_id)
                .ok_or_else(|| "Credential not found".to_string())?;

            let encrypted: crypto::EncryptedData = serde_json::from_str(&cred.encrypted_secret)
                .map_err(|e| format!("Failed to parse credential secret: {}", e))?;

            let decrypted = crypto::decrypt_secret(&kek, &encrypted)
                .map_err(|_| "Decryption error. Since the vault login password was disabled, previously saved credentials cannot be decrypted. Please edit the linked credential in the Vault and enter the password again. (Помилка розшифрування. Оскільки пароль на вхід був вимкнений, старі збережені облікові дані не можуть бути розшифровані. Будь ласка, відредагуйте пов'язаний запис у сейфі та введіть пароль заново.)".to_string())?;

            if username.is_none() {
                username = Some(cred.username.clone());
            }
            if cred.r#type == "password" {
                decrypted_password = Some(decrypted);
            }
        }
    }

    let app_data_dir = app.path().app_data_dir().map_err(|e| e.to_string())?;

    let res = rdp::launch_rdp_session(
        &host,
        port,
        fullscreen,
        username.as_deref(),
        decrypted_password.as_deref(),
        app_data_dir,
        server_id.clone(),
        rdp_clipboard,
        rdp_drives,
        rdp_printers,
        rdp_smart_sizing,
        rdp_audio,
        rdp_smartcards,
        rdp_webauthn,
        rdp_multimon,
    );

    if let Some(ref srv_id) = server_id {
        let hist = db::ConnectionHistory {
            id: Uuid::new_v4().to_string(),
            server_id: srv_id.clone(),
            timestamp: String::new(),
            status: if res.is_ok() { "connected".to_string() } else { "failed".to_string() },
            log: if res.is_ok() {
                format!("External RDP session launched to {}:{}", host, port)
            } else {
                format!("Failed to launch RDP session: {}", res.as_ref().err().unwrap())
            },
        };
        let _ = db::add_history(&conn, &hist);
    }

    res
}

#[tauri::command]
fn connect_rdp_embedded(
    session_id: String,
    host: String,
    port: u32,
    credential_id: Option<String>,
    server_id: Option<String>,
    x: i32,
    y: i32,
    width: i32,
    height: i32,
    device_pixel_ratio: f64,
    app: AppHandle,
    state: State<'_, SessionState>,
    _rdp_state: State<'_, rdp::RdpState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let mut decrypted_password = None;
    let mut username = None;
    let mut rdp_clipboard = true;
    let mut rdp_drives = false;
    let mut rdp_printers = false;
    let mut rdp_smart_sizing = true;
    let mut rdp_audio = 0;
    let mut rdp_smartcards = false;
    let mut rdp_webauthn = false;
    let mut rdp_fullscreen = false;
    let mut rdp_multimon = false;

    let conn = db.conn.lock().unwrap();

    // Check if server has manual credentials first
    if let Some(ref srv_id) = server_id {
        let list = db::get_servers(&conn)?;
        if let Some(srv) = list.iter().find(|s| s.id == *srv_id) {
            rdp_clipboard = srv.rdp_clipboard.unwrap_or(1) != 0;
            rdp_drives = srv.rdp_drives.unwrap_or(0) != 0;
            rdp_printers = srv.rdp_printers.unwrap_or(0) != 0;
            rdp_smart_sizing = srv.rdp_smart_sizing.unwrap_or(1) != 0;
            rdp_audio = srv.rdp_audio.unwrap_or(0) as u32;
            rdp_smartcards = srv.rdp_smartcards.unwrap_or(0) != 0;
            rdp_webauthn = srv.rdp_webauthn.unwrap_or(0) != 0;
            rdp_fullscreen = srv.rdp_fullscreen.unwrap_or(0) != 0;
            rdp_multimon = srv.rdp_multimon.unwrap_or(0) != 0;

            if let Some(ref manual_user) = srv.username {
                if !manual_user.is_empty() {
                    username = Some(manual_user.clone());
                }
            }
            if let Some(ref encrypted_pass_json) = srv.encrypted_password {
                if !encrypted_pass_json.is_empty() {
                    let kek_guard = state.kek.lock().unwrap();
                    let kek = kek_guard.ok_or_else(|| "Vault is locked. Cannot decrypt manual password.".to_string())?;
                    let encrypted: crypto::EncryptedData = serde_json::from_str(encrypted_pass_json)
                        .map_err(|e| format!("Failed to parse manual password: {}", e))?;
                    let decrypted = crypto::decrypt_secret(&kek, &encrypted)
                        .map_err(|_| "Decryption error. Since the vault login password was disabled, previously saved passwords cannot be decrypted. Please edit this connection and enter the password again. (Помилка розшифрування. Оскільки пароль на вхід був вимкнений, старі збережені паролі не можуть бути розшифровані. Будь ласка, відредагуйте це підключення та введіть пароль заново.)".to_string())?;
                    decrypted_password = Some(decrypted);
                }
            }
        }
    }

    // Fallback to linked vault credential if password was not found from manual config
    if decrypted_password.is_none() {
        if let Some(cred_id) = credential_id {
            let kek_guard = state.kek.lock().unwrap();
            let kek = kek_guard.ok_or_else(|| "Vault is locked. Cannot retrieve credentials.".to_string())?;

            let list = db::get_credentials(&conn)?;
            let cred = list.iter().find(|c| c.id == cred_id)
                .ok_or_else(|| "Credential not found".to_string())?;

            let encrypted: crypto::EncryptedData = serde_json::from_str(&cred.encrypted_secret)
                .map_err(|e| format!("Failed to parse credential secret: {}", e))?;

            let decrypted = crypto::decrypt_secret(&kek, &encrypted)
                .map_err(|_| "Decryption error. Since the vault login password was disabled, previously saved credentials cannot be decrypted. Please edit the linked credential in the Vault and enter the password again. (Помилка розшифрування. Оскільки пароль на вхід був вимкнений, старі збережені облікові дані не можуть бути розшифровані. Будь ласка, відредагуйте пов'язаний запис у сейфі та введіть пароль заново.)".to_string())?;

            if username.is_none() {
                username = Some(cred.username.clone());
            }
            if cred.r#type == "password" {
                decrypted_password = Some(decrypted);
            }
        }
    }

    // Get parent HWND from main window
    let main_window = app.get_webview_window("main").ok_or_else(|| "Main window not found".to_string())?;
    let parent_hwnd = windows::Win32::Foundation::HWND(main_window.hwnd().map_err(|e| e.to_string())?.0 as *mut _);
    let app_data_dir = app.path().app_data_dir().map_err(|e| e.to_string())?;

    let res = if rdp_fullscreen || rdp_multimon {
        // If fullscreen or multimon, we must launch externally because embedding breaks these modes
        rdp::launch_rdp_session(
            &host,
            port,
            rdp_fullscreen, // fullscreen parameter
            username.as_deref(),
            decrypted_password.as_deref(),
            app_data_dir,
            server_id.clone(),
            rdp_clipboard,
            rdp_drives,
            rdp_printers,
            rdp_smart_sizing,
            rdp_audio,
            rdp_smartcards,
            rdp_webauthn,
            rdp_multimon, // we need to add this param to launch_rdp_session
        )
    } else {
        rdp::launch_rdp_embedded(
            session_id,
            &host,
            port,
            username.as_deref(),
            decrypted_password.as_deref(),
            parent_hwnd,
            x,
            y,
            width,
            height,
            device_pixel_ratio,
            app_data_dir,
            app.clone(),
            server_id.clone(),
            rdp_clipboard,
            rdp_drives,
            rdp_printers,
            rdp_smart_sizing,
            rdp_audio,
            rdp_smartcards,
            rdp_webauthn,
        )
    };

    if let Some(ref srv_id) = server_id {
        let hist = db::ConnectionHistory {
            id: Uuid::new_v4().to_string(),
            server_id: srv_id.clone(),
            timestamp: String::new(),
            status: if res.is_ok() { "connected".to_string() } else { "failed".to_string() },
            log: if res.is_ok() {
                format!("Embedded RDP session launched to {}:{}", host, port)
            } else {
                format!("Failed to launch embedded RDP session: {}", res.as_ref().err().unwrap())
            },
        };
        let _ = db::add_history(&conn, &hist);
    }

    res
}

#[tauri::command]
fn resize_rdp_embedded(
    session_id: String,
    x: i32,
    y: i32,
    width: i32,
    height: i32,
    device_pixel_ratio: f64,
    app: AppHandle,
    rdp_state: State<'_, rdp::RdpState>,
) -> Result<(), String> {
    rdp::resize_rdp_embedded(&session_id, x, y, width, height, device_pixel_ratio, &app, rdp_state.inner())
}

#[tauri::command]
fn disconnect_rdp_embedded(
    session_id: String,
    rdp_state: State<'_, rdp::RdpState>,
    app: AppHandle,
) -> Result<(), String> {
    rdp::disconnect_rdp_embedded(&session_id, rdp_state.inner(), &app)
}

#[tauri::command]
fn bypass_rdp_warnings() -> Result<(), String> {
    let script = "Start-Process cmd.exe -ArgumentList '/c reg add \"HKLM\\Software\\Policies\\Microsoft\\Windows NT\\Terminal Services\\Client\" /v RedirectionWarningDialogVersion /t REG_DWORD /d 1 /f' -Verb RunAs";
    
    let status = std::process::Command::new("powershell")
        .args(&["-Command", script])
        .status()
        .map_err(|e| format!("Failed to spawn elevated process: {}", e))?;
        
    if !status.success() {
        return Err("UAC elevation was cancelled or failed".to_string());
    }
        
    Ok(())
}

fn re_encrypt_database(
    db_path: &std::path::Path,
    old_kek: &[u8; 32],
    new_kek: &[u8; 32],
    new_salt_hex: &str,
) -> Result<(), String> {
    let conn = rusqlite::Connection::open(db_path)
        .map_err(|e| format!("Failed to open export database for re-encryption: {}", e))?;

    // Update settings table
    let sentinel_plaintext = "rdm-auth-sentinel";
    let encrypted_sentinel = crypto::encrypt_secret(new_kek, sentinel_plaintext)?;
    let sentinel_json = serde_json::to_string(&encrypted_sentinel)
        .map_err(|e| format!("Failed to serialize sentinel: {}", e))?;

    conn.execute(
        "UPDATE settings SET value = ?1 WHERE key = 'salt';",
        [&new_salt_hex],
    ).map_err(|e| e.to_string())?;

    conn.execute(
        "UPDATE settings SET value = ?1 WHERE key = 'sentinel';",
        [&sentinel_json],
    ).map_err(|e| e.to_string())?;

    // Re-encrypt credentials table
    let mut stmt = conn
        .prepare("SELECT id, encrypted_secret FROM credentials")
        .map_err(|e| e.to_string())?;
    let mut rows = stmt.query([]).map_err(|e| e.to_string())?;
    
    let mut creds_to_update = Vec::new();
    while let Some(row) = rows.next().map_err(|e| e.to_string())? {
        let id: String = row.get(0).map_err(|e| e.to_string())?;
        let encrypted_secret_json: String = row.get(1).map_err(|e| e.to_string())?;
        
        let encrypted_secret: crypto::EncryptedData = serde_json::from_str(&encrypted_secret_json)
            .map_err(|e| format!("Failed to parse credential secret: {}", e))?;
        
        let secret = crypto::decrypt_secret(old_kek, &encrypted_secret)?;
        let re_encrypted = crypto::encrypt_secret(new_kek, &secret)?;
        let re_encrypted_json = serde_json::to_string(&re_encrypted)
            .map_err(|e| e.to_string())?;
            
        creds_to_update.push((id, re_encrypted_json));
    }
    drop(rows);
    drop(stmt);

    for (id, secret_json) in creds_to_update {
        conn.execute(
            "UPDATE credentials SET encrypted_secret = ?1 WHERE id = ?2",
            [&secret_json, &id],
        ).map_err(|e| e.to_string())?;
    }

    // Re-encrypt servers table
    let mut stmt = conn
        .prepare("SELECT id, encrypted_password FROM servers WHERE encrypted_password IS NOT NULL AND encrypted_password != ''")
        .map_err(|e| e.to_string())?;
    let mut rows = stmt.query([]).map_err(|e| e.to_string())?;
    
    let mut servers_to_update = Vec::new();
    while let Some(row) = rows.next().map_err(|e| e.to_string())? {
        let id: String = row.get(0).map_err(|e| e.to_string())?;
        let encrypted_password_json: String = row.get(1).map_err(|e| e.to_string())?;
        
        let encrypted_password: crypto::EncryptedData = serde_json::from_str(&encrypted_password_json)
            .map_err(|e| format!("Failed to parse server password: {}", e))?;
        
        let password = crypto::decrypt_secret(old_kek, &encrypted_password)?;
        let re_encrypted = crypto::encrypt_secret(new_kek, &password)?;
        let re_encrypted_json = serde_json::to_string(&re_encrypted)
            .map_err(|e| e.to_string())?;
            
        servers_to_update.push((id, re_encrypted_json));
    }
    drop(rows);
    drop(stmt);

    for (id, password_json) in servers_to_update {
        conn.execute(
            "UPDATE servers SET encrypted_password = ?1 WHERE id = ?2",
            [&password_json, &id],
        ).map_err(|e| e.to_string())?;
    }

    Ok(())
}

#[tauri::command]
fn export_database_backup(
    destination_path: String,
    password: String,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let app_dir = app.path().app_data_dir().unwrap();
    let db_path = app_dir.join("rdm.db");

    // Lock local connection
    let _conn_guard = db.conn.lock().unwrap();

    // Copy to destination
    std::fs::copy(&db_path, &destination_path)
        .map_err(|e| format!("Failed to export database: {}", e))?;

    // Derive KEKs
    let local_kek = state.kek.lock().unwrap().ok_or_else(|| "Vault is locked".to_string())?;

    let mut export_salt = [0u8; 16];
    thread_rng().fill_bytes(&mut export_salt);
    let export_salt_hex = hex::encode(export_salt);
    let export_kek = crypto::derive_key(&password, &export_salt)?;

    // Re-encrypt the exported file
    re_encrypt_database(
        std::path::Path::new(&destination_path),
        &local_kek,
        &export_kek,
        &export_salt_hex,
    )?;

    Ok(())
}

#[tauri::command]
fn import_database_backup(
    source_path: String,
    password: String,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    // 1. Open source database and read salt
    let backup_conn = rusqlite::Connection::open(&source_path)
        .map_err(|e| format!("Failed to open backup database: {}", e))?;

    let salt_hex: String = backup_conn
        .query_row(
            "SELECT value FROM settings WHERE key = 'salt'",
            [],
            |row| row.get(0),
        )
        .map_err(|_| "Selected file is not a valid RDM backup (salt missing)".to_string())?;

    let sentinel_json: String = backup_conn
        .query_row(
            "SELECT value FROM settings WHERE key = 'sentinel'",
            [],
            |row| row.get(0),
        )
        .map_err(|_| "Selected file is not a valid RDM backup (sentinel missing)".to_string())?;

    let backup_salt = hex::decode(&salt_hex)
        .map_err(|e| format!("Invalid salt encoding in backup: {}", e))?;

    // Derive KEK for the backup database
    let backup_kek = crypto::derive_key(&password, &backup_salt)?;

    // Verify sentinel
    let encrypted_sentinel: crypto::EncryptedData = serde_json::from_str(&sentinel_json)
        .map_err(|e| format!("Failed to parse backup sentinel: {}", e))?;

    let decrypted = crypto::decrypt_secret(&backup_kek, &encrypted_sentinel)
        .map_err(|_| "Invalid backup password. Cannot restore. (Неправильний пароль резервної копії.)".to_string())?;

    if decrypted != "rdm-auth-sentinel" {
        return Err("Invalid sentinel inside backup database.".to_string());
    }

    drop(backup_conn); // Release handle to backup file

    // 2. Prepare re-encryption to local_kek (derived from "default_rdm_key")
    let app_dir = app.path().app_data_dir().unwrap();
    let db_path = app_dir.join("rdm.db");
    let temp_db_path = app_dir.join("rdm_import_temp.db");

    if temp_db_path.exists() {
        let _ = std::fs::remove_file(&temp_db_path);
    }

    // Copy backup to temp
    std::fs::copy(&source_path, &temp_db_path)
        .map_err(|e| format!("Failed to create temporary import file: {}", e))?;

    // Generate new local salt & KEK for rdm.db
    let mut local_salt = [0u8; 16];
    thread_rng().fill_bytes(&mut local_salt);
    let local_salt_hex = hex::encode(local_salt);
    let local_kek = crypto::derive_key("default_rdm_key", &local_salt)?;

    // Re-encrypt temporary import file to local_kek
    re_encrypt_database(
        &temp_db_path,
        &backup_kek,
        &local_kek,
        &local_salt_hex,
    )?;

    // 3. Swap active connection to point to the imported file
    let mut conn_guard = db.conn.lock().unwrap();
    
    // Open in-memory temporary database to release locks on rdm.db
    let temp_conn = rusqlite::Connection::open_in_memory().unwrap();
    let old_conn = std::mem::replace(&mut *conn_guard, temp_conn);
    drop(old_conn); // Closes rdm.db file handle

    // Copy temp file over active database
    std::fs::copy(&temp_db_path, &db_path)
        .map_err(|e| format!("Failed to copy database file: {}", e))?;

    let _ = std::fs::remove_file(&temp_db_path);

    // Reopen database connection
    let new_conn = rusqlite::Connection::open(&db_path)
        .map_err(|e| format!("Failed to reopen database: {}", e))?;

    *conn_guard = new_conn;

    // Save new KEK in session state
    let mut session_kek = state.kek.lock().unwrap();
    *session_kek = Some(local_kek);

    Ok(())
}

#[tauri::command]
fn import_devolutions_csv(
    csv_content: String,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<u32, String> {
    let kek_guard = state.kek.lock().unwrap();
    let kek = kek_guard.ok_or_else(|| "Vault is locked. Unlock vault to import passwords.".to_string())?;

    let conn = db.conn.lock().unwrap();

    // Detect delimiter dynamically (handles comma, semicolon, tab)
    let mut delimiter = b',';
    if let Some(first_line) = csv_content.lines().next() {
        let commas = first_line.matches(',').count();
        let semicolons = first_line.matches(';').count();
        let tabs = first_line.matches('\t').count();
        
        if semicolons > commas && semicolons > tabs {
            delimiter = b';';
        } else if tabs > commas && tabs > semicolons {
            delimiter = b'\t';
        }
    }

    let mut reader = csv::ReaderBuilder::new()
        .has_headers(true)
        .delimiter(delimiter)
        .from_reader(csv_content.as_bytes());

    let headers = reader.headers()
        .map_err(|e| format!("Failed to read CSV headers: {}", e))?
        .clone();

    // Map headers to indexes
    let mut name_idx = None;
    let mut host_idx = None;
    let mut port_idx = None;
    let mut group_idx = None;
    let mut protocol_idx = None;
    let mut username_idx = None;
    let mut password_idx = None;
    let mut description_idx = None;

    for (idx, header) in headers.iter().enumerate() {
        let h = header.to_lowercase().replace(' ', "").replace('_', "").replace('-', "");
        if h == "name" || h == "connectionname" || h == "displayname" || h == "session" || h == "sessionname" || h == "title" || h == "назва" || h == "имя" {
            name_idx = Some(idx);
        } else if h == "host" || h == "computer" || h == "ip" || h == "hostname" || h == "ipaddress" || h == "хост" || h == "адреса" || h == "адрес" {
            host_idx = Some(idx);
        } else if h == "port" || h == "порт" {
            port_idx = Some(idx);
        } else if h == "group" || h == "folder" || h == "folderpath" || h == "directory" || h == "група" || h == "группа" || h == "папка" {
            group_idx = Some(idx);
        } else if h == "type" || h == "connectiontype" || h == "protocol" || h == "тип" || h == "протокол" {
            protocol_idx = Some(idx);
        } else if h == "username" || h == "user" || h == "credentialusername" || h == "login" || h == "користувач" || h == "логін" || h == "пользователь" || h == "логин" {
            username_idx = Some(idx);
        } else if h == "password" || h == "pass" || h == "credentialpassword" || h == "secret" || h == "пароль" {
            password_idx = Some(idx);
        } else if h == "description" || h == "notes" || h == "comment" || h == "опис" || h == "описание" || h == "примітка" || h == "примечание" {
            description_idx = Some(idx);
        }
    }

    let name_idx = name_idx.ok_or_else(|| "CSV must contain a 'Name', 'DisplayName', 'Session', 'Title' or 'Назва' column".to_string())?;
    let host_idx = host_idx.ok_or_else(|| "CSV must contain a 'Host', 'Computer', 'IP', 'Hostname' or 'Хост' column".to_string())?;

    let existing_servers = db::get_servers(&conn)?;
    let mut seen_servers = std::collections::HashSet::new();
    for s in existing_servers {
        seen_servers.insert((s.name, s.hostname));
    }

    let mut imported_count = 0;

    for result in reader.records() {
        let record = result.map_err(|e| format!("Error reading CSV row: {}", e))?;
        
        let name = record.get(name_idx).unwrap_or("").trim().to_string();
        let host = record.get(host_idx).unwrap_or("").trim().to_string();
        if name.is_empty() || host.is_empty() {
            continue;
        }

        if seen_servers.contains(&(name.clone(), host.clone())) {
            continue; // Skip duplicates
        }
        seen_servers.insert((name.clone(), host.clone()));

        let port_str = port_idx.and_then(|idx| record.get(idx)).unwrap_or("").trim();
        let folder_path = group_idx.and_then(|idx| record.get(idx)).unwrap_or("").trim().to_string();
        let description = description_idx.and_then(|idx| record.get(idx)).unwrap_or("").trim().to_string();

        let protocol_str = protocol_idx.and_then(|idx| record.get(idx)).unwrap_or("").trim().to_lowercase();
        let mut protocol = if protocol_str.contains("rdp") || protocol_str.contains("remote") {
            "rdp".to_string()
        } else {
            "ssh".to_string()
        };

        let port = port_str.parse::<u32>().unwrap_or_else(|_| {
            if protocol == "rdp" { 3389 } else { 22 }
        });

        // Override protocol based on standard port overrides if mismatching
        if port == 3389 && protocol != "rdp" {
            protocol = "rdp".to_string();
        } else if port == 22 && protocol != "ssh" {
            protocol = "ssh".to_string();
        }

        let username = username_idx.and_then(|idx| record.get(idx)).unwrap_or("").trim().to_string();
        let password = password_idx.and_then(|idx| record.get(idx)).unwrap_or("").trim().to_string();

        let mut credential_id = None;

        if !username.is_empty() {
            let cred_id = Uuid::new_v4().to_string();
            let encrypted = crypto::encrypt_secret(&kek, &password)?;
            let encrypted_json = serde_json::to_string(&encrypted)
                .map_err(|e| format!("Failed to serialize credential secret: {}", e))?;

            let cred = db::Credential {
                id: cred_id.clone(),
                name: format!("Imported: {}", name),
                r#type: "password".to_string(),
                username,
                encrypted_secret: encrypted_json,
                created_at: String::new(),
                updated_at: String::new(),
            };

            db::add_credential(&conn, &cred)?;
            credential_id = Some(cred_id);
        }

        let os = if protocol == "rdp" { "windows".to_string() } else { "linux".to_string() };

        let srv = db::Server {
            id: Uuid::new_v4().to_string(),
            name,
            hostname: host.clone(),
            ip: host,
            port,
            protocol,
            os,
            folder_path,
            tags: "imported".to_string(),
            description,
            credential_id,
            username: None,
            encrypted_password: None,
            created_at: String::new(),
            updated_at: String::new(),
            rdp_fullscreen: Some(0),
            rdp_multimon: Some(0),
            rdp_clipboard: Some(1),
            rdp_drives: Some(0),
            rdp_printers: Some(0),
            rdp_smart_sizing: Some(1),
            rdp_audio: Some(0),
            rdp_smartcards: Some(0),
            rdp_webauthn: Some(0),
        };

        db::add_server(&conn, &srv)?;
        imported_count += 1;
    }

    Ok(imported_count)
}

#[tauri::command]
fn select_and_export_backup(
    password: String,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<String, String> {
    let file_path = rfd::FileDialog::new()
        .set_title("Save RDM Backup")
        .add_filter("SQLite Database", &["db", "sqlite"])
        .set_file_name("rdm_backup.db")
        .save_file();

    if let Some(path) = file_path {
        let dest = path.to_string_lossy().to_string();
        export_database_backup(dest, password, app, state, db)?;
        Ok(path.file_name().unwrap().to_string_lossy().to_string())
    } else {
        Err("Save cancelled".to_string())
    }
}

#[tauri::command]
fn select_and_import_backup(
    password: String,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<String, String> {
    let file_path = rfd::FileDialog::new()
        .set_title("Open RDM Backup")
        .add_filter("SQLite Database", &["db", "sqlite"])
        .pick_file();

    if let Some(path) = file_path {
        let src = path.to_string_lossy().to_string();
        import_database_backup(src, password, app, state, db)?;
        Ok(path.file_name().unwrap().to_string_lossy().to_string())
    } else {
        Err("Import cancelled".to_string())
    }
}

// SFTP Commands
fn get_ssh_creds(
    server_id: &Option<String>,
    credential_id: &Option<String>,
    app_username: &str,
    state: &State<'_, SessionState>,
    db: &State<'_, DbState>,
) -> Result<(String, Option<String>, Option<String>), String> {
    let conn = db.conn.lock().unwrap();
    let mut decrypted_password = None;
    let mut decrypted_key = None;
    let mut final_username = app_username.to_string();

    if let Some(ref srv_id) = server_id {
        let list = db::get_servers(&conn)?;
        if let Some(srv) = list.iter().find(|s| s.id == *srv_id) {
            if let Some(ref manual_user) = srv.username {
                if !manual_user.is_empty() { final_username = manual_user.clone(); }
            }
            if let Some(ref encrypted_pass_json) = srv.encrypted_password {
                if !encrypted_pass_json.is_empty() {
                    let kek_guard = state.kek.lock().unwrap();
                    let kek = kek_guard.as_ref().ok_or("Vault is locked.")?;
                    let encrypted: crypto::EncryptedData = serde_json::from_str(encrypted_pass_json)
                        .map_err(|e| e.to_string())?;
                    decrypted_password = Some(crypto::decrypt_secret(kek, &encrypted).map_err(|_| "Decryption error")?);
                }
            }
        }
    }

    if decrypted_password.is_none() {
        if let Some(cred_id) = credential_id {
            let kek_guard = state.kek.lock().unwrap();
            let kek = kek_guard.as_ref().ok_or("Vault is locked.")?;
            let list = db::get_credentials(&conn)?;
            let cred = list.iter().find(|c| c.id == *cred_id).ok_or("Credential not found")?;
            let encrypted: crypto::EncryptedData = serde_json::from_str(&cred.encrypted_secret)
                .map_err(|e| e.to_string())?;
            let decrypted = crypto::decrypt_secret(kek, &encrypted).map_err(|_| "Decryption error")?;
            
            if cred.r#type == "password" {
                decrypted_password = Some(decrypted);
            } else if cred.r#type == "ssh_key" {
                if decrypted.starts_with('{') {
                    #[derive(serde::Deserialize)]
                    struct KeyDetails { key: String }
                    if let Ok(details) = serde_json::from_str::<KeyDetails>(&decrypted) {
                        decrypted_key = Some(details.key);
                    } else { decrypted_key = Some(decrypted); }
                } else { decrypted_key = Some(decrypted); }
            }
        }
    }
    
    Ok((final_username, decrypted_password, decrypted_key))
}

#[tauri::command]
fn sftp_ls(
    host: String,
    port: u16,
    username: String,
    path: String,
    credential_id: Option<String>,
    server_id: Option<String>,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<String, String> {
    let (final_user, pwd, key) = get_ssh_creds(&server_id, &credential_id, &username, &state, &db)?;
    let app_data = app.path().app_data_dir().unwrap();
    
    // We use ssh to run `ls -la --full-time <path>`
    let args = vec![
        "-p".to_string(), port.to_string(),
        format!("{}@{}", final_user, host),
        format!("ls -la {}", path),
    ];
    
    sftp::run_ssh_command_sync(app_data, "ssh", &args, pwd.as_deref(), key.as_deref())
}

#[tauri::command]
fn sftp_download(
    host: String,
    port: u16,
    username: String,
    remote_path: String,
    local_path: String,
    credential_id: Option<String>,
    server_id: Option<String>,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<String, String> {
    let (final_user, pwd, key) = get_ssh_creds(&server_id, &credential_id, &username, &state, &db)?;
    let app_data = app.path().app_data_dir().unwrap();
    
    let args = vec![
        "-P".to_string(), port.to_string(),
        format!("{}@{}:{}", final_user, host, remote_path),
        local_path,
    ];
    
    sftp::run_ssh_command_sync(app_data, "scp", &args, pwd.as_deref(), key.as_deref())
}

#[tauri::command]
fn sftp_upload(
    host: String,
    port: u16,
    username: String,
    local_path: String,
    remote_path: String,
    credential_id: Option<String>,
    server_id: Option<String>,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<String, String> {
    let (final_user, pwd, key) = get_ssh_creds(&server_id, &credential_id, &username, &state, &db)?;
    let app_data = app.path().app_data_dir().unwrap();
    
    let args = vec![
        "-P".to_string(), port.to_string(),
        local_path,
        format!("{}@{}:{}", final_user, host, remote_path),
    ];
    
    sftp::run_ssh_command_sync(app_data, "scp", &args, pwd.as_deref(), key.as_deref())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .setup(|app| {
            let app_dir = app.path().app_data_dir().unwrap();
            let conn = db::init_db(app_dir)?;
            let session_state = SessionState::new();

            // Auto-unlock vault with default password if not already unlocked
            let is_setup = db::get_setting(&conn, "sentinel")?.is_some();
            if !is_setup {
                let _ = setup_master_password_impl(&conn, &session_state.kek, "default_rdm_key");
            } else {
                let _ = unlock_vault_impl(&conn, &session_state.kek, "default_rdm_key");
            }

            app.manage(DbState { conn: Mutex::new(conn) });
            app.manage(session_state);
            app.manage(ssh::SshState::new());
            app.manage(rdp::RdpState::new());
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            is_vault_setup,
            get_setting,
            set_setting,
            is_vault_unlocked,
            setup_master_password,
            unlock_vault,
            lock_vault,
            get_credentials,
            add_credential,
            update_credential,
            delete_credential,
            decrypt_credential_secret,
            get_servers,
            add_server,
            update_server,
            delete_server,
            get_connection_history,
            add_connection_history,
            connect_ssh,
            write_ssh_input,
            resize_ssh_pty,
            disconnect_ssh,
            connect_rdp,
            connect_rdp_embedded,
            resize_rdp_embedded,
            disconnect_rdp_embedded,
            export_database_backup,
            import_database_backup,
            import_devolutions_csv,
            sftp_ls,
            sftp_download,
            sftp_upload,
            select_and_export_backup,
            select_and_import_backup,
            decrypt_server_password,
            bypass_rdp_warnings
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
