using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SonOfMud.Tools.LevelCheck;

/// <summary>
/// 路线的一步（给真实按键机器人用）：在 Room 里走到 StartX（房间局部像素），用 Form 形态按 Script 做动作，
/// 期望结束在 TargetRoom 的目标段上（或从出口离开 / 打碎一块地形 / 过门）。坐标都是房间局部像素。
/// </summary>
public sealed class RouteStep
{
    public string Room = "";
    public float StartX;
    /// <summary>起点顶着墙：-1 左墙、1 右墙、0 不是（机器人要顶到墙为止，而不是停在某个 x）。</summary>
    public int WallSide;
    public float FloorY;
    public int Form;
    public EdgeKind Kind;
    public InputScript Script;
    public string TargetRoom = "";
    public float TargetFloorY;
    public float TargetMinX, TargetMaxX;
    public Vector2 ExpectedEnd;
    public int BreakDir;
    public Vector2I BreakTile;
    public string Text = "";

    public override string ToString() => Text;
}

public static class RoutePlan
{
    public static List<RouteStep> From(IEnumerable<MoveEdge> edges) => edges.Select(Step).ToList();

    public static RouteStep Step(MoveEdge e)
    {
        LevelRoom from = e.From.Room;
        var step = new RouteStep
        {
            Room = from.Id,
            StartX = (float)(e.StartX - from.Left),
            WallSide = e.From.WallLeft && e.StartX <= e.From.MinX ? -1 : e.From.WallRight && e.StartX >= e.From.MaxX ? 1 : 0,
            FloorY = (float)(e.From.FloorY - from.Top),
            Form = e.Form,
            Kind = e.Kind,
            Script = e.Action?.Script,
            BreakDir = e.BreakDir,
            BreakTile = e.BreakTile,
            Text = e.Describe(),
        };
        if (e.To != null)
        {
            LevelRoom to = e.To.Room;
            step.TargetRoom = to.Id;
            step.TargetFloorY = (float)(e.To.FloorY - to.Top);
            step.TargetMinX = (float)(e.To.MinX - to.Left);
            step.TargetMaxX = (float)(e.To.MaxX - to.Left);
            step.ExpectedEnd = e.End - to.Origin;
        }
        else if (e.Kind == EdgeKind.Exit)
        {
            step.TargetRoom = e.Exit.Target;
            step.ExpectedEnd = e.End - from.Origin;
        }
        return step;
    }
}
