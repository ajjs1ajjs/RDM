import { useState, useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";
import { Server } from "../types";
import { useDialogs } from "../components/AppDialogs";

export function useServerForm(
  selectedFolder: string,
  loadServers: () => Promise<void>,
  setSelectedServer: (s: Server | null) => void,
) {
  const dialogs = useDialogs();
  const [serverModalOpen, setServerModalOpen] = useState<boolean>(false);
  const [editingServer, setEditingServer] = useState<Server | null>(null);

  const [srvName, setSrvName] = useState("");
  const [srvHost, setSrvHost] = useState("");
  const [srvIp, setSrvIp] = useState("");
  const [srvPort, setSrvPort] = useState(22);
  const [srvProto, setSrvProto] = useState<'ssh' | 'rdp'>("ssh");
  const [srvOs, setSrvOs] = useState<'linux' | 'windows'>("linux");
  const [srvFolder, setSrvFolder] = useState("");
  const [srvTags, setSrvTags] = useState("");
  const [srvDesc, setSrvDesc] = useState("");
  const [srvCredId, setSrvCredId] = useState("");
  const [srvUsername, setSrvUsername] = useState("");
  const [srvPassword, setSrvPassword] = useState("");
  const [rdpClipboard, setRdpClipboard] = useState<boolean>(true);
  const [rdpDrives, setRdpDrives] = useState<boolean>(false);
  const [rdpPrinters, setRdpPrinters] = useState<boolean>(false);
  const [rdpSmartSizing, setRdpSmartSizing] = useState<boolean>(true);
  const [rdpAudio, setRdpAudio] = useState<number>(0);
  const [rdpSmartcards, setRdpSmartcards] = useState<boolean>(false);
  const [rdpWebauthn, setRdpWebauthn] = useState<boolean>(false);
  const [rdpFullscreen, setRdpFullscreen] = useState<boolean>(false);
  const [rdpMultimon, setRdpMultimon] = useState<boolean>(false);

  const openServerForm = useCallback((srv: Server | null = null) => {
    setEditingServer(srv);
    if (srv) {
      setSrvName(srv.name);
      setSrvHost(srv.hostname);
      setSrvIp(srv.ip);
      setSrvPort(srv.port);
      setSrvProto(srv.protocol);
      setSrvOs(srv.os);
      setSrvFolder(srv.folder_path);
      setSrvTags(srv.tags);
      setSrvDesc(srv.description);
      setSrvCredId(srv.credential_id || "");
      setSrvUsername(srv.username || "");
      setSrvPassword(srv.encrypted_password ? "__UNCHANGED__" : "");
      setRdpClipboard(srv.rdp_clipboard !== undefined ? srv.rdp_clipboard !== 0 : true);
      setRdpDrives(srv.rdp_drives !== undefined ? srv.rdp_drives !== 0 : false);
      setRdpPrinters(srv.rdp_printers !== undefined ? srv.rdp_printers !== 0 : false);
      setRdpSmartSizing(srv.rdp_smart_sizing !== undefined ? srv.rdp_smart_sizing !== 0 : true);
      setRdpAudio(srv.rdp_audio !== undefined ? srv.rdp_audio : 0);
      setRdpSmartcards(srv.rdp_smartcards !== undefined ? srv.rdp_smartcards !== 0 : false);
      setRdpWebauthn(srv.rdp_webauthn !== undefined ? srv.rdp_webauthn !== 0 : false);
      setRdpFullscreen(srv.rdp_fullscreen !== undefined ? srv.rdp_fullscreen !== 0 : false);
      setRdpMultimon(srv.rdp_multimon !== undefined ? srv.rdp_multimon !== 0 : false);
    } else {
      setSrvName("");
      setSrvHost("");
      setSrvIp("");
      setSrvPort(22);
      setSrvProto("ssh");
      setSrvOs("linux");
      setSrvFolder(selectedFolder);
      setSrvTags("");
      setSrvDesc("");
      setSrvCredId("");
      setSrvUsername("");
      setSrvPassword("");
      setRdpClipboard(true);
      setRdpDrives(false);
      setRdpPrinters(false);
      setRdpSmartSizing(true);
      setRdpAudio(0);
      setRdpSmartcards(false);
      setRdpWebauthn(false);
      setRdpFullscreen(false);
      setRdpMultimon(false);
    }
    setServerModalOpen(true);
  }, [selectedFolder]);

  const saveServer = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (editingServer) {
        await invoke("update_server", {
          id: editingServer.id, name: srvName, hostname: srvHost, ip: srvIp,
          port: srvPort, protocol: srvProto, os: srvOs,
          folderPath: srvFolder, tags: srvTags, description: srvDesc,
          credentialId: srvCredId || null, username: srvUsername || null,
          password: srvPassword || null,
          rdpClipboard: rdpClipboard ? 1 : 0, rdpDrives: rdpDrives ? 1 : 0,
          rdpPrinters: rdpPrinters ? 1 : 0, rdpSmartSizing: rdpSmartSizing ? 1 : 0,
          rdpAudio: rdpAudio, rdpSmartcards: rdpSmartcards ? 1 : 0,
          rdpWebauthn: rdpWebauthn ? 1 : 0, rdpFullscreen: rdpFullscreen ? 1 : 0,
          rdpMultimon: rdpMultimon ? 1 : 0,
        });
      } else {
        await invoke("add_server", {
          name: srvName, hostname: srvHost, ip: srvIp, port: srvPort,
          protocol: srvProto, os: srvOs, folderPath: srvFolder,
          tags: srvTags, description: srvDesc,
          credentialId: srvCredId || null, username: srvUsername || null,
          password: srvPassword || null,
          rdpClipboard: rdpClipboard ? 1 : 0, rdpDrives: rdpDrives ? 1 : 0,
          rdpPrinters: rdpPrinters ? 1 : 0, rdpSmartSizing: rdpSmartSizing ? 1 : 0,
          rdpAudio: rdpAudio, rdpSmartcards: rdpSmartcards ? 1 : 0,
          rdpWebauthn: rdpWebauthn ? 1 : 0, rdpFullscreen: rdpFullscreen ? 1 : 0,
          rdpMultimon: rdpMultimon ? 1 : 0,
        });
      }
      setServerModalOpen(false);
      loadServers();
      setSelectedServer(null);
    } catch (e: any) {
      setServerModalOpen(false);
      await dialogs.alert(`Failed to save server: ${e}`);
    }
  }, [editingServer, srvName, srvHost, srvIp, srvPort, srvProto, srvOs,
      srvFolder, srvTags, srvDesc, srvCredId, srvUsername, srvPassword,
      rdpClipboard, rdpDrives, rdpPrinters, rdpSmartSizing, rdpAudio,
      rdpSmartcards, rdpWebauthn, rdpFullscreen, rdpMultimon,
      loadServers, setSelectedServer, dialogs]);

  const deleteServer = useCallback(async (id: string) => {
    if (!await dialogs.confirm("Are you sure you want to delete this server configuration?")) return;
    try {
      await invoke("delete_server", { id });
      loadServers();
      setSelectedServer(null);
    } catch (e: any) {
      await dialogs.alert(`Failed to delete server: ${e}`);
    }
  }, [loadServers, setSelectedServer, dialogs]);

  return {
    serverModalOpen, setServerModalOpen,
    editingServer, setEditingServer,
    srvName, setSrvName, srvHost, setSrvHost, srvIp, setSrvIp,
    srvPort, setSrvPort, srvProto, setSrvProto, srvOs, setSrvOs,
    srvFolder, setSrvFolder, srvTags, setSrvTags, srvDesc, setSrvDesc,
    srvCredId, setSrvCredId, srvUsername, setSrvUsername, srvPassword, setSrvPassword,
    rdpClipboard, setRdpClipboard, rdpDrives, setRdpDrives,
    rdpPrinters, setRdpPrinters, rdpSmartSizing, setRdpSmartSizing,
    rdpAudio, setRdpAudio, rdpSmartcards, setRdpSmartcards,
    rdpWebauthn, setRdpWebauthn, rdpFullscreen, setRdpFullscreen,
    rdpMultimon, setRdpMultimon,
    openServerForm, saveServer, deleteServer,
  };
}
