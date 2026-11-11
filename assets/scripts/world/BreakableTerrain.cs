using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 可破坏地形（文字图里的 B / H，关卡几何计划 v3 第 2 节）：一个连通块一个节点。
/// World 碰撞层的实心体（能站、能攀爬）+ 敌人受击层的受击框，被玩家的普通攻击（不要求重击）打中就碎。
/// 碎了写进度变量 <see cref="BreakKey"/>（broken.&lt;房间&gt;.&lt;序号&gt; = true），之后进房间、读档都保持碎掉。
/// H（HiddenWall）外观和 # 完全一样（同一个图块）；B 用带裂纹的图块。
/// 子节点（碰撞形状、受击框、显示用的瓦片层）都在 _Ready 里按 Cells 生成，场景里只存 Cells / HiddenWall / BreakKey。
/// 由 BuildWorld --geometry 生成，放在房间根下的 <see cref="ContainerName"/> 里，位置 (0, -Shift)。
/// </summary>
[GlobalClass]
public partial class BreakableTerrain : StaticBody2D
{
    public const string ContainerName = "Breakables";

    /// <summary>这一块占的格子（文字图坐标）。</summary>
    [Export] public Godot.Collections.Array<Vector2I> Cells { get; set; } = new();
    /// <summary>H：外观和 # 一样、地图上不标。</summary>
    [Export] public bool HiddenWall { get; set; }
    /// <summary>碎掉后写入的进度变量。</summary>
    [Export] public StringName BreakKey { get; set; } = "";

    public bool Broken { get; private set; }
    public HurtBox HurtBox { get; private set; }
    public Stats Stats { get; private set; }
    /// <summary>显示用的瓦片层（不带碰撞）。</summary>
    public TileMapLayer Tiles { get; private set; }

    public static StringName KeyFor(string roomId, int index) => $"broken.{roomId}.{index}";

    /// <summary>生成工具和测试共用。</summary>
    public static BreakableTerrain Create(IEnumerable<Vector2I> cells, bool hidden, StringName key, int shift, string name) =>
        new()
        {
            Name = name,
            Cells = new Godot.Collections.Array<Vector2I>(cells.OrderBy(c => c.Y).ThenBy(c => c.X)),
            HiddenWall = hidden,
            BreakKey = key,
            Position = new Vector2(0, -shift),
        };

    public override void _Ready()
    {
        CollisionLayer = Layers.World;
        CollisionMask = 0;
        Stats = new Stats { Name = "Stats", MaxHealth = 1 };
        AddChild(Stats);
        HurtBox = new HurtBox { Name = "HurtBox", EntityRoot = this, Stats = Stats, HeavyOnly = false, CollisionLayer = Layers.EnemyHurt, CollisionMask = 0 };
        Tiles = new TileMapLayer { Name = "Tiles", TileSet = GD.Load<TileSet>(TerrainTiles.TileSetPath), CollisionEnabled = false };
        foreach (Rect2 run in Runs())
        {
            AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = run.Size }, Position = run.GetCenter() });
            HurtBox.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = run.Size }, Position = run.GetCenter() });
        }
        foreach (Vector2I cell in Cells) Tiles.SetCell(cell, TerrainTiles.SourceId, HiddenWall ? TerrainTiles.SolidAtlas : TerrainTiles.CrackedAtlas);
        AddChild(HurtBox);
        AddChild(Tiles);
        Stats.Depleted += Break;
        if (!string.IsNullOrEmpty(BreakKey) && GameSession.Progress.Flag(BreakKey)) ApplyBroken();
    }

    /// <summary>每一行里连续的格子合成一个矩形（局部像素）。</summary>
    private IEnumerable<Rect2> Runs()
    {
        int t = TerrainTiles.Tile;
        foreach (var row in Cells.GroupBy(c => c.Y))
        {
            List<int> xs = row.Select(c => c.X).OrderBy(x => x).ToList();
            int start = xs[0], prev = xs[0];
            foreach (int x in xs.Skip(1).Append(int.MinValue))
            {
                if (x == prev + 1) { prev = x; continue; }
                yield return new Rect2(start * t, row.Key * t, (prev - start + 1) * t, t);
                start = prev = x;
            }
        }
    }

    private void Break()
    {
        if (Broken) return;
        if (!string.IsNullOrEmpty(BreakKey)) GameSession.Progress.SetVar(BreakKey, true);
        ApplyBroken();
    }

    private void ApplyBroken()
    {
        Broken = true;
        CollisionLayer = 0;
        Visible = false;
        if (HurtBox != null)
        {
            HurtBox.Receiving = false;
            HurtBox.CollisionLayer = 0;
        }
        foreach (Node child in GetChildren())
            if (child is CollisionShape2D shape) shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
    }
}
