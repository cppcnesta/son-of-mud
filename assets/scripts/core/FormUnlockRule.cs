using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 开局只有泥形态，打赢守坑人（剧情标记 boss_keeper 为 true）才学会水形态、能切换（战斗策划表 3.1）。
/// - 游戏中 boss_keeper 变成 true：解锁切换形态，提示条显示 <see cref="UnlockText"/>（SaveService 调 <see cref="TryUnlock"/>）。
/// - 新游戏 / 读档 / 试玩补完变量后：按 boss_keeper 核对能力（<see cref="Reconcile"/>），不弹提示。
///   旧存档里有切换能力但还没打赢守坑人的拿掉；打赢了但没有的补上。
/// </summary>
public static class FormUnlockRule
{
    public static readonly StringName Ability = "switch_form";
    public static readonly StringName Flag = "boss_keeper";
    /// <summary>学会水形态时的提示（占位，UI 美术出来再改；Q 是切换形态的按键）。</summary>
    public const string UnlockText = "学会了水形态。按 Q 在水和泥之间切换。";

    /// <summary>按 boss_keeper 核对切换形态能力。返回改了几处（0 或 1）。</summary>
    public static int Reconcile(ProgressStore progress)
    {
        if (progress == null) return 0;
        bool earned = progress.Flag(Flag);
        if (!earned && progress.Revoke(Ability)) return 1;
        if (earned && !progress.HasAbility(Ability)) return progress.EnsureAbilities(new[] { Ability });
        return 0;
    }

    /// <summary>变量 key 刚变成 value：如果是 boss_keeper 变成 true 且还没学会，就解锁并返回 true（调用方弹提示）。</summary>
    public static bool TryUnlock(ProgressStore progress, StringName key, Variant value)
    {
        if (progress == null || key != Flag || value.VariantType != Variant.Type.Bool || !value.AsBool()) return false;
        if (progress.HasAbility(Ability)) return false;
        progress.Unlock(Ability);
        return true;
    }
}
