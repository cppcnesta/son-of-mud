using System;
using Godot;

namespace SonOfMud.Narrative.Views;

/// <summary>泥板 / 铭文阅读界面（技术占位版）：屏幕中间的面板，标题 + 正文。推进键关闭（由剧情层读键）。</summary>
[GlobalClass]
public partial class TabletView : Control
{
    public bool IsOpen { get; private set; }
    public string CurrentTitle => _title?.Text ?? "";
    public string CurrentText => _body?.Text ?? "";
    public bool ShowingFull { get; private set; }
    public int OpenCount { get; private set; }

    private Label _title;
    private RichTextLabel _body;
    private Action _onClosed;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var dim = new ColorRect { Name = "Dim", Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var panel = new Panel { Name = "Panel", MouseFilter = MouseFilterEnum.Ignore };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -320;
        panel.OffsetRight = 320;
        panel.OffsetTop = -240;
        panel.OffsetBottom = 240;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.36f, 0.27f, 0.19f), BorderColor = new Color(0.2f, 0.14f, 0.1f), BorderWidthTop = 4, BorderWidthBottom = 4, BorderWidthLeft = 4, BorderWidthRight = 4 });
        AddChild(panel);
        _title = new Label { Name = "Title", Position = new Vector2(32, 24), Size = new Vector2(576, 40), HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 26);
        panel.AddChild(_title);
        _body = new RichTextLabel { Name = "Body", BbcodeEnabled = true, Position = new Vector2(40, 84), Size = new Vector2(560, 340), ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore };
        _body.AddThemeFontSizeOverride("normal_font_size", 22);
        panel.AddChild(_body);
        var hint = new Label { Name = "Hint", Text = "按 E / 空格 继续", Position = new Vector2(32, 436), Size = new Vector2(576, 30), HorizontalAlignment = HorizontalAlignment.Right };
        panel.AddChild(hint);
        Visible = false;
    }

    public void Open(string title, string text, bool full, Action onClosed)
    {
        OpenCount++;
        _onClosed = onClosed;
        _title.Text = title ?? "";
        _body.Text = text ?? "";
        ShowingFull = full;
        IsOpen = true;
        Visible = true;
    }

    /// <summary>关闭并回调（只回调一次）。没打开时什么也不做。</summary>
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        Action callback = _onClosed;
        _onClosed = null;
        callback?.Invoke();
    }
}
