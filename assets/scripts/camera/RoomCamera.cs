using Godot;
using SonOfMud.Core;

namespace SonOfMud.Camera;

/// <summary>
/// 房间镜头：固定缩放、跟随玩家；进入房间时按 MetSys 房间尺寸设边界并重置平滑。
/// 震屏：Events.CameraShakeRequested 由打击感服务（Feel/FeedbackService，经 Juicee）处理，它只晃镜头下的震动挂点 ShakeRig；
/// 镜头每帧读挂点位置，按 Settings.MaxShakeOffset 截断长度后写到自己的 Offset（Offset 只有镜头自己写）。
/// 玩家从 Events.PlayerSpawned 得到（只当 Node2D 用）。
/// </summary>
[GlobalClass]
public partial class RoomCamera : Camera2D
{
    [Export] public CameraSettings Settings { get; set; }

    public Node2D Target { get; private set; }

    /// <summary>震动挂点：震屏效果只改它的位置，镜头读它、截断后作为 Offset。</summary>
    public Node2D ShakeRig { get; private set; }

    public override void _EnterTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.PlayerSpawned += SetTarget;
        Events.Instance.RoomEntered += OnRoomEntered;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.PlayerSpawned -= SetTarget;
        Events.Instance.RoomEntered -= OnRoomEntered;
    }

    public override void _Ready()
    {
        Settings ??= new CameraSettings();
        Zoom = Settings.Zoom;
        PositionSmoothingEnabled = Settings.SmoothingEnabled;
        PositionSmoothingSpeed = Settings.SmoothingSpeed;
        ShakeRig = new Node2D { Name = "ShakeRig" };
        AddChild(ShakeRig);
        MakeCurrent();
    }

    public void SetTarget(Node2D target)
    {
        Target = target;
        SnapToTarget();
    }

    private void OnRoomEntered(string roomId)
    {
        if (MetSysApi.Available) MetSysApi.CurrentRoom?.AdjustCameraLimits(this);
        SnapToTarget();
    }

    /// <summary>瞬间对准目标并清掉平滑的残余（换房后玩家坐标跳变，不能让镜头慢慢滑过去）。</summary>
    public void SnapToTarget()
    {
        if (IsInstanceValid(Target)) GlobalPosition = Target.GlobalPosition;
        ResetSmoothing();
    }

    /// <summary>震屏（转成 Events.CameraShakeRequested，由打击感服务处理）。</summary>
    public void Shake(float strength, float duration)
    {
        if (duration <= 0f || strength <= 0f) return;
        Events.Instance?.EmitSignal(Events.SignalName.CameraShakeRequested, strength, duration);
    }

    /// <summary>挂点还没回到原位（震屏进行中）。</summary>
    public bool IsShaking => ShakeRig != null && ShakeRig.Position != Vector2.Zero;

    public override void _Process(double delta)
    {
        if (IsInstanceValid(Target)) GlobalPosition = Target.GlobalPosition;
        Vector2 shake = ShakeRig?.Position ?? Vector2.Zero;
        Offset = shake.LimitLength(Settings.MaxShakeOffset);
    }
}
