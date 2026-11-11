using Godot;
using Godot.Collections;

namespace SonOfMud.World;

/// <summary>
/// 房间背景（最远景 / 中景）的配置。图片按命名自动查找：
/// <c>{ArtRoot}/s1/tex_bg_s1_03_mid.png</c>（房间专属）→ <c>{ArtRoot}/s1/tex_bg_s1_mid.png</c>（区域通用）→ 代码生成的占位图。
/// </summary>
[Tool]
[GlobalClass]
public partial class BackgroundSettings : Resource
{
    public const string DefaultPath = "res://assets/data/world/background_settings.tres";

    /// <summary>背景图根目录，下面按区域分 s1～s4 子目录。测试会换成测试素材目录。</summary>
    [Export] public string ArtRoot { get; set; } = "res://assets/art/world/bg";

    /// <summary>最远景的视差系数：镜头移动 ΔC 时，这一层在屏幕上移动 −p × ΔC。</summary>
    [Export] public Vector2 FarScroll { get; set; } = new(0.15f, 0.15f);
    [Export] public Vector2 MidScroll { get; set; } = new(0.5f, 0.5f);

    /// <summary>横向无缝的层，宽度不够时横向平铺（不缩放）；否则等比放大。</summary>
    [Export] public bool FarSeamless { get; set; } = true;
    [Export] public bool MidSeamless { get; set; } = false;

    /// <summary>
    /// 震屏余量（像素）。Camera2D.Offset 会越过镜头范围，背景必须多留出这么多。
    /// World 不能引用 Camera 系统，这个值由测试保证不小于 CameraSettings.MaxShakeOffset。
    /// </summary>
    [Export] public float ShakeMargin { get; set; } = 24f;

    /// <summary>缺图时占位图的区域主色，按 s1～s4 的顺序。</summary>
    [Export] public Array<Color> RegionColors { get; set; } = new()
    {
        new Color(0.30f, 0.20f, 0.12f),
        new Color(0.12f, 0.20f, 0.24f),
        new Color(0.24f, 0.16f, 0.26f),
        new Color(0.14f, 0.13f, 0.16f),
    };

    public static BackgroundSettings LoadDefault() =>
        ResourceLoader.Exists(DefaultPath) ? GD.Load<BackgroundSettings>(DefaultPath) : new BackgroundSettings();

    public Vector2 ScrollOf(string layer) => layer == RoomBackground.Far ? FarScroll : MidScroll;
    public bool SeamlessOf(string layer) => layer == RoomBackground.Far ? FarSeamless : MidSeamless;

    public Color RegionColor(string region)
    {
        int index = region.Length >= 2 && char.IsDigit(region[1]) ? region[1] - '1' : -1;
        return index >= 0 && index < RegionColors.Count ? RegionColors[index] : new Color(0.2f, 0.2f, 0.2f);
    }
}
