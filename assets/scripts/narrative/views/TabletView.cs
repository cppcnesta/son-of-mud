using System;
using Godot;

namespace SonOfMud.Narrative.Views;

/// <summary>
/// 泥板 / 铭文阅读界面。界面在 tablet_view.tscn：Dim（压暗）、Panel（泥板底图）→ Title / Body / Hint。推进键关闭（由剧情层读键）。
/// </summary>
[GlobalClass]
public partial class TabletView : Control
{
    public const string ScenePath = "res://assets/scenes/narrative/views/tablet_view.tscn";

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
        ViewNodes.Need<Control>(this, "Dim", ScenePath);
        ViewNodes.Need<Control>(this, "Panel", ScenePath);
        _title = ViewNodes.Need<Label>(this, "Panel/Title", ScenePath);
        _body = ViewNodes.Need<RichTextLabel>(this, "Panel/Body", ScenePath);
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
