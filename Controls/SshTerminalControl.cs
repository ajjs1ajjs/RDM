using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RemoteManager.Models;
using RemoteManager.Services;
using Renci.SshNet;

namespace RemoteManager.Controls;

public partial class SshTerminalControl : TerminalControl
{
    private static readonly Regex AnsiSequenceRegex = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex OscSequenceRegex = new(@"\x1B\].*?(\x07|\x1B\\)", RegexOptions.Compiled);
    private static readonly Regex CursorSequenceRegex = new(@"\x1B\[(?<count>\d*)(?<cmd>[CDHKJ])", RegexOptions.Compiled);
    private static readonly Regex SgrSequenceRegex = new(@"\x1B\[(?<params>[0-9;]*)m", RegexOptions.Compiled);
    private static readonly Regex PromptRegex = new(@"^(?<prompt>[a-zA-Z0-9_\-\.]+@[a-zA-Z0-9_\-\.]+[:~][^$#]*[$#]\s*)(?<rest>.*)$", RegexOptions.Compiled);

    private readonly object _writeLock = new();
    private readonly object _screenLock = new();
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
    private uint _currentColumns = 120;
    private uint _currentRows = 40;
    private readonly List<byte> _charColors = new();
    private byte _currentForegroundColor = 0;

    public event EventHandler<string>? ConnectionClosed;

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
        SizeChanged -= OnSizeChanged;

        _output = GetTemplateChild("PART_OutputText") as TextBlock;
        _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        if (_output == null)
            return;

        Focusable = true;
        _output.Inlines.Clear();
        _screen.Clear();
        _charColors.Clear();
        _currentForegroundColor = 0;
        _lineStartIndex = 0;
        _cursorIndex = 0;
        PreviewKeyDown += OnPreviewKeyDown;
        TextInput += OnTextInput;
        PreviewMouseDown += OnPreviewMouseDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        SizeChanged += OnSizeChanged;
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

            var size = CalculateTerminalSize(settings);
            _currentColumns = size.Columns;
            _currentRows = size.Rows;
            _shell = _client.CreateShellStream("xterm", _currentColumns, _currentRows, 900, 600, 4096);
            _shell.Closed += OnShellClosed;

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

        if (_shell != null)
        {
            try { _shell.Closed -= OnShellClosed; } catch { }
        }

        var shell = _shell;
        var client = _client;
        _shell = null;
        _client = null;

        _ = Task.Run(() =>
        {
            try { shell?.Close(); } catch (Exception ex) { Log.Debug("SSH shell close error: " + ex.Message); }
            try { client?.Disconnect(); } catch (Exception ex) { Log.Debug("SSH disconnect error: " + ex.Message); }
            try { client?.Dispose(); } catch (Exception ex) { Log.Debug("SSH client dispose error: " + ex.Message); }
        });

