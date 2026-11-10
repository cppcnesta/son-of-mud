using Godot;
using SonOfMud.Combat;

namespace SonOfMud.Tools;

/// <summary>
/// 测试场用的脆弱地面：一块 World 层的地面，带一个只吃重击的 HurtBox（HeavyOnly）。被打空血量就移除自己。
/// 正式的可破坏物属于 World 系统，这里只用来验证下砸。
/// </summary>
[GlobalClass]
public partial class LabBreakable : StaticBody2D
{
    [Export] public Vector2 Size { get; set; } = new(192, 64);

    public HurtBox HurtBox { get; private set; }
    public Stats Stats { get; private set; }
    public bool Broken { get; private set; }

    public override void _Ready()
    {
        CollisionLayer = SonOfMud.Core.Layers.World;
        CollisionMask = 0;
        AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = Size } });
        Stats = new Stats { Name = "Stats", MaxHealth = 1 };
        AddChild(Stats);
        HurtBox = new HurtBox { Name = "HurtBox", EntityRoot = this, Stats = Stats, HeavyOnly = true,
            CollisionLayer = SonOfMud.Core.Layers.EnemyHurt, CollisionMask = 0 };
        HurtBox.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = Size } });
        AddChild(HurtBox);
        Stats.Depleted += Break;
        QueueRedraw();
    }

    private void Break()
    {
        Broken = true;
        QueueFree();
    }

    public override void _Draw() => DrawRect(new Rect2(-Size / 2f, Size), new Color("8a6a3c"));
}
