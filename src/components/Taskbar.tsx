import React, { useState, useEffect } from "react";
import { Terminal, Monitor, HardDrive } from "lucide-react";

interface TaskbarTab {
  id: string;
  title: string;
  type: string;
}

interface TaskbarProps {
  activeTabs: TaskbarTab[];
  currentTabId: string;
  onSelectTab: (tab: any) => void;
  visible: boolean;
}

const ClockDisplay: React.FC = () => {
  const [time, setTime] = useState(
    new Date().toLocaleTimeString("uk-UA", { hour: "2-digit", minute: "2-digit" })
  );
  useEffect(() => {
    const tick = () => {
      setTime(new Date().toLocaleTimeString("uk-UA", { hour: "2-digit", minute: "2-digit" }));
    };
    tick();
    const interval = setInterval(tick, 15000);
    return () => clearInterval(interval);
  }, []);
  return <>{time}</>;
};

export const Taskbar: React.FC<TaskbarProps> = ({ activeTabs, currentTabId, onSelectTab, visible }) => {
  const [startOpen, setStartOpen] = useState(false);

  if (!visible) return null;

  const tabIcon = (type: string) => {
    switch (type) {
      case "rdp": return <Monitor size={12} style={{ color: "var(--accent-cyan)", flexShrink: 0 }} />;
      case "ssh": return <Terminal size={12} style={{ color: "var(--accent-green)", flexShrink: 0 }} />;
      case "sftp": return <HardDrive size={12} style={{ color: "var(--accent-purple)", flexShrink: 0 }} />;
      default: return null;
    }
  };

  return (
    <div className="taskbar">
      <div className="taskbar-start-section">
        <button
          className={`taskbar-start-btn ${startOpen ? "active" : ""}`}
          onClick={() => setStartOpen(!startOpen)}
          title="Start"
        >
          <svg viewBox="0 0 48 48" width="18" height="18" style={{ filter: "drop-shadow(0 0 4px rgba(0,242,254,0.4))" }}>
            <defs>
              <linearGradient id="slogo" x1="0" y1="0" x2="48" y2="48">
                <stop offset="0%" stopColor="#00f2fe"/>
                <stop offset="100%" stopColor="#bf5af2"/>
              </linearGradient>
            </defs>
            <rect x="4" y="4" width="16" height="16" rx="3" fill="url(#slogo)" opacity="0.8"/>
            <rect x="28" y="4" width="16" height="16" rx="3" fill="url(#slogo)" opacity="0.6"/>
            <rect x="4" y="28" width="16" height="16" rx="3" fill="url(#slogo)" opacity="0.6"/>
            <rect x="28" y="28" width="16" height="16" rx="3" fill="url(#slogo)" opacity="0.8"/>
          </svg>
        </button>

        {startOpen && (
          <>
            <div className="taskbar-overlay" onClick={() => setStartOpen(false)} />
            <div className="start-menu">
              <div className="start-menu-header">
                <svg viewBox="0 0 48 48" width="32" height="32">
                  <rect x="4" y="4" width="16" height="16" rx="3" fill="#00f2fe" opacity="0.8"/>
                  <rect x="28" y="4" width="16" height="16" rx="3" fill="#bf5af2" opacity="0.6"/>
                  <rect x="4" y="28" width="16" height="16" rx="3" fill="#bf5af2" opacity="0.6"/>
                  <rect x="28" y="28" width="16" height="16" rx="3" fill="#00f2fe" opacity="0.8"/>
                </svg>
                <span>RDM Manager</span>
              </div>
              <div className="start-menu-items">
                <div className="start-menu-item" onClick={() => setStartOpen(false)}>
                  <Terminal size={16} />
                  <span>Open Terminal</span>
                </div>
              </div>
              <div className="start-menu-footer">
                <ClockDisplay />
              </div>
            </div>
          </>
        )}
      </div>

      <div className="taskbar-tabs">
        <div
          className={`taskbar-tab ${currentTabId === "dashboard" ? "active" : ""}`}
          onClick={() => onSelectTab("dashboard")}
        >
          <span style={{ fontSize: "0.7rem" }}>&#9776;</span>
        </div>
        {activeTabs.map((tab) => (
          <div
            key={tab.id}
            className={`taskbar-tab ${currentTabId === tab.id ? "active" : ""}`}
            onClick={() => onSelectTab(tab)}
          >
            {tabIcon(tab.type)}
            <span className="taskbar-tab-title">{tab.title}</span>
          </div>
        ))}
      </div>

      <div className="taskbar-tray">
        <div className="taskbar-tray-item" title="Connected">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--accent-green)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M22 12h-4l-3 9L9 3l-3 9H2"/>
          </svg>
        </div>
        <div className="taskbar-clock">
          <ClockDisplay />
        </div>
      </div>
    </div>
  );
};
