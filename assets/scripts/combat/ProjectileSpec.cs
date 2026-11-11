using Godot;

namespace SonOfMud.Combat;

/// <summary>
/// 投掷物的飞行参数（assets/data/combat/*.tres）。伤害不在这里，沿用 HitSpec / Hit。
/// 敌人扔泥肝和玩家扔泥肝可以共用同一份。
/// </summary>
[GlobalClass]
public partial class ProjectileSpec : Resource
{
    /// <summary>向下的重力加速度（像素 / 秒²）；0 表示直线飞行。</summary>
    [Export] public float Gravity { get; set; } = 1200f;
    /// <summary>判定半径（圆形），同时用来撞世界碰撞。</summary>
    [Export] public float Radius { get; set; } = 10f;
    /// <summary>最长飞行时间（秒），到了自己消失。</summary>
    [Export] public float MaxLifetime { get; set; } = 4f;
    [Export] public Color Tint { get; set; } = new(0.55f, 0.2f, 0.16f);
}
