using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RemoteManager.Models;
using Renci.SshNet;

namespace RemoteManager.Controls;

public partial class SshTerminalControl : TerminalControl
{
    private static readonly Regex AnsiSequenceRegex = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex OscSequenceRegex = new(@"\x1B\].*?(\x07|\x1B\\)", RegexOptions.Compiled);
    private static readonly Regex CursorSequenceRegex = new(@"\x1B\[(?<count>\d*)(?<cmd>[CDHKJ])", RegexOptions.Compiled);
    private static readonly Regex SgrSequenceRegex = new(@"\x1B\[(?<params>[0-9;]*)m", RegexOptions.Compiled);

    private readonly object _writeLock = new();
    private CancellationTokenSource? _readCts;
    private SshClient? _client;
    private ShellStream? _shell;
    private TextBlock? _output;
    private ScrollViewer? _scrollViewer;
    private readonly StringBuilder _screen = new();
    private int _lineStartIndex;
    private int _cursorIndex;
    private string _lastInput = string.Empty;
    private bool _disconnectRequested;
    private string _selectedText = string.Empty;
    private int _selectionStart = -1;
    private int _selectionEnd = -1;

    public event EventHandler? ConnectionClosed;

    static SshTerminalControl() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SshTerminalControl), new FrameworkPropertyMetadata(typeof(SshTerminalControl)));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        PreviewKeyDown -= OnPreviewKeyDown;
        TextInput -= OnTextInput;
        PreviewMouseDown -= OnPreviewMouseDown;
        MouseLeftButtonDown -= OnMouseLeftButtonDown;
        MouseLeftButtonUp -= OnMouseLeftButtonUp;

        _output = GetTemplateChild("PART_OutputText") as TextBlock;
        _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        if (_output == null)
            return;

        Focusable = true;
        _output.Inlines.Clear();
        _screen.Clear();
        _lineStartIndex = 0;
        _cursorIndex = 0;
        PreviewKeyDown += OnPreviewKeyDown;
        TextInput += OnTextInput;
        PreviewMouseDown += OnPreviewMouseDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    public async Task<bool> ConnectAsync(string host, int port, string user, string pass, SSHSettings? settings)
    {
        try
        {
            var connectionInfo = settings?.AuthType == SshAuthType.Key && !string.IsNullOrWhiteSpace(settings.PrivateKeyPath)
                ? new ConnectionInfo(host, port, user, new PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(settings.PrivateKeyPath, settings.PrivateKeyPassphrase ?? pass)))
                : new ConnectionInfo(host, port, user, new PasswordAuthenticationMethod(user, pass));

            connectionInfo.Timeout = TimeSpan.FromSeconds(15);

            _client = new SshClient(connectionInfo);
            await Task.Run(() => _client.Connect());
            _disconnectRequested = false;

            var columns = settings?.TerminalColumns ?? 120;
            var rows = settings?.TerminalRows ?? 40;
            _shell = _client.CreateShellStream("xterm", (uint)columns, (uint)rows, 900, 600, 4096);

            _readCts = new CancellationTokenSource();
            _ = Task.Run(() => ReadOutputLoopAsync(_readCts.Token));

            _ = Dispatcher.BeginInvoke(() => Focus());
            AppendOutput($"Connected to {host}.\r\n");
            return true;
        }
        catch (Exception ex)
        {
            AppendOutput($"Failed: {ex.Message}\r\n");
            return false;
        }
    }

    public override void Disconnect()
    {
        _disconnectRequested = true;
        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;

        var shell = _shell;
        var client = _client;
        _shell = null;
        _client = null;

        _ = Task.Run(() =>
        {
            try { shell?.Close(); } catch { }
            try { client?.Disconnect(); } catch { }
            try { client?.Dispose(); } catch { }
        });

        AppendOutput("Disconnected.\r\n");
        ConnectionClosed?.Invoke(this, EventArgs.Empty);
    }

    public override void Clear()
    {
        _output?.Dispatcher.BeginInvoke(() =>
        {
            _screen.Clear();
            _lineStartIndex = 0;
            _cursorIndex = 0;
            _output.Inlines.Clear();
        });
    }

    private async Task ReadOutputLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var shell = _shell;
            var client = _client;
            if (shell == null || client == null || !client.IsConnected || !shell.CanRead)
                break;

            try
            {
                var buffer = new StringBuilder();
                while (!token.IsCancellationRequested && shell.DataAvailable)
                {
                    var data = shell.Read();
                    if (!string.IsNullOrEmpty(data))
                        buffer.Append(data);

                    if (buffer.Length >= 8192)
                        break;
                }

                if (buffer.Length > 0)
                    AppendOutput(buffer.ToString());
            }
            catch (Exception ex)
            {
                AppendOutput($"\r\n[Connection error: {ex.Message}]\r\n");
                break;
            }

            try
            {
                await Task.Delay(25, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        if (!_disconnectRequested)
            _ = Dispatcher.BeginInvoke(() => ConnectionClosed?.Invoke(this, EventArgs.Empty));
    }

    private void AppendOutput(string text)
    {
        var output = _output;
        if (output == null)
            return;

        output.Dispatcher.BeginInvoke(() =>
        {
            AppendTerminalText(text);
            RenderScreen();
            _scrollViewer?.ScrollToEnd();
        });
    }

    private void AppendTerminalText(string text)
    {
        if (!string.IsNullOrEmpty(_lastInput) && text.StartsWith(_lastInput, StringComparison.Ordinal))
        {
            text = text[_lastInput.Length..];
            _lastInput = string.Empty;
        }

        var cleaned = OscSequenceRegex.Replace(text, string.Empty);

        cleaned = CursorSequenceRegex.Replace(cleaned, match =>
        {
            var countText = match.Groups["count"].Value;
            var count = string.IsNullOrEmpty(countText) ? 1 : int.Parse(countText);

            switch (match.Groups["cmd"].Value)
            {
                case "C":
                    MoveCursorRight(count);
                    break;
                case "D":
                    MoveCursorLeft(count);
                    break;
                case "K":
                    if (count == 2)
                        EraseCurrentLine();
                    else
                        EraseFromCursorToEndOfLine();
                    break;
                case "H":
                    _cursorIndex = _lineStartIndex;
                    break;
                case "J":
                    if (count == 2)
                        ClearScreenBuffer();
                    break;
            }

            return string.Empty;
        });

        cleaned = SgrSequenceRegex.Replace(cleaned, string.Empty);

        cleaned = AnsiSequenceRegex.Replace(cleaned, string.Empty);

        for (var i = 0; i < cleaned.Length; i++)
        {
            var c = cleaned[i];
            switch (c)
            {
                case '\r':
                    if (i + 1 < cleaned.Length && cleaned[i + 1] == '\n')
                        break;
                    _cursorIndex = _lineStartIndex;
                    break;
                case '\n':
                    _screen.Append(Environment.NewLine);
                    _lineStartIndex = _screen.Length;
                    _cursorIndex = _screen.Length;
                    break;
                case '\b':
                case '\x7f':
                    MoveCursorLeft(1);
                    break;
                case '\0':
                    break;
                default:
                    if (!char.IsControl(c))
                        PutChar(c);
                    break;
            }
        }
    }

    private void PutChar(char c)
    {
        if (_cursorIndex < _screen.Length)
        {
            _screen[_cursorIndex] = c;
        }
        else
        {
            _screen.Append(c);
        }

        _cursorIndex++;
    }

    private void MoveCursorLeft(int count)
    {
        _cursorIndex = Math.Max(_lineStartIndex, _cursorIndex - count);
    }

    private void MoveCursorRight(int count)
    {
        _cursorIndex = Math.Min(_screen.Length, _cursorIndex + count);
    }

    private void EraseFromCursorToEndOfLine()
    {
        if (_cursorIndex < _screen.Length)
            _screen.Remove(_cursorIndex, _screen.Length - _cursorIndex);
    }

    private void EraseCurrentLine()
    {
        if (_lineStartIndex < _screen.Length)
            _screen.Remove(_lineStartIndex, _screen.Length - _lineStartIndex);

        _cursorIndex = _lineStartIndex;
    }

    private void ClearScreenBuffer()
    {
        _screen.Clear();
        _lineStartIndex = 0;
        _cursorIndex = 0;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_shell == null || !_shell.CanWrite)
            return;

        var sequence = e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\b",
            Key.Tab => "\t",
            Key.Space => " ",
            Key.Up => "\x1b[A",
            Key.Down => "\x1b[B",
            Key.Right => "\x1b[C",
            Key.Left => "\x1b[D",
            Key.Escape => "\x1b",
            Key.Delete => "\x1b[3~",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            _ => null
        };

        if (sequence != null)
        {
            if (e.Key is Key.Back)
                ApplyLocalBackspace();
            else if (e.Key is Key.Space)
                ApplyLocalInput(" ");
            else if (e.Key is Key.Enter)
                ApplyLocalInput(Environment.NewLine, trackRemoteEcho: false);
            else if (e.Key is Key.Tab)
                ApplyLocalInput("    ", trackRemoteEcho: false);

            WriteToShell(sequence);
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            return;

        var isCtrlShift = (Keyboard.Modifiers & ModifierKeys.Control) != 0 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (isCtrlShift && e.Key == Key.C)
        {
            CopySelectedText();
            e.Handled = true;
            return;
        }

        if (isCtrlShift && e.Key == Key.V)
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            if (e.Key == Key.L)
                Clear();
            else if (e.Key == Key.C)
                ApplyLocalInput("^C" + Environment.NewLine, trackRemoteEcho: false);

            WriteToShell([(byte)(e.Key - Key.A + 1)]);
            e.Handled = true;
        }
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_shell == null || !_shell.CanWrite)
            return;

        ApplyLocalInput(e.Text);
        WriteToShell(e.Text);
        e.Handled = true;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        e.Handled = true;
    }

    private void WriteToShell(string text)
    {
        var shell = _shell;
        if (shell == null || !shell.CanWrite)
            return;

        _ = Task.Run(() =>
        {
            lock (_writeLock)
            {
                try { shell.Write(text); } catch { }
            }
        });
    }

    private void ApplyLocalInput(string text, bool trackRemoteEcho = true)
    {
        if (trackRemoteEcho)
            _lastInput += text;

        _output?.Dispatcher.BeginInvoke(() =>
        {
            AppendTerminalText(text);
            if (_output == null)
                return;

            RenderScreen();
            _scrollViewer?.ScrollToEnd();
        });
    }

    private void ApplyLocalBackspace()
    {
        _lastInput = string.Empty;
        _output?.Dispatcher.BeginInvoke(() =>
        {
            MoveCursorLeft(1);
            EraseFromCursorToEndOfLine();
            if (_output == null)
                return;

            RenderScreen();
            _scrollViewer?.ScrollToEnd();
        });
    }

    private const int MaxScreenLines = 5000;
    private const int MaxRenderedLines = 500;

    private void RenderScreen()
    {
        if (_output == null)
            return;

        var fullText = _screen.ToString();
        var lines = fullText.Split(["\r\n", "\n"], StringSplitOptions.None);

        if (lines.Length > MaxScreenLines)
        {
            var skip = lines.Length - MaxScreenLines;
            _screen.Clear();
            _screen.Append(string.Join(Environment.NewLine, lines.Skip(skip)));
            _lineStartIndex = 0;
            _cursorIndex = _screen.Length;
            lines = _screen.ToString().Split(["\r\n", "\n"], StringSplitOptions.None);
        }

        var linesToRender = lines;
        if (lines.Length > MaxRenderedLines)
        {
            linesToRender = lines.Skip(lines.Length - MaxRenderedLines).ToArray();
        }

        _output.Inlines.Clear();

        for (var i = 0; i < linesToRender.Length; i++)
        {
            RenderLine(linesToRender[i]);
            if (i < linesToRender.Length - 1)
                _output.Inlines.Add(new LineBreak());
        }

        _output.Inlines.Add(new Run("█") { Foreground = System.Windows.Media.Brushes.LimeGreen });
    }

    private void RenderLine(string line)
    {
        if (_output == null)
            return;

        if (line.StartsWith("Connected to ", StringComparison.Ordinal) || line.StartsWith("sa@", StringComparison.Ordinal))
        {
            var promptEnd = line.IndexOf("$ ", StringComparison.Ordinal);
            if (promptEnd >= 0)
            {
                _output.Inlines.Add(new Run(line[..(promptEnd + 2)]) { Foreground = System.Windows.Media.Brushes.LimeGreen, FontWeight = FontWeights.Bold });
                _output.Inlines.Add(new Run(line[(promptEnd + 2)..]) { Foreground = System.Windows.Media.Brushes.White });
                return;
            }

            _output.Inlines.Add(new Run(line) { Foreground = System.Windows.Media.Brushes.LimeGreen, FontWeight = FontWeights.Bold });
            return;
        }

        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            _output.Inlines.Add(new Run(line) { Foreground = System.Windows.Media.Brushes.IndianRed });
            return;
        }

        if (line.Contains("active (running)", StringComparison.OrdinalIgnoreCase) || line.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            _output.Inlines.Add(new Run(line) { Foreground = System.Windows.Media.Brushes.LightGreen });
            return;
        }

        _output.Inlines.Add(new Run(line) { Foreground = System.Windows.Media.Brushes.Gainsboro });
    }

    private void WriteToShell(byte[] bytes)
    {
        var shell = _shell;
        if (shell == null || !shell.CanWrite)
            return;

        _ = Task.Run(() =>
        {
            lock (_writeLock)
            {
                try { shell.Write(bytes, 0, bytes.Length); } catch { }
            }
        });
    }

    private void CopySelectedText()
    {
        if (!string.IsNullOrEmpty(_selectedText))
        {
            System.Windows.Clipboard.SetText(_selectedText);
        }
    }

    private void PasteFromClipboard()
    {
        try
        {
            var text = System.Windows.Clipboard.GetText();
            if (!string.IsNullOrEmpty(text))
            {
                WriteToShell(text);
            }
        }
        catch
        {
            // Clipboard access may fail
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_output == null) return;

        var pos = e.GetPosition(_output);
        var hit = VisualTreeHelper.HitTest(_output, pos);
        if (hit?.VisualHit is Run run)
        {
            _selectionStart = _screen.ToString().IndexOf(run.Text, StringComparison.Ordinal);
            if (_selectionStart >= 0)
                _selectionEnd = _selectionStart + run.Text.Length;
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
        {
            var fullText = _screen.ToString();
            if (_selectionEnd <= fullText.Length)
            {
                _selectedText = fullText[_selectionStart.._selectionEnd].Trim();
            }
        }
    }
}
