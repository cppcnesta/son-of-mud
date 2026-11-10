using Godot;
using SonOfMud.Core;

namespace SonOfMud.Camera;

/// <summary>
/// 房间镜头：固定缩放、跟随玩家；进入房间时按 MetSys 房间尺寸设边界并重置平滑；响应 Events.CameraShakeRequested 震屏。
/// 玩家从 Events.PlayerSpawned 得到（只当 Node2D 用）。
/// </summary>
[GlobalClass]
public partial class RoomCamera : Camera2D
{
    [Export] public CameraSettings Settings { get; set; }

    public Node2D Target { get; private set; }

    private float _shakeStrength;
    private float _shakeDuration;
    private float _shakeLeft;
    private readonly RandomNumberGenerator _rng = new();

    public override void _EnterTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.PlayerSpawned += SetTarget;
        Events.Instance.RoomEntered += OnRoomEntered;
        Events.Instance.CameraShakeRequested += Shake;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.PlayerSpawned -= SetTarget;
        Events.Instance.RoomEntered -= OnRoomEntered;
        Events.Instance.CameraShakeRequested -= Shake;
    }

    public override void _Ready()
    {
        Settings ??= new CameraSettings();
        Zoom = Settings.Zoom;
        PositionSmoothingEnabled = Settings.SmoothingEnabled;
        PositionSmoothingSpeed = Settings.SmoothingSpeed;
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

    public void Shake(float strength, float duration)
    {
        if (duration <= 0f || strength <= 0f) return;
        _shakeStrength = Mathf.Min(Mathf.Max(strength, _shakeLeft > 0f ? _shakeStrength : 0f), Settings.MaxShakeOffset);
        _shakeDuration = duration;
        _shakeLeft = duration;
    }

    public bool IsShaking => _shakeLeft > 0f;

    public override void _Process(double delta)
    {
        if (IsInstanceValid(Target)) GlobalPosition = Target.GlobalPosition;
        if (_shakeLeft > 0f)
        {
            _shakeLeft = Mathf.Max(0f, _shakeLeft - (float)delta);
            float amount = _shakeStrength * Mathf.Pow(_shakeLeft / _shakeDuration, Settings.ShakeFalloff);
            Offset = amount > 0f ? new Vector2(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)) * amount : Vector2.Zero;
        }
        else if (Offset != Vector2.Zero)
        {
            Offset = Vector2.Zero;
        }
    }
}
