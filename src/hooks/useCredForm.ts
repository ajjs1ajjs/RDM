import { useState, useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";
import { Credential } from "../types";
import { useDialogs } from "../components/AppDialogs";

export function useCredForm(loadCredentials: () => Promise<void>) {
  const dialogs = useDialogs();
  const [credModalOpen, setCredModalOpen] = useState<boolean>(false);
  const [editingCred, setEditingCred] = useState<Credential | null>(null);
  const [credName, setCredName] = useState("");
  const [credType, setCredType] = useState<'password' | 'ssh_key'>("password");
  const [credUser, setCredUser] = useState("");
  const [credSecret, setCredSecret] = useState("");

  const openCredForm = useCallback((cred: Credential | null = null) => {
    setEditingCred(cred);
    if (cred) {
      setCredName(cred.name);
      setCredType(cred.type);
      setCredUser(cred.username);
      setCredSecret("");
    } else {
      setCredName("");
      setCredType("password");
      setCredUser("");
      setCredSecret("");
    }
    setCredModalOpen(true);
  }, []);

  const saveCredential = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (editingCred) {
        await invoke("update_credential", {
          id: editingCred.id, name: credName,
          credType: credType, username: credUser,
          secret: credSecret || null,
        });
      } else {
        await invoke("add_credential", {
          name: credName, credType: credType,
          username: credUser, secret: credSecret,
        });
      }
      setCredModalOpen(false);
      loadCredentials();
    } catch (e: any) {
      setCredModalOpen(false);
      await dialogs.alert(`Failed to save credential: ${e}`);
    }
  }, [editingCred, credName, credType, credUser, credSecret, loadCredentials, dialogs]);

  const deleteCredential = useCallback(async (id: string) => {
    if (!await dialogs.confirm("Are you sure you want to delete this credential? Any linked servers will lose their auto-connection credentials.")) return;
    try {
      await invoke("delete_credential", { id });
      loadCredentials();
    } catch (e: any) {
      await dialogs.alert(`Failed to delete credential: ${e}`);
    }
  }, [loadCredentials, dialogs]);

  return {
    credModalOpen, setCredModalOpen,
    editingCred, setEditingCred,
    credName, setCredName, credType, setCredType,
    credUser, setCredUser, credSecret, setCredSecret,
    openCredForm, saveCredential, deleteCredential,
  };
}
