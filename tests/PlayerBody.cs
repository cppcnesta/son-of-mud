using Godot;

/// <summary>
/// 测试用：正式主角场景（player_v2.tscn）身体碰撞框的尺寸。主角原点在身体中心，脚底 = 原点 + HalfH。
/// 测试里不要再写死 24（旧的 48×48 身体），用这里读出来的值。
/// </summary>
public static class PlayerBody
{
    public const string ScenePath = "res://assets/scenes/player/player_v2.tscn";
    private static Vector2? _size;

    public static Vector2 Size
    {
        get
        {
            if (_size is Vector2 s) return s;
            Node player = GD.Load<PackedScene>(ScenePath).Instantiate();
            var shape = player.GetNode<CollisionShape2D>("CollisionShape2D");
            Vector2 size = ((RectangleShape2D)shape.Shape).Size;
            player.Free();
            _size = size;
            return size;
        }
    }

    public static float HalfW => Size.X * 0.5f;
    public static float HalfH => Size.Y * 0.5f;
}
