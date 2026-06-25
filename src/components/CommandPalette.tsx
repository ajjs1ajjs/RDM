import React, { useState, useEffect, useRef } from "react";
import { Server } from "../types";
import { Terminal, Monitor } from "lucide-react";

interface CommandPaletteProps {
  servers: Server[];
  isOpen: boolean;
  onClose: () => void;
  onSelectServer: (server: Server) => void;
}

export const CommandPalette: React.FC<CommandPaletteProps> = ({
  servers,
  isOpen,
  onClose,
  onSelectServer,
}) => {
  const [search, setSearch] = useState<string>("");
  const [selectedIndex, setSelectedIndex] = useState<number>(0);
  const inputRef = useRef<HTMLInputElement>(null);

  // Filter servers based on input
  const filtered = servers.filter((s) => {
    const term = search.toLowerCase();
    return (
      s.name.toLowerCase().includes(term) ||
      s.hostname.toLowerCase().includes(term) ||
      s.ip.toLowerCase().includes(term) ||
      s.tags.toLowerCase().includes(term)
    );
  }).slice(0, 8); // Max 8 results

  useEffect(() => {
    if (isOpen) {
      setSearch("");
      setSelectedIndex(0);
      setTimeout(() => {
        inputRef.current?.focus();
      }, 50);
    }
  }, [isOpen]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (!isOpen) return;

      if (e.key === "Escape") {
        onClose();
      } else if (e.key === "ArrowDown") {
        e.preventDefault();
        setSelectedIndex((prev) => (prev + 1) % Math.max(1, filtered.length));
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        setSelectedIndex((prev) => (prev - 1 + filtered.length) % Math.max(1, filtered.length));
      } else if (e.key === "Enter") {
        e.preventDefault();
        if (filtered[selectedIndex]) {
          onSelectServer(filtered[selectedIndex]);
          onClose();
        }
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, filtered, selectedIndex]);

  if (!isOpen) return null;

  return (
    <div className="cmd-palette-overlay" onClick={onClose}>
      <div className="cmd-palette-box" onClick={(e) => e.stopPropagation()}>
        <div style={{ display: "flex", alignItems: "center" }}>
          <input
            ref={inputRef}
            type="text"
            className="cmd-palette-input"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setSelectedIndex(0);
            }}
            placeholder="Type server name or IP... (Enter to connect, Esc to exit)"
          />
        </div>
        <div className="cmd-palette-results">
          {filtered.length === 0 ? (
            <div style={{ padding: "20px", textAlign: "center", color: "var(--text-muted)", fontSize: "0.9rem" }}>
              No connections found matching search.
            </div>
          ) : (
            filtered.map((s, idx) => (
              <div
                key={s.id}
                className={`cmd-palette-item ${idx === selectedIndex ? "active" : ""}`}
                onClick={() => {
                  onSelectServer(s);
                  onClose();
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: "12px" }}>
                  {s.protocol === "ssh" ? (
                    <Terminal size={16} style={{ color: "var(--accent-cyan)" }} />
                  ) : (
                    <Monitor size={16} style={{ color: "var(--accent-purple)" }} />
                  )}
                  <div>
                    <div style={{ fontWeight: 500, fontSize: "0.9rem" }}>{s.name}</div>
                    <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                      {s.hostname || s.ip} &bull; {s.folder_path || "Root"}
                    </div>
                  </div>
                </div>
                <div style={{ display: "flex", gap: "4px" }}>
                  {s.tags.split(",").map(t => t.trim()).filter(Boolean).map(tag => (
                    <span key={tag} className="tag-badge" style={{ fontSize: "0.65rem", padding: "1px 6px" }}>
                      {tag}
                    </span>
                  ))}
                </div>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
};
