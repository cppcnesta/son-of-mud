using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 场景里的物件（泥板、雕像、地上的道具）换成美术图：按固定高度显示（宽度按图的比例），图的底边中点放在物件原点（脚 / 地面）。
/// 有图时把物件下名为 Placeholder 的占位色块藏起来；没有图时什么都不做，照旧显示色块。
/// </summary>
public static class ArtSprite
{
    public const string CollectibleDirectory = "res://assets/art/collectible";
    public const string ItemDirectory = "res://assets/art/item";
    public const string NodeName = "Art";

    /// <summary>按命名找一张单帧图（<c>baseName.png</c> 或 <c>baseName_1x1.png</c>），找不到返回 null。</summary>
    public static Texture2D Load(string directory, string baseName) => SpriteStrip.Find(directory, baseName, "物件图", out _, out _);

    /// <summary>给 owner 加一张高 height 像素的图（底边中点在原点），藏起占位色块。texture 为 null 时不做任何事，返回 null。</summary>
    public static Sprite2D Attach(Node2D owner, Texture2D texture, float height)
    {
        if (owner == null || texture == null || texture.GetHeight() <= 0) return null;
        if (owner.GetNodeOrNull<CanvasItem>("Placeholder") is { } placeholder) placeholder.Visible = false;
        owner.GetNodeOrNull<Node>(NodeName)?.Free();
        float scale = height / texture.GetHeight();
        var sprite = new Sprite2D
        {
            Name = NodeName,
            Texture = texture,
            Centered = true,
            Offset = new Vector2(0f, -texture.GetHeight() * 0.5f),
            Scale = new Vector2(scale, scale),
        };
        owner.AddChild(sprite);
        return sprite;
    }
}
