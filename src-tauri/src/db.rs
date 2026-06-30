use rusqlite::{params, Connection};
use std::path::PathBuf;
use std::fs;
use serde::{Serialize, Deserialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Server {
    pub id: String,
    pub name: String,
    pub hostname: String,
    pub ip: String,
    pub port: u32,
    pub protocol: String,
    pub os: String,
    pub folder_path: String,
    pub tags: String,
    pub description: String,
    pub credential_id: Option<String>,
    pub username: Option<String>,
    pub encrypted_password: Option<String>,
    pub created_at: String,
    pub updated_at: String,
    pub rdp_clipboard: Option<i32>,
    pub rdp_drives: Option<i32>,
    pub rdp_printers: Option<i32>,
    pub rdp_smart_sizing: Option<i32>,
    pub rdp_audio: Option<i32>,
    pub rdp_smartcards: Option<i32>,
    pub rdp_webauthn: Option<i32>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Credential {
    pub id: String,
    pub name: String,
    pub r#type: String,
    pub username: String,
    pub encrypted_secret: String, // JSON string of EncryptedData
    pub created_at: String,
    pub updated_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ConnectionHistory {
    pub id: String,
    pub server_id: String,
    pub timestamp: String,
    pub status: String,
    pub log: String,
}

fn column_exists(conn: &Connection, table: &str, column: &str) -> Result<bool, rusqlite::Error> {
    let mut stmt = conn.prepare(&format!("PRAGMA table_info({})", table))?;
    let mut rows = stmt.query([])?;
    while let Some(row) = rows.next()? {
        let name: String = row.get(1)?;
        if name == column {
            return Ok(true);
        }
    }
    Ok(false)
}

pub fn init_db(app_dir: PathBuf) -> Result<Connection, String> {
    if !app_dir.exists() {
        fs::create_dir_all(&app_dir).map_err(|e| format!("Failed to create app data dir: {}", e))?;
    }
    
    let db_path = app_dir.join("rdm.db");
    let conn = Connection::open(db_path).map_err(|e| format!("Failed to open database: {}", e))?;
    
    // Enable foreign keys
    conn.execute("PRAGMA foreign_keys = ON;", []).map_err(|e| e.to_string())?;

    // Create tables
    conn.execute(
        "CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );",
        [],
    ).map_err(|e| e.to_string())?;

    conn.execute(
        "CREATE TABLE IF NOT EXISTS credentials (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            type TEXT NOT NULL,
            username TEXT NOT NULL,
            encrypted_secret TEXT NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );",
        [],
    ).map_err(|e| e.to_string())?;

    conn.execute(
        "CREATE TABLE IF NOT EXISTS servers (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            hostname TEXT NOT NULL,
            ip TEXT NOT NULL,
            port INTEGER NOT NULL,
            protocol TEXT NOT NULL,
            os TEXT NOT NULL,
            folder_path TEXT NOT NULL,
            tags TEXT NOT NULL,
            description TEXT NOT NULL,
            credential_id TEXT,
            username TEXT,
            encrypted_password TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            rdp_clipboard INTEGER DEFAULT 1,
            rdp_drives INTEGER DEFAULT 0,
            rdp_printers INTEGER DEFAULT 0,
            rdp_smart_sizing INTEGER DEFAULT 1,
            rdp_audio INTEGER DEFAULT 0,
            rdp_smartcards INTEGER DEFAULT 0,
            rdp_webauthn INTEGER DEFAULT 0,
            FOREIGN KEY(credential_id) REFERENCES credentials(id) ON DELETE SET NULL
        );",
        [],
    ).map_err(|e| e.to_string())?;

    conn.execute(
        "CREATE TABLE IF NOT EXISTS connection_history (
            id TEXT PRIMARY KEY,
            server_id TEXT NOT NULL,
            timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
            status TEXT NOT NULL,
            log TEXT NOT NULL,
            FOREIGN KEY(server_id) REFERENCES servers(id) ON DELETE CASCADE
        );",
        [],
    ).map_err(|e| e.to_string())?;

    // Safe versioned schema migration
    let current_version: u32 = conn
        .query_row("PRAGMA user_version;", [], |row| row.get(0))
        .map_err(|e| e.to_string())?;

    if current_version < 2 {
        let columns_to_add = [
            ("username", "TEXT"),
            ("encrypted_password", "TEXT"),
            ("rdp_clipboard", "INTEGER DEFAULT 1"),
            ("rdp_drives", "INTEGER DEFAULT 0"),
            ("rdp_printers", "INTEGER DEFAULT 0"),
            ("rdp_smart_sizing", "INTEGER DEFAULT 1"),
            ("rdp_audio", "INTEGER DEFAULT 0"),
            ("rdp_smartcards", "INTEGER DEFAULT 0"),
            ("rdp_webauthn", "INTEGER DEFAULT 0"),
        ];

        for &(col_name, col_type) in &columns_to_add {
            if !column_exists(&conn, "servers", col_name).map_err(|e| e.to_string())? {
                conn.execute(
                    &format!("ALTER TABLE servers ADD COLUMN {} {};", col_name, col_type),
                    [],
                ).map_err(|e| e.to_string())?;
            }
        }

        conn.execute("PRAGMA user_version = 2;", []).map_err(|e| e.to_string())?;
    }

    Ok(conn)
}

