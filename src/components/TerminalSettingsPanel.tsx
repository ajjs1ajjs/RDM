import React from "react";
import { TerminalSettings } from "../hooks/useTerminalSettings";
import { RotateCcw } from "lucide-react";

interface Props {
  settings: TerminalSettings;
  onUpdate: <K extends keyof TerminalSettings>(key: K, value: TerminalSettings[K]) => void;
  onReset: () => void;
}

const PresetButton: React.FC<{
  label: string;
  active: boolean;
  onClick: () => void;
}> = ({ label, active, onClick }) => (
  <button
    onClick={onClick}
    style={{
      padding: "4px 10px",
      borderRadius: "4px",
      border: "1px solid",
      borderColor: active ? "var(--accent-cyan)" : "var(--border-color)",
      background: active ? "rgba(0, 242, 254, 0.1)" : "transparent",
      color: active ? "var(--accent-cyan)" : "var(--text-secondary)",
      cursor: "pointer",
      fontSize: "0.75rem",
      fontFamily: "var(--font-sans)",
    }}
  >
    {label}
  </button>
);

const ColorInput: React.FC<{
  label: string;
  value: string;
  onChange: (v: string) => void;
}> = ({ label, value, onChange }) => (
  <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
    <span style={{ fontSize: "0.8rem", color: "var(--text-secondary)", minWidth: "130px" }}>{label}</span>
    <input
      type="color"
      value={value.startsWith("rgba") || value.startsWith("var(") ? "#000000" : value}
      onChange={(e) => onChange(e.target.value)}
      style={{ width: "32px", height: "24px", border: "none", cursor: "pointer", background: "none", padding: 0 }}
    />
    <input
      type="text"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      style={{
        background: "var(--bg-input)",
        border: "1px solid var(--border-color)",
        borderRadius: "4px",
        color: "var(--text-primary)",
        padding: "2px 6px",
        fontSize: "0.75rem",
        fontFamily: "var(--font-mono)",
        width: "130px",
      }}
    />
  </div>
);

const SliderInput: React.FC<{
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  unit?: string;
  onChange: (v: number) => void;
}> = ({ label, value, min, max, step = 1, unit = "", onChange }) => (
  <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
    <span style={{ fontSize: "0.8rem", color: "var(--text-secondary)", minWidth: "130px" }}>{label}</span>
    <input
      type="range"
      min={min}
      max={max}
      step={step}
      value={value}
      onChange={(e) => onChange(Number(e.target.value))}
      style={{ flex: 1, cursor: "pointer" }}
    />
    <span style={{ fontSize: "0.8rem", color: "var(--accent-cyan)", minWidth: "50px", textAlign: "right" }}>
      {value}{unit}
    </span>
  </div>
);

