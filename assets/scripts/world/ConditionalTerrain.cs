using System.Collections.Generic;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 条件地形（文字图里的 R，关卡几何计划 v3 第 4.1 节）：一层瓦片，条件（GameSession.Progress.Meets）成立时才显示并启用碰撞。
/// 出现后外观和 # 一样，可攀爬。
/// - 收到 VariableChanged / AbilityUnlocked 时重新判断（和 GateBarrier 一样）；
/// - 条件成立时，如果玩家的身体和任一瓦片重叠，先不出现，每个物理帧检查一次，等玩家离开再出现（不把人卡在地形里）；
/// - 进房间、读档时（_Ready）条件已经成立就直接出现（生成工具保证出生点不和 R 重叠）。
/// 由 BuildWorld --geometry 生成，节点名 <see cref="NodeName"/>，位置 (0, -Shift)，和 Geometry 对齐。
/// </summary>
[GlobalClass]
public partial class ConditionalTerrain : TileMapLayer
{
    public const string NodeName = "ConditionalTerrain";

    [Export] public GateRequirement Requirement { get; set; }

    /// <summary>已经出现（可见、有碰撞）。</summary>
    public bool IsShown { get; private set; }
    /// <summary>条件已经成立，但玩家挡着，正在等他离开。</summary>
    public bool IsWaiting { get; private set; }

    /// <summary>生成工具和测试共用：按格子坐标建一个条件地形（瓦片用 # 的图块）。</summary>
    public static ConditionalTerrain Create(IEnumerable<Vector2I> cells, StringName flag, int shift)
    {
        var layer = new ConditionalTerrain
        {
            Name = NodeName,
            TileSet = GD.Load<TileSet>(TerrainTiles.TileSetPath),
            Position = new Vector2(0, -shift),
            Requirement = new GateRequirement { Flags = new Godot.Collections.Array<StringName> { flag } },
        };
        foreach (Vector2I cell in cells) layer.SetCell(cell, TerrainTiles.SourceId, TerrainTiles.SolidAtlas);
        return layer;
    }

    public override void _EnterTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.VariableChanged += OnVariableChanged;
        Events.Instance.AbilityUnlocked += OnAbilityUnlocked;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.VariableChanged -= OnVariableChanged;
        Events.Instance.AbilityUnlocked -= OnAbilityUnlocked;
    }

    public override void _Ready()
    {
        SetPhysicsProcess(false);
        Refresh(entering: true);
    }

    private void OnVariableChanged(StringName key, Variant value) => Refresh();
    private void OnAbilityUnlocked(StringName abilityId) => Refresh();

    /// <summary>重新判断条件。entering 为 true 表示刚进房间（条件成立就直接出现，不等玩家）。</summary>
    public void Refresh(bool entering = false)
    {
        bool met = GameSession.Progress.Meets(Requirement, out _);
        if (!met)
        {
            IsWaiting = false;
            SetPhysicsProcess(false);
            Apply(false);
            return;
        }
        if (IsShown) return;
        if (!entering && PlayerOverlaps())
        {
            IsWaiting = true;
            SetPhysicsProcess(true);
            return;
        }
        IsWaiting = false;
        SetPhysicsProcess(false);
        Apply(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsWaiting)
        {
            SetPhysicsProcess(false);
            return;
        }
        if (PlayerOverlaps()) return;
        IsWaiting = false;
        SetPhysicsProcess(false);
        Apply(true);
    }

    private void Apply(bool shown)
    {
        IsShown = shown;
        Visible = shown;
        CollisionEnabled = shown;
        if (IsInsideTree()) UpdateInternals();
    }

    /// <summary>
    /// 当前玩家（PlayerRegistry）的碰撞形状和任一瓦片是否重叠（贴边不算）。按几何计算，不用物理查询，
    /// 所以在物理回调里（例如区域信号触发的剧情标记）调用也安全。
    /// </summary>
    public bool PlayerOverlaps()
    {
        if (PlayerRegistry.Current is not CollisionObject2D player || !IsInsideTree()) return false;
        var bodies = new List<Rect2>();
        foreach (Node child in player.GetChildren())
            if (child is CollisionShape2D { Disabled: false, Shape: not null } shape)
                bodies.Add(shape.GlobalTransform * shape.Shape.GetRect());
        if (bodies.Count == 0) return false;
        Vector2 size = TileSet?.TileSize ?? new Vector2I(TerrainTiles.Tile, TerrainTiles.Tile);
        foreach (Vector2I cell in GetUsedCells())
        {
            Rect2 tile = GlobalTransform * new Rect2(MapToLocal(cell) - size / 2f, size);
            foreach (Rect2 body in bodies)
                if (tile.Intersects(body)) return true;
        }
        return false;
    }
}
