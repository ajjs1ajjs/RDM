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
  width: number;
  height: number;
}

// Maps mouse buttons to the names the backend expects.
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
  const [connected, setConnected] = useState(false);
  const [closed, setClosed] = useState(false);
  const [loadingStep, setLoadingStep] = useState("Initializing connection...");
  const [progress, setProgress] = useState(15);
  const [debugInfo, setDebugInfo] = useState("");

  // Remote desktop resolution as reported by the latest frame — used to map
  // canvas-space mouse coordinates back to RDP-space pixel coordinates.
  const remoteSize = useRef({ width: 0, height: 0 });

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

    const startRdp = async () => {
      if (!containerRef.current) return;

      let rect = containerRef.current.getBoundingClientRect();
      for (let i = 0; i < 30; i++) {
        if (rect.width > 100 && rect.height > 100) break;
        await new Promise((resolve) => setTimeout(resolve, 100));
        if (!active || !containerRef.current) return;
        rect = containerRef.current.getBoundingClientRect();
      }
      if (!active) return;

      let finalWidth = Math.round(rect.width);
      let finalHeight = Math.round(rect.height);
      if (finalWidth < 100) finalWidth = 800;
      if (finalHeight < 100) finalHeight = 600;

      const dpr = window.devicePixelRatio || 1.0;
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
      } catch (err: any) {
        console.error("Failed to connect RDP:", err);
        alert(`RDP Error: ${err}`);
      }
    };

    startRdp();

    window.addEventListener("resize", handleResize);
    const resizeObserver = new ResizeObserver(() => handleResize());
    if (containerRef.current) resizeObserver.observe(containerRef.current);

    let frameCount = 0;
    let anyEventCount = 0;
    const unlistenPromises = [
      listen<RdpFramePayload>("rdp-frame", async (event) => {
        anyEventCount++;
        if (event.payload.session_id !== sid) {
          setDebugInfo(`event #${anyEventCount} for OTHER session (${event.payload.session_id})`);
          return;
        }
        if (!canvasRef.current) {
          setDebugInfo(`event #${anyEventCount} matched but no canvas ref`);
          return;
        }
        setConnected(true);
        remoteSize.current = { width: event.payload.width, height: event.payload.height };

        try {
          const bytes = Uint8Array.from(atob(event.payload.data), (c) => c.charCodeAt(0));
          const blob = new Blob([bytes], { type: "image/jpeg" });
          const bitmap = await createImageBitmap(blob);
          const canvas = canvasRef.current;
          if (!canvas) return;
          if (canvas.width !== bitmap.width) canvas.width = bitmap.width;
          if (canvas.height !== bitmap.height) canvas.height = bitmap.height;
          const ctx = canvas.getContext("2d");
          ctx?.drawImage(bitmap, 0, 0);
          bitmap.close();
          frameCount++;
          setDebugInfo(`frames: ${frameCount} (${event.payload.width}x${event.payload.height})`);
        } catch (err) {
          console.error("Failed to draw RDP frame:", err);
          setDebugInfo(`draw error: ${err}`);
        }
      }).catch((err) => {
        console.error("Failed to register rdp-frame listener:", err);
        setDebugInfo(`listen error: ${err}`);
        return () => {};
      }),
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
    const { width, height } = remoteSize.current;
    if (!canvas || width === 0 || height === 0) return { x: 0, y: 0 };
    const rect = canvas.getBoundingClientRect();
    const relX = (e.clientX - rect.left) / rect.width;
    const relY = (e.clientY - rect.top) / rect.height;
    return {
      x: Math.round(Math.min(Math.max(relX, 0), 1) * width),
      y: Math.round(Math.min(Math.max(relY, 0), 1) * height),
    };
  };

  const sendMouse = (action: string, e: React.MouseEvent, wheelDelta = 0) => {
    const { x, y } = toRemoteCoords(e);
    const button = BUTTON_NAMES[e.button] ?? "left";
    invoke("send_rdp_mouse", { sessionId, x, y, button, action, wheelDelta }).catch(() => {});
  };

  const sendKey = (e: React.KeyboardEvent, keyUp: boolean) => {
    e.preventDefault();
    invoke("send_rdp_key", { sessionId, vk: e.keyCode, keyUp }).catch(() => {});
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
        <span style={{ color: "var(--accent-cyan)" }}>{debugInfo || "no events yet"}</span>
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
                {debugInfo && (
                  <div style={{ color: "var(--accent-cyan)", fontSize: "0.75rem", fontFamily: "var(--font-mono)" }}>
                    {debugInfo}
                  </div>
                )}
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
              }}>
                Connection closed
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};
