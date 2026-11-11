using System;
using Godot;

namespace SonOfMud.Narrative.Views;

/// <summary>剧情层自己的黑幕（[转场]），盖在对话框上面。和 World 换房的黑幕无关。</summary>
[GlobalClass]
public partial class FadeLayer : ColorRect
{
    public bool IsBlack => Color.A >= 0.999f;

    private Tween _tween;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Color = new Color(0, 0, 0, 0);
    }

    public void FadeTo(bool toBlack, float seconds, Action done)
    {
        _tween?.Kill();
        float target = toBlack ? 1f : 0f;
        if (seconds <= 0f)
        {
            SetBlack(toBlack);
            done?.Invoke();
            return;
        }
        _tween = CreateTween();
        _tween.SetPauseMode(Tween.TweenPauseMode.Process);
        _tween.TweenProperty(this, "color:a", target, seconds);
        _tween.Finished += () => done?.Invoke();
    }

    public void SetBlack(bool black)
    {
        _tween?.Kill();
        _tween = null;
        Color = new Color(0, 0, 0, black ? 1f : 0f);
    }

    /// <summary>取消正在进行的渐变（不回调），回到透明。</summary>
    public void Cancel() => SetBlack(false);
}
