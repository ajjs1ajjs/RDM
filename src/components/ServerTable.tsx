import React, { useState } from "react";
import { Server } from "../types";
import { Plus, Search, Play, Edit3, Trash2, Monitor, Terminal, Star } from "lucide-react";

interface ServerTableProps {
  servers: Server[];
  selectedFolder: string;
  selectedTag: string;
  favorites: string[];
  onConnect: (server: Server) => void;
  onEdit: (server: Server) => void;
  onDelete: (id: string) => void;
  onAddServer: () => void;
  onImportCSV: () => void;
  onToggleFavorite: (id: string) => void;
}

export const ServerTable: React.FC<ServerTableProps> = ({
  servers,
  selectedFolder,
  selectedTag,
  favorites,
  onConnect,
  onEdit,
  onDelete,
  onAddServer,
  onImportCSV,
  onToggleFavorite,
}) => {
  const [search, setSearch] = useState<string>("");

  // Filter servers
  const filteredServers = servers.filter((s) => {
    // 1. Search term match
    const term = search.toLowerCase();
    const matchesSearch =
      !search ||
      s.name.toLowerCase().includes(term) ||
      s.hostname.toLowerCase().includes(term) ||
      s.ip.toLowerCase().includes(term) ||
      s.tags.toLowerCase().includes(term) ||
      s.protocol.toLowerCase().includes(term) ||
      s.os.toLowerCase().includes(term);

    // 2. Folder match
    const matchesFolder = !selectedFolder || s.folder_path === selectedFolder || s.folder_path.startsWith(selectedFolder + "/");

    // 3. Tag match
    const matchesTag =
      !selectedTag ||
      s.tags
        .split(",")
        .map((t) => t.trim().toLowerCase())
        .includes(selectedTag.toLowerCase());

    return matchesSearch && matchesFolder && matchesTag;
  });

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
                return (
                  <tr key={s.id} onDoubleClick={() => onConnect(s)}>
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
