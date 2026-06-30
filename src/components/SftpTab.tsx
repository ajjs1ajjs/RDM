import React, { useState, useEffect } from "react";
import { invoke } from "@tauri-apps/api/core";
import { File, Folder, Download, Upload, RefreshCw, ChevronRight, HardDrive } from "lucide-react";

interface SftpTabProps {
  sessionId: string;
  host: string;
  port: number;
  username: string;
  credentialId?: string;
  serverId?: string;
}

interface FileItem {
  name: string;
  isDirectory: boolean;
  size: string;
  date: string;
  permissions: string;
}

export const SftpTab: React.FC<SftpTabProps> = ({
  host,
  port,
  username,
  credentialId,
  serverId,
}) => {
  const [currentPath, setCurrentPath] = useState<string>("/");
  const [files, setFiles] = useState<FileItem[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  const fetchFiles = async (path: string) => {
    setLoading(true);
    setError(null);
    try {
      const output = await invoke<string>("sftp_ls", {
        host,
        port,
        username,
        path,
        credentialId: credentialId || null,
        serverId: serverId || null,
      });

      const lines = output.split("\n").map(l => l.trim()).filter(l => l.length > 0);
      const parsedFiles: FileItem[] = [];

      for (const line of lines) {
        // Simple regex to parse `ls -la` output:
        // drwxr-xr-x 2 root root 4096 Jan 1 00:00 filename
        const parts = line.split(/\s+/);
        if (parts.length >= 8 && parts[0].length >= 10 && (parts[0].startsWith('d') || parts[0].startsWith('-'))) {
          const isDirectory = parts[0].startsWith('d');
          const permissions = parts[0];
          const size = parts[4];
          
          // Reconstruct the name which might contain spaces
          const nameIndex = line.indexOf(parts[7], line.indexOf(parts[6]));
          const name = line.substring(nameIndex).trim();

          if (name !== "." && name !== "..") {
            parsedFiles.push({
              name,
              isDirectory,
              size,
              date: `${parts[5]} ${parts[6]} ${parts[7]}`, // Roughly works for full-time
              permissions
            });
          }
        }
      }

      setFiles(parsedFiles.sort((a, b) => {
        if (a.isDirectory && !b.isDirectory) return -1;
        if (!a.isDirectory && b.isDirectory) return 1;
        return a.name.localeCompare(b.name);
      }));
      setCurrentPath(path);
    } catch (err: any) {
      console.error("SFTP Error:", err);
      setError(err.toString());
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchFiles("/");
  }, []);

  const handleNavigate = (folder: string) => {
    const newPath = currentPath === "/" ? `/${folder}` : `${currentPath}/${folder}`;
    fetchFiles(newPath);
  };

  const handleNavigateUp = () => {
    if (currentPath === "/") return;
    const parts = currentPath.split("/").filter(Boolean);
    parts.pop();
    const newPath = parts.length === 0 ? "/" : `/${parts.join("/")}`;
    fetchFiles(newPath);
  };

  const handleDownload = async (file: FileItem) => {
    try {
      const { invoke } = await import('@tauri-apps/api/core');
      const { save } = await import('@tauri-apps/plugin-dialog');
      
      const localPath = await save({ defaultPath: file.name });
      if (!localPath) return;

      setLoading(true);
      await invoke("sftp_download", {
        host,
        port,
        username,
        remotePath: currentPath === "/" ? `/${file.name}` : `${currentPath}/${file.name}`,
        localPath,
        credentialId: credentialId || null,
        serverId: serverId || null,
      });
      alert("Download completed!");
    } catch (err: any) {
      alert(`Download failed: ${err}`);
    } finally {
      setLoading(false);
    }
  };

  const handleUpload = async () => {
    try {
      const { invoke } = await import('@tauri-apps/api/core');
      const { open } = await import('@tauri-apps/plugin-dialog');
      
      const localPath = await open({ multiple: false });
      if (!localPath || typeof localPath !== 'string') return;
      
      // Get filename from localPath
      const filename = localPath.split(/[/\\]/).pop();

      setLoading(true);
      await invoke("sftp_upload", {
        host,
        port,
        username,
        localPath,
        remotePath: currentPath === "/" ? `/${filename}` : `${currentPath}/${filename}`,
        credentialId: credentialId || null,
        serverId: serverId || null,
      });
      alert("Upload completed!");
      fetchFiles(currentPath);
    } catch (err: any) {
      alert(`Upload failed: ${err}`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="sftp-container" style={{ display: 'flex', flexDirection: 'column', height: '100%', backgroundColor: 'var(--bg-primary)', color: '#fff' }}>
      <div className="terminal-header" style={{ display: 'flex', justifyContent: 'space-between', padding: '10px 20px', backgroundColor: 'var(--bg-secondary)', borderBottom: '1px solid var(--border-color)' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
          <HardDrive size={18} color="var(--accent-blue)" />
          <span style={{ fontWeight: 600 }}>SFTP: {host}</span>
        </div>
        <div style={{ display: 'flex', gap: '10px' }}>
          <button className="btn btn-secondary" onClick={() => fetchFiles(currentPath)} disabled={loading} style={{ padding: '4px 8px' }}>
            <RefreshCw size={14} className={loading ? "spin" : ""} />
          </button>
          <button className="btn btn-primary" onClick={handleUpload} disabled={loading} style={{ padding: '4px 8px' }}>
            <Upload size={14} /> Upload
          </button>
        </div>
      </div>

      <div style={{ padding: '10px 20px', backgroundColor: 'var(--bg-tertiary)', display: 'flex', alignItems: 'center', gap: '5px', fontSize: '0.9rem', borderBottom: '1px solid var(--border-color)' }}>
        <span style={{ cursor: 'pointer', color: 'var(--text-secondary)' }} onClick={() => fetchFiles("/")}>/</span>
        {currentPath.split('/').filter(Boolean).map((part, index, arr) => (
          <React.Fragment key={index}>
            <ChevronRight size={14} color="var(--text-muted)" />
            <span 
              style={{ cursor: 'pointer', color: index === arr.length - 1 ? 'var(--accent-purple)' : 'var(--text-secondary)' }}
              onClick={() => fetchFiles("/" + arr.slice(0, index + 1).join("/"))}
            >
              {part}
            </span>
          </React.Fragment>
        ))}
      </div>

      <div style={{ flex: 1, overflowY: 'auto', padding: '10px' }}>
        {loading && files.length === 0 ? (
          <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100%' }}>
            <div className="loading-spinner"></div>
          </div>
        ) : error ? (
          <div style={{ color: 'var(--error-color)', padding: '20px', textAlign: 'center' }}>
            {error}
          </div>
        ) : (
          <table className="server-table" style={{ width: '100%' }}>
            <thead>
              <tr>
                <th style={{ width: '50%' }}>Name</th>
                <th>Size</th>
                <th>Permissions</th>
                <th style={{ textAlign: 'right' }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {currentPath !== "/" && (
                <tr onClick={handleNavigateUp} style={{ cursor: 'pointer' }}>
                  <td colSpan={4} style={{ color: 'var(--accent-blue)' }}>
                    <Folder size={14} style={{ display: 'inline', marginRight: '8px' }} />
                    ..
                  </td>
                </tr>
              )}
              {files.map((file, idx) => (
                <tr key={idx} onDoubleClick={() => file.isDirectory && handleNavigate(file.name)} style={{ cursor: file.isDirectory ? 'pointer' : 'default' }}>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                      {file.isDirectory ? <Folder size={16} color="var(--accent-blue)" /> : <File size={16} color="var(--text-secondary)" />}
                      {file.name}
                    </div>
                  </td>
                  <td style={{ color: 'var(--text-muted)' }}>{file.isDirectory ? '-' : file.size}</td>
                  <td style={{ fontFamily: 'monospace', color: 'var(--text-muted)', fontSize: '0.85rem' }}>{file.permissions}</td>
                  <td style={{ textAlign: 'right' }}>
                    {!file.isDirectory && (
                      <button className="btn btn-secondary" style={{ padding: '4px 8px', fontSize: '0.8rem' }} onClick={(e) => { e.stopPropagation(); handleDownload(file); }}>
                        <Download size={14} />
                      </button>
                    )}
                  </td>
                </tr>
              ))}
              {files.length === 0 && (
                <tr>
                  <td colSpan={4} style={{ textAlign: 'center', padding: '20px', color: 'var(--text-muted)' }}>
                    Empty directory
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
};
