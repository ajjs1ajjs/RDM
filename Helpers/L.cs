using System.Resources;

namespace RemoteManager.Helpers;

internal static class L
{
    private static readonly ResourceManager _rm = new("RemoteManager.Resources.Strings.AppStrings",
        typeof(L).Assembly);

    private static string Get(string name) => _rm.GetString(name) ?? name;

    public static string Get(string name, params object?[] args)
    {
        var s = _rm.GetString(name);
        return s != null ? string.Format(s, args) : name;
    }

    // Dialog Titles
    public static string Title_Error => Get(nameof(Title_Error));
    public static string Title_Success => Get(nameof(Title_Success));
    public static string Title_Warning => Get(nameof(Title_Warning));
    public static string Title_Information => Get(nameof(Title_Information));
    public static string Title_Confirm => Get(nameof(Title_Confirm));
    public static string Title_ValidationError => Get(nameof(Title_ValidationError));
    public static string Title_Settings => Get(nameof(Title_Settings));

    // App
    public static string App_Title => Get(nameof(App_Title));
    public static string App_TrayMinimized => Get(nameof(App_TrayMinimized));
    public static string App_StartupFailed => Get(nameof(App_StartupFailed));

    // Status
    public static string Status_Disconnected => Get(nameof(Status_Disconnected));
    public static string Status_Connecting => Get(nameof(Status_Connecting));
    public static string Status_Connected => Get(nameof(Status_Connected));
    public static string Status_ConnectionFailed => Get(nameof(Status_ConnectionFailed));
    public static string Status_LoggedOut => Get(nameof(Status_LoggedOut));
    public static string Status_Reconnecting => Get(nameof(Status_Reconnecting));
    public static string Status_ReconnectFailed => Get(nameof(Status_ReconnectFailed));
    public static string Status_TerminalNotReady => Get(nameof(Status_TerminalNotReady));
    public static string Status_RdpHostNotReady => Get(nameof(Status_RdpHostNotReady));

    // Connection Edit
    public static string ConnEdit_TitleNew => Get(nameof(ConnEdit_TitleNew));
    public static string ConnEdit_TitleEdit => Get(nameof(ConnEdit_TitleEdit));
    public static string ConnEdit_DefaultName => Get(nameof(ConnEdit_DefaultName));
    public static string ConnEdit_NameRequired => Get(nameof(ConnEdit_NameRequired));
    public static string ConnEdit_HostRequired => Get(nameof(ConnEdit_HostRequired));
    public static string ConnEdit_UrlRequired => Get(nameof(ConnEdit_UrlRequired));
    public static string ConnEdit_InvalidHost => Get(nameof(ConnEdit_InvalidHost));
    public static string ConnEdit_InvalidIp => Get(nameof(ConnEdit_InvalidIp));
    public static string ConnEdit_InvalidPort => Get(nameof(ConnEdit_InvalidPort));
    public static string ConnEdit_KeyRequired => Get(nameof(ConnEdit_KeyRequired));
    public static string ConnEdit_KeyNotFound => Get(nameof(ConnEdit_KeyNotFound));
    public static string ConnEdit_BrowseKeyTitle => Get(nameof(ConnEdit_BrowseKeyTitle));

    // SSH Key Generator
    public static string KeyGen_BrowseTitle => Get(nameof(KeyGen_BrowseTitle));
    public static string KeyGen_DirRequired => Get(nameof(KeyGen_DirRequired));
    public static string KeyGen_NameRequired => Get(nameof(KeyGen_NameRequired));
    public static string KeyGen_FileExists => Get(nameof(KeyGen_FileExists));
    public static string KeyGen_Failed => Get(nameof(KeyGen_Failed));

    // Tabs
    public static string Tab_Settings => Get(nameof(Tab_Settings));
    public static string Tab_CloseConfirm => Get(nameof(Tab_CloseConfirm));
    public static string Tab_CloseTitle => Get(nameof(Tab_CloseTitle));

