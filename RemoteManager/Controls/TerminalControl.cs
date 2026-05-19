using System.Windows.Controls;

namespace RemoteManager.Controls;

public abstract partial class TerminalControl : ContentControl
{
    public virtual void Disconnect() { }
    public virtual void Clear() { }
}
