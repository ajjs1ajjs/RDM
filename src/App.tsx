import React, { useState, useEffect } from "react";
import { invoke } from "@tauri-apps/api/core";
import { useVault } from "./hooks/useVault";
import { useServers } from "./hooks/useServers";
import { useCredentials } from "./hooks/useCredentials";
import { useConnectionTabs } from "./hooks/useConnectionTabs";
import { useServerForm } from "./hooks/useServerForm";
import { useCredForm } from "./hooks/useCredForm";
import { useFolderModal } from "./hooks/useFolderModal";

import { Sidebar } from "./components/Sidebar";
import { ServerTable } from "./components/ServerTable";
import { DetailsPanel } from "./components/DetailsPanel";
import { TerminalTab } from "./components/TerminalTab";
import { RdpTab } from "./components/RdpTab";
import { SftpTab } from "./components/SftpTab";
import { CommandPalette } from "./components/CommandPalette";
import { Taskbar } from "./components/Taskbar";
import { useDialogs } from "./components/AppDialogs";
import { Terminal, X } from "lucide-react";
import "./App.css";

const MigrationDialog: React.FC<{
  migrating: boolean;
  error: string;
  onMigrate: (password: string) => Promise<void>;
  onReset: () => void;
}> = ({ migrating, error, onMigrate, onReset }) => {
  const [password, setPassword] = useState("");
  const [localError, setLocalError] = useState("");

  const handleSubmit = async () => {
    if (!password) return;
    setLocalError("");
    try {
      await onMigrate(password);
    } catch (e: any) {
      const msg = typeof e === "string" ? e : e?.message || String(e);
      setLocalError(msg);
    }
  };

  return (
    <div style={{
      background: "var(--bg-card, #1a1a2e)", border: "1px solid var(--border-color, rgba(255,255,255,0.1))",
      borderRadius: "12px", padding: "32px", width: "380px",
      boxShadow: "0 8px 32px rgba(0,0,0,0.4)",
      display: "flex", flexDirection: "column", alignItems: "center", gap: "16px",
    }}>
      <div style={{ fontSize: "1.1rem", fontWeight: 600, color: "#fff", textAlign: "center" }}>
        Vault Migration Required
      </div>
      <div style={{ fontSize: "0.85rem", color: "var(--text-secondary, rgba(255,255,255,0.6))", textAlign: "center", lineHeight: 1.5 }}>
        Your vault was previously protected with a master password.
        Please enter it to migrate your data to the new encryption key.
      </div>
      <input
        type="password"
        value={password}
        onChange={(e) => { setPassword(e.target.value); setLocalError(""); }}
        onKeyDown={(e) => { if (e.key === "Enter") handleSubmit(); }}
        placeholder="Enter your vault master password"
        autoFocus
        style={{
          width: "100%", padding: "10px 12px",
          background: "rgba(255,255,255,0.05)",
          border: "1px solid rgba(255,255,255,0.1)", borderRadius: "6px",
          color: "#fff", fontFamily: "var(--font-mono, monospace)", fontSize: "0.85rem",
          outline: "none", boxSizing: "border-box",
        }}
      />
      {(localError || error) && (
        <div style={{ fontSize: "0.8rem", color: "#ff6b6b", textAlign: "center", lineHeight: 1.4 }}>
          {localError || error}
        </div>
      )}
      <button
        onClick={handleSubmit}
        disabled={!password || migrating}
        style={{
          width: "100%", padding: "10px",
          background: !password || migrating
            ? "rgba(255,255,255,0.05)"
            : "linear-gradient(90deg, var(--accent-cyan, #00f0ff), var(--accent-purple, #a855f7))",
          border: "none", borderRadius: "6px",
          color: !password || migrating ? "rgba(255,255,255,0.3)" : "#fff",
          cursor: !password || migrating ? "default" : "pointer",
          fontSize: "0.85rem", fontFamily: "var(--font-mono, monospace)",
        }}
      >
        {migrating ? "Migrating..." : "Unlock & Migrate"}
      </button>
      <div style={{ fontSize: "0.75rem", color: "var(--text-muted, rgba(255,255,255,0.3))", textAlign: "center" }}>
        Forgot your password?
      </div>
      <button
        onClick={onReset}
        disabled={migrating}
        style={{
          width: "100%", padding: "8px",
          background: "transparent",
          border: "1px solid rgba(255,100,100,0.3)", borderRadius: "6px",
          color: migrating ? "rgba(255,255,255,0.3)" : "rgba(255,100,100,0.8)",
          cursor: migrating ? "default" : "pointer",
          fontSize: "0.8rem", fontFamily: "var(--font-mono, monospace)",
        }}
      >
        Reset Vault (lose encrypted passwords)
      </button>
    </div>
  );
};

