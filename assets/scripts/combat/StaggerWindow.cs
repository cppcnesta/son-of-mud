using Godot;

namespace SonOfMud.Combat;

/// <summary>
/// 反制僵直窗口：Begin 之后持续 Config.Duration 秒，期间 Stats 的易伤倍率为 Config.DamageMultiplier；
/// 每受一次伤（Stats 血量下降，不管打在哪个 HurtBox 上），剩余时间减 Config.ReducePerHit，减到 0 立即结束。
/// 进入 / 退出僵直的动作由拥有者自己的状态机决定，这里只管计时和倍率。
/// </summary>
[GlobalClass]
public partial class StaggerWindow : Node
{
    [Signal] public delegate void StartedEventHandler();
    [Signal] public delegate void EndedEventHandler();

    private static readonly StringName Source = "stagger";

    [Export] public StaggerConfig Config { get; set; }
    /// <summary>没填就用父节点下第一个 Stats。</summary>
    [Export] public Stats Stats { get; set; }

    public float Remaining { get; private set; }
    public bool Active => Remaining > 0f;

    public override void _Ready()
    {
        Config ??= new StaggerConfig();
        if (Stats == null)
            foreach (Node sibling in GetParent().GetChildren())
                if (sibling is Stats s) { Stats = s; break; }
        if (Stats != null) Stats.HealthChanged += OnHealthChanged;
    }

    public void Begin()
    {
        bool was = Active;
        Remaining = Config.Duration;
        Stats?.SetIncomingMultiplier(Source, Config.DamageMultiplier);
        if (!was) EmitSignal(SignalName.Started);
    }

    public void End()
    {
        if (!Active) return;
        Remaining = 0f;
        Stats?.SetIncomingMultiplier(Source, 1f);
        EmitSignal(SignalName.Ended);
    }

    /// <summary>推进计时。节点在树里时每个物理帧自动调用。</summary>
    public void Tick(float delta)
    {
        if (!Active) return;
        Remaining -= delta;
        if (Remaining <= 0.000001f) End();
    }

    public override void _PhysicsProcess(double delta) => Tick((float)delta);

    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        if (!Active || newHealth >= oldHealth) return;
        Remaining -= Config.ReducePerHit;
        if (Remaining <= 0.000001f) End();
    }
}
