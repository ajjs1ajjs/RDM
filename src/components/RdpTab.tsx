import React, { useEffect, useRef } from "react";
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

  useEffect(() => {
    let active = true;

    const startRdp = async () => {
      if (!containerRef.current) return;
      
      // Wait for DOM to lay out
      await new Promise((resolve) => setTimeout(resolve, 300));
      if (!active || !containerRef.current) return;

      const rect = containerRef.current.getBoundingClientRect();

      try {
        // Trigger connect and embed on the backend
        await invoke("connect_rdp_embedded", {
          sessionId,
          serverId,
          host,
          port,
          credentialId: credentialId || null,
          x: Math.round(rect.left),
          y: Math.round(rect.top),
          width: Math.round(rect.width),
          height: Math.round(rect.height),
        });
      } catch (err: any) {
        console.error("Failed to connect RDP:", err);
        alert(`RDP Error: ${err}`);
      }
    };

    startRdp();

    // Listen to resize events
    const handleResize = () => {
      if (!containerRef.current) return;
      const rect = containerRef.current.getBoundingClientRect();
      invoke("resize_rdp_embedded", {
        sessionId,
        x: Math.round(rect.left),
        y: Math.round(rect.top),
        width: Math.round(rect.width),
        height: Math.round(rect.height),
      }).catch((err) => console.error("RDP resize error:", err));
    };

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
      
      // Notify backend to close the RDP process
      invoke("disconnect_rdp_embedded", { sessionId }).catch((err) =>
        console.error("RDP disconnect error:", err)
      );
    };
  }, [sessionId, serverId, host, port, credentialId]);

  return (
    <div className="terminal-container">
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
          position: "relative"
        }}
      >
        <div style={{
          position: "absolute",
          top: "50%",
          left: "50%",
          transform: "translate(-50%, -50%)",
          color: "var(--text-secondary)",
          fontSize: "0.9rem"
        }}>
          Connecting to Remote Desktop...
        </div>
      </div>
    </div>
  );
};