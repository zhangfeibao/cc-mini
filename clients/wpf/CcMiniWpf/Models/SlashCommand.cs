namespace CcMiniWpf.Models;

/// <summary>
/// 斜杠命令定义。
/// </summary>
public sealed record SlashCommand(string Name, string Description, string? Args = null)
{
    public string DisplayName => $"/{Name}";
    public string DisplayText => Args is not null ? $"/{Name} {Args}" : $"/{Name}";

    /// <summary>cc-mini 内置命令列表。</summary>
    public static readonly SlashCommand[] BuiltIn =
    [
        new("help",     "显示可用命令"),
        new("compact",  "压缩对话上下文", "[instructions]"),
        new("resume",   "恢复历史会话", "[number|session-id]"),
        new("history",  "列出当前目录的会话记录"),
        new("clear",    "清空对话，开始新会话"),
        new("memory",   "显示当前记忆索引"),
        new("remember", "保存笔记到日志", "[text]"),
        new("dream",    "合并日志到主题文件"),
        new("skills",   "列出所有可用技能"),
        new("cost",     "显示 token 用量和费用"),
        new("model",    "查看或切换模型", "[model-name]"),
        new("provider", "查看或切换 provider", "[anthropic|openai]"),
        new("plan",     "进入计划模式或查看当前计划"),
    ];
}