    // SFTP
    public static string Sftp_Title => Get(nameof(Sftp_Title));
    public static string Sftp_Connecting => Get(nameof(Sftp_Connecting));
    public static string Sftp_Connected => Get(nameof(Sftp_Connected));
    public static string Sftp_Failed => Get(nameof(Sftp_Failed));
    public static string Sftp_Loading => Get(nameof(Sftp_Loading));
    public static string Sftp_Ready => Get(nameof(Sftp_Ready));
    public static string Sftp_LoadFailed => Get(nameof(Sftp_LoadFailed));
    public static string Sftp_DownloadTitle => Get(nameof(Sftp_DownloadTitle));
    public static string Sftp_UploadTitle => Get(nameof(Sftp_UploadTitle));
    public static string Sftp_TransferComplete => Get(nameof(Sftp_TransferComplete));
    public static string Sftp_TransferFailed => Get(nameof(Sftp_TransferFailed));
    public static string Sftp_TransferErrorTitle => Get(nameof(Sftp_TransferErrorTitle));
    public static string Sftp_DeleteConfirm => Get(nameof(Sftp_DeleteConfirm));
    public static string Sftp_DeleteTitle => Get(nameof(Sftp_DeleteTitle));
    public static string Sftp_Deleting => Get(nameof(Sftp_Deleting));
    public static string Sftp_Deleted => Get(nameof(Sftp_Deleted));
    public static string Sftp_DeleteFailed => Get(nameof(Sftp_DeleteFailed));
    public static string Sftp_DeleteError => Get(nameof(Sftp_DeleteError));

    // Groups
    public static string Group_DefaultName => Get(nameof(Group_DefaultName));
    public static string Group_Ungrouped => Get(nameof(Group_Ungrouped));
    public static string Group_AddDialog_Message => Get(nameof(Group_AddDialog_Message));
    public static string Group_AddDialog_Title => Get(nameof(Group_AddDialog_Title));
    public static string Group_RenameDialog => Get(nameof(Group_RenameDialog));
    public static string Group_DeleteConfirm => Get(nameof(Group_DeleteConfirm));
    public static string Group_DeleteTitle => Get(nameof(Group_DeleteTitle));

    // Connection Operations
    public static string Conn_DeleteConfirm => Get(nameof(Conn_DeleteConfirm));
    public static string Conn_DeleteTitle => Get(nameof(Conn_DeleteTitle));
    public static string Conn_DuplicateSuffix => Get(nameof(Conn_DuplicateSuffix));

    // WoL
    public static string WoL_NotConfigured => Get(nameof(WoL_NotConfigured));
    public static string WoL_Title => Get(nameof(WoL_Title));
    public static string WoL_Success => Get(nameof(WoL_Success));
    public static string WoL_Failed => Get(nameof(WoL_Failed));

    // Import/Export
    public static string Import_FileDialogTitle => Get(nameof(Import_FileDialogTitle));
    public static string Import_PreviewTitle => Get(nameof(Import_PreviewTitle));
    public static string Import_Success => Get(nameof(Import_Success));
    public static string Import_Failed => Get(nameof(Import_Failed));
    public static string Import_SuccessWithDetails => Get(nameof(Import_SuccessWithDetails));
    public static string Import_PreviewMessage => Get(nameof(Import_PreviewMessage));
    public static string Import_PreviewEncryptedMessage => Get(nameof(Import_PreviewEncryptedMessage));
    public static string Export_FileDialogTitle => Get(nameof(Export_FileDialogTitle));
    public static string Export_Success => Get(nameof(Export_Success));
    public static string Export_Failed => Get(nameof(Export_Failed));

