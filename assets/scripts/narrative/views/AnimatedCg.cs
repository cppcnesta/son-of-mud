using System;
using Godot;
using SonOfMud.Narrative.Data;

namespace SonOfMud.Narrative.Views;

/// <summary>
/// 动画 CG 场景的根节点（assets/scenes/narrative/cg/cg_&lt;id&gt;.tscn）。
/// - 画面：场景里的各个图层节点；流星、星星闪烁、头发摆动这类循环动画放在各自循环的 AnimationPlayer 里，和结束无关。
/// - Timeline：唯一的总时间轴（不循环，动画名 TimelineAnimation）：开头黑场淡入、滤镜慢慢加上、收尾。它播完 = 整段 CG 播完。
/// - 字幕：Subtitles 列表（检查器里改说话人、文字、出现 / 消失的秒数），按 Timeline 的当前秒数显示在 Who / Line 上；旁白不显示名字、用 NarrationFont。
/// 暂停时照常播放（剧情期间游戏是暂停的）。
/// </summary>
[GlobalClass]
public partial class AnimatedCg : Control
{
    [Export] public AnimationPlayer Timeline { get; set; }
    [Export] public string TimelineAnimation { get; set; } = "play";
    [Export] public Godot.Collections.Array<CgSubtitle> Subtitles { get; set; } = new();
    [Export] public Label Who { get; set; }
    [Export] public Label Line { get; set; }
    /// <summary>旁白用的字体（斜体）；不填就和其他台词一样。</summary>
    [Export] public Font NarrationFont { get; set; }
    /// <summary>每句字幕淡入 / 淡出的秒数。</summary>
    [Export] public float FadeIn { get; set; } = 0.8f;
    [Export] public float FadeOut { get; set; } = 0.4f;

    /// <summary>现在显示的是第几句（从 0 开始），没有字幕时是 -1。</summary>
    public int ShownIndex { get; private set; } = -1;
    public bool Playing { get; private set; }
    public bool Finished { get; private set; }

    private Action _onFinished;
    private bool _connected;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ShowSubtitle(-1, 0f);
    }

    /// <summary>总时长（秒）：Timeline 里那段动画的长度；没有时是 0。</summary>
    public double Duration =>
        Timeline != null && Timeline.HasAnimation(TimelineAnimation) ? Timeline.GetAnimation(TimelineAnimation).Length : 0;

    /// <summary>总时间轴现在播到第几秒。</summary>
    public double Position => Timeline != null && Timeline.IsPlaying() ? Timeline.CurrentAnimationPosition : Finished ? Duration : 0;

    /// <summary>从头开始播。缺总时间轴时返回出错原因（不开始播）；成功返回 null。onFinished 只在总时间轴播完时调用一次。</summary>
    public string Begin(Action onFinished)
    {
        if (Timeline == null) return $"动画 CG {Name} 没有总时间轴 Timeline";
        if (!Timeline.HasAnimation(TimelineAnimation)) return $"动画 CG {Name} 的总时间轴里没有动画 {TimelineAnimation}";
        if (Timeline.GetAnimation(TimelineAnimation).LoopMode != Animation.LoopModeEnum.None)
            return $"动画 CG {Name} 的总时间轴 {TimelineAnimation} 是循环的，不能用来判断播完";
        _onFinished = onFinished;
        Finished = false;
        Playing = true;
        Timeline.ProcessMode = ProcessModeEnum.Always;
        if (!_connected) { Timeline.AnimationFinished += OnTimelineFinished; _connected = true; }
        Timeline.Play(TimelineAnimation);
        return null;
    }

    /// <summary>停下来，之后不会再回调。</summary>
    public void StopPlayback()
    {
        _onFinished = null;
        Playing = false;
        if (Timeline != null && IsInstanceValid(Timeline))
        {
            if (_connected) { Timeline.AnimationFinished -= OnTimelineFinished; _connected = false; }
            Timeline.Stop();
        }
    }

    private void OnTimelineFinished(StringName name)
    {
        if (name != TimelineAnimation || !Playing) return;
        Playing = false;
        Finished = true;
        if (_connected) { Timeline.AnimationFinished -= OnTimelineFinished; _connected = false; }
        ShowSubtitle(-1, 0f);
        Action callback = _onFinished;
        _onFinished = null;
        callback?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (!Playing) return;
        float t = (float)Position;
        int index = -1;
        float alpha = 0f;
        for (int i = 0; i < Subtitles.Count; i++)
        {
            CgSubtitle s = Subtitles[i];
            if (s == null || t < s.Start || t >= s.End) continue;
            index = i;
            alpha = Mathf.Clamp((t - s.Start) / Mathf.Max(FadeIn, 0.01f), 0f, 1f) * Mathf.Clamp((s.End - t) / Mathf.Max(FadeOut, 0.01f), 0f, 1f);
            break;
        }
        ShowSubtitle(index, alpha);
    }

    private void ShowSubtitle(int index, float alpha)
    {
        if (index != ShownIndex)
        {
            ShownIndex = index;
            CgSubtitle s = index >= 0 ? Subtitles[index] : null;
            if (Who != null) Who.Text = s == null || s.IsNarration ? "" : s.Speaker;
            if (Line != null)
            {
                Line.Text = s?.Text ?? "";
                if (s != null && s.IsNarration && NarrationFont != null) Line.AddThemeFontOverride("font", NarrationFont);
                else Line.RemoveThemeFontOverride("font");
            }
        }
        if (Who != null) Who.Modulate = new Color(1, 1, 1, alpha);
        if (Line != null) Line.Modulate = new Color(1, 1, 1, alpha);
    }

    public override void _ExitTree() => StopPlayback();
}
