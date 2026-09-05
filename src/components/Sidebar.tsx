import React, { useState, useEffect, useMemo } from "react";
import { Server } from "../types";
import { Folder, FolderOpen, Shield, Settings, LayoutDashboard, Tag, ChevronDown, ChevronRight, Star, Plus, Edit2, Trash2 } from "lucide-react";

interface SidebarProps {
  servers: Server[];
  customFolders: string[];
  activeTabType: string;
  selectedFolder: string;
  selectedTag: string;
  favoritesOnly: boolean;
  onSelectFolder: (folder: string) => void;
  onSelectTag: (tag: string) => void;
  onToggleFavorites: () => void;
  onNavigateTo: (type: 'dashboard' | 'credentials' | 'settings') => void;
  onCreateFolder: (parentFolder: string) => void;
  onRenameFolder: (folderPath: string) => void;
  onDeleteFolder: (folderPath: string) => void;
}

interface FolderNode {
  name: string;
  fullPath: string;
  children: { [key: string]: FolderNode };
}

export const Sidebar: React.FC<SidebarProps> = ({
  servers,
  customFolders,
  activeTabType,
  selectedFolder,
  selectedTag,
  favoritesOnly,
  onSelectFolder,
  onSelectTag,
  onToggleFavorites,
  onNavigateTo,
  onCreateFolder,
  onRenameFolder,
  onDeleteFolder,
}) => {
  const [collapsedFolders, setCollapsedFolders] = useState<{ [key: string]: boolean }>(() => {
    const saved = localStorage.getItem("rdm_collapsedFolders");
    return saved ? JSON.parse(saved) : {};
  });

  useEffect(() => {
    localStorage.setItem("rdm_collapsedFolders", JSON.stringify(collapsedFolders));
  }, [collapsedFolders]);

  // 1. Build folder tree dynamically from servers and custom manually created folders
  const buildFolderTree = (serversList: Server[], customFoldersList: string[]): FolderNode => {
    const root: FolderNode = { name: "Root", fullPath: "", children: {} };
    
    // Add custom manually created folders first
    customFoldersList.forEach((folderPath) => {
      if (!folderPath) return;
      const parts = folderPath.split("/").filter(Boolean);
      let current = root;
      let accumulatedPath = "";
      
      parts.forEach((part) => {
        accumulatedPath = accumulatedPath ? `${accumulatedPath}/${part}` : part;
        if (!current.children[part]) {
          current.children[part] = {
            name: part,
            fullPath: accumulatedPath,
            children: {},
          };
        }
        current = current.children[part];
      });
    });
    
    // Add folders from servers
    serversList.forEach((server) => {
      if (!server.folder_path) return;
      const parts = server.folder_path.split("/").filter(Boolean);
      let current = root;
      let accumulatedPath = "";
      
      parts.forEach((part) => {
        accumulatedPath = accumulatedPath ? `${accumulatedPath}/${part}` : part;
        if (!current.children[part]) {
          current.children[part] = {
            name: part,
            fullPath: accumulatedPath,
            children: {},
          };
        }
        current = current.children[part];
      });
    });
    
    return root;
  };

  const folderTree = useMemo(() => buildFolderTree(servers, customFolders), [servers, customFolders]);

  const tags = useMemo(() => {
    const tagsSet = new Set<string>();
    servers.forEach((s) => {
      if (s.tags) {
        s.tags.split(",").map(t => t.trim()).filter(Boolean).forEach(t => tagsSet.add(t));
      }
    });
    return Array.from(tagsSet).sort();
  }, [servers]);

  const toggleFolder = (path: string, e: React.MouseEvent) => {
    e.stopPropagation();
    setCollapsedFolders(prev => ({
      ...prev,
      [path]: !prev[path]
    }));
  };

  // Recursive folder node renderer
  const renderFolderNode = (node: FolderNode, depth: number = 0) => {
    const childrenKeys = Object.keys(node.children).sort((a, b) => a.localeCompare(b));
    if (depth > 0 && node.name === "Root") return null;

    const isCollapsed = collapsedFolders[node.fullPath];
    const isSelected = selectedFolder === node.fullPath && activeTabType === "dashboard";

    return (
      <div key={node.fullPath || "root-node"} className="tree-node">
        {depth > 0 && (
          <div
            className={`tree-row ${isSelected ? "selected" : ""}`}
            style={{ paddingLeft: `${(depth - 1) * 12 + 6}px` }}
            onClick={() => {
              onSelectFolder(node.fullPath);
              onNavigateTo("dashboard");
            }}
          >
            <div onClick={(e) => toggleFolder(node.fullPath, e)} style={{ display: "flex", alignItems: "center" }}>
              {childrenKeys.length > 0 ? (
                isCollapsed ? <ChevronRight size={14} /> : <ChevronDown size={14} />
              ) : (
                <div style={{ width: 14 }} />
              )}
            </div>
            {isCollapsed ? (
              <Folder size={16} className="logo-icon" style={{ color: "var(--accent-cyan)" }} />
            ) : (
              <FolderOpen size={16} className="logo-icon" style={{ color: "var(--accent-cyan)" }} />
            )}
            <span style={{ marginLeft: "4px", flexGrow: 1 }} className="folder-name-text">{node.name}</span>

            <div className="folder-row-actions">
              <button
                className="folder-action-btn"
                onClick={(e) => {
                  e.stopPropagation();
                  onCreateFolder(node.fullPath);
                }}
                title="Create Subfolder"
              >
                <Plus size={12} />
              </button>
              <button
                className="folder-action-btn"
                onClick={(e) => {
                  e.stopPropagation();
                  onRenameFolder(node.fullPath);
                }}
                title="Rename Folder"
              >
                <Edit2 size={12} />
              </button>
              <button
                className="folder-action-btn"
                onClick={(e) => {
                  e.stopPropagation();
                  onDeleteFolder(node.fullPath);
                }}
                title="Delete Folder"
              >
                <Trash2 size={12} />
              </button>
            </div>
          </div>
        )}

        {/* Render children if not collapsed */}
        {(depth === 0 || !isCollapsed) &&
          childrenKeys.map((key) => renderFolderNode(node.children[key], depth + 1))}
      </div>
    );
  };

  return (
    <aside className="sidebar">
      <div className="sidebar-header">
        <img src="/logo.svg" alt="RDM" className="logo-icon" style={{ width: "28px", height: "28px" }} />
        <span className="logo-text">RDM MANAGER</span>
      </div>

      <div className="sidebar-nav">
        <div
          className={`sidebar-item ${activeTabType === "dashboard" && !selectedFolder && !selectedTag && !favoritesOnly ? "active" : ""}`}
          onClick={() => {
            onSelectFolder("");
            onSelectTag("");
            onNavigateTo("dashboard");
          }}
        >
          <LayoutDashboard size={18} />
          <span>Dashboard</span>
        </div>

        <div
          className={`sidebar-item ${activeTabType === "dashboard" && favoritesOnly ? "active" : ""}`}
          onClick={() => {
            onToggleFavorites();
            onNavigateTo("dashboard");
          }}
        >
          <Star size={18} style={{ fill: favoritesOnly ? "var(--accent-warn)" : "none", color: favoritesOnly ? "var(--accent-warn)" : "inherit" }} />
          <span>Favorites</span>
        </div>

        <div
          className={`sidebar-item ${activeTabType === "credentials" ? "active" : ""}`}
          onClick={() => onNavigateTo("credentials")}
        >
          <Shield size={18} />
          <span>Credential Vault</span>
        </div>

        <div
          className={`sidebar-item ${activeTabType === "settings" ? "active" : ""}`}
          onClick={() => onNavigateTo("settings")}
        >
          <Settings size={18} />
          <span>Settings</span>
        </div>
      </div>

      <div className="sidebar-section-title" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <span>Servers Directory</span>
        <button
          className="folder-action-btn"
          onClick={() => onCreateFolder("")}
          title="Create Root Folder"
          style={{ background: "none", border: "none", color: "var(--text-muted)", cursor: "pointer", padding: 0 }}
        >
          <Plus size={14} />
        </button>
      </div>
      <div className="folder-tree">
        {renderFolderNode(folderTree, 0)}
      </div>

      {tags.length > 0 && (
        <>
          <div className="sidebar-section-title">Smart Groups by Tag</div>
          <div className="sidebar-nav" style={{ paddingBottom: "20px" }}>
            {tags.map((tag) => (
              <div
                key={tag}
                className={`sidebar-item ${activeTabType === "dashboard" && selectedTag === tag ? "active" : ""}`}
                onClick={() => {
                  onSelectTag(tag);
                  onNavigateTo("dashboard");
                }}
                style={{ padding: "6px 12px", fontSize: "0.8rem" }}
              >
                <Tag size={14} style={{ color: selectedTag === tag ? "var(--accent-cyan)" : "var(--text-muted)" }} />
                <span>{tag}</span>
              </div>
            ))}
          </div>
        </>
      )}
    </aside>
  );
};

