using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>交互提示：收到 InteractPromptChanged(prompt) 时显示“E 提示文字”，空字符串时隐藏。</summary>
public partial class InteractPrompt : Label
{
    /// <summary>可调数值；不导出，统一用 UiSettings.Shared（原因见那里）。测试可以替换。</summary>
    public UiSettings Settings { get; set; }

    /// <summary>最近一次收到的提示文字（不含按键）。</summary>
    public string Prompt { get; private set; } = "";

    public override void _EnterTree()
    {
        Settings ??= UiSettings.Shared;
        if (Events.Instance != null) Events.Instance.InteractPromptChanged += Apply;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.InteractPromptChanged -= Apply;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Apply("");
    }

    public void Apply(string prompt)
    {
        Prompt = prompt ?? "";
        Visible = Prompt.Length > 0;
        Text = Visible ? string.Format(Settings.PromptFormat, Settings.InteractKeyLabel, Prompt) : "";
    }
}
