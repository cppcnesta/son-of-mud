using Godot;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Views;

/// <summary>
/// 字幕条（技术占位版）：玩家能动时的插话、拾取台词。不暂停、不锁输入、不读推进键，
/// 每句打完字后由执行器按字数停留一会儿，自动换下一句，播完收起。一次只显示一句（不往下攒）。
/// </summary>
[GlobalClass]
public partial class SubtitleBar : Control, ILineSurface
{
    [Export] public NarrativeSettings Settings { get; set; }

    public bool IsTyping { get; private set; }
    public bool Rushing { get; private set; }
    public bool IsShowing => _bar != null && _bar.Visible;
    public string CurrentName { get; private set; } = "";
    public string CurrentText { get; private set; } = "";
    public int LineCount { get; private set; }

    private PanelContainer _bar;
    private RichTextLabel _label;
    private double _shown;

    public override void _Ready()
    {
        Settings ??= new NarrativeSettings();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _bar = new PanelContainer { Name = "Bar", MouseFilter = MouseFilterEnum.Ignore };
        _bar.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _bar.OffsetLeft = -420;
        _bar.OffsetRight = 420;
        _bar.OffsetTop = -132;
        _bar.OffsetBottom = -84;
        _bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.55f), ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 8, ContentMarginBottom = 8 });
        AddChild(_bar);
        _label = new RichTextLabel { Name = "Text", BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
        _label.AddThemeFontSizeOverride("normal_font_size", 20);
        _bar.AddChild(_label);
        _bar.Visible = false;
    }

    public void ShowLine(string name, string text, CharacterData character, string expression)
    {
        LineCount++;
        CurrentName = name ?? "";
        CurrentText = text ?? "";
        _bar.Visible = true;
        Rushing = false;
        string color = (character?.NameColor ?? new Color(0.95f, 0.85f, 0.6f)).ToHtml(false);
        _label.Text = string.IsNullOrEmpty(name) ? CurrentText : $"[color=#{color}]{name}[/color]：{CurrentText}";
        IsTyping = true;
        _shown = 0;
        _label.VisibleCharacters = 0;
    }

    /// <summary>字幕条一次只显示一句。</summary>
    public bool CanAppend(string name) => false;

    public void AppendLine(string text) => ShowLine(CurrentName, text, null, "");

    public void BreakParagraph() { }

    public void Rush()
    {
        Rushing = true;
        CompleteTyping();
    }

    public void StopRush() => Rushing = false;

    public void CompleteTyping()
    {
        IsTyping = false;
        if (_label != null) _label.VisibleCharacters = -1;
    }

    public new void Hide()
    {
        CompleteTyping();
        Rushing = false;
        CurrentName = "";
        CurrentText = "";
        if (_bar != null) _bar.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!IsTyping) return;
        int total = _label.GetTotalCharacterCount();
        _shown += Settings.CharsPerSecond * delta;
        if (_shown >= total) CompleteTyping();
        else _label.VisibleCharacters = (int)_shown;
    }
}
