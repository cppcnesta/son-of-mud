using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 32 像素地形瓦片集（关卡几何计划 v3 第 3.2 节）：瓦片集由这里按代码生成（BuildWorld 写到 <see cref="TileSetPath"/>），
/// 生成工具、条件地形、可破坏地形都用这里的图块坐标，保证 H / R 的外观和 # 完全一样。
/// 图块（同一个图集源，从左到右）：
///   # 可攀实心（棕色）；X 不可攀实心（深色加斜条纹，自定义数据 no_climb = true）；
///   - 单向平台（只有顶部 8 像素的木板，碰撞也只在顶部、开启 one_way）；B 可破坏（偏橙、带深色裂纹，只用来显示）。
/// </summary>
public static class TerrainTiles
{
    public const string TileSetPath = "res://assets/data/world/world_tileset.tres";
    public const int Tile = RoomGrid.Tile;
    public const int SourceId = 0;
    /// <summary>单向平台碰撞条的厚度（从瓦片顶边往下）。</summary>
    public const int OneWayThickness = 8;

    public static readonly Vector2I SolidAtlas = new(0, 0);
    public static readonly Vector2I NoClimbAtlas = new(1, 0);
    public static readonly Vector2I OneWayAtlas = new(2, 0);
    public static readonly Vector2I CrackedAtlas = new(3, 0);

    public static readonly Color SolidColor = new(0.36f, 0.31f, 0.26f);
    public static readonly Color SolidEdge = new(0.46f, 0.40f, 0.33f);
    public static readonly Color NoClimbColor = new(0.13f, 0.12f, 0.16f);
    public static readonly Color NoClimbStripe = new(0.24f, 0.22f, 0.30f);
    public static readonly Color OneWayColor = new(0.66f, 0.53f, 0.33f);
    public static readonly Color CrackedColor = new(0.58f, 0.40f, 0.22f);
    public static readonly Color CrackColor = new(0.18f, 0.10f, 0.06f);

    private static readonly Vector2I[] AllAtlas = { SolidAtlas, NoClimbAtlas, OneWayAtlas, CrackedAtlas };

    /// <summary>文字图字符 → 画这一格用的图块。'.' 和标记字符返回 null。H、R 用 # 的图块。</summary>
    public static Vector2I? AtlasFor(char ch) => ch switch
    {
        RoomGrid.Solid or RoomGrid.HiddenBreakable or RoomGrid.Conditional => SolidAtlas,
        RoomGrid.NoClimb => NoClimbAtlas,
        RoomGrid.OneWay => OneWayAtlas,
        RoomGrid.Breakable => CrackedAtlas,
        _ => null,
    };

    /// <summary>Geometry 层里的图块 → 字符（导出文字图时用；旧的 64 像素瓦片集只有 (0,0)，也是 #）。</summary>
    public static char CharForGeometry(Vector2I atlas)
    {
        if (atlas == NoClimbAtlas) return RoomGrid.NoClimb;
        if (atlas == OneWayAtlas) return RoomGrid.OneWay;
        return RoomGrid.Solid;
    }

