using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间背景“按命名导入”的配置（RoomRoot 检查器里的「按命名导入背景」按钮用）。图片查找顺序：
/// <c>{ArtRoot}/s1/tex_bg_s1_03_far.png</c>（房间专属）→ <c>{ArtRoot}/s1/tex_bg_s1_far.png</c>（区域通用）→ 没有就跳过这一层。
/// </summary>
[Tool]
[GlobalClass]
public partial class BackgroundSettings : Resource
{
    public const string DefaultPath = "res://assets/data/world/background_settings.tres";

    /// <summary>背景图根目录，下面按区域分 s1～s4 子目录。测试会换成测试素材目录。</summary>
    [Export] public string ArtRoot { get; set; } = "res://assets/art/world/bg";

    /// <summary>导入时最远景的视差系数：镜头移动 ΔC 时，这一层在屏幕上移动 −p × ΔC。导入后可在 Far 节点上改。</summary>
    [Export] public Vector2 FarScroll { get; set; } = new(0.15f, 0.15f);
    [Export] public Vector2 MidScroll { get; set; } = new(0.5f, 0.5f);

    /// <summary>
    /// 震屏余量（像素）。Camera2D.Offset 会越过镜头范围，背景要多盖这么多。
    /// World 不能引用 Camera 系统，这个值由测试保证不小于 CameraSettings.MaxShakeOffset。
    /// </summary>
    [Export] public float ShakeMargin { get; set; } = 24f;

    public static BackgroundSettings LoadDefault() =>
        ResourceLoader.Exists(DefaultPath) ? GD.Load<BackgroundSettings>(DefaultPath) : new BackgroundSettings();

    public Vector2 ScrollOf(string layer) => layer == BackgroundImporter.Far ? FarScroll : MidScroll;
}
