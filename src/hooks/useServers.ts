import { useState, useEffect, useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";
import { Server } from "../types";

export function useServers() {
  const [servers, setServers] = useState<Server[]>([]);
  const [favorites, setFavorites] = useState<string[]>([]);
  const [selectedServer, setSelectedServer] = useState<Server | null>(null);
  const [selectedFolder, setSelectedFolder] = useState<string>("");
  const [selectedTag, setSelectedTag] = useState<string>("");
  const [favoritesOnly, setFavoritesOnly] = useState<boolean>(() => {
    return localStorage.getItem("rdm_favoritesOnly") === "true";
  });
  const [customFolders, setCustomFolders] = useState<string[]>([]);

  useEffect(() => {
    localStorage.setItem("rdm_favoritesOnly", favoritesOnly.toString());
  }, [favoritesOnly]);

  const loadServers = useCallback(async () => {
    try {
      const list = await invoke<Server[]>("get_servers");
      const normalizedList = list.map(s => ({
        ...s,
        folder_path: s.folder_path ? s.folder_path.replace(/\\/g, '/') : ""
      }));
      setServers(normalizedList);
    } catch (e) {
      console.error("Failed to load servers", e);
    }
  }, []);

  const loadFavorites = useCallback(async () => {
    try {
      const favsJson = await invoke<string | null>("get_setting", { key: "favorites" });
      if (favsJson) {
        setFavorites(JSON.parse(favsJson));
      }
    } catch (e) {
      console.error("Failed to load favorites setting", e);
    }
  }, []);

  const loadCustomFolders = useCallback(async () => {
    try {
      const foldersJson = await invoke<string | null>("get_setting", { key: "custom_folders" });
      if (foldersJson) {
        const parsed: string[] = JSON.parse(foldersJson);
        const normalized = parsed.map(p => p ? p.replace(/\\/g, '/') : "");
        setCustomFolders(normalized);
      }
    } catch (e) {
      console.error("Failed to load custom folders setting", e);
    }
  }, []);

  const saveCustomFolders = useCallback(async (folders: string[]) => {
    setCustomFolders(folders);
    try {
      await invoke("set_setting", { key: "custom_folders", value: JSON.stringify(folders) });
    } catch (e) {
      console.error("Failed to save custom folders setting", e);
    }
  }, []);

  const toggleFavorite = useCallback(async (id: string) => {
    const isFav = favorites.includes(id);
    const newFavs = isFav ? favorites.filter((fid) => fid !== id) : [...favorites, id];
    setFavorites(newFavs);
    try {
      await invoke("set_setting", { key: "favorites", value: JSON.stringify(newFavs) });
    } catch (e) {
      console.error("Failed to save favorites", e);
    }
  }, [favorites]);

  const handleRenameFolder = useCallback(async (oldPath: string, newPath: string) => {
    if (!newPath || oldPath === newPath) return;

    try {
      const serversToUpdate = servers.filter(s =>
        s.folder_path === oldPath || s.folder_path.startsWith(oldPath + "/")
      );

      for (const s of serversToUpdate) {
        let updatedFolderPath = newPath;
        if (s.folder_path.startsWith(oldPath + "/")) {
          updatedFolderPath = newPath + s.folder_path.substring(oldPath.length);
        }

        await invoke("update_server", {
          id: s.id, name: s.name, hostname: s.hostname, ip: s.ip,
          port: s.port, protocol: s.protocol, os: s.os,
          folderPath: updatedFolderPath, tags: s.tags,
          description: s.description, credentialId: s.credential_id || null,
          username: s.username || null, password: "",
          passwordChanged: false,
          rdpClipboard: s.rdp_clipboard, rdpDrives: s.rdp_drives,
          rdpPrinters: s.rdp_printers, rdpSmartSizing: s.rdp_smart_sizing,
          rdpAudio: s.rdp_audio, rdpSmartcards: s.rdp_smartcards,
          rdpWebauthn: s.rdp_webauthn, rdpFullscreen: s.rdp_fullscreen,
          rdpMultimon: s.rdp_multimon,
        });
      }

      const updatedCustomFolders = customFolders.map(path => {
        if (path === oldPath) return newPath;
        if (path.startsWith(oldPath + "/")) return newPath + path.substring(oldPath.length);
        return path;
      });

      await saveCustomFolders(updatedCustomFolders);
      await loadServers();

      if (selectedFolder === oldPath) {
        setSelectedFolder(newPath);
      } else if (selectedFolder.startsWith(oldPath + "/")) {
        setSelectedFolder(newPath + selectedFolder.substring(oldPath.length));
      }
    } catch (e) {
      console.error("Failed to rename folder", e);
      alert("Error renaming folder: " + e);
    }
  }, [servers, customFolders, selectedFolder, saveCustomFolders, loadServers]);

  const handleDeleteFolder = useCallback(async (folderPath: string) => {
    try {
      const serversToDelete = servers.filter(s =>
        s.folder_path === folderPath || s.folder_path.startsWith(folderPath + "/")
      );

      for (const s of serversToDelete) {
        await invoke("delete_server", { id: s.id });
      }

      const updatedCustomFolders = customFolders.filter(path =>
        path !== folderPath && !path.startsWith(folderPath + "/")
      );

      await saveCustomFolders(updatedCustomFolders);
      await loadServers();

      if (selectedFolder === folderPath || selectedFolder.startsWith(folderPath + "/")) {
        setSelectedFolder("");
      }
    } catch (e) {
      console.error("Failed to delete folder", e);
      alert("Error deleting folder: " + e);
    }
  }, [servers, customFolders, selectedFolder, saveCustomFolders, loadServers]);

  return {
    servers, setServers,
    favorites, setFavorites,
    selectedServer, setSelectedServer,
    selectedFolder, setSelectedFolder,
    selectedTag, setSelectedTag,
    favoritesOnly, setFavoritesOnly,
    customFolders,
    loadServers, loadFavorites, loadCustomFolders,
    saveCustomFolders, toggleFavorite,
    handleRenameFolder, handleDeleteFolder,
  };
}
