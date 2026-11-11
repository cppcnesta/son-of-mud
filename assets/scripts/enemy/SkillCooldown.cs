namespace SonOfMud.Enemy;

/// <summary>
/// 「有阻回」的技能冷却（boss.md 第 3 节），纯 C# 类：
/// - 三种状态：就绪 / 施法中 / 回充中；
/// - BeginCast 进入施法中，施法期间不回充；
/// - EndCast（本技能完全结束）后从 0 开始充，充满 Duration 秒变成就绪；
/// - Reduce 只在回充中生效，最多减到 0（减到 0 立即就绪）；施法中或已就绪时调用不起作用，也不存起来（Q5）。
/// 显示用的「已充 / 总长」：Charged / Duration，例如横扫开战 5/5、蝎尾开战 0/20。
/// </summary>
public sealed class SkillCooldown
{
    public float Duration { get; }
    public SkillPhase Phase { get; private set; }
    /// <summary>回充中还剩多少秒；就绪时是 0；施法中保持施法前的值（施法从就绪开始，所以一般是 0）。</summary>
    public float Remaining { get; private set; }

    public bool IsReady => Phase == SkillPhase.Ready;
    public bool IsCasting => Phase == SkillPhase.Casting;
    public bool IsRecharging => Phase == SkillPhase.Recharging;
    /// <summary>已经充了多少秒（就绪时等于 Duration，施法中是 0）。</summary>
    public float Charged => Phase switch
    {
        SkillPhase.Ready => Duration,
        SkillPhase.Casting => 0f,
        _ => Duration - Remaining,
    };

    /// <param name="duration">冷却总长（秒）。</param>
    /// <param name="startReady">true：开局就绪（横扫 5/5）；false：开局从 0 开始充（蝎尾 0/20）。</param>
    public SkillCooldown(float duration, bool startReady)
    {
        Duration = duration < 0f ? 0f : duration;
        if (startReady || Duration <= 0f)
        {
            Phase = SkillPhase.Ready;
            Remaining = 0f;
        }
        else
        {
            Phase = SkillPhase.Recharging;
            Remaining = Duration;
        }
    }

    /// <summary>开始施法（前摇开始）。之后直到 EndCast 都不回充。</summary>
    public void BeginCast() => Phase = SkillPhase.Casting;

    /// <summary>本技能完全结束：从 0 开始充。</summary>
    public void EndCast()
    {
        if (Duration <= 0f)
        {
            Phase = SkillPhase.Ready;
            Remaining = 0f;
            return;
        }
        Phase = SkillPhase.Recharging;
        Remaining = Duration;
    }

    /// <summary>推进计时：只有回充中才减少剩余时间。</summary>
    public void Tick(float delta)
    {
        if (Phase != SkillPhase.Recharging || delta <= 0f) return;
        Remaining -= delta;
        if (Remaining <= 0.000001f) FinishCharge();
    }

    /// <summary>减少剩余冷却。只在回充中生效（返回 true），最多减到 0；施法中或已就绪时直接作废（返回 false）。</summary>
    public bool Reduce(float seconds)
    {
        if (Phase != SkillPhase.Recharging || seconds <= 0f) return false;
        Remaining -= seconds;
        if (Remaining <= 0.000001f) FinishCharge();
        return true;
    }

    private void FinishCharge()
    {
        Remaining = 0f;
        Phase = SkillPhase.Ready;
    }
}
