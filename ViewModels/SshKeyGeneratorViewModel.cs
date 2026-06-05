using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Renci.SshNet;
using SshNet.Keygen;
using SshNet.Keygen.Extensions;

namespace RemoteManager.ViewModels;

public partial class SshKeyGeneratorViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedKeyTypeIndex = 2; // ED25519 by default

    [ObservableProperty]
    private string _passphrase = "";

    [ObservableProperty]
    private string _saveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\.ssh";

    [ObservableProperty]
    private string _keyName = "id_ed25519";

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private void BrowseDirectory()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select directory to save SSH keys",
            UseDescriptionForTitle = true,
            SelectedPath = SaveDirectory
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SaveDirectory = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void SaveKeys()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(SaveDirectory))
        {
            ErrorMessage = "Save directory is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(KeyName))
        {
            ErrorMessage = "Key name is required.";
            return;
        }

        try
        {
            if (!Directory.Exists(SaveDirectory))
            {
                Directory.CreateDirectory(SaveDirectory);
            }

            var privateKeyPath = Path.Combine(SaveDirectory, KeyName);
            var publicKeyPath = privateKeyPath + ".pub";

            if (File.Exists(privateKeyPath))
            {
                ErrorMessage = $"File '{privateKeyPath}' already exists.";
                return;
            }

            var info = new SshKeyGenerateInfo { KeyType = SshKeyType.ED25519 };
            switch (SelectedKeyTypeIndex)
            {
                case 0: info = new SshKeyGenerateInfo { KeyType = SshKeyType.RSA, KeyLength = 2048 }; break;
                case 1: info = new SshKeyGenerateInfo { KeyType = SshKeyType.RSA, KeyLength = 4096 }; break;
                case 2: info = new SshKeyGenerateInfo { KeyType = SshKeyType.ED25519 }; break;
                case 3: info = new SshKeyGenerateInfo { KeyType = SshKeyType.ECDSA, KeyLength = 256 }; break;
                case 4: info = new SshKeyGenerateInfo { KeyType = SshKeyType.ECDSA, KeyLength = 384 }; break;
                case 5: info = new SshKeyGenerateInfo { KeyType = SshKeyType.ECDSA, KeyLength = 521 }; break;
            }

            var key = SshKey.Generate(info);

            var privateKeyData = string.IsNullOrEmpty(Passphrase) 
                ? key.ToOpenSshFormat() 
                : key.ToOpenSshFormat(Passphrase);
            var publicKeyData = key.ToOpenSshPublicFormat();

            File.WriteAllText(privateKeyPath, privateKeyData);
            File.WriteAllText(publicKeyPath, publicKeyData);
            
            // set permissions on unix? No, this is windows WPF app.
            
            ErrorMessage = string.Empty; // Success
        }
        catch (Exception ex)
        {
            ErrorMessage = "Failed to generate key: " + ex.Message;
        }
    }
}
