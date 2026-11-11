using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Core;
using SonOfMud.World;

namespace SonOfMud.Tools.LevelCheck;

/// <summary>参与检查的一个房间：房间表定义 + 文字图 + 全局像素位置。</summary>
public sealed class LevelRoom
{
    public RoomDef Def { get; }
    public RoomGrid Grid { get; }
    /// <summary>房间左上角的全局像素坐标 = Origin × 格子尺寸（和 MetSys 一致）。</summary>
    public Vector2 Origin { get; }
    public Vector2 SizePx { get; }
    public string Id => Def.Id;

    public LevelRoom(RoomDef def, RoomGrid grid, Vector2 cellSize)
    {
        Def = def;
        Grid = grid;
        Origin = new Vector2(def.Origin.X * cellSize.X, def.Origin.Y * cellSize.Y);
        SizePx = new Vector2(def.Size.X * cellSize.X, def.Size.Y * cellSize.Y);
    }

    public double Left => Origin.X;
    public double Right => Origin.X + SizePx.X;
    public double Top => Origin.Y;
    public double Bottom => Origin.Y + SizePx.Y;

    /// <summary>文字图里 (c, r) 这一格“脚站在这里”的全局位置。</summary>
    public Vector2 FeetGlobal(int c, int r) => Origin + Grid.FeetAt(c, r);
    public Vector2 ToLocal(Vector2 global) => global - Origin;
}

/// <summary>检查时的进度状态：已成立的剧情标记、各房间已打碎的 B / H、有没有攀墙能力。</summary>
public sealed class LevelState
{
    public HashSet<string> Flags { get; } = new();
    public Dictionary<string, HashSet<Vector2I>> Broken { get; } = new();
    public bool HasWallClimb { get; set; } = true;
    /// <summary>单房间检查时按“条件成立 / 不成立”两种情况算：不为 null 时覆盖所有房间的 R 条件。</summary>
    public bool? ConditionOverride { get; set; }

    public bool ConditionMet(LevelRoom room) =>
        ConditionOverride ?? (room.Grid.Condition.Length > 0 && Flags.Contains(room.Grid.Condition));

    public bool FlagsMet(GateRequirement req) =>
        req == null || req.Flags.All(f => Flags.Contains(f.ToString()));

    public HashSet<Vector2I> BrokenIn(string roomId)
    {
        if (!Broken.TryGetValue(roomId, out var set)) Broken[roomId] = set = new HashSet<Vector2I>();
        return set;
    }

    public LevelState Clone()
    {
        var c = new LevelState { HasWallClimb = HasWallClimb, ConditionOverride = ConditionOverride };
        c.Flags.UnionWith(Flags);
        foreach (var (k, v) in Broken) c.Broken[k] = new HashSet<Vector2I>(v);
        return c;
    }

    public string Signature() =>
        string.Join(",", Flags.OrderBy(f => f)) + "|" + ConditionOverride + "|" + HasWallClimb + "|" +
        string.Join(";", Broken.OrderBy(b => b.Key).Select(b => b.Key + ":" + string.Join(" ", b.Value.OrderBy(v => v.X).ThenBy(v => v.Y))));
}

public enum RunEnd
{
    /// <summary>脚本走完、落地站稳。</summary>
    Settled,
    /// <summary>超时（例如攀墙卡在天花板下面）。</summary>
    Timeout,
    /// <summary>从出口离开了参与检查的房间（目标房间不在检查范围内）。</summary>
    Exited,
    /// <summary>离开房间，但那里既没有出口也没有房间。</summary>
    OutOfWorld,
    /// <summary>条件门没开，过不去。</summary>
    Blocked,
    /// <summary>换房后身体和新房间的实心地形重叠（开口没对齐）。</summary>
    Stuck,
}

/// <summary>一次模拟的结果。坐标全部是全局像素。</summary>
public sealed class SimRun
{
    public RunEnd End;
    public int Frames;
    public BodySim Body;
    public LevelRoom StartRoom;
    public LevelRoom EndRoom;
    /// <summary>Exited / OutOfWorld：从哪个房间、哪个出口（没有出口时为 null）离开。</summary>
    public ExitDef Exit;
    public List<Vector2> Path;
    public List<SimState> States;
    public List<string> Crossings = new();
    public string Detail = "";

    public Vector2 EndPos => new((float)Body.X, (float)Body.Y);
}

/// <summary>
/// 参与检查的房间集合，以及“在这些房间里跑一段输入脚本”的模拟。
/// 换房规则照 WorldGame.SwapRoom(def, null)：走边缘换房时保持全局坐标，身体中心进入另一个房间的格子就换成那个房间的地形
/// （同一时刻只有当前房间的地形，其他房间不存在）；只有门、读档、复活才用出生点。
/// </summary>
public sealed class LevelWorld
{
    public static readonly Vector2 DefaultCellSize = new(576, 324);

    public Vector2 CellSize { get; }
    public Dictionary<string, LevelRoom> Rooms { get; } = new();

    public LevelWorld(Vector2 cellSize) => CellSize = cellSize;

    public LevelRoom Add(RoomDef def, RoomGrid grid)
    {
        var room = new LevelRoom(def, grid, CellSize);
        Rooms[def.Id] = room;
        return room;
    }

