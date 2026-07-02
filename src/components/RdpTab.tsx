import React, { useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

interface RdpTabProps {
  sessionId: string;
  serverId: string;
  host: string;
  port: number;
  credentialId?: string;
}

interface RdpFramePayload {
  session_id: string;
  data: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

const BUTTON_NAMES: Record<number, string> = { 0: "left", 1: "middle", 2: "right" };

export const RdpTab: React.FC<RdpTabProps> = ({
  sessionId,
  serverId,
  host,
  port,
  credentialId,
}) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const workerRef = useRef<Worker | null>(null);
  const [connected, setConnected] = useState(false);
  const [closed, setClosed] = useState(false);
  const [loadingStep, setLoadingStep] = useState("Initializing connection...");
  const [progress, setProgress] = useState(15);
  const [frameCountText, setFrameCountText] = useState("");
  const frameCountRef = useRef(0);
  const frameCountTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const connectedRef = useRef(false);
  const remoteSize = useRef({ width: 0, height: 0 });
  const lastMouseMoveTime = useRef(0);
  const lastMouseMoveCoords = useRef({ x: -1, y: -1 });

  useEffect(() => {
    const steps = [
      { p: 35, t: "Securing connection..." },
      { p: 60, t: "Authenticating credentials..." },
      { p: 85, t: "Loading remote desktop environment..." },
      { p: 98, t: "Negotiating display protocol..." }
    ];

    let currentStep = 0;
    const interval = setInterval(() => {
      if (currentStep < steps.length) {
        setLoadingStep(steps[currentStep].t);
        setProgress(steps[currentStep].p);
        currentStep++;
      } else {
        clearInterval(interval);
      }
    }, 600);

    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    frameCountTimerRef.current = setInterval(() => {
      setFrameCountText(`frames: ${frameCountRef.current}`);
    }, 1000);
    return () => {
      if (frameCountTimerRef.current) clearInterval(frameCountTimerRef.current);
    };
  }, []);

  useEffect(() => {
    let active = true;
    const sid = sessionId;

    const doResizeIpc = (width: number, height: number, dpr: number) => {
      invoke("resize_rdp_embedded", {
        sessionId: sid,
        width,
        height,
        devicePixelRatio: dpr,
      }).catch((err) => console.error("RDP resize error:", err));
    };

    let resizeTimer: ReturnType<typeof setTimeout> | null = null;
    const handleResize = () => {
      if (!containerRef.current) return;
      const rect = containerRef.current.getBoundingClientRect();
      const width = Math.round(rect.width);
      const height = Math.round(rect.height);
      if (width < 50 || height < 50) return;
      const dpr = window.devicePixelRatio || 1.0;

      if (resizeTimer) clearTimeout(resizeTimer);
      resizeTimer = setTimeout(() => doResizeIpc(width, height, dpr), 150);
    };

    // Spawn the OffscreenCanvas worker — all frame decoding + canvas rendering
    // happens off the main thread so the UI stays responsive.
    const worker = new Worker(
      new URL("../workers/rdpRenderer.worker.ts", import.meta.url),
      { type: "module" }
    );
    workerRef.current = worker;
    worker.addEventListener("message", (e: MessageEvent) => {
      if (e.data?.type === "rendered") {
        frameCountRef.current = e.data.count;
      }
    });

    const startRdp = async () => {
      if (!containerRef.current || !canvasRef.current) return;

      let rect = containerRef.current.getBoundingClientRect();
      for (let i = 0; i < 30; i++) {
        if (rect.width > 100 && rect.height > 100) break;
        await new Promise((resolve) => setTimeout(resolve, 100));
        if (!active || !containerRef.current || !canvasRef.current) return;
        rect = containerRef.current.getBoundingClientRect();
      }
      if (!active) return;

      let finalWidth = Math.round(rect.width);
      let finalHeight = Math.round(rect.height);
      if (finalWidth < 100) finalWidth = 800;
      if (finalHeight < 100) finalHeight = 600;

      const dpr = window.devicePixelRatio || 1.0;

      // Transfer canvas to worker thread
      const offscreen = canvasRef.current.transferControlToOffscreen();
      offscreen.width = finalWidth;
      offscreen.height = finalHeight;
      worker.postMessage({ type: "init", canvas: offscreen }, [offscreen]);

      try {
        await invoke("connect_rdp_embedded", {
          sessionId: sid,
          serverId,
          host,
          port,
          credentialId: credentialId || null,
          width: finalWidth,
          height: finalHeight,
          devicePixelRatio: dpr,
        });
        remoteSize.current = { width: finalWidth, height: finalHeight };
      } catch (err: any) {
        console.error("Failed to connect RDP:", err);
        alert(`RDP Error: ${err}`);
      }
    };

    startRdp();

    window.addEventListener("resize", handleResize);
    const resizeObserver = new ResizeObserver(() => handleResize());
    if (containerRef.current) resizeObserver.observe(containerRef.current);

    const unlistenPromises = [
      listen<RdpFramePayload>("rdp-frame", (event) => {
        if (event.payload.session_id !== sid) return;
        if (!connectedRef.current) {
          connectedRef.current = true;
          setConnected(true);
        }
        // Forward to worker — non-blocking, fires-and-forgets
        worker.postMessage({ type: "frame", payload: event.payload });
      }).catch(() => () => {}),
      listen<string>("rdp-closed", (event) => {
        if (event.payload !== sid) return;
        setClosed(true);
      }),
    ];

    return () => {
      active = false;
      if (resizeTimer) clearTimeout(resizeTimer);
      window.removeEventListener("resize", handleResize);
      resizeObserver.disconnect();
      worker.terminate();
      workerRef.current = null;
      Promise.all(unlistenPromises).then((fns) => fns.forEach((fn) => fn()));
      invoke("disconnect_rdp_embedded", { sessionId: sid }).catch((err) =>
        console.error("RDP disconnect error:", err)
      );
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Maps a mouse event on the canvas to RDP-space pixel coordinates.
  const toRemoteCoords = (e: React.MouseEvent | React.WheelEvent): { x: number; y: number } => {
    const canvas = canvasRef.current;
    const { width: remoteW, height: remoteH } = remoteSize.current;
    if (!canvas || remoteW === 0 || remoteH === 0) return { x: 0, y: 0 };
    
    const rect = canvas.getBoundingClientRect();
    const containerW = rect.width;
    const containerH = rect.height;

    // Calculate the scaled size and offsets due to object-fit: contain
    const remoteRatio = remoteW / remoteH;
    const containerRatio = containerW / containerH;

    let renderW = containerW;
    let renderH = containerH;
    let offsetX = 0;
    let offsetY = 0;

    if (containerRatio > remoteRatio) {
      // Container is wider than the remote screen (black bars on left/right)
      renderW = containerH * remoteRatio;
      offsetX = (containerW - renderW) / 2;
    } else {
      // Container is taller than the remote screen (black bars on top/bottom)
      renderH = containerW / remoteRatio;
      offsetY = (containerH - renderH) / 2;
    }

    const clickX = e.clientX - rect.left - offsetX;
    const clickY = e.clientY - rect.top - offsetY;

    const relX = clickX / renderW;
    const relY = clickY / renderH;

    return {
      x: Math.round(Math.min(Math.max(relX, 0), 1) * remoteW),
      y: Math.round(Math.min(Math.max(relY, 0), 1) * remoteH),
    };
  };

  const sendMouse = (action: string, e: React.MouseEvent, wheelDelta = 0) => {
    const { x, y } = toRemoteCoords(e);

    if (action === "move") {
      const now = Date.now();
      if (x === lastMouseMoveCoords.current.x && y === lastMouseMoveCoords.current.y) {
        return; // Skip duplicate move at the same coordinates
      }
      if (now - lastMouseMoveTime.current < 16) {
        return; // Throttle to maximum ~60 mouse moves per second
      }
      lastMouseMoveTime.current = now;
      lastMouseMoveCoords.current = { x, y };
    }

    const button = BUTTON_NAMES[e.button] ?? "left";
    invoke("send_rdp_mouse", { sessionId, x, y, button, action, wheelDelta }).catch(() => {});
  };

  const sendKey = (e: React.KeyboardEvent, keyUp: boolean) => {
    e.preventDefault();
    const code = e.code;
    const vkMap: Record<string, number> = {
      'Backspace': 0x08, 'Tab': 0x09, 'Enter': 0x0D, 'ShiftLeft': 0xA0, 'ShiftRight': 0xA1,
      'ControlLeft': 0xA2, 'ControlRight': 0xA3, 'AltLeft': 0xA4, 'AltRight': 0xA5,
      'CapsLock': 0x14, 'Escape': 0x1B, 'Space': 0x20, 'PageUp': 0x21, 'PageDown': 0x22,
      'End': 0x23, 'Home': 0x24, 'ArrowLeft': 0x25, 'ArrowUp': 0x26, 'ArrowRight': 0x27, 'ArrowDown': 0x28,
      'Insert': 0x2D, 'Delete': 0x2E,
      'Digit0': 0x30, 'Digit1': 0x31, 'Digit2': 0x32, 'Digit3': 0x33, 'Digit4': 0x34,
      'Digit5': 0x35, 'Digit6': 0x36, 'Digit7': 0x37, 'Digit8': 0x38, 'Digit9': 0x39,
      'KeyA': 0x41, 'KeyB': 0x42, 'KeyC': 0x43, 'KeyD': 0x44, 'KeyE': 0x45, 'KeyF': 0x46,
      'KeyG': 0x47, 'KeyH': 0x48, 'KeyI': 0x49, 'KeyJ': 0x4A, 'KeyK': 0x4B, 'KeyL': 0x4C,
      'KeyM': 0x4D, 'KeyN': 0x4E, 'KeyO': 0x4F, 'KeyP': 0x50, 'KeyQ': 0x51, 'KeyR': 0x52,
      'KeyS': 0x53, 'KeyT': 0x54, 'KeyU': 0x55, 'KeyV': 0x56, 'KeyW': 0x57, 'KeyX': 0x58,
      'KeyY': 0x59, 'KeyZ': 0x5A,
      'F1': 0x70, 'F2': 0x71, 'F3': 0x72, 'F4': 0x73, 'F5': 0x74, 'F6': 0x75,
      'F7': 0x76, 'F8': 0x77, 'F9': 0x78, 'F10': 0x79, 'F11': 0x7A, 'F12': 0x7B,
      'Semicolon': 0xBA, 'Equal': 0xBB, 'Comma': 0xBC, 'Minus': 0xBD, 'Period': 0xBE,
      'Slash': 0xBF, 'Backquote': 0xC0, 'BracketLeft': 0xDB, 'Backslash': 0xDC,
      'BracketRight': 0xDD, 'Quote': 0xDE,
    };
    const vk = vkMap[code] || 0;
    if (vk !== 0) {
      invoke("send_rdp_key", { sessionId, vk, keyUp }).catch(() => {});
    }
  };

  return (
      <div className="terminal-container" style={{ minHeight: 0 }}>
      <style>{`
        @keyframes rdp-spin {
          0% { transform: rotate(0deg); }
          100% { transform: rotate(360deg); }
        }
        .rdp-spinner-anim {
          animation: rdp-spin 1.2s cubic-bezier(0.5, 0.1, 0.4, 0.9) infinite;
        }
      `}</style>
      <div className="terminal-header">
        <span>RDP: {host}:{port}</span>
        <span style={{ color: "var(--accent-cyan)" }}>{frameCountText || "connecting..."}</span>
        <span style={{ color: "var(--accent-purple)" }}>embedded tab</span>
      </div>
      <div
        ref={containerRef}
        className="rdp-body-placeholder"
        style={{
          flex: 1,
          minWidth: 0,
          minHeight: 0,
          backgroundColor: "#000",
          position: "relative",
          overflow: "hidden",
        }}
      >
        <canvas
          ref={canvasRef}
          tabIndex={0}
          style={{
            display: connected && !closed ? "block" : "none",
            width: "100%",
            height: "100%",
            objectFit: "contain",
            outline: "none",
            cursor: "default",
            imageRendering: "pixelated",
          }}
          onMouseMove={(e) => sendMouse("move", e)}
          onMouseDown={(e) => { (e.currentTarget as HTMLCanvasElement).focus(); sendMouse("down", e); }}
          onMouseUp={(e) => sendMouse("up", e)}
          onContextMenu={(e) => e.preventDefault()}
          onWheel={(e) => { e.preventDefault(); sendMouse("wheel", e, -e.deltaY); }}
          onKeyDown={(e) => sendKey(e, false)}
          onKeyUp={(e) => sendKey(e, true)}
        />

        {(!connected || closed) && (
          <div style={{
            position: "absolute",
            top: "50%",
            left: "50%",
            transform: "translate(-50%, -50%)",
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            gap: "20px",
            width: "300px",
          }}>
            {!closed ? (
              <>
                <div className="rdp-spinner-anim" style={{
                  width: "40px",
                  height: "40px",
                  border: "3px solid rgba(0, 240, 255, 0.08)",
                  borderTop: "3px solid var(--accent-cyan)",
                  borderRight: "3px solid var(--accent-cyan)",
                  borderRadius: "50%",
                  boxShadow: "0 0 10px rgba(0, 240, 255, 0.2)",
                }} />
                <div style={{
                  color: "var(--text-main)",
                  fontSize: "0.9rem",
                  fontFamily: "var(--font-mono)",
                  letterSpacing: "0.5px",
                  textAlign: "center",
                  textShadow: "0 0 8px rgba(0, 240, 255, 0.2)",
                  minHeight: "20px",
                }}>
                  {loadingStep}
                </div>
                <div style={{ color: "var(--accent-cyan)", fontSize: "0.75rem", fontFamily: "var(--font-mono)" }}>
                  {frameCountText || ""}
                </div>
                <div style={{
                  width: "100%",
                  height: "6px",
                  backgroundColor: "rgba(255, 255, 255, 0.03)",
                  borderRadius: "3px",
                  overflow: "hidden",
                  border: "1px solid rgba(255, 255, 255, 0.07)",
                }}>
                  <div style={{
                    width: `${progress}%`,
                    height: "100%",
                    background: "linear-gradient(90deg, var(--accent-cyan), var(--accent-purple))",
                    boxShadow: "0 0 8px var(--accent-cyan)",
                    transition: "width 0.6s cubic-bezier(0.4, 0, 0.2, 1)",
                  }} />
                </div>
              </>
            ) : (
              <div style={{
                color: "var(--text-main)",
                fontSize: "0.9rem",
                fontFamily: "var(--font-mono)",
                textAlign: "center",
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                gap: "15px",
              }}>
                <div>Connection closed</div>
                <div style={{ fontSize: "0.75rem", color: "rgba(255, 255, 255, 0.5)", maxWidth: "260px", lineHeight: "1.4" }}>
                  If Network Level Authentication (NLA) is required, please configure credentials in connection settings.
                </div>
                <button
                  onClick={() => {
                    invoke("connect_rdp", {
                      serverId,
                      host,
                      port,
                      credentialId: credentialId || null,
                      fullscreen: false,
                    }).catch((err) => console.error("Failed to launch externally:", err));
                  }}
                  style={{
                    padding: "8px 16px",
                    background: "linear-gradient(90deg, var(--accent-cyan), var(--accent-purple))",
                    border: "none",
                    borderRadius: "4px",
                    color: "#fff",
                    cursor: "pointer",
                    fontSize: "0.8rem",
                    fontFamily: "var(--font-mono)",
                    boxShadow: "0 0 8px rgba(0, 240, 255, 0.3)",
                  }}
                  onMouseEnter={(e) => {
                    e.currentTarget.style.boxShadow = "0 0 12px rgba(0, 240, 255, 0.5)";
                  }}
                  onMouseLeave={(e) => {
                    e.currentTarget.style.boxShadow = "0 0 8px rgba(0, 240, 255, 0.3)";
                  }}
                >
                  Launch in External Window (mstsc)
                </button>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};
