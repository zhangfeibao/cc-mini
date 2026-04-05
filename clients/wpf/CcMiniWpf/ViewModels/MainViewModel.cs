using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CcMiniWpf.Models;
using CcMiniWpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CcMiniWpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CcMiniClient _client = new();
    private CancellationTokenSource? _submitCts;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _currentProvider = "anthropic";
    [ObservableProperty] private string _currentModel = "...";
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private bool _autoApprove;
    [ObservableProperty] private string _workingDirectory = Directory.GetCurrentDirectory();

    // ── 命令补全 ──
    public ObservableCollection<SlashCommand> FilteredCommands { get; } = [];
    [ObservableProperty] private bool _isCommandPopupOpen;
    [ObservableProperty] private int _selectedCommandIndex = -1;

    public MainViewModel()
    {
        _client.PermissionRequested += OnPermissionRequested;
        _client.ProcessExited += msg =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                StatusText = msg;
                IsProcessing = false;
            });
        };
    }

    /// <summary>初始化并启动 cc-mini 进程。</summary>
    public async Task InitializeAsync()
    {
        await StartClientAsync();
    }

    private async Task StartClientAsync()
    {
        try
        {
            StatusText = "正在启动 cc-mini...";
            _client.ExtraArgs = AutoApprove ? "--auto-approve" : "";
            _client.WorkingDirectory = WorkingDirectory;
            await _client.StartAsync();
            StatusText = $"就绪 · {WorkingDirectory}";
        }
        catch (Exception ex)
        {
            StatusText = $"启动失败: {ex.Message}";
        }
    }

    // ── 自动批准切换 ──

    partial void OnAutoApproveChanged(bool value)
    {
        _ = RestartClientAsync();
    }

    // ── 工作目录切换 ──

    [RelayCommand]
    private async Task SelectWorkingDirectoryAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择工作目录",
            InitialDirectory = WorkingDirectory,
        };

        if (dialog.ShowDialog() == true)
        {
            WorkingDirectory = dialog.FolderName;
            Messages.Clear();
            CostText = "";
            await RestartClientAsync();
        }
    }

    private async Task RestartClientAsync()
    {
        _submitCts?.Cancel();
        IsProcessing = false;
        _client.Stop();
        await StartClientAsync();
    }

    // ── 命令补全逻辑 ──

    partial void OnInputTextChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
        UpdateCommandPopup(value);
    }

    private void UpdateCommandPopup(string text)
    {
        // 仅当输入以 "/" 开头且是第一行时触发
        var firstLine = text.Split('\n')[0];
        if (firstLine.StartsWith('/') && !firstLine.Contains(' '))
        {
            var prefix = firstLine[1..].ToLowerInvariant();
            FilteredCommands.Clear();
            foreach (var cmd in SlashCommand.BuiltIn)
            {
                if (string.IsNullOrEmpty(prefix) || cmd.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    FilteredCommands.Add(cmd);
            }
            IsCommandPopupOpen = FilteredCommands.Count > 0;
            SelectedCommandIndex = IsCommandPopupOpen ? 0 : -1;
        }
        else
        {
            CloseCommandPopup();
        }
    }

    /// <summary>应用选中的命令补全。</summary>
    public void ApplySelectedCommand()
    {
        if (SelectedCommandIndex < 0 || SelectedCommandIndex >= FilteredCommands.Count) return;
        var cmd = FilteredCommands[SelectedCommandIndex];
        // 替换为命令文本，保留光标在末尾可以继续输入参数
        InputText = cmd.DisplayName + (cmd.Args is not null ? " " : "");
        CloseCommandPopup();
    }

    public void CloseCommandPopup()
    {
        IsCommandPopupOpen = false;
        SelectedCommandIndex = -1;
        FilteredCommands.Clear();
    }

    /// <summary>命令列表中向上/向下选择。返回 true 表示已处理。</summary>
    public bool HandleCommandNavigation(Key key)
    {
        if (!IsCommandPopupOpen || FilteredCommands.Count == 0) return false;

        switch (key)
        {
            case Key.Up:
                SelectedCommandIndex = SelectedCommandIndex <= 0
                    ? FilteredCommands.Count - 1
                    : SelectedCommandIndex - 1;
                return true;

            case Key.Down:
                SelectedCommandIndex = SelectedCommandIndex >= FilteredCommands.Count - 1
                    ? 0
                    : SelectedCommandIndex + 1;
                return true;

            case Key.Tab:
            case Key.Enter:
                ApplySelectedCommand();
                return true;

            case Key.Escape:
                CloseCommandPopup();
                return true;
        }
        return false;
    }

    // ── 发送消息 ──

    partial void OnIsProcessingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        CloseCommandPopup();

        Messages.Add(new ChatMessage { Role = MessageRole.User, Text = text, Status = MessageStatus.Complete });
        InputText = "";
        IsProcessing = true;

        var assistantMsg = new ChatMessage { Role = MessageRole.Assistant, Status = MessageStatus.Streaming };
        Messages.Add(assistantMsg);

        _submitCts = new CancellationTokenSource();
        ToolCallInfo? currentToolCall = null;

        try
        {
            await foreach (var ev in _client.SubmitAsync(text, _submitCts.Token))
            {
                switch (ev.Event)
                {
                    case "text":
                        assistantMsg.AppendText(ev.GetString("chunk"));
                        break;

                    case "tool_call":
                        currentToolCall = new ToolCallInfo
                        {
                            Name = ev.GetString("name"),
                            InputSummary = ToolCallInfo.SummarizeInput(ev.GetElement("input")),
                            Activity = ev.GetString("activity"),
                            Status = ToolCallStatus.Pending,
                        };
                        assistantMsg.ToolCalls.Add(currentToolCall);
                        break;

                    case "tool_executing":
                        if (currentToolCall is not null)
                            currentToolCall.Status = ToolCallStatus.Executing;
                        break;

                    case "tool_result":
                        if (currentToolCall is not null)
                        {
                            currentToolCall.ResultContent = ev.GetString("content");
                            currentToolCall.IsError = ev.GetBool("is_error");
                            currentToolCall.Status = ToolCallStatus.Complete;
                        }
                        currentToolCall = null;
                        break;

                    case "usage":
                        var input = ev.GetInt("input_tokens");
                        var output = ev.GetInt("output_tokens");
                        CostText = $"↑{input} ↓{output}";
                        break;

                    case "error":
                        var errorMsg = ev.GetString("message");
                        if (!string.IsNullOrEmpty(errorMsg))
                        {
                            assistantMsg.AppendText($"\n[错误] {errorMsg}");
                            assistantMsg.Status = MessageStatus.Error;
                        }
                        break;

                    case "done":
                        if (assistantMsg.Status != MessageStatus.Error)
                            assistantMsg.Status = MessageStatus.Complete;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            assistantMsg.AppendText("\n[已中断]");
            assistantMsg.Status = MessageStatus.Complete;
        }
        catch (Exception ex)
        {
            assistantMsg.AppendText($"\n[异常] {ex.Message}");
            assistantMsg.Status = MessageStatus.Error;
        }
        finally
        {
            IsProcessing = false;
            _submitCts = null;
        }
    }

    private bool CanSend() => !IsProcessing && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand]
    private async Task AbortAsync()
    {
        _submitCts?.Cancel();
        try { await _client.AbortAsync(); } catch { }
    }

    // ── 权限请求处理 ──

    private void OnPermissionRequested(CcMiniEvent ev)
    {
        if (AutoApprove)
        {
            _ = _client.RespondPermissionAsync(true);
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            var toolName = ev.GetString("tool");
            var input = ev.GetElement("input")?.ToString() ?? "";
            if (input.Length > 500) input = input[..500] + "...";

            var result = MessageBox.Show(
                $"工具: {toolName}\n\n参数:\n{input}\n\n是否允许执行？\n\n「是」= 允许  「否」= 拒绝",
                "cc-mini 权限请求",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            await _client.RespondPermissionAsync(result == MessageBoxResult.Yes);
        });
    }

    /// <summary>处理输入框按键。</summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
        // 命令补全导航优先
        if (HandleCommandNavigation(e.Key))
        {
            e.Handled = true;
            return;
        }

        // Enter 发送（非补全状态下）
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && CanSend())
        {
            e.Handled = true;
            _ = SendAsync();
        }
    }

    public void Cleanup()
    {
        _submitCts?.Cancel();
        _client.Dispose();
    }
}
