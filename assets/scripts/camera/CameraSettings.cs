using Godot;

namespace SonOfMud.Camera;

/// <summary>镜头的可调数值。数值放在 assets/data/camera/camera_settings.tres。</summary>
[GlobalClass]
public partial class CameraSettings : Resource
{
    /// <summary>固定缩放。房间最小 2×2 格 = 一屏，缩放 1 时不会看到房间外面。</summary>
    [Export] public Vector2 Zoom { get; set; } = Vector2.One;
    [Export] public bool SmoothingEnabled { get; set; } = true;
    [Export] public float SmoothingSpeed { get; set; } = 8f;
    /// <summary>震屏强度上限（像素）。</summary>
    [Export] public float MaxShakeOffset { get; set; } = 24f;
    /// <summary>震屏衰减曲线的指数：1 = 线性衰减，越大收得越快。</summary>
    [Export] public float ShakeFalloff { get; set; } = 2f;
}
