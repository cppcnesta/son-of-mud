using Godot;
using SonOfMud.Player;

namespace SonOfMud.Tools.LevelCheck;

/// <summary>一个形态里静态模拟用到的参数，全部从正式的 FormConfig（water.tres / mud.tres）抄过来，不写死数字。</summary>
public sealed class FormParams
{
    public string Name = "";
    public float MoveSpeed;
    public JumpStyle Style;
    public float JumpWindup;
    public float JumpSpeed;
    public float Gravity;
    public float DescentSpeed;
    public float TerminalSpeed;
    public float WallClimbSpeed;
    public float AttackReach;
    public float AttackHeight;
    public float AttackForwardOffset;

    public FormParams Copy() => (FormParams)MemberwiseClone();

    public static FormParams From(FormConfig c) => new()
    {
        Name = c.DisplayName,
        MoveSpeed = c.MoveSpeed,
        Style = c.JumpStyle,
        JumpWindup = c.JumpWindup,
        JumpSpeed = c.JumpSpeed,
        Gravity = c.Gravity,
        DescentSpeed = c.DescentSpeed,
        TerminalSpeed = c.TerminalSpeed,
        WallClimbSpeed = c.WallClimbSpeed,
        AttackReach = c.AttackReach,
        AttackHeight = c.AttackHeight,
        AttackForwardOffset = c.AttackForwardOffset,
    };
}

/// <summary>
/// 静态模拟器的全部配置：从正式玩家场景 player_v2.tscn 读两种形态（FormController 上挂的 .tres）、
/// PlayerRules（土狼时间、跳跃缓冲）、身体碰撞框尺寸和 CharacterBody2D 的贴地距离；步长取物理帧率。
/// </summary>
public sealed class SimConfig
{
    public const string PlayerScenePath = "res://assets/scenes/player/player_v2.tscn";
    public const int Water = FormController.Water;
    public const int Mud = FormController.Mud;

    public FormParams WaterForm = new();
    public FormParams MudForm = new();
    public float CoyoteTime;
    public float JumpBuffer;
    public Vector2 BodySize;
    public float Dt;
    public float FloorSnapLength;
    /// <summary>单向平台的判定余量（瓦片集里单向碰撞多边形的 one_way_margin；校准用的瓦片集用同一个值）。</summary>
    public float OneWayMargin = 1f;
    /// <summary>不可攀规则（计划 2.1）：接触 X 时不能开始攀墙、攀墙中掉下、不能蹬墙跳。</summary>
    public bool NoClimbRule = true;

    public float HalfW => BodySize.X * 0.5f;
    public float HalfH => BodySize.Y * 0.5f;

    public FormParams Form(int form) => form == Mud ? MudForm : WaterForm;

    /// <summary>读正式配置（每次都重新实例化一次玩家场景来读，不做静态缓存）。</summary>
    public static SimConfig Load()
    {
        var scene = GD.Load<PackedScene>(PlayerScenePath);
        var player = scene.Instantiate<PlayerCharacter>();
        var config = new SimConfig();
        try
        {
            var forms = player.GetNode<FormController>("FormController");
            config.WaterForm = FormParams.From(forms.WaterConfig);
            config.MudForm = FormParams.From(forms.MudConfig);
            PlayerRules rules = player.Rules ?? new PlayerRules();
            config.CoyoteTime = rules.CoyoteTime;
            config.JumpBuffer = rules.JumpBuffer;
            var shape = player.GetNode<CollisionShape2D>("CollisionShape2D");
            config.BodySize = ((RectangleShape2D)shape.Shape).Size;
            config.FloorSnapLength = player.FloorSnapLength;
        }
        finally
        {
            player.Free();
        }
        config.Dt = (float)(1.0 / Engine.PhysicsTicksPerSecond);
        return config;
    }

    public SimConfig Clone()
    {
        var c = (SimConfig)MemberwiseClone();
        c.WaterForm = WaterForm.Copy();
        c.MudForm = MudForm.Copy();
        return c;
    }
}
