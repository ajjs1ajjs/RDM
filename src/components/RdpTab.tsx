import React, { useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

interface RdpTabProps {
  sessionId: string;
  serverId: string;
  host: string;
  port: number;
  credentialId?: string;
  isActive: boolean;
}

export const RdpTab: React.FC<RdpTabProps> = ({
  sessionId,
  serverId,
  host,
  port,
  credentialId,
  isActive,
}) => {
  const sid = sessionId;
  const containerRef = useRef<HTMLDivElement>(null);
  const [connected, setConnected] = useState(false);
  const [closed, setClosed] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string>("");
  const [loadingStep, setLoadingStep] = useState("Initializing native RDP client...");
  const [progress, setProgress] = useState(10);
  const activeRef = useRef(true);

  // Connection startup steps animation
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

  // Connect on mount, disconnect on unmount
  useEffect(() => {
    activeRef.current = true;
    
    const startRdp = async () => {
      if (!containerRef.current) return;
      
      // Wait for layout rendering
      let rect = containerRef.current.getBoundingClientRect();
      for (let i = 0; i < 30; i++) {
        if (rect.width > 50 && rect.height > 50) break;
        await new Promise((resolve) => setTimeout(resolve, 100));
        if (!activeRef.current || !containerRef.current) return;
        rect = containerRef.current.getBoundingClientRect();
      }
      if (!activeRef.current) return;

      const finalWidth = Math.round(rect.width);
      const finalHeight = Math.round(rect.height);

      try {
        const x = Math.round(rect.left);
        const y = Math.round(rect.top);
        await invoke("connect_rdp_embedded", {
          sessionId: sid,
          serverId,
          host,
          port,
          credentialId: credentialId || null,
          x,
          y,
          width: finalWidth,
          height: finalHeight,
          devicePixelRatio: window.devicePixelRatio || 1.0,
        });
        
        if (activeRef.current) {
          setConnected(true);
          // Initial position sync
          const x = Math.round(rect.left);
          const y = Math.round(rect.top);
          await invoke("resize_rdp_embedded", {
            sessionId: sid,
            x,
            y,
            width: finalWidth,
            height: finalHeight,
            devicePixelRatio: window.devicePixelRatio || 1.0,
          });
        }
      } catch (err: any) {
        console.error("Failed to connect native RDP:", err);
        setErrorMessage(typeof err === "string" ? err : err?.message || String(err));
        setClosed(true);
      }
    };

    startRdp();

    // Listen for close from backend
    const unlistenPromise = listen<string>("rdp-closed", (event) => {
      if (event.payload === sid) {
        setClosed(true);
      }
    });

    return () => {
      activeRef.current = false;
      unlistenPromise.then((fn) => fn());
      invoke("disconnect_rdp_embedded", { sessionId: sid }).catch((err) =>
        console.error("RDP disconnect error:", err)
      );
    };
  }, [sid, serverId, host, port, credentialId]);

  // Handle position/size updates when container changes or when tab becomes active/inactive
  useEffect(() => {
    if (!connected || closed) return;

    const syncPosition = () => {
      if (!containerRef.current) return;
      const rect = containerRef.current.getBoundingClientRect();
      const x = Math.round(rect.left);
      const y = Math.round(rect.top);
      const w = Math.round(rect.width);
      const h = Math.round(rect.height);

      if (!isActive || w < 10 || h < 10) {
        // Hide window when tab is not active or size is invalid
        invoke("resize_rdp_embedded", {
          sessionId: sid,
          x: 0,
          y: 0,
          width: 0,
          height: 0,
          devicePixelRatio: window.devicePixelRatio || 1.0,
        }).catch((err) => console.error("RDP hide error:", err));
      } else {
        // Move & resize child window
        invoke("resize_rdp_embedded", {
          sessionId: sid,
          x,
          y,
          width: w,
          height: h,
          devicePixelRatio: window.devicePixelRatio || 1.0,
        }).catch((err) => console.error("RDP resize error:", err));
      }
    };

    // Immediate sync
    syncPosition();

    // Observe size changes
    const resizeObserver = new ResizeObserver(() => syncPosition());
    if (containerRef.current) {
      resizeObserver.observe(containerRef.current);
    }

    // Sync on window resize and scroll events
    window.addEventListener("resize", syncPosition);
    window.addEventListener("scroll", syncPosition, true);

    // Sync on Tauri window move events to update screen coordinates when the app is dragged
    const unlistenMove = listen("tauri://move", () => syncPosition());

    return () => {
      resizeObserver.disconnect();
      window.removeEventListener("resize", syncPosition);
      window.removeEventListener("scroll", syncPosition, true);
      unlistenMove.then((fn) => fn());
    };
  }, [connected, closed, isActive, sid]);

  return (
    <div style={{ display: "flex", flexDirection: "column", width: "100%", height: "100%" }}>
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
                <div style={{ fontSize: "0.75rem", color: "rgba(255, 200, 100, 0.8)", maxWidth: "300px", lineHeight: "1.4", wordBreak: "break-word" }}>
                  {errorMessage || "If Network Level Authentication (NLA) is required, please configure credentials in connection settings."}
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
