using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 房间表的只读查询，给不能引用 World 的界面用（大地图要知道每个房间占哪些格、某一格属于哪个房间）。
/// World 启动时把房间表登记成 <see cref="Provider"/>（WorldGame 进场景树时），离开时取消；没登记时查询都返回空。
/// </summary>
public static class RoomCatalog
{
    public interface IProvider
    {
        /// <summary>房间占的全部格（含图层）；没有这个房间时为空。</summary>
        IEnumerable<Vector3I> CellsOf(string roomId);
        /// <summary>这一格属于哪个房间；不属于任何房间时是空字符串。</summary>
        string RoomAt(Vector3I cell);
    }

    public static IProvider Provider { get; set; }

    public static IEnumerable<Vector3I> CellsOf(string roomId) =>
        Provider == null || string.IsNullOrEmpty(roomId) ? Enumerable.Empty<Vector3I>() : Provider.CellsOf(roomId);

    public static string RoomAt(Vector3I cell) => Provider?.RoomAt(cell) ?? "";
}
