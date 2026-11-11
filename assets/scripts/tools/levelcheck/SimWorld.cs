using System;
using System.Collections.Generic;
using Godot;
using SonOfMud.World;

namespace SonOfMud.Tools.LevelCheck;

/// <summary>模拟器眼里的瓦片：空、实心可攀、实心不可攀、单向平台。</summary>
public enum TileKind
{
    Empty,
    Solid,
    NoClimb,
    OneWay,
}

public struct SimTile
{
    public double X0, Y0, X1, Y1;
    public TileKind Kind;
    public int C, R;
}

/// <summary>模拟器查询碰撞的世界：返回和给定矩形相交的非空瓦片。</summary>
public interface ISimWorld
{
    void Query(double x0, double y0, double x1, double y1, List<SimTile> output);
}

/// <summary>
/// 一张 RoomGrid 在某个“条件状态”下的碰撞世界：R 是否出现、哪些 B / H 已经打碎。
/// 坐标 = 房间局部像素 + Offset（单房间检查用局部坐标，跨房间检查用全局坐标）。
/// 瓦片 (c, r) 的矩形 = (Offset.X + c*32, Offset.Y + r*32 - Shift)，和 RoomGrid.TileRect 一致。
/// 房间范围外一律是空（和 RoomGrid.At 一致）。
/// </summary>
public sealed class GridWorld : ISimWorld
{
    public RoomGrid Grid { get; }
    public bool ConditionMet { get; }
    public IReadOnlySet<Vector2I> Broken { get; }
    public Vector2 Offset { get; }

    public GridWorld(RoomGrid grid, bool conditionMet, IReadOnlySet<Vector2I> broken = null, Vector2 offset = default)
    {
        Grid = grid;
        ConditionMet = conditionMet;
        Broken = broken ?? new HashSet<Vector2I>();
        Offset = offset;
    }

    public static TileKind KindOf(char ch, bool conditionMet) => ch switch
    {
        RoomGrid.NoClimb => TileKind.NoClimb,
        RoomGrid.OneWay => TileKind.OneWay,
        _ => RoomGrid.IsSolidChar(ch, conditionMet) ? TileKind.Solid : TileKind.Empty,
    };

    public TileKind KindAt(int c, int r)
    {
        if (!Grid.InBounds(c, r)) return TileKind.Empty;
        if (Broken.Count > 0 && Broken.Contains(new Vector2I(c, r))) return TileKind.Empty;
        return KindOf(Grid.At(c, r), ConditionMet);
    }

    public bool IsBlocking(int c, int r) => KindAt(c, r) is TileKind.Solid or TileKind.NoClimb;

    /// <summary>瓦片 (c, r) 顶边的 Y（当前坐标系）。</summary>
    public double RowTop(int r) => Offset.Y + r * (double)RoomGrid.Tile - Grid.Shift;
    public double ColLeft(int c) => Offset.X + c * (double)RoomGrid.Tile;

    public int ColAt(double x) => (int)Math.Floor((x - Offset.X) / RoomGrid.Tile);
    public int RowAt(double y) => (int)Math.Floor((y - Offset.Y + Grid.Shift) / RoomGrid.Tile);

    public void Query(double x0, double y0, double x1, double y1, List<SimTile> output)
    {
        int c0 = Math.Max(0, ColAt(x0)), c1 = Math.Min(Grid.Cols - 1, ColAt(x1));
        int r0 = Math.Max(0, RowAt(y0)), r1 = Math.Min(Grid.Rows - 1, RowAt(y1));
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                TileKind kind = KindAt(c, r);
                if (kind == TileKind.Empty) continue;
                double left = ColLeft(c), top = RowTop(r);
                output.Add(new SimTile { X0 = left, Y0 = top, X1 = left + RoomGrid.Tile, Y1 = top + RoomGrid.Tile, Kind = kind, C = c, R = r });
            }
    }

    /// <summary>矩形（当前坐标系）是否和挡路的瓦片重叠（重叠深度大于 eps）。</summary>
    public bool Overlaps(double x0, double y0, double x1, double y1, double eps = 0.01)
    {
        var list = new List<SimTile>();
        Query(x0, y0, x1, y1, list);
        foreach (SimTile t in list)
        {
            if (t.Kind == TileKind.OneWay) continue;
            if (x1 > t.X0 + eps && x0 < t.X1 - eps && y1 > t.Y0 + eps && y0 < t.Y1 - eps) return true;
        }
        return false;
    }
}
