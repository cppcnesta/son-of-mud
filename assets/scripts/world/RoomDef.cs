using Godot;
using Godot.Collections;

namespace SonOfMud.World;

/// <summary>房间清单里的一个房间：位置和尺寸都以 MetSys 格为单位（1 格 = MetSysSettings.in_game_cell_size）。</summary>
[GlobalClass]
public partial class RoomDef : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.File, "*.tscn")] public string ScenePath { get; set; } = "";
    /// <summary>左上角格子在全局地图上的坐标。</summary>
    [Export] public Vector2I Origin { get; set; }
    [Export] public Vector2I Size { get; set; } = Vector2I.One;
    [Export] public int Layer { get; set; }
    [Export] public bool HasSavePoint { get; set; }
    [Export] public Array<ExitDef> Exits { get; set; } = new();

    public Rect2I CellRect => new(Origin, Size);

    public bool ContainsCell(Vector2I cell) => CellRect.HasPoint(cell);

    /// <summary>出口所在的那一格（全局格坐标）。</summary>
    public Vector2I ExitCell(ExitDef exit) => exit.Side switch
    {
        ExitSide.Right => new Vector2I(Origin.X + Size.X - 1, Origin.Y + exit.Offset),
        ExitSide.Left => new Vector2I(Origin.X, Origin.Y + exit.Offset),
        ExitSide.Down => new Vector2I(Origin.X + exit.Offset, Origin.Y + Size.Y - 1),
        _ => new Vector2I(Origin.X + exit.Offset, Origin.Y),
    };

    public static Vector2I Step(ExitSide side) => side switch
    {
        ExitSide.Right => Vector2I.Right,
        ExitSide.Left => Vector2I.Left,
        ExitSide.Down => Vector2I.Down,
        _ => Vector2I.Up,
    };

    public static ExitSide Opposite(ExitSide side) => (ExitSide)(((int)side + 2) % 4);
}
