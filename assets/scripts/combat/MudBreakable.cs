using Godot;
using GDictionary = Godot.Collections.Dictionary;

[GlobalClass]
public partial class MudBreakable : StaticBody2D, IMudDamageable
{
    [Export] public bool RequiresHeavy { get; set; } = true;
    [Export] public string FlagKey { get; set; } = "";
    [Export] public Vector2 Dimensions { get; set; } = new(128, 32);
    private readonly MudAttackLedger _ledger = new();
    public override void _Ready()
    {
        CollisionLayer = 5;
        CollisionMask = 0;
        AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = Dimensions } });
        QueueRedraw();
    }
    public bool ReceiveHit(float amount, string attackId, GDictionary info)
    {
        if (!_ledger.Accept(attackId) || (RequiresHeavy && !info.Bool("heavy"))) return false;
        if (FlagKey.Length > 0) MudProgress.Instance.Mark(FlagKey);
        QueueFree();
        return true;
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(-Dimensions * 0.5f, Dimensions), new Color("c4a04f"));
        DrawLine(-Dimensions * 0.5f, Dimensions * 0.5f, new Color("4b352b"), 4);
    }
}
