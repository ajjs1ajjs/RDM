import { useState, useEffect, useCallback } from "react";

export interface TerminalSettings {
  fontSize: number;
  fontFamily: string;
  lineHeight: number;
  cursorStyle: "block" | "underline" | "bar";
  cursorBlink: boolean;
  background: string;
  foreground: string;
  cursor: string;
  cursorAccent: string;
  selectionBackground: string;
  selectionForeground: string;
  black: string;
  red: string;
  green: string;
  yellow: string;
  blue: string;
  magenta: string;
  cyan: string;
  white: string;
  brightBlack: string;
  brightRed: string;
  brightGreen: string;
  brightYellow: string;
  brightBlue: string;
  brightMagenta: string;
  brightCyan: string;
  brightWhite: string;
}

const DEFAULTS: TerminalSettings = {
  fontSize: 14,
  fontFamily: "var(--font-mono)",
  lineHeight: 1.2,
  cursorStyle: "block",
  cursorBlink: true,
  background: "#05070d",
  foreground: "#f5f6f9",
  cursor: "var(--accent-cyan)",
  cursorAccent: "#05070d",
  selectionBackground: "rgba(0, 242, 254, 0.35)",
  selectionForeground: "#ffffff",
  black: "#000000",
  red: "#ff453a",
  green: "#30d158",
  yellow: "#ffd60a",
  blue: "#0a84ff",
  magenta: "#bf5af2",
  cyan: "#5ffd6b",
  white: "#f5f6f9",
  brightBlack: "#5e6675",
  brightRed: "#ff6961",
  brightGreen: "#30d158",
  brightYellow: "#ffd60a",
  brightBlue: "#409cff",
  brightMagenta: "#da8eff",
  brightCyan: "#00f2fe",
  brightWhite: "#ffffff",
};

const STORAGE_KEY = "rdm_terminal_settings";

export const useTerminalSettings = () => {
  const [settings, setSettings] = useState<TerminalSettings>(() => {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved) {
        return { ...DEFAULTS, ...JSON.parse(saved) };
      }
    } catch {}
    return DEFAULTS;
  });

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
    } catch {}
  }, [settings]);

  const updateSetting = useCallback(<K extends keyof TerminalSettings>(
    key: K,
    value: TerminalSettings[K]
  ) => {
    setSettings((prev) => ({ ...prev, [key]: value }));
  }, []);

  const resetToDefaults = useCallback(() => {
    setSettings(DEFAULTS);
  }, []);

  return { settings, updateSetting, resetToDefaults };
};