    // Encrypted Backup
    public static string Backup_ExportTitle => Get(nameof(Backup_ExportTitle));
    public static string Backup_PasswordDialog => Get(nameof(Backup_PasswordDialog));
    public static string Backup_PasswordDialogTitle => Get(nameof(Backup_PasswordDialogTitle));
    public static string Backup_PasswordEmpty => Get(nameof(Backup_PasswordEmpty));
    public static string Backup_ExportSuccess => Get(nameof(Backup_ExportSuccess));
    public static string Backup_ExportFailed => Get(nameof(Backup_ExportFailed));
    public static string Backup_ImportTitle => Get(nameof(Backup_ImportTitle));
    public static string Backup_ImportPasswordDialog => Get(nameof(Backup_ImportPasswordDialog));
    public static string Backup_ImportPasswordTitle => Get(nameof(Backup_ImportPasswordTitle));
    public static string Backup_ImportSuccess => Get(nameof(Backup_ImportSuccess));
    public static string Backup_DecryptFailed => Get(nameof(Backup_DecryptFailed));

    // Master Password
    public static string MasterPwd_Invalid => Get(nameof(MasterPwd_Invalid));
    public static string MasterPwd_Empty => Get(nameof(MasterPwd_Empty));
    public static string MasterPwd_Changed => Get(nameof(MasterPwd_Changed));

    // Settings
    public static string Settings_Saved => Get(nameof(Settings_Saved));
    public static string Settings_DbPathUpdated => Get(nameof(Settings_DbPathUpdated));
    public static string Settings_DbMigrateMessage => Get(nameof(Settings_DbMigrateMessage));
    public static string Settings_DbMigrateTitle => Get(nameof(Settings_DbMigrateTitle));
    public static string Settings_DbSelectTitle => Get(nameof(Settings_DbSelectTitle));
    public static string Settings_MigrationFailed => Get(nameof(Settings_MigrationFailed));
    public static string Settings_BackupFolderTitle => Get(nameof(Settings_BackupFolderTitle));

    // First Run
    public static string FirstRun_Title => Get(nameof(FirstRun_Title));
    public static string FirstRun_Message => Get(nameof(FirstRun_Message));
    public static string FirstRun_RestoreFolderTitle => Get(nameof(FirstRun_RestoreFolderTitle));
    public static string FirstRun_RestoreSuccess => Get(nameof(FirstRun_RestoreSuccess));
    public static string FirstRun_RestoreFailed => Get(nameof(FirstRun_RestoreFailed));
    public static string FirstRun_ImportFilter => Get(nameof(FirstRun_ImportFilter));
    public static string FirstRun_ImportTitle => Get(nameof(FirstRun_ImportTitle));
    public static string FirstRun_DecryptDialog => Get(nameof(FirstRun_DecryptDialog));
    public static string FirstRun_DecryptTitle => Get(nameof(FirstRun_DecryptTitle));
    public static string FirstRun_BackupMessage => Get(nameof(FirstRun_BackupMessage));
    public static string FirstRun_BackupTitle => Get(nameof(FirstRun_BackupTitle));
    public static string FirstRun_BackupFolderTitle => Get(nameof(FirstRun_BackupFolderTitle));
    public static string FirstRun_BackupConfigured => Get(nameof(FirstRun_BackupConfigured));
    public static string FirstRun_BackupConfiguredTitle => Get(nameof(FirstRun_BackupConfiguredTitle));

    // Additional Status
    public static string Status_ConnectingTo => Get(nameof(Status_ConnectingTo));
    public static string Status_ConnectedText => Get(nameof(Status_ConnectedText));

    // Session Info
    public static string SessionInfo_Sftp => Get(nameof(SessionInfo_Sftp));
    public static string SessionInfo_Ssh => Get(nameof(SessionInfo_Ssh));
    public static string SessionInfo_Rdp => Get(nameof(SessionInfo_Rdp));
    public static string SessionInfo_Web => Get(nameof(SessionInfo_Web));

    // Sftp tab header
    public static string Sftp_TabHeader => Get(nameof(Sftp_TabHeader));

    // === UI Labels ===

