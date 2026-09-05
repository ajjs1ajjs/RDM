using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace RDM
{
    public class Server
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string hostname { get; set; } = "";
        public string ip { get; set; } = "";
        public long port { get; set; } = 3389;
        public string protocol { get; set; } = "";
        public string os { get; set; } = "";
        public string folder_path { get; set; } = "";
        public string tags { get; set; } = "";
        public string description { get; set; } = "";
        public string? credential_id { get; set; }
        public string? username { get; set; }
        public string? encrypted_password { get; set; }
        public string created_at { get; set; } = "";
        public string updated_at { get; set; } = "";
        public long? rdp_clipboard { get; set; } = 1;
        public long? rdp_drives { get; set; } = 0;
        public long? rdp_printers { get; set; } = 0;
        public long? rdp_smart_sizing { get; set; } = 1;
        public long? rdp_audio { get; set; } = 0;
        public long? rdp_smartcards { get; set; } = 0;
        public long? rdp_webauthn { get; set; } = 0;
        public long? rdp_fullscreen { get; set; } = 0;
        public long? rdp_multimon { get; set; } = 0;
    }

    public class Credential
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string type { get; set; } = "";
        public string username { get; set; } = "";
        public string encrypted_secret { get; set; } = "";
        public string created_at { get; set; } = "";
        public string updated_at { get; set; } = "";
    }

    public class ConnectionHistory
    {
        public string id { get; set; } = "";
        public string server_id { get; set; } = "";
        public string timestamp { get; set; } = "";
        public string status { get; set; } = "";
        public string log { get; set; } = "";
    }

    public static class Db
    {
        private static string GetConnectionString(string appDir)
        {
            if (!Directory.Exists(appDir))
            {
                Directory.CreateDirectory(appDir);
            }
            string dbPath = Path.Combine(appDir, "rdm.db");
            return $"Data Source={dbPath}";
        }

        public static void InitDb(string appDir)
        {
            string connectionString = GetConnectionString(appDir);
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_keys = ON;";
                    cmd.ExecuteNonQuery();

                    // Create tables
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS settings (
                            key TEXT PRIMARY KEY,
                            value TEXT NOT NULL
                        );";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS credentials (
                            id TEXT PRIMARY KEY,
                            name TEXT NOT NULL,
                            type TEXT NOT NULL,
                            username TEXT NOT NULL,
                            encrypted_secret TEXT NOT NULL,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        );";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS servers (
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
                            rdp_fullscreen INTEGER DEFAULT 0,
                            rdp_multimon INTEGER DEFAULT 0,
                            FOREIGN KEY(credential_id) REFERENCES credentials(id) ON DELETE SET NULL
                        );";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS connection_history (
                            id TEXT PRIMARY KEY,
                            server_id TEXT NOT NULL,
                            timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                            status TEXT NOT NULL,
                            log TEXT NOT NULL,
                            FOREIGN KEY(server_id) REFERENCES servers(id) ON DELETE CASCADE
                        );";
                    cmd.ExecuteNonQuery();

                    // Safe versioned schema migration
                    cmd.CommandText = "PRAGMA user_version;";
                    long currentVersion = (long)(cmd.ExecuteScalar() ?? 0L);

                    if (currentVersion < 2)
                    {
                        AddColumnIfNotExists(conn, "servers", "username", "TEXT");
                        AddColumnIfNotExists(conn, "servers", "encrypted_password", "TEXT");
                        AddColumnIfNotExists(conn, "servers", "rdp_clipboard", "INTEGER DEFAULT 1");
                        AddColumnIfNotExists(conn, "servers", "rdp_drives", "INTEGER DEFAULT 0");
                        AddColumnIfNotExists(conn, "servers", "rdp_printers", "INTEGER DEFAULT 0");
                        AddColumnIfNotExists(conn, "servers", "rdp_smart_sizing", "INTEGER DEFAULT 1");
                        AddColumnIfNotExists(conn, "servers", "rdp_audio", "INTEGER DEFAULT 0");
                        AddColumnIfNotExists(conn, "servers", "rdp_smartcards", "INTEGER DEFAULT 0");
                        AddColumnIfNotExists(conn, "servers", "rdp_webauthn", "INTEGER DEFAULT 0");

                        cmd.CommandText = "PRAGMA user_version = 2;";
                        cmd.ExecuteNonQuery();
                    }

                    if (currentVersion < 3)
                    {
                        AddColumnIfNotExists(conn, "servers", "rdp_fullscreen", "INTEGER DEFAULT 0");
                        AddColumnIfNotExists(conn, "servers", "rdp_multimon", "INTEGER DEFAULT 0");

                        cmd.CommandText = "PRAGMA user_version = 3;";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static void AddColumnIfNotExists(SqliteConnection conn, string table, string column, string type)
        {
            // Whitelist validation to prevent SQL injection
            if (table != "servers")
                throw new ArgumentException("Invalid table name");
            if (!System.Text.RegularExpressions.Regex.IsMatch(column, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                throw new ArgumentException("Invalid column name");
            if (!System.Text.RegularExpressions.Regex.IsMatch(type, @"^(TEXT|INTEGER(\s+DEFAULT\s+\d+)?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new ArgumentException("Invalid column type");

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({table});";
                bool exists = false;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }

                if (!exists)
                {
                    cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // Settings API
        public static string? GetSetting(string appDir, string key)
        {
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT value FROM settings WHERE key = $key";
                    cmd.Parameters.AddWithValue("$key", key);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return reader.GetString(0);
                        }
                    }
                }
            }
            return null;
        }

        public static void SetSetting(string appDir, string key, string value)
        {
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES ($key, $val)";
                    cmd.Parameters.AddWithValue("$key", key);
                    cmd.Parameters.AddWithValue("$val", value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // Credentials API
        public static void AddCredential(string appDir, Credential cred)
        {
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO credentials (id, name, type, username, encrypted_secret) VALUES ($id, $name, $type, $username, $secret)";
                    cmd.Parameters.AddWithValue("$id", cred.id);
                    cmd.Parameters.AddWithValue("$name", cred.name);
                    cmd.Parameters.AddWithValue("$type", cred.type);
                    cmd.Parameters.AddWithValue("$username", cred.username);
                    cmd.Parameters.AddWithValue("$secret", cred.encrypted_secret);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void UpdateCredential(string appDir, Credential cred)
        {
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE credentials SET name = $name, type = $type, username = $username, encrypted_secret = $secret, updated_at = CURRENT_TIMESTAMP WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", cred.id);
                    cmd.Parameters.AddWithValue("$name", cred.name);
                    cmd.Parameters.AddWithValue("$type", cred.type);
                    cmd.Parameters.AddWithValue("$username", cred.username);
                    cmd.Parameters.AddWithValue("$secret", cred.encrypted_secret);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void DeleteCredential(string appDir, string id)
        {
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM credentials WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static List<Credential> GetCredentials(string appDir)
        {
            var list = new List<Credential>();
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, name, type, username, encrypted_secret, datetime(created_at, 'localtime'), datetime(updated_at, 'localtime') FROM credentials ORDER BY name ASC";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new Credential
                            {
                                id = reader.GetString(0),
                                name = reader.GetString(1),
                                type = reader.GetString(2),
                                username = reader.GetString(3),
                                encrypted_secret = reader.GetString(4),
                                created_at = reader.GetString(5),
                                updated_at = reader.GetString(6)
                            });
                        }
                    }
                }
            }
            return list;
        }

        // Servers API
        public static void AddServer(string appDir, Server srv)
        {
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO servers (id, name, hostname, ip, port, protocol, os, folder_path, tags, description, credential_id, username, encrypted_password, rdp_clipboard, rdp_drives, rdp_printers, rdp_smart_sizing, rdp_audio, rdp_smartcards, rdp_webauthn, rdp_fullscreen, rdp_multimon)
                        VALUES ($id, $name, $hostname, $ip, $port, $protocol, $os, $folder_path, $tags, $description, $cred_id, $username, $enc_pass, $rdp_clip, $rdp_drv, $rdp_prn, $rdp_sz, $rdp_aud, $rdp_sc, $rdp_wa, $rdp_fs, $rdp_mm)";
                    cmd.Parameters.AddWithValue("$id", srv.id);
                    cmd.Parameters.AddWithValue("$name", srv.name);
                    cmd.Parameters.AddWithValue("$hostname", srv.hostname);
                    cmd.Parameters.AddWithValue("$ip", srv.ip);
                    cmd.Parameters.AddWithValue("$port", srv.port);
                    cmd.Parameters.AddWithValue("$protocol", srv.protocol);
                    cmd.Parameters.AddWithValue("$os", srv.os);
                    cmd.Parameters.AddWithValue("$folder_path", srv.folder_path);
                    cmd.Parameters.AddWithValue("$tags", srv.tags);
                    cmd.Parameters.AddWithValue("$description", srv.description);
                    cmd.Parameters.AddWithValue("$cred_id", (object?)srv.credential_id ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$username", (object?)srv.username ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$enc_pass", (object?)srv.encrypted_password ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$rdp_clip", srv.rdp_clipboard ?? 1L);
                    cmd.Parameters.AddWithValue("$rdp_drv", srv.rdp_drives ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_prn", srv.rdp_printers ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_sz", srv.rdp_smart_sizing ?? 1L);
                    cmd.Parameters.AddWithValue("$rdp_aud", srv.rdp_audio ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_sc", srv.rdp_smartcards ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_wa", srv.rdp_webauthn ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_fs", srv.rdp_fullscreen ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_mm", srv.rdp_multimon ?? 0L);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void UpdateServer(string appDir, Server srv)
        {
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        UPDATE servers SET name = $name, hostname = $hostname, ip = $ip, port = $port, protocol = $protocol, os = $os, folder_path = $folder_path, tags = $tags, description = $description, credential_id = $cred_id, username = $username, encrypted_password = $enc_pass, rdp_clipboard = $rdp_clip, rdp_drives = $rdp_drv, rdp_printers = $rdp_prn, rdp_smart_sizing = $rdp_sz, rdp_audio = $rdp_aud, rdp_smartcards = $rdp_sc, rdp_webauthn = $rdp_wa, rdp_fullscreen = $rdp_fs, rdp_multimon = $rdp_mm, updated_at = CURRENT_TIMESTAMP WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", srv.id);
                    cmd.Parameters.AddWithValue("$name", srv.name);
                    cmd.Parameters.AddWithValue("$hostname", srv.hostname);
                    cmd.Parameters.AddWithValue("$ip", srv.ip);
                    cmd.Parameters.AddWithValue("$port", srv.port);
                    cmd.Parameters.AddWithValue("$protocol", srv.protocol);
                    cmd.Parameters.AddWithValue("$os", srv.os);
                    cmd.Parameters.AddWithValue("$folder_path", srv.folder_path);
                    cmd.Parameters.AddWithValue("$tags", srv.tags);
                    cmd.Parameters.AddWithValue("$description", srv.description);
                    cmd.Parameters.AddWithValue("$cred_id", (object?)srv.credential_id ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$username", (object?)srv.username ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$enc_pass", (object?)srv.encrypted_password ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$rdp_clip", srv.rdp_clipboard ?? 1L);
                    cmd.Parameters.AddWithValue("$rdp_drv", srv.rdp_drives ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_prn", srv.rdp_printers ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_sz", srv.rdp_smart_sizing ?? 1L);
                    cmd.Parameters.AddWithValue("$rdp_aud", srv.rdp_audio ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_sc", srv.rdp_smartcards ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_wa", srv.rdp_webauthn ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_fs", srv.rdp_fullscreen ?? 0L);
                    cmd.Parameters.AddWithValue("$rdp_mm", srv.rdp_multimon ?? 0L);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void DeleteServer(string appDir, string id)
        {
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM servers WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static List<Server> GetServers(string appDir)
        {
            var list = new List<Server>();
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, name, hostname, ip, port, protocol, os, folder_path, tags, description, credential_id, username, encrypted_password, datetime(created_at, 'localtime'), datetime(updated_at, 'localtime'), rdp_clipboard, rdp_drives, rdp_printers, rdp_smart_sizing, rdp_audio, rdp_smartcards, rdp_webauthn, rdp_fullscreen, rdp_multimon FROM servers ORDER BY name ASC";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new Server
                            {
                                id = reader.GetString(0),
                                name = reader.GetString(1),
                                hostname = reader.GetString(2),
                                ip = reader.GetString(3),
                                port = reader.GetInt64(4),
                                protocol = reader.GetString(5),
                                os = reader.GetString(6),
                                folder_path = reader.GetString(7),
                                tags = reader.GetString(8),
                                description = reader.GetString(9),
                                credential_id = reader.IsDBNull(10) ? null : reader.GetString(10),
                                username = reader.IsDBNull(11) ? null : reader.GetString(11),
                                encrypted_password = reader.IsDBNull(12) ? null : reader.GetString(12),
                                created_at = reader.GetString(13),
                                updated_at = reader.GetString(14),
                                rdp_clipboard = reader.IsDBNull(15) ? null : reader.GetInt64(15),
                                rdp_drives = reader.IsDBNull(16) ? null : reader.GetInt64(16),
                                rdp_printers = reader.IsDBNull(17) ? null : reader.GetInt64(17),
                                rdp_smart_sizing = reader.IsDBNull(18) ? null : reader.GetInt64(18),
                                rdp_audio = reader.IsDBNull(19) ? null : reader.GetInt64(19),
                                rdp_smartcards = reader.IsDBNull(20) ? null : reader.GetInt64(20),
                                rdp_webauthn = reader.IsDBNull(21) ? null : reader.GetInt64(21),
                                rdp_fullscreen = reader.IsDBNull(22) ? null : reader.GetInt64(22),
                                rdp_multimon = reader.IsDBNull(23) ? null : reader.GetInt64(23)
                            });
                        }
                    }
                }
            }
            return list;
        }

        // History API
        public static void AddHistory(string appDir, ConnectionHistory hist)
        {
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO connection_history (id, server_id, status, log) VALUES ($id, $server_id, $status, $log)";
                    cmd.Parameters.AddWithValue("$id", hist.id);
                    cmd.Parameters.AddWithValue("$server_id", hist.server_id);
                    cmd.Parameters.AddWithValue("$status", hist.status);
                    cmd.Parameters.AddWithValue("$log", hist.log);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static List<ConnectionHistory> GetHistory(string appDir, string serverId)
        {
            var list = new List<ConnectionHistory>();
            using (var conn = new SqliteConnection(GetConnectionString(appDir)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, server_id, datetime(timestamp, 'localtime'), status, log FROM connection_history WHERE server_id = $server_id ORDER BY timestamp DESC";
                    cmd.Parameters.AddWithValue("$server_id", serverId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new ConnectionHistory
                            {
                                id = reader.GetString(0),
                                server_id = reader.GetString(1),
                                timestamp = reader.GetString(2),
                                status = reader.GetString(3),
                                log = reader.GetString(4)
                            });
                        }
                    }
                }
            }
            return list;
        }
    }
}
