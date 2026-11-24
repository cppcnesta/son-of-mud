using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 收到 ItemDropped(物品, 位置) 后，在当前房间的 Objects 下生成一个 Pickup（只听 Events，不认识 Enemy）。
/// 拾取规则按物品编号从 Items 里找（泥瓶、泥肝）；找不到就生成普通计数物品。
/// 拾取物属于房间：离开房间没捡的就随房间一起释放。生成放到本帧末尾，避免在战斗结算的回调里加节点。
/// 掉落物没有重力：生成时从掉落位置往下找地面（世界层），放在地面上，不会悬在半空（敌人死在空中、被打飞时）。
/// 也负责“用掉一个存着的道具”（Events.UseItemRequested，收集页里喝泥瓶）：见 <see cref="UseItem"/>。
/// </summary>
[GlobalClass]
public partial class DropSpawner : Node
{
    /// <summary>往下找地面的最远距离（像素）。超过这么远都没有地面就留在原处。</summary>
    public const float GroundSearch = 2000f;

    [Export] public WorldGame World { get; set; }
    /// <summary>各种拾取物的定义（assets/data/world/items/）。</summary>
    [Export] public Godot.Collections.Array<PickupItem> Items { get; set; } = new();

    public int Spawned { get; private set; }

    public override void _EnterTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.ItemDropped += OnItemDropped;
        Events.Instance.UseItemRequested += OnUseItemRequested;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.ItemDropped -= OnItemDropped;
        Events.Instance.UseItemRequested -= OnUseItemRequested;
    }

    private void OnUseItemRequested(StringName itemId) => UseItem(itemId);

    /// <summary>
    /// 用掉一个存着的回血道具（泥瓶）：有存货、玩家活着且没满血时回 HealAmount、计数减 1，返回 null；
    /// 用不了时什么都不改，返回原因（给界面显示）。
    /// </summary>
    public string UseItem(StringName itemId)
    {
        PickupItem item = Find(itemId);
        if (item == null || !item.Counted || item.HealAmount <= 0) return "这个东西不能用。";
        long count = Pickup.CountOf(itemId);
        if (count <= 0) return $"没有{itemId}了。";
        Node2D player = PlayerRegistry.Current;
        Combat.Stats stats = player != null && IsInstanceValid(player) ? Pickup.FindStats(player) : null;
        if (stats == null || stats.IsDepleted) return "现在不能用。";
        if (stats.Health >= stats.MaxHealth) return "血是满的，现在不用喝。";
        stats.Heal(item.HealAmount);
        GameSession.Progress.SetVar(Pickup.CountKey(itemId), count - 1);
        return null;
    }

    private void OnItemDropped(StringName itemId, Vector2 position) =>
        Callable.From(() => Spawn(itemId, position)).CallDeferred();

    public Pickup Spawn(StringName itemId, Vector2 position)
    {
        if (!IsInstanceValid(this) || World == null || !IsInstanceValid(World.CurrentRoom)) return null;
        RoomRoot room = World.CurrentRoom;
        Node2D parent = room.GetNodeOrNull<Node2D>("Objects") ?? room;
        var pickup = new Pickup { Name = $"Pickup_{itemId}_{++Spawned}", ItemId = itemId, Item = Find(itemId) };
        parent.AddChild(pickup);
        pickup.GlobalPosition = Ground(pickup, position);
        return pickup;
    }

    /// <summary>从 position 往下找最近的地面（世界层的地形、门、平台），返回地面上的点；找不到就原样返回。</summary>
    public static Vector2 Ground(Node2D node, Vector2 position)
    {
        PhysicsDirectSpaceState2D space = node.GetWorld2D()?.DirectSpaceState;
        if (space == null) return position;
        // 从上面一点开始找：掉落位置正好在地面上（敌人的脚底）时，射线不会从地面里面出发而漏掉这块地
        var from = position + new Vector2(0f, -8f);
        var query = PhysicsRayQueryParameters2D.Create(from, position + new Vector2(0f, GroundSearch), Layers.World);
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        return hit.Count > 0 ? hit["position"].AsVector2() : position;
    }

    public PickupItem Find(StringName itemId)
    {
        if (Items == null) return null;
        foreach (PickupItem item in Items)
            if (item != null && item.ItemId == itemId) return item;
        return null;
    }
}
