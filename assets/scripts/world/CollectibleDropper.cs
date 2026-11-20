using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 敌人掉落的收集品：收到 EnemyKilled(种类, 位置) 时，Collectibles 里 DroppedBy 含这个种类、而且还没捡过的，
/// 在当前房间的 Objects 下生成一个 CollectiblePickup。没捡就离开房间，它随房间释放；下次打死同种敌人还会再掉。
/// 位置从敌人倒下的点往下找地面（找不到就用原位置）。和泥瓶 / 泥肝的掉落（DropSpawner）互不影响。
/// 生成放到本帧末尾，避免在战斗结算的回调里加节点。只听 Events，不认识 Enemy。
/// </summary>
[GlobalClass]
public partial class CollectibleDropper : Node
{
    /// <summary>往下找地面的最远距离（像素）。</summary>
    public const float GroundSearch = 400f;

    [Export] public WorldGame World { get; set; }
    [Export] public Godot.Collections.Array<CollectibleDef> Collectibles { get; set; } = new();

    public int Spawned { get; private set; }

    public override void _EnterTree()
    {
        if (Events.Instance != null) Events.Instance.EnemyKilled += OnEnemyKilled;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.EnemyKilled -= OnEnemyKilled;
    }

    private void OnEnemyKilled(StringName kind, Vector2 position)
    {
        if (Collectibles == null) return;
        foreach (CollectibleDef def in Collectibles)
            if (def != null && def.DroppedBy.Contains(kind) && !def.Collected)
                Callable.From(() => Spawn(def, position)).CallDeferred();
    }

    public CollectiblePickup Spawn(CollectibleDef def, Vector2 position)
    {
        if (!IsInstanceValid(this) || def == null || def.Collected || World == null || !IsInstanceValid(World.CurrentRoom)) return null;
        RoomRoot room = World.CurrentRoom;
        Node2D parent = room.GetNodeOrNull<Node2D>("Objects") ?? room;
        var pickup = new CollectiblePickup { Name = $"Collectible_{def.Id}_{++Spawned}", Def = def };
        parent.AddChild(pickup);
        pickup.GlobalPosition = Ground(room, position);
        return pickup;
    }

    /// <summary>从 from 往下 GroundSearch 像素内找第一块地面（World 层）；找不到返回 from。</summary>
    private static Vector2 Ground(Node2D room, Vector2 from)
    {
        var space = room.GetWorld2D()?.DirectSpaceState;
        if (space == null) return from;
        var query = PhysicsRayQueryParameters2D.Create(from + new Vector2(0f, -8f), from + new Vector2(0f, GroundSearch), Layers.World);
        var hit = space.IntersectRay(query);
        return hit.Count > 0 ? (Vector2)hit["position"] : from;
    }
}
