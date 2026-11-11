using Godot;
using SonOfMud.Core;

/// <summary>
/// 测试用的玩家替身（只放在 tests/）：只会左右走和跳。原点在脚底中心。
/// 和正式 Player 的约定一致：_Ready 里发 PlayerSpawned；收到 PlayerTeleported 时速度清零。
/// </summary>
public partial class TestMover : CharacterBody2D
{
    [Export] public float Speed { get; set; } = 300f;
    [Export] public float Gravity { get; set; } = 1800f;
    [Export] public float JumpVelocity { get; set; } = -620f;
    /// <summary>手动游玩时读键盘（move_left / move_right / jump）；测试里由 MoveDir 驱动。</summary>
    [Export] public bool UseInput { get; set; }

    public float MoveDir { get; set; }
    public bool JumpRequested { get; set; }
    public int TeleportCount { get; private set; }

    public static readonly Vector2 BodySize = new(40, 96);

    public override void _EnterTree()
    {
        CollisionLayer = Layers.PlayerBody;
        CollisionMask = Layers.World;
        if (GetNodeOrNull("Shape") == null)
            AddChild(new CollisionShape2D { Name = "Shape", Shape = new RectangleShape2D { Size = BodySize }, Position = new Vector2(0, -BodySize.Y / 2f) });
        if (GetNodeOrNull("Body") == null)
            AddChild(new ColorRect { Name = "Body", Color = new Color(0.9f, 0.85f, 0.75f), Size = BodySize, Position = new Vector2(-BodySize.X / 2f, -BodySize.Y) });
        if (Events.Instance != null) Events.Instance.PlayerTeleported += OnTeleported;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.PlayerTeleported -= OnTeleported;
    }

    public override void _Ready() => Events.Instance?.EmitSignal(Events.SignalName.PlayerSpawned, this);

    private void OnTeleported(StringName spawnId)
    {
        TeleportCount++;
        Velocity = Vector2.Zero;
        JumpRequested = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dir = MoveDir;
        bool jump = JumpRequested;
        if (UseInput)
        {
            dir = InputLock.IsLocked ? 0f : Input.GetAxis("move_left", "move_right");
            jump = !InputLock.IsLocked && Input.IsActionJustPressed("jump");
        }
        Vector2 v = Velocity;
        v.X = dir * Speed;
        v.Y += Gravity * (float)delta;
        if (jump && IsOnFloor()) v.Y = JumpVelocity;
        JumpRequested = false;
        Velocity = v;
        MoveAndSlide();
    }
}
