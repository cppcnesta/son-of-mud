using System.Collections.Generic;
using Godot;

namespace SonOfMud.World;

/// <summary>
/// 「地形图」：美术画了地板、台阶、洞口的那张背景图，碰撞照它描，所以它必须跟地面一起动（视差 1），
/// 并按固定的缩放和位置放（不用导入器的远景铺法：远景视差 0.15，镜头一动图上的地板就和碰撞错开）。
/// 按命名导入背景后，由 <see cref="Fix"/> 把这几张图的节点设成下表的值；以后再导入时导入器只换图、不改这些节点的参数。
/// 表里：房间 → (图片文件名, 缩放（画布像素 → 游戏像素）, 图左上角在房间里的位置)。
/// </summary>
public static class TerrainArt
{
    public const string Meta = "terrain_art";

    public readonly record struct Entry(string File, float Scale, Vector2 Position);

    /// <summary>
    /// S4_01：1380 高 → 648（0.4696），宽约 1188，左右各出房间约 18 像素；
    /// S4_02：按 0.5 = 1702×616，放进 1728×648，左右各留 13、上下各留 16；
    /// S4_0304：按 2304 ÷ 4497（约 0.5123）= 2304×766，上下各裁约 59。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Entry> Rooms = new Dictionary<string, Entry>
    {
        ["S4_01"] = new("tex_bg_s4_01_far_01.png", 648f / 1380f, new Vector2((1152f - 2529f * 648f / 1380f) / 2f, 0f)),
        ["S4_02"] = new("tex_bg_s4_02_far_01.png", 0.5f, new Vector2((1728f - 3405f * 0.5f) / 2f, (648f - 1232f * 0.5f) / 2f)),
        ["S4_0304"] = new("tex_bg_s4_0304_far_02.png", 2304f / 4497f, new Vector2(0f, (648f - 1495f * 2304f / 4497f) / 2f)),
    };

    /// <summary>房间里的地形图节点（Sprite2D 的图片文件名对上表里的）；没有返回 null。</summary>
    public static Sprite2D FindImage(Node room, string file)
    {
        Node background = room.GetNodeOrNull(BackgroundImporter.NodeName);
        if (background == null) return null;
        foreach (Parallax2D parallax in BackgroundImporter.ParallaxNodes(background))
            foreach (Node child in parallax.GetChildren())
                if (child is Sprite2D sprite && sprite.Texture?.ResourcePath?.EndsWith("/" + file) == true) return sprite;
        return null;
    }

    /// <summary>把房间里的地形图设成视差 1、固定缩放和位置。返回改了没有（房间不在表里、或找不到图时为 false）。</summary>
    public static bool Fix(Node room)
    {
        if (room is not RoomRoot root || !Rooms.TryGetValue(root.RoomId, out Entry entry)) return false;
        Sprite2D image = FindImage(room, entry.File);
        if (image?.GetParent() is not Parallax2D parallax) return false;
        parallax.ScrollScale = Vector2.One;
        parallax.ScrollOffset = Vector2.Zero;
        parallax.Position = Vector2.Zero;
        parallax.SetMeta(Meta, entry.File);
        image.Centered = false;
        image.Scale = new Vector2(entry.Scale, entry.Scale);
        image.Position = entry.Position;
        return true;
    }

    /// <summary>地形图放好了没有（视差 1、缩放和位置对）；不对时返回原因。</summary>
    public static string Problem(Node room)
    {
        if (room is not RoomRoot root || !Rooms.TryGetValue(root.RoomId, out Entry entry)) return "房间不在地形图表里";
        Sprite2D image = FindImage(room, entry.File);
        if (image == null) return $"找不到地形图 {entry.File}";
        if (image.GetParent() is not Parallax2D parallax) return "地形图不在 Parallax2D 下";
        if (parallax.ScrollScale != Vector2.One) return $"视差是 {parallax.ScrollScale}，应为 (1, 1)";
        if (parallax.ScrollOffset != Vector2.Zero || parallax.Position != Vector2.Zero) return "视差节点有偏移";
        if (!image.Scale.IsEqualApprox(new Vector2(entry.Scale, entry.Scale)) || !image.Position.IsEqualApprox(entry.Position) || image.Centered)
            return $"缩放 / 位置是 {image.Scale} / {image.Position}，应为 {entry.Scale} / {entry.Position}";
        return "";
    }
}
