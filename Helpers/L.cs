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
}