export const TerminalSettingsPanel: React.FC<Props> = ({ settings, onUpdate, onReset }) => {
  const sectionStyle: React.CSSProperties = {
    borderBottom: "1px solid var(--border-color)",
    paddingBottom: "15px",
    marginBottom: "15px",
  };

  const rowStyle: React.CSSProperties = {
    display: "flex",
    flexDirection: "column",
    gap: "8px",
    marginBottom: "10px",
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "4px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "8px" }}>
        <h3 style={{ fontSize: "1rem", margin: 0 }}>Terminal Appearance / Вигляд терміналу</h3>
        <button
          className="btn btn-secondary"
          onClick={onReset}
          style={{ display: "flex", alignItems: "center", gap: "4px", padding: "4px 10px", fontSize: "0.75rem" }}
        >
          <RotateCcw size={12} />
          Reset
        </button>
      </div>

      <div style={sectionStyle}>
        <h4 style={{ fontSize: "0.85rem", color: "var(--accent-cyan)", marginBottom: "10px" }}>Font / Шрифт</h4>
        <div style={rowStyle}>
          <SliderInput
            label="Font Size / Розмір"
            value={settings.fontSize}
            min={10}
            max={24}
            onChange={(v) => onUpdate("fontSize", v)}
          />
          <SliderInput
            label="Line Height / Висота рядка"
            value={settings.lineHeight}
            min={1.0}
            max={2.0}
            step={0.05}
            unit="x"
            onChange={(v) => onUpdate("lineHeight", v)}
          />
        </div>
      </div>

      <div style={sectionStyle}>
        <h4 style={{ fontSize: "0.85rem", color: "var(--accent-cyan)", marginBottom: "10px" }}>Cursor / Курсор</h4>
        <div style={{ display: "flex", gap: "6px", marginBottom: "8px" }}>
          {(["block", "underline", "bar"] as const).map((s) => (
            <PresetButton
              key={s}
              label={s.charAt(0).toUpperCase() + s.slice(1)}
              active={settings.cursorStyle === s}
              onClick={() => onUpdate("cursorStyle", s)}
            />
          ))}
        </div>
        <div style={rowStyle}>
          <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
            <input
              type="checkbox"
              checked={settings.cursorBlink}
              onChange={(e) => onUpdate("cursorBlink", e.target.checked)}
              id="cursorBlink"
            />
            <label htmlFor="cursorBlink" style={{ fontSize: "0.8rem", color: "var(--text-secondary)", cursor: "pointer" }}>
              Blink / Миготіння
            </label>
          </div>
        </div>
      </div>

      <div style={sectionStyle}>
        <h4 style={{ fontSize: "0.85rem", color: "var(--accent-cyan)", marginBottom: "10px" }}>Colors / Кольори</h4>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "4px 20px" }}>
          <ColorInput label="Background / Фон" value={settings.background} onChange={(v) => onUpdate("background", v)} />
          <ColorInput label="Foreground / Текст" value={settings.foreground} onChange={(v) => onUpdate("foreground", v)} />
          <ColorInput label="Cursor / Курсор" value={settings.cursor} onChange={(v) => onUpdate("cursor", v)} />
          <ColorInput label="Selection BG" value={settings.selectionBackground} onChange={(v) => onUpdate("selectionBackground", v)} />
          <ColorInput label="Selection FG" value={settings.selectionForeground} onChange={(v) => onUpdate("selectionForeground", v)} />
        </div>
      </div>

      <div style={sectionStyle}>
        <h4 style={{ fontSize: "0.85rem", color: "var(--accent-cyan)", marginBottom: "10px" }}>ANSI Palette / Палітра ANSI</h4>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "4px 20px" }}>
          <ColorInput label="Black" value={settings.black} onChange={(v) => onUpdate("black", v)} />
          <ColorInput label="Bright Black" value={settings.brightBlack} onChange={(v) => onUpdate("brightBlack", v)} />
          <ColorInput label="Red" value={settings.red} onChange={(v) => onUpdate("red", v)} />
          <ColorInput label="Bright Red" value={settings.brightRed} onChange={(v) => onUpdate("brightRed", v)} />
          <ColorInput label="Green" value={settings.green} onChange={(v) => onUpdate("green", v)} />
          <ColorInput label="Bright Green" value={settings.brightGreen} onChange={(v) => onUpdate("brightGreen", v)} />
          <ColorInput label="Yellow" value={settings.yellow} onChange={(v) => onUpdate("yellow", v)} />
          <ColorInput label="Bright Yellow" value={settings.brightYellow} onChange={(v) => onUpdate("brightYellow", v)} />
          <ColorInput label="Blue" value={settings.blue} onChange={(v) => onUpdate("blue", v)} />
          <ColorInput label="Bright Blue" value={settings.brightBlue} onChange={(v) => onUpdate("brightBlue", v)} />
          <ColorInput label="Magenta" value={settings.magenta} onChange={(v) => onUpdate("magenta", v)} />
          <ColorInput label="Bright Magenta" value={settings.brightMagenta} onChange={(v) => onUpdate("brightMagenta", v)} />
          <ColorInput label="Cyan" value={settings.cyan} onChange={(v) => onUpdate("cyan", v)} />
          <ColorInput label="Bright Cyan" value={settings.brightCyan} onChange={(v) => onUpdate("brightCyan", v)} />
          <ColorInput label="White" value={settings.white} onChange={(v) => onUpdate("white", v)} />
          <ColorInput label="Bright White" value={settings.brightWhite} onChange={(v) => onUpdate("brightWhite", v)} />
        </div>
      </div>
    </div>
  );
};
