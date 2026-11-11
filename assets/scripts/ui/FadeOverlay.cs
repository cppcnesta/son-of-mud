using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 黑幕的最小实现（UI 那一轮再换成正式表现）。只通过 Events 通信：
/// 收到 FadeRequested(id, fadeOut) → 渐变到全黑 / 全透明 → 真正完成后才发 FadeCompleted(id, fadeOut)。
/// 新请求会打断旧的渐变，被打断的请求不会再收到完成通知。
/// </summary>
[GlobalClass]
public partial class FadeOverlay : CanvasLayer
{
    [Export] public FadeSettings Settings { get; set; }

    private ColorRect _rect;
    private Tween _tween;

    public float Alpha => _rect?.Color.A ?? 0f;

    public override void _EnterTree()
    {
        if (Events.Instance != null) Events.Instance.FadeRequested += OnFadeRequested;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.FadeRequested -= OnFadeRequested;
    }

    public override void _Ready()
    {
        Settings ??= new FadeSettings();
        Layer = 100;
        ProcessMode = ProcessModeEnum.Always;
        _rect = GetNodeOrNull<ColorRect>("Black");
        if (_rect == null)
        {
            _rect = new ColorRect { Name = "Black" };
            AddChild(_rect);
        }
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _rect.MouseFilter = Control.MouseFilterEnum.Ignore;
        Color c = Settings.Color;
        _rect.Color = new Color(c.R, c.G, c.B, 0f);
    }

    private void OnFadeRequested(int requestId, bool fadeOut)
    {
        _tween?.Kill();
        float seconds = fadeOut ? Settings.FadeOutSeconds : Settings.FadeInSeconds;
        _tween = CreateTween();
        _tween.SetPauseMode(Tween.TweenPauseMode.Process);
        _tween.TweenProperty(_rect, "color:a", fadeOut ? 1f : 0f, Mathf.Max(seconds, 0.001f));
        _tween.Finished += () => Events.Instance?.EmitSignal(Events.SignalName.FadeCompleted, requestId, fadeOut);
    }
}
