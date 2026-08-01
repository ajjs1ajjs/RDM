import React, { useState, useEffect, useRef } from "react";
import { Terminal } from "xterm";
import { FitAddon } from "xterm-addon-fit";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { Copy, Clipboard } from "lucide-react";
import "xterm/css/xterm.css";

interface TerminalTabProps {
  sessionId: string;
  host: string;
  port: number;
  username: string;
  credentialId?: string;
  serverId?: string;
}

export const TerminalTab: React.FC<TerminalTabProps> = ({
  sessionId,
  host,
  port,
  username,
  credentialId,
  serverId,
}) => {
  const terminalRef = useRef<HTMLDivElement>(null);
  const xtermRef = useRef<Terminal | null>(null);
  const fitAddonRef = useRef<FitAddon | null>(null);
  const isConnectedRef = useRef(false);
  const [, setStatus] = useState<'connecting' | 'connected' | 'disconnected'>('connecting');
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number } | null>(null);
  const [hasSelection, setHasSelection] = useState(false);

  useEffect(() => {
    if (!terminalRef.current) return;

    // Initialize xterm.js
    const term = new Terminal({
      cursorBlink: true,
      scrollback: 100000,
      fontFamily: "var(--font-mono)",
      fontSize: 14,
      rightClickSelectsWord: true,
      theme: {
        background: "#05070d",
        foreground: "#f5f6f9",
        cursor: "var(--accent-cyan)",
        selectionBackground: "rgba(0, 242, 254, 0.25)",
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
      },
    });

    const fitAddon = new FitAddon();
    term.loadAddon(fitAddon);
    term.open(terminalRef.current);
    fitAddon.fit();

    xtermRef.current = term;
    fitAddonRef.current = fitAddon;

    // Listen for selection changes to update context menu state
    const selectionSub = term.onSelectionChange(() => {
      setHasSelection(term.hasSelection());
    });

    // Custom keyboard handler for copy/paste
    term.attachCustomKeyEventHandler((event: KeyboardEvent) => {
      if (event.type === "keydown") {
        const isCtrlOrCmd = event.ctrlKey || event.metaKey;

        // Copy: Ctrl+C / Cmd+C / Ctrl+Shift+C / Cmd+Shift+C / Ctrl+Insert
        if (
          (isCtrlOrCmd && (event.key === "c" || event.key === "C")) ||
          (event.ctrlKey && event.key === "Insert")
        ) {
          if (event.shiftKey || term.hasSelection()) {
            const selected = term.getSelection();
            if (selected) {
              navigator.clipboard.writeText(selected).catch((err) => {
                console.error("Clipboard write error:", err);
              });
            }
            return false; // Prevent sending \x03
          }
          // If no selection and no shift, allow Ctrl+C to send \x03 (SIGINT)
          return true;
        }

        // Paste: Ctrl+V / Cmd+V / Ctrl+Shift+V / Cmd+Shift+V / Shift+Insert
        if (
          (isCtrlOrCmd && (event.key === "v" || event.key === "V")) ||
          (event.shiftKey && event.key === "Insert")
        ) {
          navigator.clipboard
            .readText()
            .then((text) => {
              if (text && isConnectedRef.current) {
                invoke("write_ssh_input", { sessionId, data: text }).catch((e) =>
                  console.error("SSH write error on paste:", e)
                );
              }
            })
            .catch((err) => {
              console.error("Clipboard read error:", err);
            });
          return false; // Prevent sending \x16
        }
      }
      return true;
    });

    term.writeln(`\r\n\x1b[1;36m[RDM] Connecting to ${username}@${host}:${port}...\x1b[0m\r\n`);

    // Get final terminal size after fit
    const dims = term;
    const cols = dims.cols;
    const rows = dims.rows;

    let isDestroyed = false;
    const unlisteners: (() => void)[] = [];

    // Setup Tauri Event Listeners
    const setupListeners = async () => {
      try {
        // Listen for stdout data stream
        const unlistenOutput = await listen<{ session_id: string; data: string }>(
          "ssh-output",
          (event) => {
            if (event.payload.session_id === sessionId) {
              term.write(event.payload.data);
            }
          }
        );
        if (isDestroyed) {
          unlistenOutput();
        } else {
          unlisteners.push(unlistenOutput);
        }

        // Listen for connection close events
        const unlistenClosed = await listen<string>("ssh-closed", (event) => {
          if (event.payload === sessionId) {
            term.writeln("\r\n\x1b[1;31m[RDM] SSH Connection closed by remote host.\x1b[0m");
            isConnectedRef.current = false;
            setStatus('disconnected');
          }
        });
        if (isDestroyed) {
          unlistenClosed();
        } else {
          unlisteners.push(unlistenClosed);
        }

        if (!isDestroyed) {
          // Trigger SSH connection on backend
          await invoke("connect_ssh", {
            sessionId,
            host,
            port,
            username,
            credentialId: credentialId || null,
            serverId: serverId || null,
            cols,
            rows,
          });
          isConnectedRef.current = true;
          setStatus('connected');
        }
      } catch (err: any) {
        if (!isDestroyed) {
          term.writeln(`\r\n\x1b[1;31m[RDM] Error: ${err}\x1b[0m`);
          setStatus('disconnected');
        }
      }
    };

    setupListeners();

    // Bind terminal user keyboard inputs to backend PTY writer
    const dataSubscription = term.onData((data) => {
      if (isConnectedRef.current) {
        invoke("write_ssh_input", { sessionId, data }).catch((e) =>
          console.error("SSH write error:", e)
        );
      }
    });

    // Handle terminal resize events with debounce
    let ptyResizeTimer: ReturnType<typeof setTimeout> | null = null;
    const resizeSubscription = term.onResize((size) => {
      if (isConnectedRef.current) {
        if (ptyResizeTimer) clearTimeout(ptyResizeTimer);
        ptyResizeTimer = setTimeout(() => {
          invoke("resize_ssh_pty", {
            sessionId,
            cols: size.cols,
            rows: size.rows,
          }).catch((e) => console.error("PTY resize error:", e));
        }, 100);
      }
    });

    // Resize handler for browser window changes
    const handleResize = () => {
      try {
        fitAddon.fit();
      } catch (e) {
        console.warn("Resize fit failed:", e);
      }
    };
    window.addEventListener("resize", handleResize);

    // DOM paste & copy listeners on the container div
    const container = terminalRef.current;
    const handleDomPaste = (e: ClipboardEvent) => {
      e.preventDefault();
      const text = e.clipboardData?.getData("text");
      if (text && isConnectedRef.current) {
        invoke("write_ssh_input", { sessionId, data: text }).catch((e) =>
          console.error("SSH paste error:", e)
        );
      }
    };

    const handleDomCopy = (e: ClipboardEvent) => {
      if (term.hasSelection()) {
        e.preventDefault();
        const selected = term.getSelection();
        if (selected) {
          e.clipboardData?.setData("text/plain", selected);
        }
      }
    };

    container.addEventListener("paste", handleDomPaste);
    container.addEventListener("copy", handleDomCopy);

    // Component Cleanup
    return () => {
      isDestroyed = true;
      isConnectedRef.current = false;
      window.removeEventListener("resize", handleResize);
      container.removeEventListener("paste", handleDomPaste);
      container.removeEventListener("copy", handleDomCopy);
      if (ptyResizeTimer) clearTimeout(ptyResizeTimer);
      selectionSub.dispose();
      dataSubscription.dispose();
      resizeSubscription.dispose();
      term.dispose();
      
      unlisteners.forEach((unsub) => unsub());
      
      // Notify backend to drop process resources
      invoke("disconnect_ssh", { sessionId }).catch((e) =>
        console.error("SSH disconnect error:", e)
      );
    };
  }, [sessionId, host, port, username, credentialId]);

  // Context Menu Handlers
  const handleContextMenu = (e: React.MouseEvent) => {
    e.preventDefault();
    setHasSelection(!!xtermRef.current?.hasSelection());
    setContextMenu({ x: e.clientX, y: e.clientY });
  };

  const handleCopyMenu = () => {
    if (xtermRef.current?.hasSelection()) {
      const text = xtermRef.current.getSelection();
      navigator.clipboard.writeText(text).catch((err) => {
        console.error("Clipboard write error:", err);
      });
    }
    setContextMenu(null);
  };

  const handlePasteMenu = () => {
    navigator.clipboard
      .readText()
      .then((text) => {
        if (text && isConnectedRef.current) {
          invoke("write_ssh_input", { sessionId, data: text }).catch((e) =>
            console.error("SSH paste error:", e)
          );
        }
      })
      .catch((err) => console.error("Clipboard read error:", err));
    setContextMenu(null);
  };

  const handleSelectAllMenu = () => {
    xtermRef.current?.selectAll();
    setContextMenu(null);
  };

  const handleClearMenu = () => {
    xtermRef.current?.clear();
    setContextMenu(null);
  };

  useEffect(() => {
    const handleClickOutside = () => setContextMenu(null);
    window.addEventListener("click", handleClickOutside);
    return () => window.removeEventListener("click", handleClickOutside);
  }, []);

  return (
    <div className="terminal-container" onContextMenu={handleContextMenu}>
      <div className="terminal-body" ref={terminalRef} />
      {contextMenu && (
        <div
          className="terminal-context-menu"
          style={{ top: contextMenu.y, left: contextMenu.x }}
          onClick={(e) => e.stopPropagation()}
        >
          <button
            className="terminal-menu-item"
            disabled={!hasSelection}
            onClick={handleCopyMenu}
          >
            <Copy size={14} />
            <span>Copy</span>
            <span className="menu-shortcut">Ctrl+C</span>
          </button>
          <button
            className="terminal-menu-item"
            onClick={handlePasteMenu}
          >
            <Clipboard size={14} />
            <span>Paste</span>
            <span className="menu-shortcut">Ctrl+V</span>
          </button>
          <div className="terminal-menu-divider" />
          <button className="terminal-menu-item" onClick={handleSelectAllMenu}>
            <span>Select All</span>
            <span className="menu-shortcut">Ctrl+A</span>
          </button>
          <button className="terminal-menu-item" onClick={handleClearMenu}>
            <span>Clear Terminal</span>
          </button>
        </div>
      )}
    </div>
  );
};

