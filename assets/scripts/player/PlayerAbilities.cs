using Godot;

namespace SonOfMud.Player;

/// <summary>
/// 玩家能力 ID（D8：只剩这四项）。格挡、切形态开局就有；攀墙 S2_02 获得；重压下砸 S3_03 获得。
/// 旧代码里的 form_switch 在新代码里统一写成 switch_form。
/// </summary>
public static class PlayerAbilities
{
    public static readonly StringName Block = "block";
    public static readonly StringName SwitchForm = "switch_form";
    public static readonly StringName WallClimb = "wall_climb";
    public static readonly StringName HeavySlam = "heavy_slam";
}
