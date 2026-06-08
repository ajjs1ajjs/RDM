using CommunityToolkit.Mvvm.ComponentModel;
using RemoteManager.Helpers;
using RemoteManager.ViewModels;

namespace RemoteManager.ViewModels;

public partial class SettingsTabViewModel : SessionTabViewModel
{
    public SettingsViewModel? Settings { get; set; }

    public SettingsTabViewModel(SettingsViewModel settings)
    {
        if (settings != null)
            Settings = settings;
        Header = L.Tab_Settings;
    }
}
