import { useState, useCallback } from "react";

export function useVault() {
  const [unlocked] = useState<boolean>(true);

  const checkUnlockStatus = useCallback(async () => {}, []);

  return {
    unlocked, setUnlocked: (_v: boolean) => {},
    checkUnlockStatus,
  };
}