    // ConnectionEdit Dialog
    public static string ConnEdit_RDP => Get(nameof(ConnEdit_RDP));
    public static string ConnEdit_SSH => Get(nameof(ConnEdit_SSH));
    public static string ConnEdit_General => Get(nameof(ConnEdit_General));
    public static string ConnEdit_Name => Get(nameof(ConnEdit_Name));
    public static string ConnEdit_Host => Get(nameof(ConnEdit_Host));
    public static string ConnEdit_Port => Get(nameof(ConnEdit_Port));
    public static string ConnEdit_Username => Get(nameof(ConnEdit_Username));
    public static string ConnEdit_Password => Get(nameof(ConnEdit_Password));
    public static string ConnEdit_Generate => Get(nameof(ConnEdit_Generate));
    public static string ConnEdit_GenerateTooltip => Get(nameof(ConnEdit_GenerateTooltip));
    public static string ConnEdit_Save => Get(nameof(ConnEdit_Save));
    public static string ConnEdit_Group => Get(nameof(ConnEdit_Group));
    public static string ConnEdit_MacAddress => Get(nameof(ConnEdit_MacAddress));
    public static string ConnEdit_MacAddressTooltip => Get(nameof(ConnEdit_MacAddressTooltip));
    public static string ConnEdit_Tags => Get(nameof(ConnEdit_Tags));
    public static string ConnEdit_TagsTooltip => Get(nameof(ConnEdit_TagsTooltip));
    public static string ConnEdit_RdpSettings => Get(nameof(ConnEdit_RdpSettings));
    public static string ConnEdit_RdpWidth => Get(nameof(ConnEdit_RdpWidth));
    public static string ConnEdit_RdpHeight => Get(nameof(ConnEdit_RdpHeight));
    public static string ConnEdit_RdpRedirectClipboard => Get(nameof(ConnEdit_RdpRedirectClipboard));
    public static string ConnEdit_RdpRedirectDrives => Get(nameof(ConnEdit_RdpRedirectDrives));
    public static string ConnEdit_RdpRedirectPrinters => Get(nameof(ConnEdit_RdpRedirectPrinters));
    public static string ConnEdit_RdpUseMultimon => Get(nameof(ConnEdit_RdpUseMultimon));
    public static string ConnEdit_RdpCredSsp => Get(nameof(ConnEdit_RdpCredSsp));
    public static string ConnEdit_RdpAudioMode => Get(nameof(ConnEdit_RdpAudioMode));
    public static string ConnEdit_RdpPlayLocally => Get(nameof(ConnEdit_RdpPlayLocally));
    public static string ConnEdit_RdpPlayOnServer => Get(nameof(ConnEdit_RdpPlayOnServer));
    public static string ConnEdit_RdpNoAudio => Get(nameof(ConnEdit_RdpNoAudio));
    public static string ConnEdit_RdpGatewayHost => Get(nameof(ConnEdit_RdpGatewayHost));
    public static string ConnEdit_RdpGatewayPort => Get(nameof(ConnEdit_RdpGatewayPort));
    public static string ConnEdit_SshSettings => Get(nameof(ConnEdit_SshSettings));
    public static string ConnEdit_SshAuthType => Get(nameof(ConnEdit_SshAuthType));
    public static string ConnEdit_SshPassword => Get(nameof(ConnEdit_SshPassword));
    public static string ConnEdit_SshKey => Get(nameof(ConnEdit_SshKey));
    public static string ConnEdit_SshKeyFile => Get(nameof(ConnEdit_SshKeyFile));
    public static string ConnEdit_Browse => Get(nameof(ConnEdit_Browse));
    public static string ConnEdit_GenerateKey => Get(nameof(ConnEdit_GenerateKey));
    public static string ConnEdit_SshKeepAlive => Get(nameof(ConnEdit_SshKeepAlive));
    public static string ConnEdit_SshJumpHost => Get(nameof(ConnEdit_SshJumpHost));
    public static string ConnEdit_SshJumpHostPort => Get(nameof(ConnEdit_SshJumpHostPort));
    public static string ConnEdit_SshJumpHostUser => Get(nameof(ConnEdit_SshJumpHostUser));
    public static string ConnEdit_SshJumpHostPass => Get(nameof(ConnEdit_SshJumpHostPass));
    public static string ConnEdit_SshKeyPassphrase => Get(nameof(ConnEdit_SshKeyPassphrase));
    public static string ConnEdit_SshPortForwarding => Get(nameof(ConnEdit_SshPortForwarding));
    public static string ConnEdit_SshPortForwardingEnable => Get(nameof(ConnEdit_SshPortForwardingEnable));
    public static string ConnEdit_SshPortForwardingLocalPort => Get(nameof(ConnEdit_SshPortForwardingLocalPort));
    public static string ConnEdit_SshPortForwardingRemoteHost => Get(nameof(ConnEdit_SshPortForwardingRemoteHost));
    public static string ConnEdit_SshPortForwardingRemotePort => Get(nameof(ConnEdit_SshPortForwardingRemotePort));
    public static string ConnEdit_Description => Get(nameof(ConnEdit_Description));
    public static string ConnEdit_Cancel => Get(nameof(ConnEdit_Cancel));
    public static string ConnEdit_SaveConnection => Get(nameof(ConnEdit_SaveConnection));