    public LevelRoom Find(string id) => Rooms.TryGetValue(id, out var r) ? r : null;

    public Vector2I CellOf(double x, double y) =>
        new((int)Math.Floor(x / CellSize.X), (int)Math.Floor(y / CellSize.Y));

    public LevelRoom RoomAtCell(Vector2I cell, int layer) =>
        Rooms.Values.FirstOrDefault(r => r.Def.Layer == layer && r.Def.ContainsCell(cell));

    public GridWorld Geometry(LevelRoom room, LevelState state) =>
        new(room.Grid, state.ConditionMet(room), state.BrokenIn(room.Id), room.Origin);

    /// <summary>从 room 出去、进入 cell 这一格时对应的出口（Edge 类型）；没有就是 null。</summary>
    public static ExitDef EdgeExitToward(LevelRoom room, Vector2I cell)
    {
        Rect2I rect = room.Def.CellRect;
        ExitSide side;
        int offset;
        if (cell.X >= rect.End.X) { side = ExitSide.Right; offset = cell.Y - rect.Position.Y; }
        else if (cell.X < rect.Position.X) { side = ExitSide.Left; offset = cell.Y - rect.Position.Y; }
        else if (cell.Y >= rect.End.Y) { side = ExitSide.Down; offset = cell.X - rect.Position.X; }
        else { side = ExitSide.Up; offset = cell.X - rect.Position.X; }
        foreach (ExitDef exit in room.Def.Exits)
            if (exit != null && exit.Kind == ExitKind.Edge && exit.Side == side && exit.Offset == offset) return exit;
        return null;
    }

    /// <summary>
    /// 在 startRoom 里从 (x, y)（身体中心，全局像素）开始跑脚本。startGrounded 为真表示从站立静止开始（Idle、上一帧在地上）。
    /// </summary>
    public SimRun Simulate(SimConfig cfg, LevelState state, LevelRoom startRoom, double x, double y, int form, InputScript script,
        bool recordPath = false, bool startGrounded = true, int maxFrames = 1500)
    {
        var body = new BodySim(cfg, Geometry(startRoom, state), x, y, form) { HasWallClimb = state.HasWallClimb };
        if (!startGrounded)
        {
            body.OnFloor = false;
            body.State = SimState.Fall;
        }
        var run = new SimRun { Body = body, StartRoom = startRoom, EndRoom = startRoom };
        if (recordPath)
        {
            run.Path = new List<Vector2>();
            run.States = new List<SimState>();
        }
        var runner = new ScriptRunner(script);
        var observations = new List<SimObservation>();
        SimObservation initial = body.Observe();
        LevelRoom room = startRoom;
        int delay = ScriptRunner.ObservationDelay;
        for (int j = 0; ; j++)
        {
            int k = j - 1 - delay;
            SimInput input = runner.Next(k < 0 ? initial : observations[k]);
            if (runner.Done)
            {
                run.End = runner.Failed ? RunEnd.Timeout : RunEnd.Settled;
                break;
            }
            if (j >= maxFrames)
            {
                run.End = RunEnd.Timeout;
                break;
            }
            body.Step(input);
            run.Frames = j + 1;
            run.Path?.Add(new Vector2((float)body.X, (float)body.Y));
            run.States?.Add(body.State);

            Vector2I cell = CellOf(body.X, body.Y);
            if (!room.Def.ContainsCell(cell))
            {
                ExitDef exit = EdgeExitToward(room, cell);
                LevelRoom next = RoomAtCell(cell, room.Def.Layer);
                if (next == null)
                {
                    run.End = exit != null ? RunEnd.Exited : RunEnd.OutOfWorld;
                    run.Exit = exit;
                    run.EndRoom = room;
                    run.Detail = $"{room.Id} 在 {Fmt(room, body)} 离开房间（{(exit != null ? $"出口 {exit.Side}{exit.Offset} → {exit.Target}" : "那里没有出口")}）";
                    break;
                }
                if (exit != null && exit.HasBarrier && !state.FlagsMet(exit.Requirement))
                {
                    run.End = RunEnd.Blocked;
                    run.EndRoom = room;
                    run.Detail = $"{room.Id} → {next.Id} 的条件门没开";
                    break;
                }
                run.Crossings.Add($"{room.Id}→{next.Id}{(exit == null ? "（房间表没有这个出口）" : "")}");
                room = next;
                run.EndRoom = room;
                body.World = Geometry(room, state);
                var geo = (GridWorld)body.World;
                if (geo.Overlaps(body.X - cfg.HalfW, body.Y - cfg.HalfH, body.X + cfg.HalfW, body.Y + cfg.HalfH, 0.5))
                {
                    run.End = RunEnd.Stuck;
                    run.Detail = $"换到 {room.Id} 时身体卡进墙里：{Fmt(room, body)}";
                    break;
                }
            }
            observations.Add(body.Observe());
        }
        return run;
    }

    public static string Fmt(LevelRoom room, BodySim body)
    {
        Vector2 local = room.ToLocal(new Vector2((float)body.X, (float)body.Y));
        int c = (int)Math.Floor(local.X / RoomGrid.Tile);
        int r = (int)Math.Floor((local.Y + room.Grid.Shift) / RoomGrid.Tile);
        return $"局部({local.X:F0},{local.Y:F0}) 格({c},{r})";
    }
}
