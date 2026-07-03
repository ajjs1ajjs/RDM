import React, { useState, useEffect, useRef } from "react";
import { Terminal } from "xterm";
import { FitAddon } from "xterm-addon-fit";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
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
  const [status, setStatus] = useState<'connecting' | 'connected' | 'disconnected'>('connecting');
  if (status) {} // prevent unused variable TS compiler error

  useEffect(() => {
    if (!terminalRef.current) return;

    // Initialize xterm.js
    const term = new Terminal({
      cursorBlink: true,
      fontFamily: "var(--font-mono)",
      fontSize: 14,
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

    term.writeln(`\r\n\x1b[1;36m[RDM] Connecting to ${username}@${host}:${port}...\x1b[0m\r\n`);

    // Get final terminal size after fit
    const dims = term;
    const cols = dims.cols;
    const rows = dims.rows;

    let isDestroyed = false;
    const unlisteners: (() => void)[] = [];
    let isConnected = false;

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
            isConnected = false;
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
          isConnected = true;
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
      if (isConnected) {
        invoke("write_ssh_input", { sessionId, data }).catch((e) =>
          console.error("SSH write error:", e)
        );
      }
    });

    // Handle terminal resize events with debounce
    let ptyResizeTimer: ReturnType<typeof setTimeout> | null = null;
    const resizeSubscription = term.onResize((size) => {
      if (isConnected) {
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

    // Component Cleanup
    return () => {
      isDestroyed = true;
      window.removeEventListener("resize", handleResize);
      if (ptyResizeTimer) clearTimeout(ptyResizeTimer);
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

  return (
    <div className="terminal-container">
      <div className="terminal-body" ref={terminalRef} />
    </div>
  );
};
