using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 掉落的拾取物（小泥偶、石头……）。玩家身体碰到就拾取：变量 count.&lt;ItemId&gt; 加 1；
/// 第一次拾取这种物品（加之前是 0）时发 StoryRequested("pickups", ItemId) 播拾取台词（字幕条），之后不再发。
/// 只认 PlayerRegistry.Current（玩家只当 Node2D 用）。拾取在本帧末尾结算，避免在物理回调里改状态。
/// </summary>
[GlobalClass]
public partial class Pickup : Area2D
{
    public const string StoryScript = "pickups";
    public static readonly Vector2 Size = new(28f, 28f);

    [Export] public StringName ItemId { get; set; } = "";

    public bool Collected { get; private set; }

    public static string CountKey(StringName itemId) => "count." + itemId;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = Layers.PlayerBody;
        Monitoring = true;
        Monitorable = false;
        if (GetChildCount() == 0)
            AddChild(new CollisionShape2D { Position = new Vector2(0f, -Size.Y * 0.5f), Shape = new RectangleShape2D { Size = Size } });
        BodyEntered += OnBodyEntered;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (Collected || body == null || !ReferenceEquals(body, PlayerRegistry.Current)) return;
        Collected = true;
        Callable.From(Collect).CallDeferred();
    }

    private void Collect()
    {
        if (!IsInstanceValid(this)) return;
        string key = CountKey(ItemId);
        Variant old = GameSession.Progress.GetVar(key, 0);
        long before = old.VariantType == Variant.Type.Int ? old.AsInt64() : 0;
        GameSession.Progress.SetVar(key, before + 1);
        if (before == 0) Events.Instance?.EmitSignal(Events.SignalName.StoryRequested, StoryScript, ItemId.ToString());
        QueueFree();
    }

    public override void _Draw() => DrawRect(new Rect2(-Size.X * 0.5f, -Size.Y, Size.X, Size.Y), new Color(0.72f, 0.55f, 0.36f));
}