    // InputDialog
    public static string Input_Rename => Get(nameof(Input_Rename));
    public static string Input_Cancel => Get(nameof(Input_Cancel));
    public static string Input_OK => Get(nameof(Input_OK));

    // MainWindow
    public static string Main_ConnectionCenter => Get(nameof(Main_ConnectionCenter));
    public static string Main_SearchTooltip => Get(nameof(Main_SearchTooltip));
    public static string Main_Group => Get(nameof(Main_Group));
    public static string Main_RDP => Get(nameof(Main_RDP));
    public static string Main_SSH => Get(nameof(Main_SSH));
    public static string Main_Settings => Get(nameof(Main_Settings));
    public static string Main_RenameGroup => Get(nameof(Main_RenameGroup));
    public static string Main_DeleteGroup => Get(nameof(Main_DeleteGroup));
    public static string Main_AddRDP => Get(nameof(Main_AddRDP));
    public static string Main_AddSSH => Get(nameof(Main_AddSSH));
    public static string Main_Connect => Get(nameof(Main_Connect));
    public static string Main_OpenSFTP => Get(nameof(Main_OpenSFTP));
    public static string Main_WakeOnLAN => Get(nameof(Main_WakeOnLAN));
    public static string Main_Edit => Get(nameof(Main_Edit));
    public static string Main_Duplicate => Get(nameof(Main_Duplicate));
    public static string Main_Delete => Get(nameof(Main_Delete));
    public static string Main_StatusUnknown => Get(nameof(Main_StatusUnknown));
    public static string Main_ConnectedSession => Get(nameof(Main_ConnectedSession));
    public static string Main_Online => Get(nameof(Main_Online));
    public static string Main_Offline => Get(nameof(Main_Offline));
    public static string Main_Checking => Get(nameof(Main_Checking));
    public static string Main_ToggleSidebar => Get(nameof(Main_ToggleSidebar));
    public static string Main_ToggleSidebarTooltip => Get(nameof(Main_ToggleSidebarTooltip));
    public static string Main_RecentSessions => Get(nameof(Main_RecentSessions));
    public static string Main_StatusDashboard => Get(nameof(Main_StatusDashboard));
    public static string Main_Active => Get(nameof(Main_Active));
    public static string Main_Pending => Get(nameof(Main_Pending));
    public static string Main_Latency => Get(nameof(Main_Latency));
    public static string Main_Latency_Unit => Get(nameof(Main_Latency_Unit));
    public static string Main_AddNewConnection => Get(nameof(Main_AddNewConnection));
    public static string Main_Tabs => Get(nameof(Main_Tabs));
    public static string Main_Tabs_Prefix => Get(nameof(Main_Tabs_Prefix));
    public static string Main_Groups => Get(nameof(Main_Groups));
    public static string Main_Groups_Prefix => Get(nameof(Main_Groups_Prefix));

