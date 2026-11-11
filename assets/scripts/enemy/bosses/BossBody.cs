using Godot;
using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses;

/// <summary>
/// Boss 共用部分：BossId、显示名（Data.DisplayName）、阶段；发 BossEncounterStarted / HealthChanged / PhaseChanged / Defeated。
/// - 进场时变量 boss_&lt;id&gt; 已经是 true → 直接进入「已击败」（EnterDefeated，由具体 Boss 决定是哪个状态）；
/// - 开战之后每次血量变化发 BossHealthChanged；
/// - BossDefeated 每个实例最多发一次（EmitDefeated 保证）；
/// - 变量 boss_&lt;id&gt; 被别的地方写成 true（调试、测试用设变量代替打 Boss）而自己不在结算击败的过程中：
///   同样进入「已击败」；如果已经开战，补发一次 BossDefeated，让竞技场门和血条收尾。
/// </summary>
[GlobalClass]
public partial class BossBody : EnemyBody
{
    [Export] public StringName BossId { get; set; } = "";

    public string FlagKey => "boss_" + BossId;
    public string BossName => Data?.DisplayName ?? "";
    public int Phase { get; private set; } = 1;
    public bool EncounterStarted { get; private set; }
    public int EncounterEmits { get; private set; }
    public int DefeatedEmits { get; private set; }
    /// <summary>已经进入「已击败」（不管是打赢的还是进场时就是已击败）。</summary>
    public bool Finished { get; protected set; }

    /// <summary>正在结算自己的击败（例如守坑人变身期间）：这时自己写 boss_&lt;id&gt; 不算外部写入。</summary>
    protected virtual bool ResolvingDefeat => false;

    private bool _listening;

    public override void _EnterTree()
    {
        base._EnterTree();
        if (Events.Instance != null && !_listening)
        {
            Events.Instance.VariableChanged += OnVariableChanged;
            _listening = true;
        }
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (_listening && Events.Instance != null) Events.Instance.VariableChanged -= OnVariableChanged;
        _listening = false;
    }

    public override void _Ready()
    {
        base._Ready();
        Stats.HealthChanged += OnBossHealthChanged;
        if (GameSession.Progress.Flag(FlagKey))
        {
            Finished = true;
            EnterDefeated();
        }
    }

    /// <summary>进入「已击败」的终态（不发任何事件）。具体 Boss 覆盖。</summary>
    protected virtual void EnterDefeated() { }

    /// <summary>开战：发 BossEncounterStarted（每个实例一次）。</summary>
    public void StartEncounter()
    {
        if (EncounterStarted || Finished) return;
        EncounterStarted = true;
        EncounterEmits++;
        Phase = 1;
        Events.Instance?.EmitSignal(Events.SignalName.BossEncounterStarted, BossId, BossName, Stats.MaxHealth);
    }

    public void ChangePhase(int phase)
    {
        if (phase == Phase) return;
        Phase = phase;
        Events.Instance?.EmitSignal(Events.SignalName.BossPhaseChanged, BossId, phase);
    }

    /// <summary>发 BossDefeated，每个实例最多一次。返回这次是否真的发了。</summary>
    public bool EmitDefeated()
    {
        if (DefeatedEmits > 0) return false;
        DefeatedEmits++;
        Finished = true;
        Events.Instance?.EmitSignal(Events.SignalName.BossDefeated, BossId, GlobalPosition);
        return true;
    }

    private void OnBossHealthChanged(int oldHealth, int newHealth)
    {
        if (!EncounterStarted || DefeatedEmits > 0) return;
        Events.Instance?.EmitSignal(Events.SignalName.BossHealthChanged, BossId, newHealth, Stats.MaxHealth);
    }

    private void OnVariableChanged(StringName key, Variant value)
    {
        if (key.ToString() != FlagKey || Finished || ResolvingDefeat || !IsInsideTree()) return;
        if (!GameSession.Progress.Flag(FlagKey)) return;
        bool engaged = EncounterStarted;
        Finished = true;
        EnterDefeated();
        if (engaged) EmitDefeated();
    }
}
