using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CcMiniWpf.Models;

public enum MessageRole { User, Assistant, System }

public enum MessageStatus { Pending, Streaming, Complete, Error }

/// <summary>
/// 一条聊天消息，包含文本和工具调用信息。
/// </summary>
public partial class ChatMessage : ObservableObject
{
    [ObservableProperty] private MessageRole _role;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private MessageStatus _status = MessageStatus.Pending;
    [ObservableProperty] private DateTime _timestamp = DateTime.Now;

    public ObservableCollection<ToolCallInfo> ToolCalls { get; } = [];

    /// <summary>流式追加文本片段。</summary>
    public void AppendText(string chunk)
    {
        Text += chunk;
    }
}

/// <summary>
/// 工具调用信息（展示用）。
/// </summary>
public partial class ToolCallInfo : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _inputSummary = "";
    [ObservableProperty] private string _activity = "";
    [ObservableProperty] private string _resultContent = "";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private ToolCallStatus _status = ToolCallStatus.Pending;

    public static string SummarizeInput(JsonElement? input)
    {
        if (input is not { ValueKind: JsonValueKind.Object } el) return "";
        var parts = new List<string>();
        foreach (var prop in el.EnumerateObject())
        {
            var val = prop.Value.ToString();
            if (val.Length > 80) val = val[..80] + "...";
            parts.Add($"{prop.Name}: {val}");
        }
        return string.Join("\n", parts);
    }
}

public enum ToolCallStatus { Pending, Executing, Complete }