    // MasterPasswordWindow
    public static string MasterPwd_Title => Get(nameof(MasterPwd_Title));
    public static string MasterPwd_EnterPassword => Get(nameof(MasterPwd_EnterPassword));
    public static string MasterPwd_Cancel => Get(nameof(MasterPwd_Cancel));
    public static string MasterPwd_Unlock => Get(nameof(MasterPwd_Unlock));

    // RdpSessionView
    public static string Rdp_FullScreen => Get(nameof(Rdp_FullScreen));
    public static string Rdp_CtrlAltDel => Get(nameof(Rdp_CtrlAltDel));
    public static string Rdp_Reconnect => Get(nameof(Rdp_Reconnect));
    public static string Rdp_Disconnect => Get(nameof(Rdp_Disconnect));

    // SettingsView
    public static string Settings_Appearance => Get(nameof(Settings_Appearance));
    public static string Settings_Theme => Get(nameof(Settings_Theme));
    public static string Settings_MinimizeToTray => Get(nameof(Settings_MinimizeToTray));
    public static string Settings_Defaults => Get(nameof(Settings_Defaults));
    public static string Settings_DefaultRDPPort => Get(nameof(Settings_DefaultRDPPort));
    public static string Settings_DefaultSSHPort => Get(nameof(Settings_DefaultSSHPort));
    public static string Settings_AutoReconnect => Get(nameof(Settings_AutoReconnect));
    public static string Settings_Database => Get(nameof(Settings_Database));
    public static string Settings_DatabasePath => Get(nameof(Settings_DatabasePath));
    public static string Settings_Browse => Get(nameof(Settings_Browse));
    public static string Settings_DatabaseHint => Get(nameof(Settings_DatabaseHint));
    public static string Settings_BackupSync => Get(nameof(Settings_BackupSync));
    public static string Settings_BackupSyncDesc => Get(nameof(Settings_BackupSyncDesc));
    public static string Settings_Import => Get(nameof(Settings_Import));
    public static string Settings_Export => Get(nameof(Settings_Export));
    public static string Settings_SecureBackup => Get(nameof(Settings_SecureBackup));
    public static string Settings_SecureBackupDesc => Get(nameof(Settings_SecureBackupDesc));
    public static string Settings_ImportSecure => Get(nameof(Settings_ImportSecure));
    public static string Settings_ExportSecure => Get(nameof(Settings_ExportSecure));
    public static string Settings_AutoBackup => Get(nameof(Settings_AutoBackup));
    public static string Settings_AutoBackupDesc => Get(nameof(Settings_AutoBackupDesc));
    public static string Settings_ClearBackup => Get(nameof(Settings_ClearBackup));
    public static string Settings_BackupNote => Get(nameof(Settings_BackupNote));
    public static string Settings_Security => Get(nameof(Settings_Security));
    public static string Settings_MasterPassword => Get(nameof(Settings_MasterPassword));
    public static string Settings_UseMasterPassword => Get(nameof(Settings_UseMasterPassword));
    public static string Settings_SetChange => Get(nameof(Settings_SetChange));
    public static string Settings_MasterPasswordWarning => Get(nameof(Settings_MasterPasswordWarning));
    public static string Settings_DomainCredentials => Get(nameof(Settings_DomainCredentials));
    public static string Settings_DomainCredentialsDesc => Get(nameof(Settings_DomainCredentialsDesc));
    public static string Settings_Domain => Get(nameof(Settings_Domain));
    public static string Settings_Username => Get(nameof(Settings_Username));
    public static string Settings_Password => Get(nameof(Settings_Password));
    public static string Settings_AddDomainCredential => Get(nameof(Settings_AddDomainCredential));
    public static string Settings_Snippets => Get(nameof(Settings_Snippets));
    public static string Settings_SnippetsDesc => Get(nameof(Settings_SnippetsDesc));
    public static string Settings_Name => Get(nameof(Settings_Name));
    public static string Settings_Command => Get(nameof(Settings_Command));
    public static string Settings_AddSnippet => Get(nameof(Settings_AddSnippet));
    public static string Settings_SaveSettings => Get(nameof(Settings_SaveSettings));
    public static string Settings_ClearBackupPath => Get(nameof(Settings_ClearBackupPath));

