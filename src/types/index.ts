export interface Server {
  id: string;
  name: string;
  hostname: string;
  ip: string;
  port: number;
  protocol: 'rdp' | 'ssh';
  os: 'windows' | 'linux';
  folder_path: string;
  tags: string; // Comma separated
  description: string;
  credential_id?: string;
  username?: string;
  encrypted_password?: string;
  created_at?: string;
  updated_at?: string;
  rdp_clipboard?: number;
  rdp_drives?: number;
  rdp_printers?: number;
  rdp_smart_sizing?: number;
  rdp_audio?: number;
  rdp_smartcards?: number;
  rdp_webauthn?: number;
}

export interface Credential {
  id: string;
  name: string;
  type: 'password' | 'ssh_key';
  username: string;
  encrypted_secret: string;
  created_at?: string;
  updated_at?: string;
}

export interface ConnectionHistory {
  id: string;
  server_id: string;
  timestamp: string;
  status: 'connected' | 'disconnected' | 'failed';
  log: string;
}

export interface ActiveTab {
  id: string; // Can be server.id or custom like 'credentials', 'dashboard'
  title: string;
  type: 'ssh' | 'rdp' | 'dashboard' | 'credentials' | 'settings';
  serverId?: string;
  hostname?: string;
}
