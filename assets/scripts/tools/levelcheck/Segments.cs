using System;
using System.Collections.Generic;
using Godot;
using SonOfMud.World;

namespace SonOfMud.Tools.LevelCheck;

/// <summary>
/// 一段能站的地面：同一个地面高度上、身体中心在 [MinX, MaxX] 之间都能站（全局像素，整数采样），在段内可以随便走。
/// 移动图的节点就是这些段。
/// </summary>
public sealed class Segment
{
    public LevelRoom Room;
    /// <summary>地面瓦片所在的行（文字图里的行号）。</summary>
    public int Row;
    /// <summary>地面顶边的全局 Y（= 站着时身体底边）。</summary>
    public double FloorY;
    public int MinX, MaxX;
    /// <summary>左 / 右端是被墙挡住（true），还是地面到头、再走就掉下去或出房间（false）。</summary>
    public bool WallLeft, WallRight;
    public int Index;

    public string Key => $"{Room.Id}:r{Row}:x{MinX - (int)Room.Left}";
    public double CenterY(SimConfig cfg) => FloorY - cfg.HalfH;
    public double MidX => (MinX + MaxX) * 0.5;

    public bool Contains(double x, double bottom, double tolX = 1.5, double tolY = 1.0) =>
        Math.Abs(bottom - FloorY) <= tolY && x >= MinX - tolX && x <= MaxX + tolX;

    /// <summary>给人看的位置：房间、局部像素范围和格子。</summary>
    public string Describe()
    {
        int c0 = (int)Math.Floor((MinX - Room.Left) / RoomGrid.Tile), c1 = (int)Math.Floor((MaxX - Room.Left) / RoomGrid.Tile);
        return $"{Room.Id} 第{Row}行地面 x∈[{MinX - Room.Left:F0},{MaxX - Room.Left:F0}]（格 {c0}～{c1},{Row}）";
    }

    public override string ToString() => Key;
}

public sealed class RoomSegments
{
    public LevelRoom Room { get; }
    public List<Segment> All { get; } = new();

    public RoomSegments(LevelRoom room) => Room = room;

    public Segment Find(double x, double bottom)
    {
        Segment best = null;
        foreach (Segment s in All)
            if (s.Contains(x, bottom) && (best == null || Math.Abs(s.MidX - x) < Math.Abs(best.MidX - x))) best = s;
        return best;
    }

    public static RoomSegments Build(LevelRoom room, GridWorld geo, SimConfig cfg)
    {
        var result = new RoomSegments(room);
        double hw = cfg.HalfW, h = cfg.BodySize.Y;
        int x0 = (int)Math.Ceiling(room.Left), x1 = (int)Math.Floor(room.Right) - 1;
        RoomGrid grid = room.Grid;
        for (int r = 0; r < grid.Rows; r++)
        {
            bool anyFloor = false;
            for (int c = 0; c < grid.Cols && !anyFloor; c++) anyFloor = geo.KindAt(c, r) != TileKind.Empty;
            if (!anyFloor) continue;
            double top = geo.RowTop(r);
            // 站着时身体中心必须在房间范围内（房间顶上那一排实心的“上面”不算能站的地方）
            double cy = top - h * 0.5;
            if (cy < room.Top || cy >= room.Bottom) continue;
            int start = int.MinValue;
            for (int x = x0; x <= x1 + 1; x++)
            {
                bool ok = x <= x1 && Standable(geo, x, top, r, hw, h);
                if (ok && start == int.MinValue) start = x;
                if (!ok && start != int.MinValue)
                {
                    int end = x - 1;
                    var seg = new Segment
                    {
                        Room = room, Row = r, FloorY = top, MinX = start, MaxX = end, Index = result.All.Count,
                        WallLeft = start - 1 >= room.Left && geo.Overlaps(start - 1 - hw, top - h, start - 1 + hw, top),
                        WallRight = end + 1 < room.Right && geo.Overlaps(end + 1 - hw, top - h, end + 1 + hw, top),
                    };
                    result.All.Add(seg);
                    start = int.MinValue;
                }
            }
        }
        return result;
    }

    private static bool Standable(GridWorld geo, int x, double top, int r, double hw, double h)
    {
        const double eps = 0.01;
        int c0 = geo.ColAt(x - hw + eps), c1 = geo.ColAt(x + hw - eps);
        bool support = false;
        for (int c = c0; c <= c1 && !support; c++) support = geo.KindAt(c, r) != TileKind.Empty;
        if (!support) return false;
        return !geo.Overlaps(x - hw, top - h, x + hw, top);
    }
}
