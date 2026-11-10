using Godot;
using SonOfMud.Combat;
using GDictionary = Godot.Collections.Dictionary;

namespace SonOfMud.Player;

/// <summary>
/// 玩家身上所有跨状态的计时：闪避冷却、格挡冷却、攻击间隔、切换冷却、移动减速恢复、泥格挡增伤。
/// 切形态、换房都不会重置这些值（ME11、规格第 4 节）。
/// </summary>
public sealed class PlayerTimers
{
    private const float Epsilon = 0.000001f;

    public float DodgeCooldown { get; set; }
    public float BlockCooldown { get; set; }
    public float AttackInterval { get; set; }
    public float SwitchCooldown { get; set; }
    public MovementRecovery Recovery { get; } = new();
    public BlockBonus Bonus { get; } = new();

    public bool DodgeReady => DodgeCooldown <= 0f;
    public bool BlockReady => BlockCooldown <= 0f;
    public bool AttackReady => AttackInterval <= 0f;
    public bool SwitchReady => SwitchCooldown <= 0f;

    public void Tick(float delta)
    {
        DodgeCooldown = Down(DodgeCooldown, delta);
        BlockCooldown = Down(BlockCooldown, delta);
        AttackInterval = Down(AttackInterval, delta);
        SwitchCooldown = Down(SwitchCooldown, delta);
        Recovery.Tick(delta);
    }

    private static float Down(float value, float delta)
    {
        value = Mathf.Max(0f, value - delta);
        return value < Epsilon ? 0f : value;
    }

    /// <summary>清掉攻击间隔和攻击来源的减速（水格挡成功、泥后摇接闪避）。</summary>
    public void ResetAttackInterval()
    {
        AttackInterval = 0f;
        if (Recovery.RecoverySource == MovementRecovery.Source.Attack) Recovery.Clear();
    }

    public GDictionary Snapshot() => new()
    {
        ["dodge"] = DodgeCooldown,
        ["block"] = BlockCooldown,
        ["attack"] = AttackInterval,
        ["switch"] = SwitchCooldown,
        ["recovery"] = Recovery.Snapshot(),
        ["bonus"] = Bonus.Ready,
        ["bonus_multiplier"] = Bonus.Multiplier,
    };

    public void Restore(GDictionary data, Curve recoveryCurve)
    {
        if (data == null) return;
        DodgeCooldown = Read(data, "dodge");
        BlockCooldown = Read(data, "block");
        AttackInterval = Read(data, "attack");
        SwitchCooldown = Read(data, "switch");
        Recovery.Restore(data.TryGetValue("recovery", out var r) ? r.AsGodotDictionary() : null, recoveryCurve);
        if (data.TryGetValue("bonus", out var b) && b.AsBool()) Bonus.Grant(Read(data, "bonus_multiplier", 1.6f));
        else Bonus.Clear();
    }

    private static float Read(GDictionary data, string key, float fallback = 0f) =>
        data.TryGetValue(key, out var v) ? Mathf.Max(0f, v.AsSingle()) : fallback;
}
