import { useState, useCallback } from "react";
import { Server } from "../types";

export function useFolderModal(
  servers: Server[],
  customFolders: string[],
  saveCustomFolders: (folders: string[]) => Promise<void>,
  handleRenameFolder: (oldPath: string, newPath: string) => Promise<void>,
  handleDeleteFolder: (folderPath: string) => Promise<void>,
) {
  const [folderModalOpen, setFolderModalOpen] = useState<boolean>(false);
  const [folderModalMode, setFolderModalMode] = useState<'create' | 'rename' | 'delete'>('create');
  const [folderModalParent, setFolderModalParent] = useState<string>('');
  const [folderModalPath, setFolderModalPath] = useState<string>('');
  const [folderModalName, setFolderModalName] = useState<string>('');

  const handleCreateFolder = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    const newPath = folderModalParent
      ? `${folderModalParent}/${folderModalName.trim()}`
      : folderModalName.trim();
    if (!folderModalName.trim()) return;
    if (customFolders.includes(newPath) || servers.some(s => s.folder_path === newPath || s.folder_path.startsWith(newPath + "/"))) {
      alert("Folder already exists!");
      return;
    }
    await saveCustomFolders([...customFolders, newPath]);
    setFolderModalOpen(false);
  }, [folderModalParent, folderModalName, customFolders, servers, saveCustomFolders]);

  const handleRenameSubmit = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    const parts = folderModalPath.split('/');
    parts[parts.length - 1] = folderModalName.trim();
    const newPath = parts.join('/');
    if (!folderModalName.trim()) return;
    if (folderModalPath !== newPath) {
      if (customFolders.includes(newPath) || servers.some(s => s.folder_path === newPath || s.folder_path.startsWith(newPath + "/"))) {
        alert("A folder with that name already exists in this location!");
        return;
      }
      await handleRenameFolder(folderModalPath, newPath);
    }
    setFolderModalOpen(false);
  }, [folderModalPath, folderModalName, customFolders, servers, handleRenameFolder]);

  const handleDeleteSubmit = useCallback(async () => {
    await handleDeleteFolder(folderModalPath);
    setFolderModalOpen(false);
  }, [folderModalPath, handleDeleteFolder]);

  return {
    folderModalOpen, setFolderModalOpen,
    folderModalMode, setFolderModalMode,
    folderModalParent, setFolderModalParent,
    folderModalPath, setFolderModalPath,
    folderModalName, setFolderModalName,
    handleCreateFolder, handleRenameSubmit, handleDeleteSubmit,
  };
}
