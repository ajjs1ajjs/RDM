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
  fontSize: 10,
  fontFamily: "Courier New",
  lineHeight: 1.2,
  cursorStyle: "block",
  cursorBlink: true,
  background: "#000000",
  foreground: "#bbbbbb",
  cursor: "#bbbbbb",
  cursorAccent: "#000000",
  selectionBackground: "#ffffff",
  selectionForeground: "#ffffff",
  black: "#000000",
  red: "#bb0000",
  green: "#00bb00",
  yellow: "#bbbb00",
  blue: "#0000bb",
  magenta: "#bb00bb",
  cyan: "#00bbbb",
  white: "#bbbbbb",
  brightBlack: "#555555",
  brightRed: "#ff0000",
  brightGreen: "#00ff00",
  brightYellow: "#ffff00",
  brightBlue: "#0000ff",
  brightMagenta: "#ff00ff",
  brightCyan: "#00ffff",
  brightWhite: "#ffffff",
};

const STORAGE_KEY = "rdm_terminal_settings";

export const TERMINAL_SETTINGS_EVENT = "rdm_terminal_settings_changed";

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
      window.dispatchEvent(new CustomEvent(TERMINAL_SETTINGS_EVENT, { detail: settings }));
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
