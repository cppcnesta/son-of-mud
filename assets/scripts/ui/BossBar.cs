using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// Boss 血条。BossEncounterStarted 显示名字和满血条；BossHealthChanged 主血条快速扣、延迟条稍后追上（平滑扣血）；
/// BossPhaseChanged 换名字（UiSettings.BossPhaseNames）；BossDefeated 收起。只认当前这场战斗的 bossId。
/// </summary>
public partial class BossBar : Control
{
    /// <summary>可调数值；不导出，统一用 UiSettings.Shared（原因见那里）。测试可以替换。</summary>
    public UiSettings Settings { get; set; }
    [Export] public Label NameLabel { get; set; }
    [Export] public TextureProgressBar Fill { get; set; }
    [Export] public TextureProgressBar Delay { get; set; }

    public bool Showing { get; private set; }
    public StringName BossId { get; private set; } = "";
    public string BaseName { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public int Phase { get; private set; }
    public int MaxHp { get; private set; }
    public int TargetHp { get; private set; }

    private Tween _fillTween;
    private Tween _delayTween;
    private Tween _fadeTween;

    public override void _EnterTree()
    {
        Settings ??= UiSettings.Shared;
        if (Events.Instance == null) return;
        Events.Instance.BossEncounterStarted += OnEncounterStarted;
        Events.Instance.BossHealthChanged += OnHealthChanged;
        Events.Instance.BossPhaseChanged += OnPhaseChanged;
        Events.Instance.BossDefeated += OnDefeated;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.BossEncounterStarted -= OnEncounterStarted;
        Events.Instance.BossHealthChanged -= OnHealthChanged;
        Events.Instance.BossPhaseChanged -= OnPhaseChanged;
        Events.Instance.BossDefeated -= OnDefeated;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        foreach (TextureProgressBar bar in new[] { Fill, Delay })
            if (bar != null) { bar.Step = 0; bar.MinValue = 0; }
        Visible = false;
        Modulate = new Color(1, 1, 1, 0);
    }

    private void OnEncounterStarted(StringName bossId, string displayName, int maxHp)
    {
        BossId = bossId;
        BaseName = displayName;
        Phase = 1;
        MaxHp = Mathf.Max(1, maxHp);
        TargetHp = MaxHp;
        SetName(displayName);
        _fillTween?.Kill();
        _delayTween?.Kill();
        foreach (TextureProgressBar bar in new[] { Fill, Delay })
        {
            if (bar == null) continue;
            bar.MaxValue = MaxHp;
            bar.Value = MaxHp;
        }
        Showing = true;
        Visible = true;
        FadeTo(1f, null);
    }

    private void OnHealthChanged(StringName bossId, int hp, int maxHp)
    {
        if (!Showing || bossId != BossId) return;
        MaxHp = Mathf.Max(1, maxHp);
        int previous = TargetHp;
        TargetHp = Mathf.Clamp(hp, 0, MaxHp);
        if (Fill != null) Fill.MaxValue = MaxHp;
        if (Delay != null) Delay.MaxValue = MaxHp;

        _fillTween?.Kill();
        _fillTween = CreateTween();
        _fillTween.TweenProperty(Fill, "value", (double)TargetHp, Mathf.Max(Settings.BossBarDrainSeconds, 0.01f));

        _delayTween?.Kill();
        if (TargetHp >= previous)
        {
            // 回血：延迟条直接跟上，不做“追赶”
            if (Delay != null) Delay.Value = TargetHp;
            return;
        }
        _delayTween = CreateTween();
        _delayTween.TweenInterval(Settings.BossBarDelaySeconds);
        _delayTween.TweenProperty(Delay, "value", (double)TargetHp, Mathf.Max(Settings.BossBarDelayDrainSeconds, 0.01f));
    }

    private void OnPhaseChanged(StringName bossId, int phase)
    {
        if (!Showing || bossId != BossId) return;
        Phase = phase;
        SetName(Settings.BossPhaseName(bossId, phase, BaseName));
    }

    private void OnDefeated(StringName bossId, Vector2 position)
    {
        if (!Showing || bossId != BossId) return;
        Showing = false;
        TargetHp = 0;
        _fillTween?.Kill();
        _delayTween?.Kill();
        if (Fill != null) Fill.Value = 0;
        if (Delay != null) Delay.Value = 0;
        FadeTo(0f, () => Visible = false);
    }

    private void SetName(string name)
    {
        DisplayName = name;
        if (NameLabel != null) NameLabel.Text = name;
    }

    private void FadeTo(float alpha, System.Action done)
    {
        _fadeTween?.Kill();
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(this, "modulate:a", alpha, Mathf.Max(Settings.BossBarFadeSeconds, 0.01f));
        if (done != null) _fadeTween.TweenCallback(Callable.From(done));
    }
}
