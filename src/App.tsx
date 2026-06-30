import { useState, useEffect } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { Server, Credential, ActiveTab } from "./types";
import { MasterPassword } from "./components/MasterPassword";
import { Sidebar } from "./components/Sidebar";
import { ServerTable } from "./components/ServerTable";
import { DetailsPanel } from "./components/DetailsPanel";
import { TerminalTab } from "./components/TerminalTab";
import { RdpTab } from "./components/RdpTab";
import { CommandPalette } from "./components/CommandPalette";
import { KeyRound, Key, Plus, Terminal, X } from "lucide-react";
import "./App.css";

function App() {
  const [unlocked, setUnlocked] = useState<boolean>(false);
  const [servers, setServers] = useState<Server[]>([]);
  const [credentials, setCredentials] = useState<Credential[]>([]);
  const [favorites, setFavorites] = useState<string[]>([]);
  const [selectedServer, setSelectedServer] = useState<Server | null>(null);

  // Filter/Sidebar States
  const [activeTabType, setActiveTabType] = useState<string>("dashboard"); // dashboard, credentials, settings, ssh
  const [activeTabs, setActiveTabs] = useState<ActiveTab[]>([]);
  const [currentTabId, setCurrentTabId] = useState<string>("dashboard");
  
  const [selectedFolder, setSelectedFolder] = useState<string>("");
  const [selectedTag, setSelectedTag] = useState<string>("");
  const [favoritesOnly, setFavoritesOnly] = useState<boolean>(false);

  // Command Palette
  const [cmdPaletteOpen, setCmdPaletteOpen] = useState<boolean>(false);

  // Modals
  const [serverModalOpen, setServerModalOpen] = useState<boolean>(false);
  const [editingServer, setEditingServer] = useState<Server | null>(null);

  const [credModalOpen, setCredModalOpen] = useState<boolean>(false);
  const [editingCred, setEditingCred] = useState<Credential | null>(null);

  // Form States
  const [srvName, setSrvName] = useState("");
  const [srvHost, setSrvHost] = useState("");
  const [srvIp, setSrvIp] = useState("");
  const [srvPort, setSrvPort] = useState(22);
  const [srvProto, setSrvProto] = useState<'ssh' | 'rdp'>("ssh");
  const [srvOs, setSrvOs] = useState<'linux' | 'windows'>("linux");
  const [srvFolder, setSrvFolder] = useState("");
  const [srvTags, setSrvTags] = useState("");
  const [srvDesc, setSrvDesc] = useState("");
  const [srvCredId, setSrvCredId] = useState("");
  const [srvUsername, setSrvUsername] = useState("");
  const [srvPassword, setSrvPassword] = useState("");
  const [rdpClipboard, setRdpClipboard] = useState<boolean>(true);
  const [rdpDrives, setRdpDrives] = useState<boolean>(false);
  const [rdpPrinters, setRdpPrinters] = useState<boolean>(false);
  const [rdpSmartSizing, setRdpSmartSizing] = useState<boolean>(true);
  const [rdpAudio, setRdpAudio] = useState<number>(0);
  const [rdpSmartcards, setRdpSmartcards] = useState<boolean>(false);
  const [rdpWebauthn, setRdpWebauthn] = useState<boolean>(false);

  const [credName, setCredName] = useState("");
  const [credType, setCredType] = useState<'password' | 'ssh_key'>("password");
  const [credUser, setCredUser] = useState("");
  const [credSecret, setCredSecret] = useState("");

  useEffect(() => {
    checkUnlockStatus();
    loadServers();
    loadFavorites();
  }, []);

  useEffect(() => {
    if (unlocked) {
      loadCredentials();
    }
  }, [unlocked]);

  // Keyboard shortcut listener for Command Palette (Ctrl+P / Ctrl+K)
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

  const checkUnlockStatus = async () => {
    try {
      const active = await invoke<boolean>("is_vault_unlocked");
      setUnlocked(active);
    } catch (e) {
      console.error(e);
    }
  };

  const loadServers = async () => {
    try {
      const list = await invoke<Server[]>("get_servers");
      setServers(list);
    } catch (e) {
      console.error("Failed to load servers", e);
    }
  };

  const loadCredentials = async () => {
    try {
      const list = await invoke<Credential[]>("get_credentials");
      setCredentials(list);
    } catch (e) {
      console.error("Failed to load credentials", e);
    }
  };

  const loadFavorites = async () => {
    try {
      const favsJson = await invoke<string | null>("get_setting", { key: "favorites" });
      if (favsJson) {
        setFavorites(JSON.parse(favsJson));
      }
    } catch (e) {
      console.error("Failed to load favorites setting", e);
    }
  };

  const toggleFavorite = async (id: string) => {
    const isFav = favorites.includes(id);
    const newFavs = isFav ? favorites.filter((fid) => fid !== id) : [...favorites, id];
    setFavorites(newFavs);
    try {
      await invoke("set_setting", { key: "favorites", value: JSON.stringify(newFavs) });
    } catch (e) {
      console.error("Failed to save favorites", e);
    }
  };

  const handleUnlock = () => {
    setUnlocked(true);
  };

  useEffect(() => {
    let unlistenRdp: (() => void) | null = null;
    let unlistenSsh: (() => void) | null = null;

    const setupListeners = async () => {
      console.log("Setting up connection closed event listeners...");
      unlistenRdp = await listen<string>("rdp-closed", (event) => {
        const closedTabId = event.payload;
        console.log("RDP session closed event received for tab ID:", closedTabId);
        setActiveTabs((prev) => prev.filter((t) => {
          console.log(`Filtering active tab ${t.id} against closed ${closedTabId}`);
          return t.id !== closedTabId;
        }));
        setCurrentTabId((curr) => {
          if (curr === closedTabId) {
            setActiveTabType("dashboard");
            return "dashboard";
          }
          return curr;
        });
      });

      unlistenSsh = await listen<string>("ssh-closed", (event) => {
        const closedTabId = event.payload;
        console.log("SSH session closed event received for tab ID:", closedTabId);
        setActiveTabs((prev) => prev.filter((t) => t.id !== closedTabId));
        setCurrentTabId((curr) => {
          if (curr === closedTabId) {
            setActiveTabType("dashboard");
            return "dashboard";
          }
          return curr;
        });
      });
    };

    if (unlocked) {
      setupListeners();
    }

    return () => {
      console.log("Cleaning up connection closed event listeners...");
      if (unlistenRdp) unlistenRdp();
      if (unlistenSsh) unlistenSsh();
    };
  }, [unlocked]);



  // Connection Handler
  const handleConnect = (srv: Server) => {
    if (srv.protocol === "rdp") {
      // Open embedded RDP tab
      const tabId = `rdp-${srv.id}-${Date.now()}`;
      const newTab: ActiveTab = {
        id: tabId,
        title: srv.name,
        type: "rdp",
        serverId: srv.id,
        hostname: srv.hostname || srv.ip,
      };

      setActiveTabs((prev) => [...prev, newTab]);
      setCurrentTabId(tabId);
      setActiveTabType("rdp");
    } else {
      // Open SSH Terminal tab
      const tabId = `ssh-${srv.id}-${Date.now()}`;
      const newTab: ActiveTab = {
        id: tabId,
        title: srv.name,
        type: "ssh",
        serverId: srv.id,
        hostname: srv.hostname || srv.ip,
      };

      setActiveTabs((prev) => [...prev, newTab]);
      setCurrentTabId(tabId);
      setActiveTabType("ssh");
    }
  };

  // Tab switcher
  const handleSelectTab = (tab: ActiveTab | string) => {
    if (typeof tab === "string") {
      setCurrentTabId(tab);
      setActiveTabType(tab);
    } else {
      setCurrentTabId(tab.id);
      setActiveTabType(tab.type);
    }
  };

  const handleCloseTab = (tabId: string, e: React.MouseEvent) => {
    e.stopPropagation();
    const remaining = activeTabs.filter((t) => t.id !== tabId);
    setActiveTabs(remaining);
    
    if (currentTabId === tabId) {
      setCurrentTabId("dashboard");
      setActiveTabType("dashboard");
    }
  };

  // Server CRUD functions
  const openServerForm = (srv: Server | null = null) => {
    setEditingServer(srv);
    if (srv) {
      setSrvName(srv.name);
      setSrvHost(srv.hostname);
      setSrvIp(srv.ip);
      setSrvPort(srv.port);
      setSrvProto(srv.protocol);
      setSrvOs(srv.os);
      setSrvFolder(srv.folder_path);
      setSrvTags(srv.tags);
      setSrvDesc(srv.description);
      setSrvCredId(srv.credential_id || "");
      setSrvUsername(srv.username || "");
      setSrvPassword(srv.encrypted_password ? "__UNCHANGED__" : "");
      setRdpClipboard(srv.rdp_clipboard !== undefined ? srv.rdp_clipboard !== 0 : true);
      setRdpDrives(srv.rdp_drives !== undefined ? srv.rdp_drives !== 0 : false);
      setRdpPrinters(srv.rdp_printers !== undefined ? srv.rdp_printers !== 0 : false);
      setRdpSmartSizing(srv.rdp_smart_sizing !== undefined ? srv.rdp_smart_sizing !== 0 : true);
      setRdpAudio(srv.rdp_audio !== undefined ? srv.rdp_audio : 0);
      setRdpSmartcards(srv.rdp_smartcards !== undefined ? srv.rdp_smartcards !== 0 : false);
      setRdpWebauthn(srv.rdp_webauthn !== undefined ? srv.rdp_webauthn !== 0 : false);
    } else {
      setSrvName("");
      setSrvHost("");
      setSrvIp("");
      setSrvPort(22);
      setSrvProto("ssh");
      setSrvOs("linux");
      setSrvFolder(selectedFolder);
      setSrvTags("");
      setSrvDesc("");
      setSrvCredId("");
      setSrvUsername("");
      setSrvPassword("");
      setRdpClipboard(true);
      setRdpDrives(false);
      setRdpPrinters(false);
      setRdpSmartSizing(true);
      setRdpAudio(0);
      setRdpSmartcards(false);
      setRdpWebauthn(false);
    }
    setServerModalOpen(true);
  };

  const saveServer = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (editingServer) {
        await invoke("update_server", {
          id: editingServer.id,
          name: srvName,
          hostname: srvHost,
          ip: srvIp,
          port: srvPort,
          protocol: srvProto,
          os: srvOs,
          folderPath: srvFolder,
          tags: srvTags,
          description: srvDesc,
          credentialId: srvCredId || null,
          username: srvUsername || null,
          password: srvPassword || null,
          rdpClipboard: rdpClipboard ? 1 : 0,
          rdpDrives: rdpDrives ? 1 : 0,
          rdpPrinters: rdpPrinters ? 1 : 0,
          rdpSmartSizing: rdpSmartSizing ? 1 : 0,
          rdpAudio: rdpAudio,
          rdpSmartcards: rdpSmartcards ? 1 : 0,
          rdpWebauthn: rdpWebauthn ? 1 : 0,
        });
      } else {
        await invoke("add_server", {
          name: srvName,
          hostname: srvHost,
          ip: srvIp,
          port: srvPort,
          protocol: srvProto,
          os: srvOs,
          folderPath: srvFolder,
          tags: srvTags,
          description: srvDesc,
          credentialId: srvCredId || null,
          username: srvUsername || null,
          password: srvPassword || null,
          rdpClipboard: rdpClipboard ? 1 : 0,
          rdpDrives: rdpDrives ? 1 : 0,
          rdpPrinters: rdpPrinters ? 1 : 0,
          rdpSmartSizing: rdpSmartSizing ? 1 : 0,
          rdpAudio: rdpAudio,
          rdpSmartcards: rdpSmartcards ? 1 : 0,
          rdpWebauthn: rdpWebauthn ? 1 : 0,
        });
      }
      setServerModalOpen(false);
      loadServers();
      setSelectedServer(null);
    } catch (e: any) {
      alert(`Failed to save server: ${e}`);
    }
  };

  const deleteServer = async (id: string) => {
    if (!confirm("Are you sure you want to delete this server configuration?")) return;
    try {
      await invoke("delete_server", { id });
      loadServers();
      setSelectedServer(null);
    } catch (e: any) {
      alert(`Failed to delete server: ${e}`);
    }
  };

  // Credential CRUD functions
  const openCredForm = (cred: Credential | null = null) => {
    setEditingCred(cred);
    if (cred) {
      setCredName(cred.name);
      setCredType(cred.type);
      setCredUser(cred.username);
      setCredSecret(""); // Decrypted is only fetched on reveal/copy, we leave field blank to preserve unless updated
    } else {
      setCredName("");
      setCredType("password");
      setCredUser("");
      setCredSecret("");
    }
    setCredModalOpen(true);
  };

  const saveCredential = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (editingCred) {
        await invoke("update_credential", {
          id: editingCred.id,
          name: credName,
          credType: credType,
          username: credUser,
          secret: credSecret || null,
        });
      } else {
        await invoke("add_credential", {
          name: credName,
          credType: credType,
          username: credUser,
          secret: credSecret,
        });
      }
      setCredModalOpen(false);
      loadCredentials();
    } catch (e: any) {
      alert(`Failed to save credential: ${e}`);
    }
  };

  const deleteCredential = async (id: string) => {
    if (!confirm("Are you sure you want to delete this credential? Any linked servers will lose their auto-connection credentials.")) return;
    try {
      await invoke("delete_credential", { id });
      loadCredentials();
    } catch (e: any) {
      alert(`Failed to delete credential: ${e}`);
    }
  };

  const handleExportBackup = async () => {
    const password = prompt("Enter a password to encrypt this database backup (Введіть пароль для шифрування резервної копії):");
    if (password === null) return; // User cancelled
    if (!password) {
      alert("Password cannot be empty (Пароль не може бути порожнім)");
      return;
    }
    try {
      const res = await invoke<string>("select_and_export_backup", { password });
      alert(`Database backup exported successfully to: ${res}`);
    } catch (e: any) {
      if (e !== "Save cancelled") {
        alert(`Export failed: ${e}`);
      }
    }
  };

  const handleImportBackup = async () => {
    if (!confirm("Warning: Restoring will overwrite all current servers and credentials. Continue? (Увага: Відновлення перезапише всі поточні сервери та облікові дані. Продовжити?)")) return;
    const password = prompt("Enter the password used to encrypt this backup (Введіть пароль, який використовувався для шифрування):");
    if (password === null) return; // User cancelled
    if (!password) {
      alert("Password cannot be empty (Пароль не може бути порожнім)");
      return;
    }
    try {
      const res = await invoke<string>("select_and_import_backup", { password });
      alert(`Database restored successfully from: ${res}`);
      loadServers();
      loadCredentials();
      setSelectedServer(null);
    } catch (e: any) {
      if (e !== "Import cancelled") {
        alert(`Restore failed: ${e}`);
      }
    }
  };

  const handleBypassWarnings = async () => {
    try {
      await invoke("bypass_rdp_warnings");
      alert("Successfully configured Windows Registry. RDP warnings will now be reverted to legacy style. (Налаштування реєстру виконано успішно. Попередження RDP переведено в класичний режим.)");
    } catch (e: any) {
      alert(`Failed to configure registry: ${e}`);
    }
  };

  const handleImportCSV = () => {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = ".csv";
    input.onchange = async (e: any) => {
      const file = e.target.files?.[0];
      if (!file) return;

      const reader = new FileReader();
      reader.onload = async (evt) => {
        const content = evt.target?.result as string;
        try {
          const count = await invoke<number>("import_devolutions_csv", { csvContent: content });
          alert(`Imported ${count} connections successfully!`);
          loadServers();
          loadCredentials();
        } catch (err: any) {
          alert(`Import failed: ${err}`);
        }
      };
      reader.readAsText(file);
    };
    input.click();
  };

  // Render workspace content based on state
  const renderTabContent = () => {
    const displayedServers = favoritesOnly
      ? servers.filter((s) => favorites.includes(s.id))
      : servers;

    return (
      <>
        {/* Dashboard */}
        <div style={{ display: activeTabType === "dashboard" ? "block" : "none", width: "100%", height: "100%" }}>
          <div className="content-grid">
            <main className="center-pane">
              <ServerTable
                servers={displayedServers}
                selectedFolder={selectedFolder}
                selectedTag={selectedTag}
                favorites={favorites}
                selectedServer={selectedServer}
                onSelectServer={setSelectedServer}
                onConnect={handleConnect}
                onEdit={openServerForm}
                onDelete={deleteServer}
                onAddServer={() => openServerForm(null)}
                onImportCSV={handleImportCSV}
                onToggleFavorite={toggleFavorite}
              />
            </main>
            <DetailsPanel
              server={selectedServer}
              credentials={credentials}
              onConnect={handleConnect}
              onEdit={openServerForm}
            />
          </div>
        </div>

        {/* Credentials */}
        <div style={{ display: activeTabType === "credentials" ? "flex" : "none", flexDirection: "column", gap: "20px", width: "100%", height: "100%", padding: "20px", overflowY: "auto" }} className="content-grid">
          <div className="header-row">
            <div>
              <h2 style={{ fontSize: "1.3rem" }}>Vault Credentials</h2>
              <p style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}>
                Secure accounts used to authenticate with remote targets.
              </p>
            </div>
            <button className="btn btn-primary" onClick={() => openCredForm(null)}>
              <Plus size={16} />
              <span>Add Credential</span>
            </button>
          </div>

          <div className="glass-card" style={{ padding: 0 }}>
            {credentials.length === 0 ? (
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
                  {credentials.map((c) => (
                    <tr key={c.id}>
                      <td style={{ fontWeight: 600, color: "#fff" }}>{c.name}</td>
                      <td style={{ textTransform: "capitalize" }}>
                        <span style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}>
                          {c.type === "ssh_key" ? <KeyRound size={14} /> : <Key size={14} />}
                          {c.type.replace("_", " ")}
                        </span>
                      </td>
                      <td style={{ fontFamily: "var(--font-mono)", fontSize: "0.85rem" }}>{c.username}</td>
                      <td>
                        <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end" }}>
                          <button className="btn btn-secondary" style={{ padding: "6px 10px" }} onClick={() => openCredForm(c)}>
                            Edit
                          </button>
                          <button className="btn btn-danger" style={{ padding: "6px 10px" }} onClick={() => deleteCredential(c.id)}>
                            Delete
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>

        {/* Settings */}
        <div style={{ display: activeTabType === "settings" ? "block" : "none", width: "100%", height: "100%", overflowY: "auto", padding: "20px" }}>
          <div style={{ padding: "30px", maxWidth: "600px", margin: "0 auto" }} className="glass-card">
            <h2 style={{ marginBottom: "20px" }}>Application Settings</h2>
            
            <div style={{ display: "flex", flexDirection: "column", gap: "25px" }}>
              <div style={{ borderBottom: "1px solid var(--border-color)", paddingBottom: "15px" }}>
                <h3 style={{ fontSize: "1rem", marginBottom: "8px" }}>Backup & Restore</h3>
                <p style={{ fontSize: "0.85rem", color: "var(--text-secondary)", marginBottom: "15px" }}>
                  Export a copy of the database, or restore from a previous backup file.
                </p>
                <div style={{ display: "flex", gap: "10px" }}>
                  <button className="btn btn-secondary" onClick={handleExportBackup}>
                    Export Backup
                  </button>
                  <button className="btn btn-secondary" onClick={handleImportBackup}>
                    Restore Backup
                  </button>
                </div>
              </div>

              <div style={{ borderBottom: "1px solid var(--border-color)", paddingBottom: "15px" }}>
                <h3 style={{ fontSize: "1rem", marginBottom: "8px" }}>Bypass RDP Warnings / Обхід попереджень RDP</h3>
                <p style={{ fontSize: "0.85rem", color: "var(--text-secondary)", marginBottom: "15px" }}>
                  Reverts Windows RDP connection prompts to legacy behavior to allow checking "Don't ask me again" for resources (like Clipboard). Requires administrator privileges.
                  <br />
                  <span style={{ fontSize: "0.8rem", color: "var(--accent-purple)", display: "block", marginTop: "5px" }}>
                    (Налаштовує систему для вимкнення постійних попереджень безпеки RDP і дозволяє обрати "Більше не запитувати" для ресурсів. Потребує прав адміністратора.)
                  </span>
                </p>
                <button className="btn btn-primary" onClick={handleBypassWarnings}>
                  Suppress Warnings / Вимкнути попередження
                </button>
              </div>

              <div>
                <h3 style={{ fontSize: "1rem", marginBottom: "8px" }}>About</h3>
                <div style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "0.85rem", color: "var(--text-secondary)" }}>
                  <div>Version: 1.0.0 (MVP)</div>
                  <div>Runtime: Tauri v2 + React</div>
                  <div>OS Backend: Windows native PTY & Command integrations</div>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Active Session Tabs */}
        {activeTabs.map((tab) => {
          const isCurrent = tab.id === currentTabId;
          const server = servers.find((s) => s.id === tab.serverId);
          
          if (tab.type === "ssh") {
            return (
              <div key={tab.id} style={{ display: isCurrent ? "block" : "none", width: "100%", height: "100%", overflow: "hidden" }}>
                <TerminalTab
                  sessionId={tab.id}
                  host={tab.hostname || "127.0.0.1"}
                  port={server?.port || 22}
                  username={server ? (server.username || credentials.find(c => c.id === server.credential_id)?.username || "root") : "root"}
                  credentialId={server?.credential_id}
                  serverId={server?.id}
                />
              </div>
            );
          }
          
          if (tab.type === "rdp") {
            return (
              <div key={tab.id} style={{ display: isCurrent ? "flex" : "none", flexDirection: "column", width: "100%", height: "100%", overflow: "hidden" }}>
                <RdpTab
                  sessionId={tab.id}
                  serverId={tab.serverId || ""}
                  host={tab.hostname || "127.0.0.1"}
                  port={server?.port || 3389}
                  credentialId={server?.credential_id}
                />
              </div>
            );
          }
          
          return null;
        })}
      </>
    );
  };

  // If vault is locked, force user to unlock it
  if (!unlocked) {
    return <MasterPassword onUnlock={handleUnlock} />;
  }

  return (
    <div className="app-container">
      {/* Dynamic Sidebar */}
      <Sidebar
        servers={servers}
        activeTabType={activeTabType}
        selectedFolder={selectedFolder}
        selectedTag={selectedTag}
        favoritesOnly={favoritesOnly}
        onSelectFolder={(folder) => {
          setSelectedFolder(folder);
          setSelectedTag("");
          setFavoritesOnly(false);
          setSelectedServer(null);
        }}
        onSelectTag={(tag) => {
          setSelectedTag(tag);
          setSelectedFolder("");
          setFavoritesOnly(false);
          setSelectedServer(null);
        }}
        onToggleFavorites={() => {
          setFavoritesOnly(true);
          setSelectedFolder("");
          setSelectedTag("");
          setSelectedServer(null);
        }}
        onNavigateTo={(type) => {
          if (type !== 'dashboard') {
            setSelectedServer(null);
          }
          setActiveTabType(type);
          setCurrentTabId(type);
        }}
      />

      <div className="main-workspace">
        {/* Connection Tabs */}
        <div className="tabs-bar">
          <div
            className={`tab ${currentTabId === "dashboard" ? "active" : ""}`}
            onClick={() => handleSelectTab("dashboard")}
          >
            <span>Connections Directory</span>
          </div>

          {activeTabs.map((tab) => (
            <div
              key={tab.id}
              className={`tab ${currentTabId === tab.id ? "active" : ""}`}
              onClick={() => handleSelectTab(tab)}
            >
              <Terminal size={14} style={{ color: "var(--accent-purple)" }} />
              <span>{tab.title}</span>
              <X size={12} className="tab-close" onClick={(e) => handleCloseTab(tab.id, e)} />
            </div>
          ))}

          {activeTabType === "credentials" && (
            <div className="tab active">
              <span>Credentials Vault</span>
            </div>
          )}

          {activeTabType === "settings" && (
            <div className="tab active">
              <span>Settings</span>
            </div>
          )}
        </div>

        {/* Dynamic Workspace Panels */}
        <div className="tab-content" onClick={() => {
          // Check if clicking on server rows
        }}>
          {renderTabContent()}
        </div>
      </div>

      {/* Fuzzy search Command Palette */}
      <CommandPalette
        servers={servers}
        isOpen={cmdPaletteOpen}
        onClose={() => setCmdPaletteOpen(false)}
        onSelectServer={handleConnect}
      />

      {/* Server CRUD Modal */}
      {serverModalOpen && (
        <div className="modal-overlay">
          <div className="modal-box glass-card">
            <div className="header-row" style={{ marginBottom: "15px" }}>
              <h3>{editingServer ? "Edit Server Configuration" : "New Server Connection"}</h3>
              <button className="btn btn-secondary" style={{ padding: "4px" }} onClick={() => setServerModalOpen(false)}>
                <X size={16} />
              </button>
            </div>

            <form onSubmit={saveServer} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div className="input-group">
                <label className="input-label">Connection Name</label>
                <input type="text" className="text-input" value={srvName} onChange={(e) => setSrvName(e.target.value)} required placeholder="e.g. Production Web DB" />
              </div>

              <div className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Hostname / Domain</label>
                  <input type="text" className="text-input" value={srvHost} onChange={(e) => setSrvHost(e.target.value)} placeholder="db.prod.local" />
                </div>
                <div className="input-group">
                  <label className="input-label">IP Address</label>
                  <input type="text" className="text-input" value={srvIp} onChange={(e) => setSrvIp(e.target.value)} placeholder="10.0.1.45" />
                </div>
              </div>

              <div className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Port</label>
                  <input type="number" className="text-input" value={srvPort} onChange={(e) => setSrvPort(parseInt(e.target.value))} required />
                </div>
                <div className="input-group">
                  <label className="input-label">Protocol</label>
                  <select className="text-input" style={{ backgroundColor: "#000" }} value={srvProto} onChange={(e) => {
                    const val = e.target.value as 'ssh' | 'rdp';
                    setSrvProto(val);
                    setSrvPort(val === 'ssh' ? 22 : 3389);
                  }}>
                    <option value="ssh">SSH</option>
                    <option value="rdp">RDP</option>
                  </select>
                </div>
              </div>

              <div className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Target OS</label>
                  <select className="text-input" style={{ backgroundColor: "#000" }} value={srvOs} onChange={(e) => setSrvOs(e.target.value as 'linux' | 'windows')}>
                    <option value="linux">Linux</option>
                    <option value="windows">Windows</option>
                  </select>
                </div>
                <div className="input-group">
                  <label className="input-label">Directory Folder</label>
                  <input
                    type="text"
                    list="existing-folders"
                    className="text-input"
                    value={srvFolder}
                    onChange={(e) => setSrvFolder(e.target.value)}
                    placeholder="Production/Linux"
                  />
                  <datalist id="existing-folders">
                    {Array.from(new Set(servers.map((s) => s.folder_path).filter(Boolean))).map((folder) => (
                      <option key={folder} value={folder} />
                    ))}
                  </datalist>
                </div>
              </div>

              <div className="input-group">
                <label className="input-label">Smart Tags (comma separated)</label>
                <input type="text" className="text-input" value={srvTags} onChange={(e) => setSrvTags(e.target.value)} placeholder="prod, db, postgres" />
              </div>

              <div className="input-group">
                <label className="input-label">Linked Vault Credential</label>
                <select className="text-input" style={{ backgroundColor: "#000" }} value={srvCredId} onChange={(e) => setSrvCredId(e.target.value)}>
                  <option value="">-- No Authentication Credential --</option>
                  {credentials.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name} ({c.username})
                    </option>
                  ))}
                </select>
              </div>

              {!srvCredId && (
                <div className="form-grid-2" style={{ gap: "10px" }}>
                  <div className="input-group">
                    <label className="input-label">Custom Username (Manual)</label>
                    <input
                      type="text"
                      className="text-input"
                      value={srvUsername}
                      onChange={(e) => setSrvUsername(e.target.value)}
                      placeholder="e.g. root / Administrator"
                    />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Custom Password (Manual)</label>
                    <input
                      type="password"
                      className="text-input"
                      value={srvPassword}
                      onChange={(e) => setSrvPassword(e.target.value)}
                      placeholder={srvPassword === "__UNCHANGED__" ? "•••••••• (Unchanged)" : "Enter password"}
                    />
                  </div>
                </div>
              )}

              {srvProto === 'rdp' && (
                <div style={{
                  display: "flex",
                  flexDirection: "column",
                  gap: "10px",
                  padding: "12px",
                  backgroundColor: "rgba(255, 255, 255, 0.03)",
                  border: "1px solid var(--border-color)",
                  borderRadius: "6px",
                  marginTop: "5px",
                  marginBottom: "5px"
                }}>
                  <span style={{ fontSize: "0.85rem", fontWeight: "600", color: "var(--accent-purple)" }}>RDP Connection Settings (Devolutions style)</span>
                  
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={rdpClipboard} onChange={(e) => setRdpClipboard(e.target.checked)} />
                      Redirect Clipboard
                    </label>

                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={rdpSmartSizing} onChange={(e) => setRdpSmartSizing(e.target.checked)} />
                      Smart Sizing
                    </label>

                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={rdpDrives} onChange={(e) => setRdpDrives(e.target.checked)} />
                      Redirect Drives
                    </label>

                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={rdpPrinters} onChange={(e) => setRdpPrinters(e.target.checked)} />
                      Redirect Printers
                    </label>

                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={rdpSmartcards} onChange={(e) => setRdpSmartcards(e.target.checked)} />
                      Redirect Smart Cards
                    </label>

                    <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                      <input type="checkbox" checked={rdpWebauthn} onChange={(e) => setRdpWebauthn(e.target.checked)} />
                      Redirect WebAuthn
                    </label>
                  </div>

                  <div className="input-group" style={{ marginTop: "5px" }}>
                    <label className="input-label" style={{ fontSize: "0.8rem" }}>Audio Redirection</label>
                    <select className="text-input" style={{ backgroundColor: "#000", padding: "4px 8px" }} value={rdpAudio} onChange={(e) => setRdpAudio(parseInt(e.target.value))}>
                      <option value={0}>Play on this computer (Local)</option>
                      <option value={1}>Play on remote computer</option>
                      <option value={2}>Do not play (Mute)</option>
                    </select>
                  </div>
                </div>
              )}

              <div className="input-group">
                <label className="input-label">Notes / Description</label>
                <textarea className="text-input" value={srvDesc} onChange={(e) => setSrvDesc(e.target.value)} rows={2} placeholder="Optional server summary details..." style={{ resize: "none" }} />
              </div>

              <div className="form-actions">
                <button type="button" className="btn btn-secondary" onClick={() => setServerModalOpen(false)}>Cancel</button>
                <button type="submit" className="btn btn-primary">Save Connection</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Credential CRUD Modal */}
      {credModalOpen && (
        <div className="modal-overlay">
          <div className="modal-box glass-card">
            <div className="header-row" style={{ marginBottom: "15px" }}>
              <h3>{editingCred ? "Edit Vault Credential" : "New Secure Credential"}</h3>
              <button className="btn btn-secondary" style={{ padding: "4px" }} onClick={() => setCredModalOpen(false)}>
                <X size={16} />
              </button>
            </div>

            <form onSubmit={saveCredential} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div className="input-group">
                <label className="input-label">Credential Name</label>
                <input type="text" className="text-input" value={credName} onChange={(e) => setCredName(e.target.value)} required placeholder="e.g. AWS Prod admin" />
              </div>

              <div className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Auth Type</label>
                  <select className="text-input" style={{ backgroundColor: "#000" }} value={credType} onChange={(e) => setCredType(e.target.value as 'password' | 'ssh_key')}>
                    <option value="password">Password</option>
                    <option value="ssh_key">SSH Private Key</option>
                  </select>
                </div>
                <div className="input-group">
                  <label className="input-label">Username</label>
                  <input type="text" className="text-input" value={credUser} onChange={(e) => setCredUser(e.target.value)} required placeholder="ubuntu / Administrator" />
                </div>
              </div>

              <div className="input-group">
                <label className="input-label">
                  {credType === "ssh_key" ? "SSH Private Key Content" : "Password Secret"}
                </label>
                {credType === "ssh_key" ? (
                  <textarea
                    className="text-input"
                    value={credSecret}
                    onChange={(e) => setCredSecret(e.target.value)}
                    rows={6}
                    placeholder="-----BEGIN OPENSSH PRIVATE KEY-----&#10;..."
                    required={!editingCred} // Not required if editing (preserves existing secret unless updated)
                    style={{ fontFamily: "var(--font-mono)", fontSize: "0.8rem", resize: "none" }}
                  />
                ) : (
                  <input
                    type="password"
                    className="text-input"
                    value={credSecret}
                    onChange={(e) => setCredSecret(e.target.value)}
                    placeholder={editingCred ? "•••••••••••• (Leave blank to keep current)" : "••••••••••••"}
                    required={!editingCred}
                  />
                )}
              </div>

              <div className="form-actions">
                <button type="button" className="btn btn-secondary" onClick={() => setCredModalOpen(false)}>Cancel</button>
                <button type="submit" className="btn btn-primary">Save Securely</button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}

export default App;
