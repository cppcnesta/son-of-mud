using Godot;
using Godot.Collections;
using SonOfMud.Core;

namespace SonOfMud.Tools;

/// <summary>测试场的地形：每个矩形生成一个 World 层的静态碰撞体，并画成色块（技术占位）。</summary>
[GlobalClass]
public partial class LabGeometry : Node2D
{
    [Export] public Array<Rect2> Solids { get; set; } = new();
    [Export] public Color Fill { get; set; } = new("3a4150");

    public override void _Ready()
    {
        foreach (Rect2 rect in Solids)
        {
            var body = new StaticBody2D { CollisionLayer = Layers.World, CollisionMask = 0, Position = rect.GetCenter() };
            body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
            AddChild(body);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (Rect2 rect in Solids) DrawRect(rect, Fill);
    }
}
