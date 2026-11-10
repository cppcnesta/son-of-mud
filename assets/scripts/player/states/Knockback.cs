using Godot;
using SonOfMud.Combat;

namespace SonOfMud.Player.States;

/// <summary>受击击退：沿「攻击来源 → 玩家」的水平方向给一个初速度。</summary>
public static class Knockback
{
    public static void Apply(Player player, Hit hit)
    {
        if (hit == null || hit.KnockbackForce <= 0f)
        {
            player.StopHorizontal();
            return;
        }
        float dir = Mathf.Sign(player.GlobalPosition.X - hit.SourcePosition.X);
        if (dir == 0f) dir = -player.Facing;
        player.Velocity = new Vector2(dir * hit.KnockbackForce, player.Velocity.Y);
    }
}
