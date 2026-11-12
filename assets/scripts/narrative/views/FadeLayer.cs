using System;
using Godot;

namespace SonOfMud.Narrative.Views;

/// <summary>剧情层自己的黑幕（[转场]），盖在对话框上面。和 World 换房的黑幕无关。界面在 fade_layer.tscn（整个就是一块全屏 ColorRect）。</summary>
[GlobalClass]
public partial class FadeLayer : ColorRect
{
    public const string ScenePath = "res://assets/scenes/narrative/views/fade_layer.tscn";

    public bool IsBlack => Color.A >= 0.999f;

    private Tween _tween;

    public override void _Ready()
    {
        Color = new Color(Color.R, Color.G, Color.B, 0f);
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
        Color = new Color(Color.R, Color.G, Color.B, black ? 1f : 0f);
    }

    /// <summary>取消正在进行的渐变（不回调），回到透明。</summary>
    public void Cancel() => SetBlack(false);
}
