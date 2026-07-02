import { useState, useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";
import { Credential } from "../types";

export function useCredentials() {
  const [credentials, setCredentials] = useState<Credential[]>([]);

  const loadCredentials = useCallback(async () => {
    try {
      const list = await invoke<Credential[]>("get_credentials");
      setCredentials(list);
    } catch (e) {
      console.error("Failed to load credentials", e);
    }
  }, []);

  return { credentials, setCredentials, loadCredentials };
}