function App() {
  const vault = useVault();
  const serversCtrl = useServers();
  const credentialsCtrl = useCredentials();
  const tabs = useConnectionTabs();

  const serverForm = useServerForm(
    serversCtrl.selectedFolder,
    serversCtrl.loadServers,
    serversCtrl.setSelectedServer,
  );
  const credForm = useCredForm(credentialsCtrl.loadCredentials);

  const folderModal = useFolderModal(
    serversCtrl.servers,
    serversCtrl.customFolders,
    serversCtrl.saveCustomFolders,
    serversCtrl.handleRenameFolder,
    serversCtrl.handleDeleteFolder,
  );

    const dialogs = useDialogs();
  const [cmdPaletteOpen, setCmdPaletteOpen] = useState<boolean>(false);
  const [updateInfo, setUpdateInfo] = useState<{ latest: string; current: string; url: string } | null>(null);

  useEffect(() => {
    vault.checkUnlockStatus();
    serversCtrl.loadServers();
    serversCtrl.loadFavorites();
    serversCtrl.loadCustomFolders();
    checkForUpdate();
  }, []);

  const checkForUpdate = async () => {
    try {
      const res = await invoke<{ available: boolean; latest_version: string; current_version: string; download_url: string }>("check_for_update");
      if (res.available) {
        setUpdateInfo({ latest: res.latest_version, current: res.current_version, url: res.download_url });
      }
    } catch { }
  };

  useEffect(() => {
    credentialsCtrl.loadCredentials();
  }, []);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && (e.key === "p" || e.key === "k")) {
        e.preventDefault();
        setCmdPaletteOpen((prev) => !prev);
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  const handleExportBackup = async () => {
    const password = await dialogs.prompt("Enter a password to encrypt this database backup (Введіть пароль для шифрування резервної копії):");
    if (password === null) return;
    if (!password) {
      await dialogs.alert("Password cannot be empty (Пароль не може бути порожнім)");
      return;
    }
    try {
      const res = await invoke<string>("select_and_export_backup", { password });
      await dialogs.alert(`Database backup exported successfully to: ${res}`);
    } catch (e: any) {
      if (e !== "Save cancelled") {
        await dialogs.alert(`Export failed: ${e}`);
      }
    }
  };

  const handleImportBackup = async () => {
    if (!await dialogs.confirm("Warning: Restoring will overwrite all current servers and credentials. Continue? (Увага: Відновлення перезапише всі поточні сервери та облікові дані. Продовжити?)")) return;
    const password = await dialogs.prompt("Enter the password used to encrypt this backup (Введіть пароль, який використовувався для шифрування):");
    if (password === null) return;
    if (!password) {
      await dialogs.alert("Password cannot be empty (Пароль не може бути порожнім)");
      return;
    }
    try {
      const res = await invoke<string>("select_and_import_backup", { password });
      await dialogs.alert(`Database restored successfully from: ${res}`);
      serversCtrl.loadServers();
      credentialsCtrl.loadCredentials();
      serversCtrl.setSelectedServer(null);
    } catch (e: any) {
      if (e !== "Import cancelled") {
        await dialogs.alert(`Restore failed: ${e}`);
      }
    }
  };

  const handleBypassWarnings = async () => {
    try {
      await invoke("bypass_rdp_warnings");
      await dialogs.alert("Successfully configured Windows Registry. RDP warnings will now be reverted to legacy style. (Налаштування реєстру виконано успішно. Попередження RDP переведено в класичний режим.)");
    } catch (e: any) {
      await dialogs.alert(`Failed to configure registry: ${e}`);
    }
  };

  const handleImportCSV = async () => {
    try {
      const count = await invoke<number>("select_and_import_devolutions_csv");
      await dialogs.alert(`Imported ${count} connections successfully!`);
      serversCtrl.loadServers();
      credentialsCtrl.loadCredentials();
    } catch (err: any) {
      if (err !== "Import cancelled") {
        await dialogs.alert(`Import failed: ${err}`);
      }
    }
  };

  const renderTabContent = () => {
    const displayedServers = serversCtrl.favoritesOnly
      ? serversCtrl.servers.filter((s) => serversCtrl.favorites.includes(s.id))
      : serversCtrl.servers;

    return (
      <>
        <div style={{ display: tabs.activeTabType === "dashboard" ? "block" : "none", width: "100%", height: "100%" }}>
          <div className="content-grid">
            <main className="center-pane">
              <ServerTable
                servers={displayedServers}
                selectedFolder={serversCtrl.selectedFolder}
                selectedTag={serversCtrl.selectedTag}
                favorites={serversCtrl.favorites}
                selectedServer={serversCtrl.selectedServer}
                onSelectServer={serversCtrl.setSelectedServer}
                onConnect={tabs.handleConnect}
                onEdit={serverForm.openServerForm}
                onDelete={serverForm.deleteServer}
                onAddServer={() => serverForm.openServerForm(null)}
                onImportCSV={handleImportCSV}
                onToggleFavorite={serversCtrl.toggleFavorite}
                onConnectSFTP={tabs.handleConnectSFTP}
                onQuickConnect={tabs.handleQuickConnect}
              />
            </main>
            <DetailsPanel
              server={serversCtrl.selectedServer}
              credentials={credentialsCtrl.credentials}
              onConnect={tabs.handleConnect}
              onEdit={serverForm.openServerForm}
            />
          </div>
        </div>

        <div style={{ display: tabs.activeTabType === "credentials" ? "flex" : "none", flexDirection: "column", gap: "20px", width: "100%", height: "100%", padding: "20px", overflowY: "auto" }} className="content-grid">
          <div className="header-row">
            <div>
              <h2 style={{ fontSize: "1.3rem" }}>Vault Credentials</h2>
              <p style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}>
                Secure accounts used to authenticate with remote targets.
              </p>
            </div>
            <button className="btn btn-primary" onClick={() => credForm.openCredForm(null)}>
              <span>Add Credential</span>
            </button>
          </div>
          <div className="glass-card" style={{ padding: 0 }}>
            {credentialsCtrl.credentials.length === 0 ? (
              <div style={{ padding: "40px", textAlign: "center", color: "var(--text-muted)" }}>
                No credentials stored in the Vault. Click "Add Credential" to create one.
              </div>
            ) : (
              <table className="server-table">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Type</th>
                    <th>Username</th>
                    <th style={{ textAlign: "right" }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {credentialsCtrl.credentials.map((c) => (
                    <tr key={c.id}>
                      <td style={{ fontWeight: 600, color: "#fff" }}>{c.name}</td>
                      <td style={{ textTransform: "capitalize" }}>{c.type.replace("_", " ")}</td>
                      <td style={{ fontFamily: "var(--font-mono)", fontSize: "0.85rem" }}>{c.username}</td>
                      <td>
                        <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end" }}>
                          <button className="btn btn-secondary" style={{ padding: "6px 10px" }} onClick={() => credForm.openCredForm(c)}>Edit</button>
                          <button className="btn btn-danger" style={{ padding: "6px 10px" }} onClick={() => credForm.deleteCredential(c.id)}>Delete</button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>

        <div style={{ display: tabs.activeTabType === "settings" ? "block" : "none", width: "100%", height: "100%", overflowY: "auto", padding: "20px" }}>
          <div style={{ padding: "30px", maxWidth: "600px", margin: "0 auto" }} className="glass-card">
            <h2 style={{ marginBottom: "20px" }}>Application Settings</h2>
            <div style={{ display: "flex", flexDirection: "column", gap: "25px" }}>
              <div style={{ borderBottom: "1px solid var(--border-color)", paddingBottom: "15px" }}>
                <h3 style={{ fontSize: "1rem", marginBottom: "8px" }}>Backup & Restore</h3>
                <p style={{ fontSize: "0.85rem", color: "var(--text-secondary)", marginBottom: "15px" }}>
                  Export a copy of the database, or restore from a previous backup file.
                </p>
                <div style={{ display: "flex", gap: "10px" }}>
                  <button className="btn btn-secondary" onClick={handleExportBackup}>Export Backup</button>
                  <button className="btn btn-secondary" onClick={handleImportBackup}>Restore Backup</button>
                </div>
              </div>
              <div style={{ borderBottom: "1px solid var(--border-color)", paddingBottom: "15px" }}>
                <h3 style={{ fontSize: "1rem", marginBottom: "8px" }}>Bypass RDP Warnings / Обхід попереджень RDP</h3>
                <p style={{ fontSize: "0.85rem", color: "var(--text-secondary)", marginBottom: "15px" }}>
                  Reverts Windows RDP connection prompts to legacy behavior.
                </p>
                <button className="btn btn-primary" onClick={handleBypassWarnings}>
                  Suppress Warnings / Вимкнути попередження
                </button>
              </div>
              <div>
                <h3 style={{ fontSize: "1rem", marginBottom: "8px" }}>About</h3>
                <div style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "0.85rem", color: "var(--text-secondary)" }}>
                  <div>Version: {updateInfo?.current ?? "—"}</div>
                  <div>Runtime: Tauri v2 + React</div>
                  <div>OS Backend: Windows native PTY & Command integrations</div>
                  {updateInfo && (
                    <div style={{ marginTop: "8px" }}>
                      <a href={updateInfo.url} target="_blank" style={{ color: "var(--accent-cyan)", textDecoration: "underline", cursor: "pointer" }}>
                        Update available: {updateInfo.latest}
                      </a>
                    </div>
                  )}
                </div>
              </div>
            </div>
          </div>
        </div>

        {tabs.activeTabs.map((tab) => {
          const isCurrent = tab.id === tabs.currentTabId;
          const server = serversCtrl.servers.find((s) => s.id === tab.serverId);
          if (tab.type === "ssh") {
            return (
              <div key={tab.id} style={{ display: isCurrent ? "block" : "none", width: "100%", height: "100%", overflow: "hidden" }}>
                <TerminalTab sessionId={tab.id} host={tab.hostname || "127.0.0.1"}
                  port={server?.port || 22}
                  username={server ? (server.username || credentialsCtrl.credentials.find(c => c.id === server.credential_id)?.username || "root") : "root"}
                  credentialId={server?.credential_id} serverId={server?.id} />
              </div>
            );
          }
          if (tab.type === "rdp") {
            return (
              <div key={tab.id} style={{ display: isCurrent ? "flex" : "none", flexDirection: "column", width: "100%", height: "100%", minHeight: 0, overflow: "hidden" }}>
                <RdpTab sessionId={tab.id} serverId={tab.serverId || ""}
                  host={tab.hostname || "127.0.0.1"} port={server?.port || 3389}
                  credentialId={server?.credential_id}
                  serverUsername={server?.username || ""}
                  isActive={isCurrent} />
              </div>
            );
          }
          if (tab.type === "sftp") {
            return (
              <div key={tab.id} style={{ display: isCurrent ? "flex" : "none", flexDirection: "column", width: "100%", height: "100%", overflow: "hidden" }}>
                <SftpTab sessionId={tab.id} serverId={tab.serverId || ""}
                  host={tab.hostname || "127.0.0.1"} port={server?.port || 22}
                  username={server ? (server.username || credentialsCtrl.credentials.find(c => c.id === server.credential_id)?.username || "root") : "root"}
                  credentialId={server?.credential_id} />
              </div>
            );
          }
          return null;
        })}
      </>
    );
  };

  const handleUpdateClick = async () => {
    if (updateInfo) {
      await invoke("plugin:opener|open_url", { url: updateInfo.url });
    }
  };

  return (
    <>
      {vault.needsMigration && (
        <div style={{
          position: "fixed", top: 0, left: 0, right: 0, bottom: 0,
          background: "rgba(0,0,0,0.85)", zIndex: 9999,
          display: "flex", alignItems: "center", justifyContent: "center",
        }}>
          <MigrationDialog
            migrating={vault.migrating}
            error={vault.migrationError}
            onMigrate={vault.migrateVault}
            onReset={vault.resetVault}
          />
        </div>
      )}
      {updateInfo && (
        <div style={{
          background: "linear-gradient(90deg, var(--accent-purple), var(--accent-cyan))",
          color: "#fff",
          textAlign: "center",
          padding: "8px 16px",
          fontSize: "0.85rem",
          fontWeight: 500,
          cursor: "pointer",
          userSelect: "none"
        }} onClick={handleUpdateClick}>
          New version {updateInfo.latest} available &mdash; click to download
        </div>
      )}
    <div className="app-container">
      <div className="app-body">
        <Sidebar
          servers={serversCtrl.servers}
          customFolders={serversCtrl.customFolders}
          activeTabType={tabs.activeTabType}
          selectedFolder={serversCtrl.selectedFolder}
          selectedTag={serversCtrl.selectedTag}
          favoritesOnly={serversCtrl.favoritesOnly}
          onSelectFolder={(folder) => {
            serversCtrl.setSelectedFolder(folder);
            serversCtrl.setSelectedTag("");
            serversCtrl.setFavoritesOnly(false);
            serversCtrl.setSelectedServer(null);
          }}
          onSelectTag={(tag) => {
            serversCtrl.setSelectedTag(tag);
            serversCtrl.setSelectedFolder("");
            serversCtrl.setFavoritesOnly(false);
            serversCtrl.setSelectedServer(null);
          }}
          onToggleFavorites={() => {
            serversCtrl.setFavoritesOnly(true);
            serversCtrl.setSelectedFolder("");
            serversCtrl.setSelectedTag("");
            serversCtrl.setSelectedServer(null);
          }}
          onCreateFolder={(parentFolder) => {
            folderModal.setFolderModalMode('create');
            folderModal.setFolderModalParent(parentFolder);
            folderModal.setFolderModalName('');
            folderModal.setFolderModalOpen(true);
          }}
          onRenameFolder={(folderPath) => {
            folderModal.setFolderModalMode('rename');
            folderModal.setFolderModalPath(folderPath);
            const parts = folderPath.split('/');
            folderModal.setFolderModalName(parts[parts.length - 1] || '');
            folderModal.setFolderModalOpen(true);
          }}
          onDeleteFolder={(folderPath) => {
            folderModal.setFolderModalMode('delete');
            folderModal.setFolderModalPath(folderPath);
            folderModal.setFolderModalOpen(true);
          }}
          onNavigateTo={(type) => {
            if (type !== 'dashboard') {
              serversCtrl.setSelectedServer(null);
            }
            tabs.setActiveTabType(type);
            tabs.setCurrentTabId(type);
          }}
        />

        <div className="main-workspace">
          <div className="tabs-bar">
            <div className={`tab ${tabs.currentTabId === "dashboard" ? "active" : ""}`}
              onClick={() => tabs.handleSelectTab("dashboard")}>
              <span>Connections Directory</span>
            </div>
            {tabs.activeTabs.map((tab) => (
              <div key={tab.id} className={`tab ${tabs.currentTabId === tab.id ? "active" : ""}`}
                onClick={() => tabs.handleSelectTab(tab)}>
                <Terminal size={14} style={{ color: "var(--accent-purple)" }} />
                <span>{tab.title}</span>
                <X size={12} className="tab-close" onClick={(e) => tabs.handleCloseTab(tab.id, e)} />
              </div>
            ))}
            {tabs.activeTabType === "credentials" && <div className="tab active"><span>Credentials Vault</span></div>}
            {tabs.activeTabType === "settings" && <div className="tab active"><span>Settings</span></div>}
          </div>

          <div className="tab-content">
            {renderTabContent()}
          </div>
        </div>
      </div>

      <Taskbar
        activeTabs={tabs.activeTabs}
        currentTabId={tabs.currentTabId}
        onSelectTab={tabs.handleSelectTab}
        visible={tabs.activeTabType === "dashboard" || tabs.activeTabType === "credentials" || tabs.activeTabType === "settings"}
      />
    </div>

      <CommandPalette
        servers={serversCtrl.servers}
        isOpen={cmdPaletteOpen}
        onClose={() => setCmdPaletteOpen(false)}
        onSelectServer={tabs.handleConnect}
      />

      {serverForm.serverModalOpen && (
        <div className="modal-overlay" onClick={() => serverForm.setServerModalOpen(false)}>
          <div className="modal-box glass-card" onClick={(e) => e.stopPropagation()}>
            <div className="header-row" style={{ marginBottom: "15px" }}>
              <h3>{serverForm.editingServer ? "Edit Server Configuration" : "New Server Connection"}</h3>
              <button className="btn btn-secondary" style={{ padding: "4px" }} onClick={() => serverForm.setServerModalOpen(false)}>
                <X size={16} />
              </button>
            </div>
            <form onSubmit={serverForm.saveServer} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div className="input-group">
                <label className="input-label">Connection Name</label>
                <input type="text" className="text-input" value={serverForm.srvName} onChange={(e) => serverForm.setSrvName(e.target.value)} required placeholder="e.g. Production Web DB" />
              </div>
              <div className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Hostname / Domain</label>
                  <input type="text" className="text-input" value={serverForm.srvHost} onChange={(e) => serverForm.setSrvHost(e.target.value)} placeholder="db.prod.local" />
                </div>
                <div className="input-group">
                  <label className="input-label">IP Address</label>
                  <input type="text" className="text-input" value={serverForm.srvIp} onChange={(e) => serverForm.setSrvIp(e.target.value)} placeholder="10.0.1.45" />
                </div>
              </div>
              <div className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Port</label>
                  <input type="number" className="text-input" value={serverForm.srvPort} onChange={(e) => serverForm.setSrvPort(parseInt(e.target.value))} required />
                </div>
                <div className="input-group">
                  <label className="input-label">Protocol</label>
                  <select className="text-input" style={{ backgroundColor: "#000" }} value={serverForm.srvProto} onChange={(e) => {
                    const val = e.target.value as 'ssh' | 'rdp';
                    serverForm.setSrvProto(val);
                    serverForm.setSrvPort(val === 'ssh' ? 22 : 3389);
                  }}>
                    <option value="ssh">SSH</option>
                    <option value="rdp">RDP</option>
                  </select>
                </div>
              </div>
              <div className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Target OS</label>
                  <select className="text-input" style={{ backgroundColor: "#000" }} value={serverForm.srvOs} onChange={(e) => serverForm.setSrvOs(e.target.value as 'linux' | 'windows')}>
                    <option value="linux">Linux</option>
                    <option value="windows">Windows</option>
                  </select>
                </div>
                <div className="input-group">
                  <label className="input-label">Directory Folder</label>
                  <input type="text" list="existing-folders" className="text-input" value={serverForm.srvFolder} onChange={(e) => serverForm.setSrvFolder(e.target.value)} placeholder="Production/Linux" />
                  <datalist id="existing-folders">
                    {Array.from(new Set([
                      ...serversCtrl.servers.map((s) => s.folder_path),
                      ...serversCtrl.customFolders
                    ].filter(Boolean))).sort().map((folder) => (
                      <option key={folder} value={folder} />
                    ))}
                  </datalist>
                </div>
              </div>
              <div className="input-group">
                <label className="input-label">Smart Tags (comma separated)</label>
                <input type="text" className="text-input" value={serverForm.srvTags} onChange={(e) => serverForm.setSrvTags(e.target.value)} placeholder="prod, db, postgres" />
              </div>
              <div className="input-group">
                <label className="input-label">Linked Vault Credential</label>
                <select className="text-input" style={{ backgroundColor: "#000" }} value={serverForm.srvCredId} onChange={(e) => serverForm.setSrvCredId(e.target.value)}>
                  <option value="">-- No Authentication Credential --</option>
                  {credentialsCtrl.credentials.map((c) => (
                    <option key={c.id} value={c.id}>{c.name} ({c.username})</option>
                  ))}
                </select>
              </div>
              {!serverForm.srvCredId && (
                <div className="form-grid-2" style={{ gap: "10px" }}>
                  <div className="input-group">
                    <label className="input-label">Custom Username (Manual)</label>
                    <input type="text" className="text-input" value={serverForm.srvUsername} onChange={(e) => serverForm.setSrvUsername(e.target.value)} placeholder="e.g. root / Administrator" />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Custom Password (Manual)</label>
                    <input type="password" className="text-input" value={serverForm.srvPassword} onChange={(e) => { serverForm.setSrvPassword(e.target.value); serverForm.setSrvPasswordChanged(true); }} placeholder={serverForm.editingServer && !serverForm.srvPasswordChanged ? "•••••••• (Unchanged)" : "Enter password"} />
                  </div>
                </div>
              )}
              {serverForm.srvProto === 'rdp' && (
                <div style={{ display: "flex", flexDirection: "column", gap: "10px", padding: "12px", backgroundColor: "rgba(255, 255, 255, 0.03)", border: "1px solid var(--border-color)", borderRadius: "6px", marginTop: "5px", marginBottom: "5px" }}>
                  <span style={{ fontSize: "0.85rem", fontWeight: "600", color: "var(--accent-purple)" }}>RDP Connection Settings (Devolutions style)</span>
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={serverForm.rdpClipboard} onChange={(e) => serverForm.setRdpClipboard(e.target.checked)} /> Redirect Clipboard
                    </label>
                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={serverForm.rdpSmartSizing} onChange={(e) => serverForm.setRdpSmartSizing(e.target.checked)} /> Smart Sizing
                    </label>
                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={serverForm.rdpDrives} onChange={(e) => serverForm.setRdpDrives(e.target.checked)} /> Redirect Drives
                    </label>
                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={serverForm.rdpPrinters} onChange={(e) => serverForm.setRdpPrinters(e.target.checked)} /> Redirect Printers
                    </label>
                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={serverForm.rdpSmartcards} onChange={(e) => serverForm.setRdpSmartcards(e.target.checked)} /> Redirect Smart Cards
                    </label>
                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={serverForm.rdpWebauthn} onChange={(e) => serverForm.setRdpWebauthn(e.target.checked)} /> WebAuthn (Windows Hello)
                    </label>
                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={serverForm.rdpFullscreen} onChange={(e) => serverForm.setRdpFullscreen(e.target.checked)} /> Fullscreen Mode (Native Window)
                    </label>
                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={serverForm.rdpMultimon} onChange={(e) => serverForm.setRdpMultimon(e.target.checked)} /> Use Multiple Monitors (Multi-mon)
                    </label>
                  </div>
                  <div className="input-group" style={{ marginTop: "5px" }}>
                    <label className="input-label" style={{ fontSize: "0.8rem" }}>Audio Redirection</label>
                    <select className="text-input" style={{ backgroundColor: "#000", padding: "4px 8px" }} value={serverForm.rdpAudio} onChange={(e) => serverForm.setRdpAudio(parseInt(e.target.value))}>
                      <option value={0}>Play on this computer (Local)</option>
                      <option value={1}>Play on remote computer</option>
                      <option value={2}>Do not play (Mute)</option>
                    </select>
                  </div>
                </div>
              )}
              <div className="input-group">
                <label className="input-label">Notes / Description</label>
                <textarea className="text-input" value={serverForm.srvDesc} onChange={(e) => serverForm.setSrvDesc(e.target.value)} rows={2} placeholder="Optional server summary details..." style={{ resize: "none" }} />
              </div>
              <div className="form-actions">
                <button type="button" className="btn btn-secondary" onClick={() => serverForm.setServerModalOpen(false)}>Cancel</button>
                <button type="submit" className="btn btn-primary">Save Connection</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {credForm.credModalOpen && (
        <div className="modal-overlay" onClick={() => credForm.setCredModalOpen(false)}>
          <div className="modal-box glass-card" onClick={(e) => e.stopPropagation()}>
            <div className="header-row" style={{ marginBottom: "15px" }}>
              <h3>{credForm.editingCred ? "Edit Vault Credential" : "New Secure Credential"}</h3>
              <button className="btn btn-secondary" style={{ padding: "4px" }} onClick={() => credForm.setCredModalOpen(false)}>
                <X size={16} />
              </button>
            </div>
            <form onSubmit={credForm.saveCredential} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div className="input-group">
                <label className="input-label">Credential Name</label>
                <input type="text" className="text-input" value={credForm.credName} onChange={(e) => credForm.setCredName(e.target.value)} required placeholder="e.g. AWS Prod admin" />
              </div>
              <div className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Auth Type</label>
                  <select className="text-input" style={{ backgroundColor: "#000" }} value={credForm.credType} onChange={(e) => credForm.setCredType(e.target.value as 'password' | 'ssh_key')}>
                    <option value="password">Password</option>
                    <option value="ssh_key">SSH Private Key</option>
                  </select>
                </div>
                <div className="input-group">
                  <label className="input-label">Username</label>
                  <input type="text" className="text-input" value={credForm.credUser} onChange={(e) => credForm.setCredUser(e.target.value)} required placeholder="ubuntu / Administrator" />
                </div>
              </div>
              <div className="input-group">
                <label className="input-label">{credForm.credType === "ssh_key" ? "SSH Private Key Content" : "Password Secret"}</label>
                {credForm.credType === "ssh_key" ? (
                  <textarea className="text-input" value={credForm.credSecret} onChange={(e) => credForm.setCredSecret(e.target.value)} rows={6} placeholder="-----BEGIN OPENSSH PRIVATE KEY-----&#10;..." required={!credForm.editingCred} style={{ fontFamily: "var(--font-mono)", fontSize: "0.8rem", resize: "none" }} />
                ) : (
                  <input type="password" className="text-input" value={credForm.credSecret} onChange={(e) => credForm.setCredSecret(e.target.value)} placeholder={credForm.editingCred ? "•••••••••••• (Leave blank to keep current)" : "••••••••••••"} required={!credForm.editingCred} />
                )}
              </div>
              <div className="form-actions">
                <button type="button" className="btn btn-secondary" onClick={() => credForm.setCredModalOpen(false)}>Cancel</button>
                <button type="submit" className="btn btn-primary">Save Securely</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {folderModal.folderModalOpen && (
        <div className="modal-overlay" onClick={() => folderModal.setFolderModalOpen(false)}>
          <div className="modal-box glass-card" style={{ maxWidth: "450px" }} onClick={(e) => e.stopPropagation()}>
            <div className="header-row" style={{ marginBottom: "15px" }}>
              <h3>
                {folderModal.folderModalMode === 'create' && "Create New Folder"}
                {folderModal.folderModalMode === 'rename' && "Rename Folder"}
                {folderModal.folderModalMode === 'delete' && "Delete Folder"}
              </h3>
              <button className="btn btn-secondary" style={{ padding: "4px" }} onClick={() => folderModal.setFolderModalOpen(false)}>
                <X size={16} />
              </button>
            </div>

            {folderModal.folderModalMode === 'delete' ? (
              <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                <p style={{ color: "var(--text-secondary)", fontSize: "0.9rem", lineHeight: "1.4" }}>
                  Are you sure you want to delete the folder <strong style={{ color: "var(--accent-warn)" }}>{folderModal.folderModalPath}</strong>?
                  <br />
                  <span style={{ color: "var(--accent-warn)", fontWeight: "bold" }}>Warning:</span> This will permanently delete the folder and <strong style={{ color: "var(--text-primary)" }}>ALL servers</strong> inside it.
                </p>
                <div className="form-actions">
                  <button type="button" className="btn btn-secondary" onClick={() => folderModal.setFolderModalOpen(false)}>Cancel</button>
                  <button type="button" className="btn btn-primary" style={{ backgroundColor: "var(--accent-warn)", border: "none" }} onClick={folderModal.handleDeleteSubmit}>Delete Folder & Servers</button>
                </div>
              </div>
            ) : (
              <form onSubmit={folderModal.folderModalMode === 'create' ? folderModal.handleCreateFolder : folderModal.handleRenameSubmit} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
                {folderModal.folderModalMode === 'create' && <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>Creating under: <strong>{folderModal.folderModalParent || "Root"}</strong></div>}
                {folderModal.folderModalMode === 'rename' && <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>Renaming: <strong>{folderModal.folderModalPath}</strong></div>}
                <div className="input-group">
                  <label className="input-label">Folder Name</label>
                  <input type="text" className="text-input" value={folderModal.folderModalName} onChange={(e) => folderModal.setFolderModalName(e.target.value)} required placeholder="e.g. Production" autoFocus />
                </div>
                <div className="form-actions">
                  <button type="button" className="btn btn-secondary" onClick={() => folderModal.setFolderModalOpen(false)}>Cancel</button>
                  <button type="submit" className="btn btn-primary">{folderModal.folderModalMode === 'create' ? "Create Folder" : "Rename Folder"}</button>
                </div>
              </form>
            )}
          </div>
        </div>
      )}
    </>
  );
}

export default App;
