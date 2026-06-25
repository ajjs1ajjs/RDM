mod crypto;
mod db;
mod rdp;
mod ssh;

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

#[tauri::command]
fn setup_master_password(
    password: String,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let conn = db.conn.lock().unwrap();
    
    // Check if already setup
    if db::get_setting(&conn, "sentinel")?.is_some() {
        return Err("Vault is already initialized".to_string());
    }

    // Generate random 16-byte salt
    let mut salt = [0u8; 16];
    thread_rng().fill_bytes(&mut salt);
    let salt_hex = hex::encode(salt);

    // Derive Key Encryption Key (KEK)
    let kek = crypto::derive_key(&password, &salt)?;

    // Encrypt authentication sentinel
    let sentinel_plaintext = "rdm-auth-sentinel";
    let encrypted_sentinel = crypto::encrypt_secret(&kek, sentinel_plaintext)?;
    let sentinel_json = serde_json::to_string(&encrypted_sentinel)
        .map_err(|e| format!("Failed to serialize sentinel: {}", e))?;

    // Store salt and sentinel in db
    db::set_setting(&conn, "salt", &salt_hex)?;
    db::set_setting(&conn, "sentinel", &sentinel_json)?;

    // Store KEK in session state
    let mut session_kek = state.kek.lock().unwrap();
    *session_kek = Some(kek);

    Ok(())
}

#[tauri::command]
fn unlock_vault(
    password: String,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<bool, String> {
    let conn = db.conn.lock().unwrap();

    let salt_hex = db::get_setting(&conn, "salt")?
        .ok_or_else(|| "Vault has not been initialized yet".to_string())?;
    
    let sentinel_json = db::get_setting(&conn, "sentinel")?
        .ok_or_else(|| "Vault sentinel not found".to_string())?;

    let salt = hex::decode(&salt_hex)
        .map_err(|e| format!("Invalid salt encoding: {}", e))?;

    let kek = crypto::derive_key(&password, &salt)?;

    let encrypted_sentinel: crypto::EncryptedData = serde_json::from_str(&sentinel_json)
        .map_err(|e| format!("Failed to parse sentinel data: {}", e))?;

    // Try to decrypt sentinel
    match crypto::decrypt_secret(&kek, &encrypted_sentinel) {
        Ok(decrypted) if decrypted == "rdm-auth-sentinel" => {
            // Save KEK in session state
            let mut session_kek = state.kek.lock().unwrap();
            *session_kek = Some(kek);
            Ok(true)
        }
        _ => Ok(false), // Incorrect password
    }
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
    db: State<'_, DbState>,
) -> Result<(), String> {
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
        created_at: String::new(),
        updated_at: String::new(),
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
    db: State<'_, DbState>,
) -> Result<(), String> {
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
        created_at: String::new(),
        updated_at: String::new(),
    };

    let conn = db.conn.lock().unwrap();
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
) -> Result<(), String> {
    // Wait, let's get db from app handle or state
    // Let's implement it inside the app
    Ok(())
}

// Connect commands
#[tauri::command]
fn connect_ssh(
    session_id: String,
    host: String,
    port: u16,
    username: String,
    credential_id: Option<String>,
    cols: u32,
    rows: u32,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let mut decrypted_password = None;
    let mut decrypted_key = None;
    let mut passphrase = None;

    if let Some(cred_id) = credential_id {
        let kek_guard = state.kek.lock().unwrap();
        let kek = kek_guard.ok_or_else(|| "Vault is locked. Cannot retrieve credentials.".to_string())?;

        let conn = db.conn.lock().unwrap();
        let list = db::get_credentials(&conn)?;
        let cred = list.iter().find(|c| c.id == cred_id)
            .ok_or_else(|| "Credential not found".to_string())?;

        let encrypted: crypto::EncryptedData = serde_json::from_str(&cred.encrypted_secret)
            .map_err(|e| format!("Failed to parse credential secret: {}", e))?;

        let decrypted = crypto::decrypt_secret(&kek, &encrypted)?;

        if cred.r#type == "password" {
            decrypted_password = Some(decrypted);
        } else if cred.r#type == "ssh_key" {
            // Check if there is passphrase. In our DB, we store private key + passphrase.
            // If the secret is JSON, let's handle it, otherwise it's just the key content.
            // We can store it as private key string or a JSON containing { key: "...", passphrase: "..." }.
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

    let password_ref = decrypted_password.as_deref();
    let key_ref = decrypted_key.as_deref();
    let passphrase_ref = passphrase.as_deref();

    ssh::connect_ssh(
        app,
        session_id,
        &host,
        port,
        &username,
        password_ref,
        key_ref,
        passphrase_ref,
        cols,
        rows,
    )
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
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let mut decrypted_password = None;
    let mut username = None;

    if let Some(cred_id) = credential_id {
        let kek_guard = state.kek.lock().unwrap();
        let kek = kek_guard.ok_or_else(|| "Vault is locked. Cannot retrieve credentials.".to_string())?;

        let conn = db.conn.lock().unwrap();
        let list = db::get_credentials(&conn)?;
        let cred = list.iter().find(|c| c.id == cred_id)
            .ok_or_else(|| "Credential not found".to_string())?;

        let encrypted: crypto::EncryptedData = serde_json::from_str(&cred.encrypted_secret)
            .map_err(|e| format!("Failed to parse credential secret: {}", e))?;

        let decrypted = crypto::decrypt_secret(&kek, &encrypted)?;

        username = Some(cred.username.clone());
        if cred.r#type == "password" {
            decrypted_password = Some(decrypted);
        }
    }

    rdp::launch_rdp_session(
        &host,
        port,
        fullscreen,
        username.as_deref(),
        decrypted_password.as_deref(),
    )
}

