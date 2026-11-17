using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 角色「脚」和「原点」之间的换算。出生点、锚点给的都是脚的位置（格子底边中点），
/// 主角的原点是身体碰撞框的中心，所以放到出生点时要把原点放在「脚 − 半个身高」。
/// 只看名为 CollisionShape2D 的矩形碰撞框，不认识具体的角色类（World 也能用）。
/// </summary>
public static class BodyFeet
{
    /// <summary>脚底相对原点的 y（碰撞框底边）；没有矩形碰撞框时为 0（原点就是脚）。</summary>
    public static float FeetOffset(Node2D body)
    {
        var shape = body?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        return shape?.Shape is RectangleShape2D rect ? shape.Position.Y + rect.Size.Y * 0.5f : 0f;
    }

    /// <summary>让 body 的脚踩在 feet：原点 = feet − 脚底偏移。</summary>
    public static void PlaceFeetAt(Node2D body, Vector2 feet) => body.GlobalPosition = feet - new Vector2(0f, FeetOffset(body));
}
