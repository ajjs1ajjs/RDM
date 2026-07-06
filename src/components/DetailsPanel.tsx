import React, { useState, useEffect, useRef } from "react";
import { Server, Credential, ConnectionHistory } from "../types";
import { invoke } from "@tauri-apps/api/core";
import { useDialogs } from "./AppDialogs";
import { Copy, Eye, EyeOff, ClipboardCheck, Terminal, Info } from "lucide-react";

interface DetailsPanelProps {
  server: Server | null;
  credentials: Credential[];
  onConnect: (server: Server) => void;
  onEdit: (server: Server) => void;
}

export const DetailsPanel: React.FC<DetailsPanelProps> = ({
  server,
  credentials,
  onConnect,
  onEdit,
}) => {
  const dialogs = useDialogs();
  const [history, setHistory] = useState<ConnectionHistory[]>([]);
  const [loadingHistory, setLoadingHistory] = useState<boolean>(false);
  const [copyingPassword, setCopyingPassword] = useState<boolean>(false);
  const [revealingPassword, setRevealingPassword] = useState<boolean>(false);
  const [decryptedSecret, setDecryptedSecret] = useState<string>("");

  const timerRef = useRef<number | null>(null);

  const associatedCred = credentials.find((c) => c.id === server?.credential_id);

  useEffect(() => {
    if (server) {
      loadHistory();
      setDecryptedSecret("");
      setRevealingPassword(false);
    }

    return () => {
      if (timerRef.current) {
        clearTimeout(timerRef.current);
        timerRef.current = null;
      }
    };
  }, [server]);

  const loadHistory = async () => {
    if (!server) return;
    setLoadingHistory(true);
    try {
      const logs = await invoke<ConnectionHistory[]>("get_connection_history", {
        serverId: server.id,
      });
      setHistory(logs);
    } catch (err) {
      console.error("Failed to load connection history:", err);
    } finally {
      setLoadingHistory(false);
    }
  };

  const handleCopyUsername = () => {
    if (!server) return;
    const user = associatedCred ? associatedCred.username : (server.username || "");
    if (!user) return;
    navigator.clipboard.writeText(user);
  };

  const handleRevealPassword = async () => {
    if (!server) return;
    if (revealingPassword) {
      setRevealingPassword(false);
      return;
    }

    try {
      let plain = "";
      if (associatedCred) {
        plain = await invoke<string>("decrypt_credential_secret", {
          id: associatedCred.id,
        });
      } else if (server.encrypted_password) {
        plain = await invoke<string>("decrypt_server_password", {
          id: server.id,
        });
      }
      setDecryptedSecret(plain);
      setRevealingPassword(true);
    } catch (err) {
      await dialogs.alert("Failed to decrypt credentials. Ensure Vault is unlocked.");
    }
  };

  const handleCopyPassword = async () => {
    if (!server) return;
    try {
      let plain = "";
      if (associatedCred) {
        plain = await invoke<string>("decrypt_credential_secret", {
          id: associatedCred.id,
        });
      } else if (server.encrypted_password) {
        plain = await invoke<string>("decrypt_server_password", {
          id: server.id,
        });
      }
      if (!plain) return;
      
      // Copy to clipboard
      await navigator.clipboard.writeText(plain);
      setCopyingPassword(true);

      if (timerRef.current) {
        clearTimeout(timerRef.current);
      }

      // Auto-clear clipboard after 15 seconds
      timerRef.current = window.setTimeout(async () => {
        await navigator.clipboard.writeText("").catch(() => {});
        setCopyingPassword(false);
        timerRef.current = null;
      }, 15000);
    } catch (err) {
      await dialogs.alert("Failed to decrypt and copy secret.");
    }
  };

  if (!server) {
    return (
      <div className="details-pane" style={{ justifyContent: "center", alignItems: "center", opacity: 0.5 }}>
        <Info size={32} style={{ marginBottom: "10px" }} />
        <span style={{ fontSize: "0.85rem" }}>Select a server to view details</span>
      </div>
    );
  }

  return (
    <div className="details-pane">
      <div>
        <h2 style={{ fontSize: "1.1rem", marginBottom: "4px" }}>{server.name}</h2>
        <span style={{ fontSize: "0.8rem", color: "var(--text-muted)", fontFamily: "var(--font-mono)" }}>
          ID: {server.id.substring(0, 8)}...
        </span>
      </div>

      <div>
        <div className="details-section-title">Connection Info</div>
        <div className="details-row">
          <div className="details-label">Host/IP Address</div>
          <div className="details-value">{server.hostname || server.ip}</div>
        </div>
        <div className="details-row">
          <div className="details-label">Port</div>
          <div className="details-value mono">{server.port}</div>
        </div>
        <div className="details-row">
          <div className="details-label">Protocol</div>
          <div className="details-value" style={{ textTransform: "uppercase", fontWeight: 600, color: "var(--accent-cyan)" }}>
            {server.protocol}
          </div>
        </div>
        <div className="details-row">
          <div className="details-label">Description</div>
          <div className="details-value">{server.description || "No description provided."}</div>
        </div>
      </div>

      {server.protocol === 'rdp' && (
        <div>
          <div className="details-section-title">RDP Redirection Settings</div>
          <div className="details-row">
            <div className="details-label">Clipboard</div>
            <div className="details-value">{server.rdp_clipboard !== 0 ? "Enabled" : "Disabled"}</div>
          </div>
          <div className="details-row">
            <div className="details-label">Smart Sizing</div>
            <div className="details-value">{server.rdp_smart_sizing !== 0 ? "Enabled" : "Disabled"}</div>
          </div>
          <div className="details-row">
            <div className="details-label">Local Drives</div>
            <div className="details-value">{server.rdp_drives !== 0 ? "Enabled" : "Disabled"}</div>
          </div>
          <div className="details-row">
            <div className="details-label">Printers</div>
            <div className="details-value">{server.rdp_printers !== 0 ? "Enabled" : "Disabled"}</div>
          </div>
          <div className="details-row">
            <div className="details-label">Smart Cards</div>
            <div className="details-value">{server.rdp_smartcards !== 0 ? "Enabled" : "Disabled"}</div>
          </div>
          <div className="details-row">
            <div className="details-label">WebAuthn</div>
            <div className="details-value">{server.rdp_webauthn !== 0 ? "Enabled" : "Disabled"}</div>
          </div>
          <div className="details-row">
            <div className="details-label">Audio</div>
            <div className="details-value">
              {server.rdp_audio === 0 ? "Play locally" : server.rdp_audio === 1 ? "Play on remote" : "Muted"}
            </div>
          </div>
        </div>
      )}

      {(associatedCred || server.username) && (
        <div>
          <div className="details-section-title">Credentials</div>
          {associatedCred ? (
            <div className="details-row">
              <div className="details-label">Linked Account</div>
              <div className="details-value">{associatedCred.name}</div>
            </div>
          ) : (
            <div className="details-row">
              <div className="details-label">Auth Mode</div>
              <div className="details-value">Custom / Manual</div>
            </div>
          )}
          <div className="details-row">
            <div className="details-label">Username</div>
            <div className="details-value" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <span>{associatedCred ? associatedCred.username : server.username}</span>
              <button
                className="btn btn-secondary"
                style={{ padding: "4px 6px" }}
                onClick={handleCopyUsername}
                title="Copy Username"
              >
                <Copy size={12} />
              </button>
            </div>
          </div>
          {(associatedCred || server.encrypted_password) && (
            <div className="details-row" style={{ marginTop: "10px" }}>
              <div style={{ display: "flex", gap: "8px" }}>
                <button className="btn btn-secondary" style={{ flexGrow: 1, padding: "8px" }} onClick={handleRevealPassword}>
                  {revealingPassword ? <EyeOff size={14} /> : <Eye size={14} />}
                  <span style={{ fontSize: "0.8rem", marginLeft: "4px" }}>{revealingPassword ? "Hide" : "Show"}</span>
                </button>
                <button className="btn btn-secondary" style={{ flexGrow: 1, padding: "8px" }} onClick={handleCopyPassword} disabled={copyingPassword}>
                  {copyingPassword ? <ClipboardCheck size={14} style={{ color: "var(--accent-green)" }} /> : <Copy size={14} />}
                  <span style={{ fontSize: "0.8rem", marginLeft: "4px" }}>
                    {copyingPassword ? "Copied (15s)" : "Copy Secret"}
                  </span>
                </button>
              </div>
              {revealingPassword && (
                <div
                  className="details-value mono"
                  style={{
                    marginTop: "8px",
                    wordBreak: "break-all",
                    backgroundColor: "rgba(0, 0, 0, 0.4)",
                    padding: "8px",
                    borderRadius: "4px",
                    border: "1px solid var(--border-color)",
                  }}
                >
                  {decryptedSecret}
                </div>
              )}
            </div>
          )}
        </div>
      )}

      <div>
        <div className="details-section-title">Quick Tasks</div>
        <div style={{ display: "flex", flexDirection: "column", gap: "8px" }}>
          <button className="btn btn-primary" onClick={() => onConnect(server)}>
            <Terminal size={14} />
            <span>Connect Shell / Session</span>
          </button>
          <button className="btn btn-secondary" onClick={() => onEdit(server)}>
            <span>Edit Configuration</span>
          </button>
        </div>
      </div>

      <div>
        <div className="details-section-title">Connection Logs</div>
        {loadingHistory ? (
          <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>Loading logs...</div>
        ) : history.length === 0 ? (
          <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>No connection history found.</div>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: "8px" }}>
            {history.map((log) => (
              <div
                key={log.id}
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  fontSize: "0.8rem",
                  padding: "4px 8px",
                  borderLeft: `2px solid ${log.status === "connected" ? "var(--accent-green)" : log.status === "failed" ? "var(--accent-red)" : "var(--text-muted)"}`,
                }}
              >
                <span>{log.status}</span>
                <span style={{ color: "var(--text-muted)" }}>{new Date(log.timestamp).toLocaleTimeString()}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
