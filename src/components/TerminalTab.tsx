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

  // Strip control/escape sequences from untrusted text written into the terminal
  // to prevent terminal escape-sequence injection (fake prompts, screen control).
  const sanitizeTerminalText = (s: string) =>
    s.replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u001B]/g, "");

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

    xtermRef.current = term;
    fitAddonRef.current = fitAddon;

    // Safe fit helper that also notifies backend PTY of new dimensions
    const handleFitAndResizePty = () => {
      try {
        fitAddon.fit();
        term.scrollToBottom();
        if (isConnectedRef.current) {
          invoke("resize_ssh_pty", {
            sessionId,
            cols: term.cols,
            rows: term.rows,
          }).catch((e) => console.error("PTY resize error:", e));
        }
      } catch (e) {
        // ignore error during unmount
      }
    };

    // Auto-fit immediately after mount
    setTimeout(() => handleFitAndResizePty(), 50);

    // Listen for selection changes: update state and auto-copy selected text to clipboard
    const selectionSub = term.onSelectionChange(() => {
      const selection = term.getSelection();
      const hasSel = !!selection && selection.length > 0;
      setHasSelection(hasSel);
      if (hasSel) {
        navigator.clipboard.writeText(selection).catch(() => {});
      }
    });

    // Custom keyboard handler for copy/paste supporting all keyboard layouts (English, Ukrainian, etc.)
    term.attachCustomKeyEventHandler((event: KeyboardEvent) => {
      if (event.type === "keydown") {
        const isCtrlOrCmd = event.ctrlKey || event.metaKey;
        const keyLower = event.key ? event.key.toLowerCase() : "";

        // Copy: Code KeyC, Insert, or key 'c' / 'с' (Ukrainian es)
        const isCopyKey =
          (isCtrlOrCmd &&
            (event.code === "KeyC" || keyLower === "c" || keyLower === "с")) ||
          (event.ctrlKey && event.code === "Insert");

        if (isCopyKey) {
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

        // Paste: Code KeyV, Insert, or key 'v' / 'м' (Ukrainian em)
        const isPasteKey =
          (isCtrlOrCmd &&
            (event.code === "KeyV" || keyLower === "v" || keyLower === "м")) ||
          (event.shiftKey && event.code === "Insert");

        if (isPasteKey) {
          navigator.clipboard
            .readText()
            .then((text) => {
              if (text && isConnectedRef.current) {
                term.paste(text);
                term.scrollToBottom();
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

    term.writeln(`\r\n\x1b[1;36m[RDM] Connecting to ${sanitizeTerminalText(username)}@${sanitizeTerminalText(host)}:${port}...\x1b[0m\r\n`);

    let isDestroyed = false;
    const unlisteners: (() => void)[] = [];

    // Setup Tauri Event Listeners
    const setupListeners = async () => {
      try {
        // Listen for stdout data stream and auto-scroll to bottom after render
        const unlistenOutput = await listen<{ session_id: string; data: string }>(
          "ssh-output",
          (event) => {
            if (event.payload.session_id === sessionId) {
              term.write(event.payload.data, () => {
                term.scrollToBottom();
              });
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
            cols: term.cols,
            rows: term.rows,
          });
          isConnectedRef.current = true;
          setStatus('connected');
          handleFitAndResizePty();
        }
      } catch (err: any) {
        if (!isDestroyed) {
          term.writeln(`\r\n\x1b[1;31m[RDM] Error: ${sanitizeTerminalText(typeof err === "string" ? err : err?.message || String(err))}\x1b[0m`);
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
        term.scrollToBottom();
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

    // ResizeObserver on the container div to re-fit whenever the div changes size (sidebar toggle, tab switch, window resize)
    const resizeObserver = new ResizeObserver(() => {
      handleFitAndResizePty();
    });
    if (terminalRef.current) {
      resizeObserver.observe(terminalRef.current);
    }

    // DOM paste & copy listeners on the container div
    const container = terminalRef.current;
    const handleDomPaste = (e: ClipboardEvent) => {
      e.preventDefault();
      const text = e.clipboardData?.getData("text");
      if (text && isConnectedRef.current) {
        term.paste(text);
        term.scrollToBottom();
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
      if (terminalRef.current) {
        resizeObserver.unobserve(terminalRef.current);
      }
      resizeObserver.disconnect();
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
        if (text && isConnectedRef.current && xtermRef.current) {
          xtermRef.current.paste(text);
          xtermRef.current.scrollToBottom();
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


