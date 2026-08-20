import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";

let cachedPlatform: string | null = null;

export function usePlatform(): string | null {
  const [platform, setPlatform] = useState<string | null>(cachedPlatform);

  useEffect(() => {
    if (cachedPlatform) return;
    invoke<string>("get_platform")
      .then((p) => {
        cachedPlatform = p;
        setPlatform(p);
      })
      .catch(() => {
        cachedPlatform = "windows";
        setPlatform("windows");
      });
  }, []);

  return platform;
}

export function useIsWindows(): boolean {
  const platform = usePlatform();
  return platform === null || platform === "windows";
}