    // SftpSessionView
    public static string Sftp_Disconnect => Get(nameof(Sftp_Disconnect));
    public static string Sftp_Reconnect => Get(nameof(Sftp_Reconnect));
    public static string Sftp_Up => Get(nameof(Sftp_Up));
    public static string Sftp_Refresh => Get(nameof(Sftp_Refresh));
    public static string Sftp_UploadFile => Get(nameof(Sftp_UploadFile));
    public static string Sftp_EnterDirectory => Get(nameof(Sftp_EnterDirectory));
    public static string Sftp_Download => Get(nameof(Sftp_Download));
    public static string Sftp_Delete => Get(nameof(Sftp_Delete));
    public static string Sftp_Name => Get(nameof(Sftp_Name));
    public static string Sftp_Size => Get(nameof(Sftp_Size));
    public static string Sftp_Modified => Get(nameof(Sftp_Modified));
    public static string Sftp_Permissions => Get(nameof(Sftp_Permissions));

    // SshKeyGeneratorDialog
    public static string KeyGen_Title => Get(nameof(KeyGen_Title));
    public static string KeyGen_GenerateKeys => Get(nameof(KeyGen_GenerateKeys));
    public static string KeyGen_KeyType => Get(nameof(KeyGen_KeyType));
    public static string KeyGen_RSA2048 => Get(nameof(KeyGen_RSA2048));
    public static string KeyGen_RSA4096 => Get(nameof(KeyGen_RSA4096));
    public static string KeyGen_ED25519 => Get(nameof(KeyGen_ED25519));
    public static string KeyGen_ECDSA256 => Get(nameof(KeyGen_ECDSA256));
    public static string KeyGen_ECDSA384 => Get(nameof(KeyGen_ECDSA384));
    public static string KeyGen_ECDSA521 => Get(nameof(KeyGen_ECDSA521));
    public static string KeyGen_Passphrase => Get(nameof(KeyGen_Passphrase));
    public static string KeyGen_SaveToDirectory => Get(nameof(KeyGen_SaveToDirectory));
    public static string KeyGen_KeyName => Get(nameof(KeyGen_KeyName));
    public static string KeyGen_Cancel => Get(nameof(KeyGen_Cancel));
    public static string KeyGen_GenerateSave => Get(nameof(KeyGen_GenerateSave));

    // SshSessionView
    public static string Ssh_Disconnect => Get(nameof(Ssh_Disconnect));
    public static string Ssh_Reconnect => Get(nameof(Ssh_Reconnect));
    public static string Ssh_Clear => Get(nameof(Ssh_Clear));
    public static string Ssh_ClearTooltip => Get(nameof(Ssh_ClearTooltip));
    public static string Ssh_Snippets => Get(nameof(Ssh_Snippets));
    public static string Ssh_CopyPasteHint => Get(nameof(Ssh_CopyPasteHint));

    // WebSessionView
    public static string Web_BrowserError => Get(nameof(Web_BrowserError));
    public static string Web_BrowserErrorTitle => Get(nameof(Web_BrowserErrorTitle));

    // MainWindow additional
    public static string Main_Shortcuts => Get(nameof(Main_Shortcuts));
}