        AppendOutput("Disconnected.\r\n");
        ConnectionClosed?.Invoke(this, "Manual");
    }

    private void OnShellClosed(object? sender, EventArgs e)
    {
        if (!_disconnectRequested)
        {
            _readCts?.Cancel();
        }
    }

    public override void Clear()
    {
        _output?.Dispatcher.BeginInvoke(() =>
        {
            lock (_screenLock)
            {
                _screen.Clear();
                _charColors.Clear();
                _lineStartIndex = 0;
                _cursorIndex = 0;
                _currentForegroundColor = 0;
            }
            _output.Inlines.Clear();
        });
    }

    public override void SendText(string text)
    {
        WriteToShell(text);
    }

    private async Task ReadOutputLoopAsync(CancellationToken token)
    {
        bool hasError = false;
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
                hasError = true;
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
            _ = Dispatcher.BeginInvoke(() => ConnectionClosed?.Invoke(this, hasError ? "Error" : "Clean"));
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
        lock (_screenLock)
        {
            InternalAppendTerminalText(text);
        }
    }

    private void InternalAppendTerminalText(string text)
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

        for (var i = 0; i < cleaned.Length; i++)
        {
            var c = cleaned[i];
            if (c == '\x1B')
            {
                if (i + 1 < cleaned.Length && cleaned[i + 1] == '[')
                {
                    var endIdx = -1;
                    for (int j = i + 2; j < cleaned.Length; j++)
                    {
                        var ch = cleaned[j];
                        if (ch >= 0x40 && ch <= 0x7E)
                        {
                            endIdx = j;
                            break;
                        }
                    }

                    if (endIdx >= 0)
                    {
                        var cmdChar = cleaned[endIdx];
                        if (cmdChar == 'm')
                        {
                            var sgrParams = cleaned[(i + 2)..endIdx];
                            UpdateCurrentForegroundColor(sgrParams);
                        }
                        i = endIdx;
                        continue;
                    }
                }
                else if (i + 1 < cleaned.Length)
                {
                    i++;
                    continue;
                }
            }

            switch (c)
            {
                case '\r':
                    _cursorIndex = _lineStartIndex;
                    if (i + 1 < cleaned.Length && cleaned[i + 1] == '\n')
                    {
                        _screen.Append(Environment.NewLine);
                        _charColors.Add(0);
                        _charColors.Add(0);
                        _lineStartIndex = _screen.Length;
                        _cursorIndex = _screen.Length;
                        i++;
                    }
                    break;
                case '\n':
                    _screen.Append(Environment.NewLine);
                    _charColors.Add(0);
                    _charColors.Add(0);
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

    private void UpdateCurrentForegroundColor(string sgrParams)
    {
        if (string.IsNullOrEmpty(sgrParams))
        {
            _currentForegroundColor = 0;
            return;
        }
        var parts = sgrParams.Split(';');
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                _currentForegroundColor = 0;
                continue;
            }
            if (int.TryParse(part, out int code))
            {
                if (code == 0)
                {
                    _currentForegroundColor = 0;
                }
                else if ((code >= 30 && code <= 37) || (code >= 90 && code <= 97) || code == 39)
                {
                    _currentForegroundColor = (byte)code;
                }
            }
        }
    }

    private static System.Windows.Media.Brush GetAnsiBrush(byte code)
    {
        return code switch
        {
            30 => System.Windows.Media.Brushes.DarkGray,
            31 => System.Windows.Media.Brushes.IndianRed,
            32 => System.Windows.Media.Brushes.LightGreen,
            33 => System.Windows.Media.Brushes.Gold,
            34 => System.Windows.Media.Brushes.LightSkyBlue,
            35 => System.Windows.Media.Brushes.MediumOrchid,
            36 => System.Windows.Media.Brushes.LightCyan,
            37 => System.Windows.Media.Brushes.White,
            90 => System.Windows.Media.Brushes.Gray,
            91 => System.Windows.Media.Brushes.Red,
            92 => System.Windows.Media.Brushes.LimeGreen,
            93 => System.Windows.Media.Brushes.Yellow,
            94 => System.Windows.Media.Brushes.DodgerBlue,
            95 => System.Windows.Media.Brushes.Magenta,
            96 => System.Windows.Media.Brushes.Cyan,
            97 => System.Windows.Media.Brushes.White,
            _ => System.Windows.Media.Brushes.Gainsboro
        };
    }

    private void PutChar(char c)
    {
        if (_cursorIndex < _screen.Length)
        {
            _screen[_cursorIndex] = c;
            _charColors[_cursorIndex] = _currentForegroundColor;
        }
        else
        {
            _screen.Append(c);
            _charColors.Add(_currentForegroundColor);
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
        {
            var count = _screen.Length - _cursorIndex;
            _screen.Remove(_cursorIndex, count);
            _charColors.RemoveRange(_cursorIndex, count);
        }
    }

    private void EraseCurrentLine()
    {
        if (_lineStartIndex < _screen.Length)
        {
            var count = _screen.Length - _lineStartIndex;
            _screen.Remove(_lineStartIndex, count);
            _charColors.RemoveRange(_lineStartIndex, count);
        }

        _cursorIndex = _lineStartIndex;
    }

    private void ClearScreenBuffer()
    {
        _screen.Clear();
        _charColors.Clear();
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
                try { shell.Write(text); } catch (Exception ex) { Log.Debug("SSH write error: " + ex.Message); }
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

        string fullText;
        int startCharIdx;
        byte[]? colors;

        lock (_screenLock)
        {
            fullText = _screen.ToString();
            var lines = fullText.Split(["\r\n", "\n"], StringSplitOptions.None);

            if (lines.Length > MaxScreenLines)
            {
                var skip = lines.Length - MaxScreenLines;
                var charIdx = 0;
                var lineCount = 0;
                while (charIdx < fullText.Length && lineCount < skip)
                {
                    if (fullText[charIdx] == '\n')
                        lineCount++;
                    charIdx++;
                }

                _screen.Remove(0, charIdx);
                _charColors.RemoveRange(0, charIdx);
                _lineStartIndex = Math.Max(0, _lineStartIndex - charIdx);
                _cursorIndex = Math.Max(0, _cursorIndex - charIdx);

                fullText = _screen.ToString();
                lines = fullText.Split(["\r\n", "\n"], StringSplitOptions.None);
            }

            var linesToRenderCount = Math.Min(lines.Length, MaxRenderedLines);
            var skipLines = lines.Length - linesToRenderCount;
            startCharIdx = 0;
            var skipLineCount = 0;
            while (startCharIdx < fullText.Length && skipLineCount < skipLines)
            {
                if (fullText[startCharIdx] == '\n')
                    skipLineCount++;
                startCharIdx++;
            }

            colors = _charColors.ToArray();
        }

        _output.Inlines.Clear();

        var currentRunText = new StringBuilder();
        byte currentRunColor = 0;

        for (int i = startCharIdx; i < fullText.Length; i++)
        {
            var ch = fullText[i];
            byte color = i < colors.Length ? colors[i] : (byte)0;

            if (ch == '\r')
            {
                continue;
            }

            if (ch == '\n')
            {
                if (currentRunText.Length > 0)
                {
                    _output.Inlines.Add(new Run(currentRunText.ToString()) { Foreground = GetAnsiBrush(currentRunColor) });
                    currentRunText.Clear();
                }
                _output.Inlines.Add(new LineBreak());
                continue;
            }

            if (color != currentRunColor && currentRunText.Length > 0)
            {
                _output.Inlines.Add(new Run(currentRunText.ToString()) { Foreground = GetAnsiBrush(currentRunColor) });
                currentRunText.Clear();
            }

            currentRunColor = color;
            currentRunText.Append(ch);
        }

        if (currentRunText.Length > 0)
        {
            _output.Inlines.Add(new Run(currentRunText.ToString()) { Foreground = GetAnsiBrush(currentRunColor) });
        }

        _output.Inlines.Add(new Run("█") { Foreground = System.Windows.Media.Brushes.LimeGreen });
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
                try { shell.Write(bytes, 0, bytes.Length); } catch (Exception ex) { Log.Debug("SSH byte write error: " + ex.Message); }
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
        catch (Exception ex)
        {
            Log.Debug("Clipboard access error: " + ex.Message);
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTerminalSize();
    }

    private void UpdateTerminalSize()
    {
        if (_shell == null)
            return;

        var (cols, rows) = CalculateTerminalSize();
        if (cols == _currentColumns && rows == _currentRows)
            return;

        _currentColumns = cols;
        _currentRows = rows;

        _shell.Write("\x1B[8;" + rows + ";" + cols + "t");
    }

    private (uint Columns, uint Rows) CalculateTerminalSize(SSHSettings? settings = null)
    {
        uint defaultCols = (uint)(settings?.TerminalColumns ?? 120);
        uint defaultRows = (uint)(settings?.TerminalRows ?? 40);

        if (_scrollViewer == null || _output == null)
            return (defaultCols, defaultRows);

        double width = ActualWidth;
        double height = ActualHeight;

        if (_scrollViewer.Padding != default)
        {
            width -= (_scrollViewer.Padding.Left + _scrollViewer.Padding.Right);
            height -= (_scrollViewer.Padding.Top + _scrollViewer.Padding.Bottom);
        }
        else
        {
            width -= 20;
            height -= 20;
        }

        if (width <= 0 || height <= 0)
            return (defaultCols, defaultRows);

        var typeface = new Typeface(_output.FontFamily, _output.FontStyle, _output.FontWeight, _output.FontStretch);
        var dpi = VisualTreeHelper.GetDpi(this);
        
        var formattedText = new FormattedText(
            "W",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            _output.FontSize,
            System.Windows.Media.Brushes.Black,
            dpi.PixelsPerDip);

        double charWidth = formattedText.Width;
        double charHeight = formattedText.Height;

        if (charWidth <= 0 || charHeight <= 0)
            return (defaultCols, defaultRows);

        uint cols = (uint)Math.Max(20, Math.Floor(width / charWidth));
        uint rows = (uint)Math.Max(5, Math.Floor(height / charHeight));

        return (cols, rows);
    }
}
