using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 游戏中的 HUD：泥珠、器物环、Boss 血条、交互提示、提示条、小地图都放在 Root 下面，各自订阅自己的事件。
/// Hud 本身只做一件事：NarrativeStarted 时整体淡出，NarrativeFinished 时淡回（按开始 / 结束配对计数）。
/// ProcessMode = Always，剧情暂停期间淡入淡出照常进行。
/// </summary>
public partial class Hud : CanvasLayer
{
    [Export] public UiSettings Settings { get; set; }
    [Export] public Control Root { get; set; }
    [Export] public HpBeadBar Beads { get; set; }
    [Export] public ArtifactRing Artifacts { get; set; }
    [Export] public BossBar Boss { get; set; }
    [Export] public InteractPrompt Prompt { get; set; }
    [Export] public Toast Toast { get; set; }
    /// <summary>右上角的小地图（MetSys Minimap.tscn 实例，track_position=true，3×3）。</summary>
    [Export] public Control Minimap { get; set; }

    public float RootAlpha => Root?.Modulate.A ?? 1f;
    public bool NarrativeActive => _narrativeDepth > 0;

    private int _narrativeDepth;
    private Tween _tween;

    public override void _EnterTree()
    {
        Settings ??= UiSettings.LoadDefault();
        if (Events.Instance == null) return;
        Events.Instance.NarrativeStarted += OnNarrativeStarted;
        Events.Instance.NarrativeFinished += OnNarrativeFinished;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.NarrativeStarted -= OnNarrativeStarted;
        Events.Instance.NarrativeFinished -= OnNarrativeFinished;
    }

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    private void OnNarrativeStarted(string scriptId, string label)
    {
        _narrativeDepth++;
        FadeTo(Settings.HudNarrativeAlpha);
    }

    private void OnNarrativeFinished(string scriptId, string label)
    {
        if (_narrativeDepth == 0) return;
        _narrativeDepth--;
        if (_narrativeDepth == 0) FadeTo(1f);
    }

    private void FadeTo(float alpha)
    {
        if (Root == null) return;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(Root, "modulate:a", alpha, Mathf.Max(Settings.HudFadeSeconds, 0.01f));
    }
}
