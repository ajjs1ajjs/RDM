import React, { useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";

interface RdpTabProps {
  sessionId: string;
  serverId: string;
  host: string;
  port: number;
  credentialId?: string;
}

export const RdpTab: React.FC<RdpTabProps> = ({
  sessionId,
  serverId,
  host,
  port,
  credentialId,
}) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const [loadingStep, setLoadingStep] = useState("Initializing connection...");
  const [progress, setProgress] = useState(15);

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

    const lastSize = { width: 0, height: 0 };

    const handleResize = () => {
      if (!containerRef.current) return;
      const rect = containerRef.current.getBoundingClientRect();
      const width = Math.round(rect.width);
      const height = Math.round(rect.height);

      if (width === lastSize.width && height === lastSize.height) {
        return;
      }
      lastSize.width = width;
      lastSize.height = height;

      invoke("resize_rdp_embedded", {
        sessionId: sid,
        x: Math.round(rect.left),
        y: Math.round(rect.top),
        width,
        height,
        devicePixelRatio: window.devicePixelRatio || 1.0,
      }).catch((err) => console.error("RDP resize error:", err));
    };

    const startRdp = async () => {
      if (!containerRef.current) return;

      // Wait until the container has actual dimensions (prevent 0x0 or tiny initial size)
      let rect = containerRef.current.getBoundingClientRect();
      let attempts = 0;
      while ((rect.width < 100 || rect.height < 100) && attempts < 20) {
        await new Promise((resolve) => setTimeout(resolve, 50));
        if (!active || !containerRef.current) return;
        rect = containerRef.current.getBoundingClientRect();
        attempts++;
      }

      if (!active || !containerRef.current) return;

      try {
        await invoke("connect_rdp_embedded", {
          sessionId: sid,
          serverId,
          host,
          port,
          credentialId: credentialId || null,
          x: Math.round(rect.left),
          y: Math.round(rect.top),
          width: Math.round(rect.width),
          height: Math.round(rect.height),
          devicePixelRatio: window.devicePixelRatio || 1.0,
        });
        // Reset lastSize so handleResize is guaranteed to run!
        lastSize.width = 0;
        lastSize.height = 0;
        handleResize();
      } catch (err: any) {
        console.error("Failed to connect RDP:", err);
        alert(`RDP Error: ${err}`);
      }
    };

    startRdp();

    window.addEventListener("resize", handleResize);

    const resizeObserver = new ResizeObserver(() => {
      handleResize();
    });
    if (containerRef.current) {
      resizeObserver.observe(containerRef.current);
    }

    return () => {
      active = false;
      window.removeEventListener("resize", handleResize);
      resizeObserver.disconnect();
      invoke("disconnect_rdp_embedded", { sessionId: sid }).catch((err) =>
        console.error("RDP disconnect error:", err)
      );
    };
  }, []);

  return (
    <div className="terminal-container">
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
        <span style={{ color: "var(--accent-purple)" }}>embedded tab</span>
      </div>
      <div
        ref={containerRef}
        className="rdp-body-placeholder"
        style={{
          flexGrow: 1,
          width: "100%",
          height: "100%",
          backgroundColor: "#000",
          position: "relative",
        }}
      >
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
          {/* Pulsing Spinner */}
          <div className="rdp-spinner-anim" style={{
            width: "40px",
            height: "40px",
            border: "3px solid rgba(0, 240, 255, 0.08)",
            borderTop: "3px solid var(--accent-cyan)",
            borderRight: "3px solid var(--accent-cyan)",
            borderRadius: "50%",
            boxShadow: "0 0 10px rgba(0, 240, 255, 0.2)",
          }} />
          
          {/* Status text */}
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

          {/* Progress Bar Container */}
          <div style={{
            width: "100%",
            height: "6px",
            backgroundColor: "rgba(255, 255, 255, 0.03)",
            borderRadius: "3px",
            overflow: "hidden",
            border: "1px solid rgba(255, 255, 255, 0.07)",
          }}>
            {/* Inner Glow Bar */}
            <div style={{
              width: `${progress}%`,
              height: "100%",
              background: "linear-gradient(90deg, var(--accent-cyan), var(--accent-purple))",
              boxShadow: "0 0 8px var(--accent-cyan)",
              transition: "width 0.6s cubic-bezier(0.4, 0, 0.2, 1)",
            }} />
          </div>
        </div>
      </div>
    </div>
  );
};