// Settings helpers
pub fn get_setting(conn: &Connection, key: &str) -> Result<Option<String>, String> {
    let mut stmt = conn
        .prepare("SELECT value FROM settings WHERE key = ?1")
        .map_err(|e| e.to_string())?;
    let mut rows = stmt.query(params![key]).map_err(|e| e.to_string())?;
    
    if let Some(row) = rows.next().map_err(|e| e.to_string())? {
        let value: String = row.get(0).map_err(|e| e.to_string())?;
        Ok(Some(value))
    } else {
        Ok(None)
    }
}

pub fn set_setting(conn: &Connection, key: &str, value: &str) -> Result<(), String> {
    conn.execute(
        "INSERT OR REPLACE INTO settings (key, value) VALUES (?1, ?2)",
        params![key, value],
    )
    .map(|_| ())
    .map_err(|e| e.to_string())
}

// Credentials helpers
pub fn add_credential(conn: &Connection, cred: &Credential) -> Result<(), String> {
    conn.execute(
        "INSERT INTO credentials (id, name, type, username, encrypted_secret) VALUES (?1, ?2, ?3, ?4, ?5)",
        params![cred.id, cred.name, cred.r#type, cred.username, cred.encrypted_secret],
    )
    .map(|_| ())
    .map_err(|e| e.to_string())
}

pub fn update_credential(conn: &Connection, cred: &Credential) -> Result<(), String> {
    conn.execute(
        "UPDATE credentials SET name = ?1, type = ?2, username = ?3, encrypted_secret = ?4, updated_at = CURRENT_TIMESTAMP WHERE id = ?5",
        params![cred.name, cred.r#type, cred.username, cred.encrypted_secret, cred.id],
    )
    .map(|_| ())
    .map_err(|e| e.to_string())
}

pub fn delete_credential(conn: &Connection, id: &str) -> Result<(), String> {
    conn.execute("DELETE FROM credentials WHERE id = ?1", params![id])
        .map(|_| ())
        .map_err(|e| e.to_string())
}

pub fn get_credentials(conn: &Connection) -> Result<Vec<Credential>, String> {
    let mut stmt = conn
        .prepare("SELECT id, name, type, username, encrypted_secret, created_at, updated_at FROM credentials ORDER BY name ASC")
        .map_err(|e| e.to_string())?;
    
    let rows = stmt
        .query_map([], |row| {
            Ok(Credential {
                id: row.get(0)?,
                name: row.get(1)?,
                r#type: row.get(2)?,
                username: row.get(3)?,
                encrypted_secret: row.get(4)?,
                created_at: row.get(5)?,
                updated_at: row.get(6)?,
            })
        })
        .map_err(|e| e.to_string())?;

    let mut list = Vec::new();
    for item in rows {
        list.push(item.map_err(|e| e.to_string())?);
    }
    Ok(list)
}

// Servers helpers
pub fn add_server(conn: &Connection, srv: &Server) -> Result<(), String> {
    conn.execute(
        "INSERT INTO servers (id, name, hostname, ip, port, protocol, os, folder_path, tags, description, credential_id, username, encrypted_password, rdp_clipboard, rdp_drives, rdp_printers, rdp_smart_sizing, rdp_audio, rdp_smartcards, rdp_webauthn) 
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20)",
        params![srv.id, srv.name, srv.hostname, srv.ip, srv.port, srv.protocol, srv.os, srv.folder_path, srv.tags, srv.description, srv.credential_id, srv.username, srv.encrypted_password, srv.rdp_clipboard, srv.rdp_drives, srv.rdp_printers, srv.rdp_smart_sizing, srv.rdp_audio, srv.rdp_smartcards, srv.rdp_webauthn],
    )
    .map(|_| ())
    .map_err(|e| e.to_string())
}

pub fn update_server(conn: &Connection, srv: &Server) -> Result<(), String> {
    conn.execute(
        "UPDATE servers SET name = ?1, hostname = ?2, ip = ?3, port = ?4, protocol = ?5, os = ?6, folder_path = ?7, tags = ?8, description = ?9, credential_id = ?10, username = ?11, encrypted_password = ?12, rdp_clipboard = ?13, rdp_drives = ?14, rdp_printers = ?15, rdp_smart_sizing = ?16, rdp_audio = ?17, rdp_smartcards = ?18, rdp_webauthn = ?19, updated_at = CURRENT_TIMESTAMP WHERE id = ?20",
        params![srv.name, srv.hostname, srv.ip, srv.port, srv.protocol, srv.os, srv.folder_path, srv.tags, srv.description, srv.credential_id, srv.username, srv.encrypted_password, srv.rdp_clipboard, srv.rdp_drives, srv.rdp_printers, srv.rdp_smart_sizing, srv.rdp_audio, srv.rdp_smartcards, srv.rdp_webauthn, srv.id],
    )
    .map(|_| ())
    .map_err(|e| e.to_string())
}

pub fn delete_server(conn: &Connection, id: &str) -> Result<(), String> {
    conn.execute("DELETE FROM servers WHERE id = ?1", params![id])
        .map(|_| ())
        .map_err(|e| e.to_string())
}

pub fn get_servers(conn: &Connection) -> Result<Vec<Server>, String> {
    let mut stmt = conn
        .prepare("SELECT id, name, hostname, ip, port, protocol, os, folder_path, tags, description, credential_id, username, encrypted_password, created_at, updated_at, rdp_clipboard, rdp_drives, rdp_printers, rdp_smart_sizing, rdp_audio, rdp_smartcards, rdp_webauthn FROM servers ORDER BY name ASC")
        .map_err(|e| e.to_string())?;
    
    let rows = stmt
        .query_map([], |row| {
            Ok(Server {
                id: row.get(0)?,
                name: row.get(1)?,
                hostname: row.get(2)?,
                ip: row.get(3)?,
                port: row.get(4)?,
                protocol: row.get(5)?,
                os: row.get(6)?,
                folder_path: row.get(7)?,
                tags: row.get(8)?,
                description: row.get(9)?,
                credential_id: row.get(10)?,
                username: row.get(11)?,
                encrypted_password: row.get(12)?,
                created_at: row.get(13)?,
                updated_at: row.get(14)?,
                rdp_clipboard: row.get(15)?,
                rdp_drives: row.get(16)?,
                rdp_printers: row.get(17)?,
                rdp_smart_sizing: row.get(18)?,
                rdp_audio: row.get(19)?,
                rdp_smartcards: row.get(20)?,
                rdp_webauthn: row.get(21)?,
            })
        })
        .map_err(|e| e.to_string())?;

    let mut list = Vec::new();
    for item in rows {
        list.push(item.map_err(|e| e.to_string())?);
    }
    Ok(list)
}

// Connection History helpers
pub fn add_history(conn: &Connection, hist: &ConnectionHistory) -> Result<(), String> {
    conn.execute(
        "INSERT INTO connection_history (id, server_id, status, log) VALUES (?1, ?2, ?3, ?4)",
        params![hist.id, hist.server_id, hist.status, hist.log],
    )
    .map(|_| ())
    .map_err(|e| e.to_string())
}

pub fn get_history(conn: &Connection, server_id: &str) -> Result<Vec<ConnectionHistory>, String> {
    let mut stmt = conn
        .prepare("SELECT id, server_id, timestamp, status, log FROM connection_history WHERE server_id = ?1 ORDER BY timestamp DESC")
        .map_err(|e| e.to_string())?;
    
    let rows = stmt
        .query_map(params![server_id], |row| {
            Ok(ConnectionHistory {
                id: row.get(0)?,
                server_id: row.get(1)?,
                timestamp: row.get(2)?,
                status: row.get(3)?,
                log: row.get(4)?,
            })
        })
        .map_err(|e| e.to_string())?;

    let mut list = Vec::new();
    for item in rows {
        list.push(item.map_err(|e| e.to_string())?);
    }
    Ok(list)
}
