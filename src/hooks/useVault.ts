import { useState, useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";

export function useVault() {
  const [unlocked, setUnlocked] = useState<boolean>(true);
  const [needsMigration, setNeedsMigration] = useState<boolean>(false);
  const [migrating, setMigrating] = useState<boolean>(false);
  const [migrationError, setMigrationError] = useState<string>("");

  const checkUnlockStatus = useCallback(async () => {
    try {
      const isSetup = await invoke<boolean>("is_vault_setup");
      if (isSetup) {
        // Vault has a real master password — needs migration to default_rdm_key
        setNeedsMigration(true);
        setUnlocked(false);
      } else {
        setNeedsMigration(false);
        setUnlocked(true);
      }
    } catch {
      // Default to unlocked if check fails
      setUnlocked(true);
    }
  }, []);

  const migrateVault = useCallback(async (oldPassword: string) => {
    setMigrating(true);
    setMigrationError("");
    try {
      await invoke("migrate_vault_to_default", { oldPassword });
      setNeedsMigration(false);
      setUnlocked(true);
    } catch (e: any) {
      const msg = typeof e === "string" ? e : e?.message || String(e);
      setMigrationError(msg);
      throw e;
    } finally {
      setMigrating(false);
    }
  }, []);

  const resetVault = useCallback(async () => {
    if (!window.confirm("Are you sure? All encrypted passwords and credentials will be permanently lost.\n\nВи впевнені? Усі зашифровані паролі та облікові дані буде втрачено назавжди.")) return;
    try {
      await invoke("reset_vault");
      setNeedsMigration(false);
      setUnlocked(true);
    } catch (e: any) {
      const msg = typeof e === "string" ? e : e?.message || String(e);
      setMigrationError(msg);
    }
  }, []);

  return {
    unlocked,
    setUnlocked: (_v: boolean) => {},
    needsMigration,
    migrating,
    migrationError,
    checkUnlockStatus,
    migrateVault,
    resetVault,
  };
}
