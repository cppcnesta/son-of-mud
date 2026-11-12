using Godot;

namespace SonOfMud.Player;

/// <summary>
/// 玩家能力 ID（D8：只剩这四项）。格挡、切形态、攀墙开局就有（初始能力见 ability_table.tres 的 Initial）；
/// 重压下砸本轮不发放，代码（SlamState）保留，可破坏物改用普通攻击打碎。
/// 旧代码里的 form_switch 在新代码里统一写成 switch_form。
/// </summary>
public static class PlayerAbilities
{
    public static readonly StringName Block = "block";
    public static readonly StringName SwitchForm = "switch_form";
    public static readonly StringName WallClimb = "wall_climb";
    public static readonly StringName HeavySlam = "heavy_slam";
}
