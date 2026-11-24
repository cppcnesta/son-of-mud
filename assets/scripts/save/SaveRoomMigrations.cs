using Godot;
using SonOfMud.Core;

namespace SonOfMud.Save;

/// <summary>
/// 旧存档迁移表（assets/data/save/room_migrations.tres）：改地图后，旧存档里的检查点房间号 / 存档点编号已经不存在时，换成新的。
/// 读档时（SaveService.LoadFromDisk）存档里的房间在 Rooms 里就换成对应的新房间，存档点编号在 SavePoints 里就换成新编号；
/// 进度变量、能力、地图数据都不动。下一次在存档点存档时写的就是新房间号。
/// 表里的旧房间号不能和现在房间表里的房间重名（测试检查）。
/// 现在只有一条：地图四按美术比例重做后，旧心室祭坛 S4_04（存档点 S4_04）→ 新心室祭坛 S4_10（存档点 S4_10）。
/// </summary>
[GlobalClass]
public partial class SaveRoomMigrations : KeptResource
{
    public const string DefaultPath = "res://assets/data/save/room_migrations.tres";

    /// <summary>旧房间号 → 新房间号。</summary>
    [Export] public Godot.Collections.Dictionary<string, string> Rooms { get; set; } = new();
    /// <summary>旧存档点编号 → 新存档点编号。</summary>
    [Export] public Godot.Collections.Dictionary<string, string> SavePoints { get; set; } = new();

    public static SaveRoomMigrations LoadDefault() =>
        ResourceLoader.Exists(DefaultPath) ? GD.Load<SaveRoomMigrations>(DefaultPath) : new SaveRoomMigrations();

    /// <summary>按表换房间号和存档点编号；返回有没有换。</summary>
    public bool Apply(ref string room, ref string savePoint)
    {
        bool changed = false;
        if (room != null && Rooms.TryGetValue(room, out string newRoom) && !string.IsNullOrEmpty(newRoom)) { room = newRoom; changed = true; }
        if (savePoint != null && SavePoints.TryGetValue(savePoint, out string newPoint) && !string.IsNullOrEmpty(newPoint)) { savePoint = newPoint; changed = true; }
        return changed;
    }
}
