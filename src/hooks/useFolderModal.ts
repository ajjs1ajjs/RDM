import { useState, useCallback } from "react";
import { Server } from "../types";
import { useDialogs } from "../components/AppDialogs";

export function useFolderModal(
  servers: Server[],
  customFolders: string[],
  saveCustomFolders: (folders: string[]) => Promise<void>,
  handleRenameFolder: (oldPath: string, newPath: string) => Promise<void>,
  handleDeleteFolder: (folderPath: string) => Promise<void>,
) {
  const dialogs = useDialogs();
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
      await dialogs.alert("Folder already exists!");
      return;
    }
    try {
      await saveCustomFolders([...customFolders, newPath]);
    } catch (e: any) {
      await dialogs.alert(`Failed to create folder: ${e}`);
    }
    setFolderModalOpen(false);
  }, [folderModalParent, folderModalName, customFolders, servers, saveCustomFolders, dialogs]);

  const handleRenameSubmit = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    const parts = folderModalPath.split('/');
    parts[parts.length - 1] = folderModalName.trim();
    const newPath = parts.join('/');
    if (!folderModalName.trim()) return;
    if (folderModalPath !== newPath) {
      if (customFolders.includes(newPath) || servers.some(s => s.folder_path === newPath || s.folder_path.startsWith(newPath + "/"))) {
        await dialogs.alert("A folder with that name already exists in this location!");
        return;
      }
      try {
        await handleRenameFolder(folderModalPath, newPath);
      } catch (e: any) {
        await dialogs.alert(`Failed to rename folder: ${e}`);
      }
    }
    setFolderModalOpen(false);
  }, [folderModalPath, folderModalName, customFolders, servers, handleRenameFolder, dialogs]);

  const handleDeleteSubmit = useCallback(async () => {
    try {
      await handleDeleteFolder(folderModalPath);
    } catch (e: any) {
      await dialogs.alert(`Failed to delete folder: ${e}`);
    }
    setFolderModalOpen(false);
  }, [folderModalPath, handleDeleteFolder, dialogs]);

  return {
    folderModalOpen, setFolderModalOpen,
    folderModalMode, setFolderModalMode,
    folderModalParent, setFolderModalParent,
    folderModalPath, setFolderModalPath,
    folderModalName, setFolderModalName,
    handleCreateFolder, handleRenameSubmit, handleDeleteSubmit,
  };
}
