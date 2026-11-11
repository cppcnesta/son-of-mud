namespace SonOfMud.Enemy;

/// <summary>技能冷却的三种状态（有阻回，boss.md 第 3 节）。</summary>
public enum SkillPhase
{
    /// <summary>已就绪，可以发动。</summary>
    Ready,
    /// <summary>施法中（前摇、判定、收招、反制僵直……）：不回充。</summary>
    Casting,
    /// <summary>回充中：剩余时间随时间减少，Reduce 只在这时生效。</summary>
    Recharging,
}
