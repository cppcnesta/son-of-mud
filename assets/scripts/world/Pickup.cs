using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 掉落的拾取物（泥瓶、泥肝……）。规则来自 PickupItem（没有定义时当作普通计数物品，不播台词）：
/// - 玩家身体和它重叠时尝试拾取（每个物理帧都看，所以站在上面时条件一满足就捡起来）；
/// - 回血道具：玩家满血不捡；没满就回 HealAmount（不超过上限）；
/// - 计数道具：count.&lt;ItemId&gt; 加 1；到了 MaxCount 就不捡；
/// - PlaysLine 为真时，第一次拾取（加之前是 0）发 StoryRequested("pickups", ItemId) 播拾取台词。
/// 捡不起来就留在地上，随房间一起释放。只认 PlayerRegistry.Current（玩家只当 Node2D 用，血量从它的 Combat.Stats 子节点读）。
/// 拾取在本帧末尾结算，避免在物理回调里改状态。
/// </summary>
[GlobalClass]
public partial class Pickup : Area2D
{
    public const string StoryScript = "pickups";
    public static readonly Vector2 Size = new(28f, 28f);

    [Export] public StringName ItemId { get; set; } = "";
    [Export] public PickupItem Item { get; set; }

    public bool Collected { get; private set; }

    public static string CountKey(StringName itemId) => ItemCounts.Key(itemId);

    /// <summary>某种物品当前的计数（没有或类型不对时是 0）。</summary>
    public static long CountOf(StringName itemId) => ItemCounts.Get(itemId);

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = Layers.PlayerBody;
        Monitoring = true;
        Monitorable = false;
        if (GetChildCount() == 0)
            AddChild(new CollisionShape2D { Position = new Vector2(0f, -Size.Y * 0.5f), Shape = new RectangleShape2D { Size = Size } });
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Collected) return;
        Node2D player = PlayerRegistry.Current;
        if (player == null || !IsInstanceValid(player) || !OverlapsBody(player) || !CanCollect(player)) return;
        Collected = true;
        Callable.From(Collect).CallDeferred();
    }

    /// <summary>现在能不能捡：回血道具要求玩家没满血；计数道具要求没到上限。</summary>
    public bool CanCollect(Node2D player)
    {
        if (Item == null) return true;
        if (Item.HealAmount > 0)
        {
            Stats stats = FindStats(player);
            if (stats == null || stats.Health >= stats.MaxHealth || stats.IsDepleted) return false;
        }
        if (Item.Counted && Item.MaxCount > 0 && CountOf(ItemId) >= Item.MaxCount) return false;
        return true;
    }

    private void Collect()
    {
        if (!IsInstanceValid(this)) return;
        Node2D player = PlayerRegistry.Current;
        // 延到帧末这段时间里条件变了（比如被打死了）：放弃这次，下一帧重新判断。
        if (player == null || !IsInstanceValid(player) || !CanCollect(player))
        {
            Collected = false;
            return;
        }
        if (Item is { HealAmount: > 0 }) FindStats(player)?.Heal(Item.HealAmount);
        if (Item == null || Item.Counted)
        {
            long before = CountOf(ItemId);
            GameSession.Progress.SetVar(CountKey(ItemId), before + 1);
            if (before == 0 && Item is { PlaysLine: true })
                Events.Instance?.EmitSignal(Events.SignalName.StoryRequested, StoryScript, ItemId.ToString());
        }
        QueueFree();
    }

    private static Stats FindStats(Node2D player)
    {
        foreach (Node child in player.GetChildren())
            if (child is Stats stats) return stats;
        return null;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw() =>
        DrawRect(new Rect2(-Size.X * 0.5f, -Size.Y, Size.X, Size.Y), Item?.Tint ?? new Color(0.72f, 0.55f, 0.36f));
}
