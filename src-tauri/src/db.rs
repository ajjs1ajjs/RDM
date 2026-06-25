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
            FOREIGN KEY(credential_id) REFERENCES credentials(id) ON DELETE SET NULL
        );",
        [],
    ).map_err(|e| e.to_string())?;

    // Migration to add custom credentials columns if updating from older database versions
    let _ = conn.execute("ALTER TABLE servers ADD COLUMN username TEXT;", []);
    let _ = conn.execute("ALTER TABLE servers ADD COLUMN encrypted_password TEXT;", []);

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

    // Auto-fix any misconfigured RDP servers imported as SSH on port 3389
    let _ = conn.execute(
        "UPDATE servers SET protocol = 'rdp', os = 'windows' WHERE port = 3389 AND protocol = 'ssh';",
        [],
    );

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
        "INSERT INTO servers (id, name, hostname, ip, port, protocol, os, folder_path, tags, description, credential_id, username, encrypted_password) 
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13)",
        params![srv.id, srv.name, srv.hostname, srv.ip, srv.port, srv.protocol, srv.os, srv.folder_path, srv.tags, srv.description, srv.credential_id, srv.username, srv.encrypted_password],
    )
    .map(|_| ())
    .map_err(|e| e.to_string())
}

pub fn update_server(conn: &Connection, srv: &Server) -> Result<(), String> {
    conn.execute(
        "UPDATE servers SET name = ?1, hostname = ?2, ip = ?3, port = ?4, protocol = ?5, os = ?6, folder_path = ?7, tags = ?8, description = ?9, credential_id = ?10, username = ?11, encrypted_password = ?12, updated_at = CURRENT_TIMESTAMP WHERE id = ?13",
        params![srv.name, srv.hostname, srv.ip, srv.port, srv.protocol, srv.os, srv.folder_path, srv.tags, srv.description, srv.credential_id, srv.username, srv.encrypted_password, srv.id],
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
        .prepare("SELECT id, name, hostname, ip, port, protocol, os, folder_path, tags, description, credential_id, username, encrypted_password, created_at, updated_at FROM servers ORDER BY name ASC")
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