#[tauri::command]
fn export_database_backup(
    destination_path: String,
    app: AppHandle,
    db: State<'_, DbState>,
) -> Result<(), String> {
    let app_dir = app.path().app_data_dir().unwrap();
    let db_path = app_dir.join("rdm.db");

    // Lock connection
    let _conn_guard = db.conn.lock().unwrap();

    std::fs::copy(&db_path, &destination_path)
        .map(|_| ())
        .map_err(|e| format!("Failed to export database: {}", e))
}

#[tauri::command]
fn import_database_backup(
    source_path: String,
    app: AppHandle,
    state: State<'_, SessionState>,
    db: State<'_, DbState>,
) -> Result<(), String> {
    // 1. Verify that this backup is valid and matches our Master Password KEK
    let kek_guard = state.kek.lock().unwrap();
    let kek = kek_guard.ok_or_else(|| "Vault is locked".to_string())?;

    let backup_conn = rusqlite::Connection::open(&source_path)
        .map_err(|e| format!("Failed to open backup database: {}", e))?;

    let sentinel_json: String = backup_conn
        .query_row(
            "SELECT value FROM settings WHERE key = 'sentinel'",
            [],
            |row| row.get(0),
        )
        .map_err(|_| "Selected file is not a valid RDM backup (sentinel missing)".to_string())?;

    let encrypted_sentinel: crypto::EncryptedData = serde_json::from_str(&sentinel_json)
        .map_err(|e| format!("Failed to parse backup sentinel: {}", e))?;

    let decrypted = crypto::decrypt_secret(&kek, &encrypted_sentinel)
        .map_err(|_| "The Master Password of the current session does not match the backup file. Cannot restore.".to_string())?;

    if decrypted != "rdm-auth-sentinel" {
        return Err("Invalid sentinel inside backup database.".to_string());
    }

    drop(backup_conn); // Release handle to backup file

    // 2. Safely swap connection and restore file
    let app_dir = app.path().app_data_dir().unwrap();
    let db_path = app_dir.join("rdm.db");

    let mut conn_guard = db.conn.lock().unwrap();
    
    // Open in-memory temporary database to release locks on rdm.db
    let temp_conn = rusqlite::Connection::open_in_memory().unwrap();
    let old_conn = std::mem::replace(&mut *conn_guard, temp_conn);
    drop(old_conn); // Closes rdm.db file handle

    // Copy backup file over active database
    std::fs::copy(&source_path, &db_path)
        .map_err(|e| format!("Failed to copy database file: {}", e))?;

    // Reopen database connection
    let new_conn = rusqlite::Connection::open(&db_path)
        .map_err(|e| format!("Failed to reopen database: {}", e))?;

    *conn_guard = new_conn;

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

    let mut imported_count = 0;

    for result in reader.records() {
        let record = result.map_err(|e| format!("Error reading CSV row: {}", e))?;
        
        let name = record.get(name_idx).unwrap_or("").trim().to_string();
        let host = record.get(host_idx).unwrap_or("").trim().to_string();
        if name.is_empty() || host.is_empty() {
            continue;
        }

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
            created_at: String::new(),
            updated_at: String::new(),
        };

        db::add_server(&conn, &srv)?;
        imported_count += 1;
    }

    Ok(imported_count)
}

#[tauri::command]
fn select_and_export_backup(
    app: AppHandle,
    db: State<'_, DbState>,
) -> Result<String, String> {
    let file_path = rfd::FileDialog::new()
        .set_title("Save RDM Backup")
        .add_filter("SQLite Database", &["db", "sqlite"])
        .set_file_name("rdm_backup.db")
        .save_file();

    if let Some(path) = file_path {
        let dest = path.to_string_lossy().to_string();
        export_database_backup(dest, app, db)?;
        Ok(path.file_name().unwrap().to_string_lossy().to_string())
    } else {
        Err("Save cancelled".to_string())
    }
}

#[tauri::command]
fn select_and_import_backup(
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
        import_database_backup(src, app, state, db)?;
        Ok(path.file_name().unwrap().to_string_lossy().to_string())
    } else {
        Err("Import cancelled".to_string())
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let app_dir = app.path().app_data_dir().unwrap();
            let conn = db::init_db(app_dir)?;
            app.manage(DbState { conn: Mutex::new(conn) });
            app.manage(SessionState::new());
            app.manage(ssh::SshState::new());
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
            connect_ssh,
            write_ssh_input,
            resize_ssh_pty,
            disconnect_ssh,
            connect_rdp,
            export_database_backup,
            import_database_backup,
            import_devolutions_csv,
            select_and_export_backup,
            select_and_import_backup
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
