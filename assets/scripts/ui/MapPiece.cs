using System;
using Godot;

namespace SonOfMud.UI;

/// <summary>
/// 大地图上的一块泥板碎片（map_screen.tscn 里 Pieces 下的一个 TextureRect）。
/// Rooms：这块碎片代表的房间（七重塔六个房间合用一块）；去过其中任何一个，这块就显示。
/// TabletIds：这些房间里能捡的泥板；还有没读过的，子节点 Tablet（图标）就显示。子节点 Save 是存档点图标，跟着碎片一起显示。
/// </summary>
[GlobalClass]
public partial class MapPiece : TextureRect
{
    [Export] public string[] Rooms { get; set; } = Array.Empty<string>();
    [Export] public string[] TabletIds { get; set; } = Array.Empty<string>();

    public bool Has(string roomId) => !string.IsNullOrEmpty(roomId) && Array.IndexOf(Rooms, roomId) >= 0;

    /// <summary>碎片中心（在 Pieces 里的坐标）。</summary>
    public Vector2 Center => Position + Size * 0.5f;
}
