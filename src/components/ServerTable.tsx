import React, { useState, useMemo } from "react";
import { Server } from "../types";
import { Plus, Search, Play, Edit3, Trash2, Monitor, Terminal, Star, HardDrive } from "lucide-react";

interface ServerTableProps {
  servers: Server[];
  selectedFolder: string;
  selectedTag: string;
  favorites: string[];
  selectedServer: Server | null;
  onSelectServer: (server: Server) => void;
  onConnect: (server: Server) => void;
  onEdit: (server: Server) => void;
  onDelete: (id: string) => void;
  onAddServer: () => void;
  onImportCSV: () => void;
  onToggleFavorite: (id: string) => void;
  onConnectSFTP: (server: Server) => void;
  onQuickConnect?: (host: string, protocol: string) => void;
}

export const ServerTable: React.FC<ServerTableProps> = ({
  servers,
  selectedFolder,
  selectedTag,
  favorites,
  selectedServer,
  onSelectServer,
  onConnect,
  onEdit,
  onDelete,
  onAddServer,
  onImportCSV,
  onToggleFavorite,
  onConnectSFTP,
  onQuickConnect,
}) => {
  const [search, setSearch] = useState<string>("");

  const filteredServers = useMemo(() => servers.filter((s) => {
    const term = search.toLowerCase();
    const matchesSearch =
      !search ||
      s.name.toLowerCase().includes(term) ||
      s.hostname.toLowerCase().includes(term) ||
      s.ip.toLowerCase().includes(term) ||
      s.tags.toLowerCase().includes(term) ||
      s.protocol.toLowerCase().includes(term) ||
      s.os.toLowerCase().includes(term);

    const matchesFolder = !selectedFolder || s.folder_path === selectedFolder || s.folder_path.startsWith(selectedFolder + "/");

    const matchesTag =
      !selectedTag ||
      s.tags
        .split(",")
        .map((t) => t.trim().toLowerCase())
        .includes(selectedTag.toLowerCase());

    return matchesSearch && matchesFolder && matchesTag;
  }), [servers, search, selectedFolder, selectedTag]);

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", width: "100%" }}>
      <div className="header-row">
        <div className="search-input-wrapper">
          <Search size={18} className="search-icon" />
          <input
            type="text"
            className="search-input"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search servers by name, IP, tag, protocol..."
          />
        </div>

        <div style={{ display: "flex", gap: "10px" }}>
          <button className="btn btn-secondary" onClick={onImportCSV}>
            Import CSV
          </button>
          <button className="btn btn-primary" onClick={onAddServer}>
            <Plus size={16} />
            <span>Add Server</span>
          </button>
        </div>
      </div>

      <div className="server-table-container glass-card" style={{ flexGrow: 1, padding: 0 }}>
        {search.trim() !== "" && onQuickConnect && (
          <div style={{ padding: "10px 20px", borderBottom: "1px solid var(--border-color)", display: "flex", alignItems: "center", gap: "10px", backgroundColor: "rgba(0, 0, 0, 0.2)" }}>
            <span style={{ color: "var(--text-secondary)", fontSize: "0.9rem" }}>Quick connect to <strong>{search}</strong>:</span>
            <button className="btn btn-secondary" style={{ padding: "4px 10px", fontSize: "0.85rem" }} onClick={() => onQuickConnect(search, "rdp")}>
              <Monitor size={14} style={{ marginRight: "4px" }}/> RDP
            </button>
            <button className="btn btn-secondary" style={{ padding: "4px 10px", fontSize: "0.85rem" }} onClick={() => onQuickConnect(search, "ssh")}>
              <Terminal size={14} style={{ marginRight: "4px" }}/> SSH
            </button>
          </div>
        )}
        
        {filteredServers.length === 0 ? (
          <div style={{ padding: "40px", textAlign: "center", color: "var(--text-secondary)" }}>
            No servers found matching the filter criteria.
          </div>
        ) : (
          <table className="server-table">
            <thead>
              <tr>
                <th style={{ width: "40px" }}></th>
                <th>Name</th>
                <th>Host / IP</th>
                <th>Port</th>
                <th>Protocol</th>
                <th>Platform</th>
                <th>Tags</th>
                <th style={{ textAlign: "right" }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredServers.map((s) => {
                const isFavorite = favorites.includes(s.id);
                const isSelected = selectedServer?.id === s.id;
                return (
                  <tr
                    key={s.id}
                    className={isSelected ? "selected" : ""}
                    onClick={() => onSelectServer(s)}
                    onDoubleClick={() => onConnect(s)}
                  >
                    <td onClick={(e) => { e.stopPropagation(); onToggleFavorite(s.id); }}>
                      <Star
                        size={16}
                        style={{
                          cursor: "pointer",
                          fill: isFavorite ? "var(--accent-warn)" : "none",
                          color: isFavorite ? "var(--accent-warn)" : "var(--text-muted)",
                        }}
                      />
                    </td>
                    <td>
                      <div className="server-name-cell">
                        <span className="status-indicator online"></span>
                        <span>{s.name}</span>
                      </div>
                    </td>
                    <td>{s.hostname || s.ip}</td>
                    <td style={{ fontFamily: "var(--font-mono)", fontSize: "0.85rem" }}>{s.port}</td>
                    <td>
                      <span
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "4px",
                          fontSize: "0.8rem",
                          fontWeight: 600,
                          color: s.protocol === "ssh" ? "var(--accent-cyan)" : "var(--accent-purple)",
                        }}
                      >
                        {s.protocol === "ssh" ? <Terminal size={14} /> : <Monitor size={14} />}
                        {s.protocol.toUpperCase()}
                      </span>
                    </td>
                    <td style={{ textTransform: "capitalize" }}>{s.os}</td>
                    <td>
                      {s.tags
                        .split(",")
                        .map((t) => t.trim())
                        .filter(Boolean)
                        .map((tag) => (
                          <span key={tag} className="tag-badge">
                            {tag}
                          </span>
                        ))}
                    </td>
                    <td>
                      <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end" }}>
                        <button
                          className="btn btn-secondary"
                          style={{ padding: "6px 10px" }}
                          onClick={() => onConnect(s)}
                          title="Connect"
                        >
                          <Play size={14} style={{ fill: "var(--accent-green)", color: "var(--accent-green)" }} />
                        </button>
                        {s.protocol.toLowerCase() === "ssh" && (
                          <button
                            className="btn btn-secondary"
                            style={{ padding: "6px 10px", color: "var(--accent-blue)" }}
                            onClick={() => onConnectSFTP(s)}
                            title="SFTP File Manager"
                          >
                            <HardDrive size={14} />
                          </button>
                        )}
                        <button
                          className="btn btn-secondary"
                          style={{ padding: "6px 10px" }}
                          onClick={() => onEdit(s)}
                          title="Edit"
                        >
                          <Edit3 size={14} />
                        </button>
                        <button
                          className="btn btn-danger"
                          style={{ padding: "6px 10px" }}
                          onClick={() => onDelete(s.id)}
                          title="Delete"
                        >
                          <Trash2 size={14} />
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
};
