import { useCallback, useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

export interface UpdateProgress {
  downloaded: number;
  total: number;
}

export type UpdatePhase = "idle" | "downloading" | "installing" | "done";

export interface AppUpdateState {
  /** An update is available (via signed updater or GitHub API fallback). */
  available: boolean;
  latest: string;
  current: string;
  /** Fallback releases page — used only when the in-app updater is unavailable. */
  fallbackUrl: string;
  /** True when the update can be installed fully in-app (signed updater path). */
  canInstallInApp: boolean;
  phase: UpdatePhase;
  progress: UpdateProgress | null;
  error: string;
  install: () => Promise<void>;
  dismissError: () => void;
}

export function useAppUpdate(): AppUpdateState {
  const [available, setAvailable] = useState(false);
  const [latest, setLatest] = useState("");
  const [current, setCurrent] = useState("");
  const [fallbackUrl, setFallbackUrl] = useState("");
  const [canInstallInApp, setCanInstallInApp] = useState(false);
  const [phase, setPhase] = useState<UpdatePhase>("idle");
  const [progress, setProgress] = useState<UpdateProgress | null>(null);
  const [error, setError] = useState("");
  const unlistenRef = useRef<(() => void) | null>(null);

  useEffect(() => {
    let cancelled = false;

    const check = async () => {
      // Preferred path: signed Tauri updater — downloads and installs without
      // ever leaving the app. Requires the release to publish latest.json.
      try {
        const res = await invoke<{ available: boolean; latest_version: string; current_version: string; download_url: string }>("check_update_status");
        if (cancelled) return;
        setCurrent(res.current_version);
        if (res.available) {
          setAvailable(true);
          setLatest(res.latest_version.startsWith("v") ? res.latest_version : `v${res.latest_version}`);
          setCanInstallInApp(true);
        }
        return;
      } catch {
        // Updater unavailable (no latest.json yet, portable build, network…) —
        // fall back to a plain GitHub API check and open the releases page.
      }
      try {
        const res = await invoke<{ available: boolean; latest_version: string; current_version: string; download_url: string }>("check_for_update");
        if (cancelled) return;
        setCurrent(res.current_version);
        if (res.available) {
          setAvailable(true);
          setLatest(res.latest_version);
          setFallbackUrl(res.download_url);
        }
      } catch {
        // No update information available — stay silent.
      }
    };

    check();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    return () => {
      unlistenRef.current?.();
    };
  }, []);

  const install = useCallback(async () => {
    if (phase === "downloading" || phase === "installing") return;
    setError("");
    setProgress({ downloaded: 0, total: 0 });
    setPhase("downloading");
    try {
      const unlisten = await listen<UpdateProgress>("update://progress", (event) => {
        setProgress(event.payload);
      });
      unlistenRef.current = unlisten;
      setPhase("installing");
      await invoke("install_update");
      // The app exits/restarts itself after a successful install.
      setPhase("done");
    } catch (e: any) {
      setError(typeof e === "string" ? e : e?.message || String(e));
      setPhase("idle");
      setProgress(null);
    } finally {
      unlistenRef.current?.();
      unlistenRef.current = null;
    }
  }, [phase]);

  const dismissError = useCallback(() => setError(""), []);

  return {
    available,
    latest,
    current,
    fallbackUrl,
    canInstallInApp,
    phase,
    progress,
    error,
    install,
    dismissError,
  };
}