    /// <summary>按代码生成完整的瓦片集（每次结果相同）。</summary>
    public static TileSet Build()
    {
        var tileSet = new TileSet { TileSize = new Vector2I(Tile, Tile) };
        tileSet.AddPhysicsLayer();
        tileSet.SetPhysicsLayerCollisionLayer(0, Layers.World);
        tileSet.SetPhysicsLayerCollisionMask(0, 0);
        tileSet.AddCustomDataLayer();
        tileSet.SetCustomDataLayerName(0, Surfaces.NoClimbData);
        tileSet.SetCustomDataLayerType(0, Variant.Type.Bool);

        var source = new TileSetAtlasSource { Texture = ImageTexture.CreateFromImage(PaintAtlas()), TextureRegionSize = new Vector2I(Tile, Tile) };
        tileSet.AddSource(source, SourceId);
        float h = Tile / 2f;
        foreach (Vector2I atlas in AllAtlas)
        {
            source.CreateTile(atlas);
            TileData data = source.GetTileData(atlas, 0);
            data.SetCollisionPolygonsCount(0, 1);
            if (atlas == OneWayAtlas)
            {
                float bottom = -h + OneWayThickness;
                data.SetCollisionPolygonPoints(0, 0, new[] { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, bottom), new Vector2(-h, bottom) });
                data.SetCollisionPolygonOneWay(0, 0, true);
            }
            else
            {
                data.SetCollisionPolygonPoints(0, 0, new[] { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, h), new Vector2(-h, h) });
            }
            data.SetCustomData(Surfaces.NoClimbData, atlas == NoClimbAtlas);
        }
        return tileSet;
    }

    /// <summary>瓦片集里和玩法有关的部分（尺寸、碰撞层、自定义数据、每个图块的碰撞和 no_climb），用来判断磁盘上的瓦片集是不是最新的。</summary>
    public static string Describe(TileSet tileSet)
    {
        if (tileSet == null) return "null";
        var sb = new StringBuilder();
        sb.Append($"size={tileSet.TileSize};physics={tileSet.GetPhysicsLayersCount()}");
        for (int i = 0; i < tileSet.GetPhysicsLayersCount(); i++)
            sb.Append($";layer{i}={tileSet.GetPhysicsLayerCollisionLayer(i)}/{tileSet.GetPhysicsLayerCollisionMask(i)}");
        sb.Append($";custom={tileSet.GetCustomDataLayersCount()}");
        for (int i = 0; i < tileSet.GetCustomDataLayersCount(); i++)
            sb.Append($";custom{i}={tileSet.GetCustomDataLayerName(i)}:{tileSet.GetCustomDataLayerType(i)}");
        if (!tileSet.HasSource(SourceId) || tileSet.GetSource(SourceId) is not TileSetAtlasSource source) return sb.Append(";no source").ToString();
        sb.Append($";sources={tileSet.GetSourceCount()};region={source.TextureRegionSize};tiles={source.GetTilesCount()}");
        var tiles = new List<Vector2I>();
        for (int i = 0; i < source.GetTilesCount(); i++) tiles.Add(source.GetTileId(i));
        foreach (Vector2I atlas in tiles.OrderBy(t => t.Y).ThenBy(t => t.X))
        {
            TileData data = source.GetTileData(atlas, 0);
            sb.Append($";tile{atlas}");
            if (tileSet.GetPhysicsLayersCount() > 0)
                for (int p = 0; p < data.GetCollisionPolygonsCount(0); p++)
                    sb.Append($" poly[{string.Join(",", data.GetCollisionPolygonPoints(0, p))}] oneway={data.IsCollisionPolygonOneWay(0, p)}");
            if (tileSet.GetCustomDataLayerByName(Surfaces.NoClimbData) >= 0) sb.Append($" no_climb={data.GetCustomData(Surfaces.NoClimbData)}");
        }
        return sb.ToString();
    }

    // ---------- 占位贴图 ----------

    private static Image PaintAtlas()
    {
        Image image = Image.CreateEmpty(Tile * AllAtlas.Length, Tile, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));
        Paint(image, SolidAtlas, (x, y) => y < 2 ? SolidEdge : SolidColor);
        Paint(image, NoClimbAtlas, (x, y) => ((x + y) / 4) % 2 == 0 ? NoClimbStripe : NoClimbColor);
        Paint(image, OneWayAtlas, (x, y) => y < OneWayThickness ? (y == 0 || y == OneWayThickness - 1 ? SolidEdge : OneWayColor) : new Color(0, 0, 0, 0));
        Paint(image, CrackedAtlas, (x, y) => IsCrack(x, y) ? CrackColor : CrackedColor);
        return image;
    }

    private static void Paint(Image image, Vector2I atlas, System.Func<int, int, Color> color)
    {
        for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++)
                image.SetPixel(atlas.X * Tile + x, atlas.Y * Tile + y, color(x, y));
    }

    /// <summary>两道折线裂纹。</summary>
    private static bool IsCrack(int x, int y) =>
        (y < 16 && Mathf.Abs(x - (6 + y / 2)) <= 1) || (y >= 16 && Mathf.Abs(x - (14 - (y - 16) / 3)) <= 1)
        || (x >= 16 && Mathf.Abs(y - (20 + (x - 16) / 2)) <= 1) || (x == 16 && y >= 6 && y < 14);
}
