using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间场景的根节点。结构：RoomInstance（MetSys，位置 0,0）、Geometry、Spawns、Objects。
/// </summary>
[GlobalClass]
public partial class RoomRoot : Node2D
{
    [Export] public string RoomId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";

    /// <summary>按 SpawnId 找出生点；找不到时退回 Default，再找不到返回 null。</summary>
    public SpawnPoint FindSpawn(StringName spawnId)
    {
        SpawnPoint exact = Search(this, spawnId);
        if (exact != null) return exact;
        SpawnPoint fallback = Search(this, SpawnPoint.DefaultId);
        GD.PushWarning($"房间 {RoomId} 没有出生点 {spawnId}，改用 Default。");
        return fallback;
    }

    private static SpawnPoint Search(Node node, StringName spawnId)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is SpawnPoint spawn && spawn.SpawnId == spawnId) return spawn;
            SpawnPoint found = Search(child, spawnId);
            if (found != null) return found;
        }
        return null;
    }
}
