import { useState, useEffect, useCallback } from "react";
import { listen } from "@tauri-apps/api/event";
import { Server, ActiveTab } from "../types";

export function useConnectionTabs() {
  const [activeTabType, setActiveTabType] = useState<string>("dashboard");
  const [activeTabs, setActiveTabs] = useState<ActiveTab[]>([]);
  const [currentTabId, setCurrentTabId] = useState<string>("dashboard");

  useEffect(() => {
    let unlistenRdp: (() => void) | null = null;
    let unlistenSsh: (() => void) | null = null;

    const setupListeners = async () => {
      unlistenRdp = await listen<string>("rdp-closed", (event) => {
        const closedTabId = event.payload;
        setActiveTabs((prev) => prev.filter((t) => t.id !== closedTabId));
      });

      unlistenSsh = await listen<string>("ssh-closed", (event) => {
        const closedTabId = event.payload;
        setActiveTabs((prev) => prev.filter((t) => t.id !== closedTabId));
      });
    };

    setupListeners();

    return () => {
      if (unlistenRdp) unlistenRdp();
      if (unlistenSsh) unlistenSsh();
    };
  }, []);

  const handleConnect = useCallback((srv: Server) => {
    if (srv.protocol === "rdp") {
      const tabId = `rdp-${srv.id}-${Date.now()}`;
      const newTab: ActiveTab = {
        id: tabId, title: srv.name, type: "rdp",
        serverId: srv.id, hostname: srv.hostname || srv.ip,
      };
      setActiveTabs((prev) => [...prev, newTab]);
      setCurrentTabId(tabId);
      setActiveTabType("rdp");
    } else {
      const tabId = `ssh-${srv.id}-${Date.now()}`;
      const newTab: ActiveTab = {
        id: tabId, title: srv.name, type: "ssh",
        serverId: srv.id, hostname: srv.hostname || srv.ip,
      };
      setActiveTabs((prev) => [...prev, newTab]);
      setCurrentTabId(tabId);
      setActiveTabType("ssh");
    }
  }, []);

  const handleQuickConnect = useCallback((input: string, protocol: string) => {
    let host = input.trim();
    let port = protocol === "rdp" ? 3389 : 22;
    const colonIdx = host.lastIndexOf(":");
    if (colonIdx > 0) {
      const portStr = host.substring(colonIdx + 1);
      const parsedPort = parseInt(portStr, 10);
      if (!isNaN(parsedPort) && parsedPort > 0 && parsedPort <= 65535) {
        port = parsedPort;
        host = host.substring(0, colonIdx);
      }
    }

    const tabId = `${protocol}-quick-${Date.now()}`;
    const newTab: ActiveTab = {
      id: tabId, title: `${host}:${port}`,
      type: protocol as ActiveTab["type"], hostname: host,
    };

    setActiveTabs((prev) => [...prev, newTab]);
    setCurrentTabId(tabId);
    setActiveTabType(protocol);
  }, []);

  const handleSelectTab = useCallback((tab: ActiveTab | string) => {
    if (typeof tab === "string") {
      setCurrentTabId(tab);
      setActiveTabType(tab);
    } else {
      setCurrentTabId(tab.id);
      setActiveTabType(tab.type);
    }
  }, []);

  const handleConnectSFTP = useCallback((server: Server) => {
    const tabId = `sftp-${server.id}-${Date.now()}`;
    setActiveTabs((prev) => [
      ...prev,
      { id: tabId, type: "sftp" as const, serverId: server.id, hostname: server.hostname || server.ip, title: server.name },
    ]);
    setCurrentTabId(tabId);
    setActiveTabType("sftp");
  }, []);

  const handleCloseTab = useCallback((tabId: string, e: React.MouseEvent) => {
    e.stopPropagation();
    const remaining = activeTabs.filter((t) => t.id !== tabId);
    setActiveTabs(remaining);
    if (currentTabId === tabId) {
      setCurrentTabId("dashboard");
      setActiveTabType("dashboard");
    }
  }, [activeTabs, currentTabId]);

  return {
    activeTabType, setActiveTabType,
    activeTabs, setActiveTabs,
    currentTabId, setCurrentTabId,
    handleConnect, handleQuickConnect,
    handleSelectTab, handleConnectSFTP, handleCloseTab,
  };
}
