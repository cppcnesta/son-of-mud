using Godot;
using SonOfMud.Core;
using SonOfMud.World;

/// <summary>
/// 测试用：在运行时把 RoomGrid 文字图搭成真实的 TileMapLayer。瓦片集在代码里建（32 像素）：
/// 0 号实心（可攀），1 号实心且自定义数据 no_climb = true（不可攀），2 号单向平台。
/// B / H 当实心铺（校准场不测打碎），R 按条件铺或不铺。
/// </summary>
public static class GridTileMap
{
    public static readonly Vector2I SolidTile = new(0, 0);
    public static readonly Vector2I NoClimbTile = new(1, 0);
    public static readonly Vector2I OneWayTile = new(2, 0);
    public const float OneWayMargin = 1f;

    public static TileSet BuildTileSet()
    {
        int t = RoomGrid.Tile;
        var tileSet = new TileSet { TileSize = new Vector2I(t, t) };
        tileSet.AddPhysicsLayer();
        tileSet.SetPhysicsLayerCollisionLayer(0, Layers.World);
        tileSet.SetPhysicsLayerCollisionMask(0, 0);
        tileSet.AddCustomDataLayer();
        tileSet.SetCustomDataLayerName(0, Surfaces.NoClimbData);
        tileSet.SetCustomDataLayerType(0, Variant.Type.Bool);

        Image image = Image.CreateEmpty(t * 3, t, false, Image.Format.Rgba8);
        image.FillRect(new Rect2I(0, 0, t, t), new Color("3a4150"));
        image.FillRect(new Rect2I(t, 0, t, t), new Color("6b3a3a"));
        image.FillRect(new Rect2I(t * 2, 0, t, t / 4), new Color("8a8f5a"));
        var source = new TileSetAtlasSource { Texture = ImageTexture.CreateFromImage(image), TextureRegionSize = new Vector2I(t, t) };
        tileSet.AddSource(source, 0);

        float h = t / 2f;
        var square = new[] { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, h), new Vector2(-h, h) };
        foreach (Vector2I coords in new[] { SolidTile, NoClimbTile, OneWayTile })
        {
            source.CreateTile(coords);
            TileData data = source.GetTileData(coords, 0);
            data.AddCollisionPolygon(0);
            data.SetCollisionPolygonPoints(0, 0, square);
            data.SetCustomData(Surfaces.NoClimbData, coords == NoClimbTile);
            if (coords == OneWayTile)
            {
                data.SetCollisionPolygonOneWay(0, 0, true);
                data.SetCollisionPolygonOneWayMargin(0, 0, OneWayMargin);
            }
        }
        return tileSet;
    }

    /// <summary>按文字图铺瓦片。返回的层放在 roomOrigin + (0, -Shift)，和 RoomGrid.TileRect 一致。</summary>
    public static TileMapLayer Build(RoomGrid grid, bool conditionMet, Vector2 roomOrigin, TileSet tileSet = null)
    {
        var layer = new TileMapLayer { Name = "Geometry", TileSet = tileSet ?? BuildTileSet(), Position = roomOrigin + new Vector2(0, -grid.Shift) };
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Cols; c++)
            {
                char ch = grid.At(c, r);
                Vector2I? tile = ch switch
                {
                    RoomGrid.NoClimb => NoClimbTile,
                    RoomGrid.OneWay => OneWayTile,
                    _ => RoomGrid.IsSolidChar(ch, conditionMet) ? SolidTile : null,
                };
                if (tile is { } coords) layer.SetCell(new Vector2I(c, r), 0, coords);
            }
        return layer;
    }
}
