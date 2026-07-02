import { useState, useEffect, useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";

export function useVault() {
  const [unlocked, setUnlocked] = useState<boolean>(true);
  const [autoLockMinutes, setAutoLockMinutes] = useState<number>(() => {
    return parseInt(localStorage.getItem("rdm_autoLockMinutes") || "0", 10);
  });

  useEffect(() => {
    localStorage.setItem("rdm_autoLockMinutes", autoLockMinutes.toString());
    if (autoLockMinutes === 0 || !unlocked) return;

    let timeoutId: number;
    const resetTimer = () => {
      window.clearTimeout(timeoutId);
      timeoutId = window.setTimeout(() => {
        setUnlocked(false);
        invoke("lock_vault").catch(console.error);
      }, autoLockMinutes * 60 * 1000);
    };
    resetTimer();

    const events = ["mousedown", "mousemove", "keypress", "scroll", "touchstart"];
    events.forEach((name) => document.addEventListener(name, resetTimer, true));

    return () => {
      window.clearTimeout(timeoutId);
      events.forEach((name) => document.removeEventListener(name, resetTimer, true));
    };
  }, [autoLockMinutes, unlocked]);

  const checkUnlockStatus = useCallback(async () => {
    try {
      const active = await invoke<boolean>("is_vault_unlocked");
      setUnlocked(active);
    } catch (e) {
      console.error(e);
    }
  }, []);

  const handleUnlock = useCallback(() => {
    setUnlocked(true);
  }, []);

  return {
    unlocked, setUnlocked,
    autoLockMinutes, setAutoLockMinutes,
    checkUnlockStatus, handleUnlock,
  };
}
