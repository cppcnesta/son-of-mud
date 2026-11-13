using Godot;
using Godot.Collections;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 整张地图的唯一清单：房间、尺寸、出口、条件。MetSys 的 MapData.txt 和房间场景骨架都由它生成（tools/world/BuildWorld.tscn），
/// 不要在 MetSys 编辑器里手改地图，否则两边会不一致。
/// </summary>
[Tool]
[GlobalClass]
public partial class WorldLayout : KeptResource
{
    public const string DefaultPath = "res://assets/data/world/world_layout.tres";

    [Export] public Array<RoomDef> Rooms { get; set; } = new();

    public RoomDef Find(string id)
    {
        foreach (RoomDef room in Rooms)
            if (room != null && room.Id == id) return room;
        return null;
    }

    public RoomDef FindByScenePath(string path)
    {
        foreach (RoomDef room in Rooms)
            if (room != null && room.ScenePath == path) return room;
        return null;
    }

    /// <summary>MetSys 的房间 ID（uid://… 或 res://…）→ 房间定义。</summary>
    public RoomDef FindByMetSysId(string metSysId)
    {
        string path = metSysId;
        if (metSysId.StartsWith("uid://"))
        {
            long uid = ResourceUid.TextToId(metSysId);
            if (uid == ResourceUid.InvalidId || !ResourceUid.HasId(uid)) return null;
            path = ResourceUid.GetIdPath(uid);
        }
        return FindByScenePath(path);
    }

    /// <summary>全局格坐标属于哪个房间；没有就是 null。</summary>
    public RoomDef RoomAtCell(Vector2I cell, int layer = 0)
    {
        foreach (RoomDef room in Rooms)
            if (room != null && room.Layer == layer && room.ContainsCell(cell)) return room;
        return null;
    }

    /// <summary>按房间编号或场景路径查找（TravelRequested 两种写法都接受）。</summary>
    public RoomDef Resolve(string idOrPath) =>
        Find(idOrPath) ?? (idOrPath.StartsWith("uid://") ? FindByMetSysId(idOrPath) : FindByScenePath(idOrPath));
}
