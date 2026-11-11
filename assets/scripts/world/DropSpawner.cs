using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 收到 ItemDropped(物品, 位置) 后，在当前房间的 Objects 下生成一个 Pickup（只听 Events，不认识 Enemy）。
/// 拾取物属于房间：离开房间没捡的就随房间一起释放。生成放到本帧末尾，避免在战斗结算的回调里加节点。
/// </summary>
[GlobalClass]
public partial class DropSpawner : Node
{
    [Export] public WorldGame World { get; set; }

    public int Spawned { get; private set; }

    public override void _EnterTree()
    {
        if (Events.Instance != null) Events.Instance.ItemDropped += OnItemDropped;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.ItemDropped -= OnItemDropped;
    }

    private void OnItemDropped(StringName itemId, Vector2 position) =>
        Callable.From(() => Spawn(itemId, position)).CallDeferred();

    public Pickup Spawn(StringName itemId, Vector2 position)
    {
        if (!IsInstanceValid(this) || World == null || !IsInstanceValid(World.CurrentRoom)) return null;
        RoomRoot room = World.CurrentRoom;
        Node2D parent = room.GetNodeOrNull<Node2D>("Objects") ?? room;
        var pickup = new Pickup { Name = $"Pickup_{itemId}_{++Spawned}", ItemId = itemId };
        parent.AddChild(pickup);
        pickup.GlobalPosition = position;
        return pickup;
    }
}
