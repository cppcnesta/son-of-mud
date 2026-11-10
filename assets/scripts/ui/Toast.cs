using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 提示条：MessageRequested（门锁提示等）、GameSaved（“已存档”）、TabletCollected（“获得泥板「…」”）。
/// 出现约 ToastSeconds 秒后淡出隐藏；新消息直接替换旧的并重新计时。
/// </summary>
public partial class Toast : Label
{
    [Export] public UiSettings Settings { get; set; }

    public bool IsShowing => Visible;
    /// <summary>一共显示过几条消息。</summary>
    public int ShownCount { get; private set; }

    private Tween _tween;

    public override void _EnterTree()
    {
        Settings ??= UiSettings.LoadDefault();
        if (Events.Instance == null) return;
        Events.Instance.MessageRequested += ShowMessage;
        Events.Instance.GameSaved += OnGameSaved;
        Events.Instance.TabletCollected += OnTabletCollected;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.MessageRequested -= ShowMessage;
        Events.Instance.GameSaved -= OnGameSaved;
        Events.Instance.TabletCollected -= OnTabletCollected;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    private void OnGameSaved() => ShowMessage(Settings.SavedText);

    private void OnTabletCollected(StringName tabletId) =>
        ShowMessage(string.Format(Settings.TabletFormat, Settings.TabletName(tabletId)));

    public void ShowMessage(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        ShownCount++;
        Text = text;
        Visible = true;
        Modulate = Colors.White;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenInterval(Mathf.Max(Settings.ToastSeconds, 0f));
        _tween.TweenProperty(this, "modulate:a", 0f, Mathf.Max(Settings.ToastFadeSeconds, 0.01f));
        _tween.TweenCallback(Callable.From(() => Visible = false));
    }
}
