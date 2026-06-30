import React, { useState, useEffect } from "react";
import { Server } from "../types";
import { Folder, FolderOpen, Shield, Settings, LayoutDashboard, Terminal, Tag, ChevronDown, ChevronRight, Star } from "lucide-react";

interface SidebarProps {
  servers: Server[];
  activeTabType: string;
  selectedFolder: string;
  selectedTag: string;
  favoritesOnly: boolean;
  onSelectFolder: (folder: string) => void;
  onSelectTag: (tag: string) => void;
  onToggleFavorites: () => void;
  onNavigateTo: (type: 'dashboard' | 'credentials' | 'settings') => void;
}

interface FolderNode {
  name: string;
  fullPath: string;
  children: { [key: string]: FolderNode };
}

export const Sidebar: React.FC<SidebarProps> = ({
  servers,
  activeTabType,
  selectedFolder,
  selectedTag,
  favoritesOnly,
  onSelectFolder,
  onSelectTag,
  onToggleFavorites,
  onNavigateTo,
}) => {
  const [collapsedFolders, setCollapsedFolders] = useState<{ [key: string]: boolean }>(() => {
    const saved = localStorage.getItem("rdm_collapsedFolders");
    return saved ? JSON.parse(saved) : {};
  });

  useEffect(() => {
    localStorage.setItem("rdm_collapsedFolders", JSON.stringify(collapsedFolders));
  }, [collapsedFolders]);

  // 1. Build folder tree dynamically
  const buildFolderTree = (serversList: Server[]): FolderNode => {
    const root: FolderNode = { name: "Root", fullPath: "", children: {} };
    
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

  const folderTree = buildFolderTree(servers);

  // 2. Extract all unique tags
  const extractTags = (serversList: Server[]): string[] => {
    const tagsSet = new Set<string>();
    serversList.forEach((s) => {
      if (s.tags) {
        s.tags.split(",").map(t => t.trim()).filter(Boolean).forEach(t => tagsSet.add(t));
      }
    });
    return Array.from(tagsSet).sort();
  };

  const tags = extractTags(servers);

  const toggleFolder = (path: string, e: React.MouseEvent) => {
    e.stopPropagation();
    setCollapsedFolders(prev => ({
      ...prev,
      [path]: !prev[path]
    }));
  };

  // Recursive folder node renderer
  const renderFolderNode = (node: FolderNode, depth: number = 0) => {
    const childrenKeys = Object.keys(node.children);
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
            <div onClick={(e) => toggleFolder(node.fullPath, e)}>
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
            <span style={{ marginLeft: "4px" }}>{node.name}</span>
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
        <Terminal className="logo-icon" size={24} />
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

      <div className="sidebar-section-title">Servers Directory</div>
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
