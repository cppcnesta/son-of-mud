using Godot;

namespace SonOfMud.Player;

[GlobalClass]
public partial class MudFormConfig : Resource
{
    [ExportGroup("形态（白盒参数，非正式平衡）")]
    [Export] public string DisplayName { get; set; } = "水";
    [Export] public Color Tint { get; set; } = new("64d8ef");
    [Export] public float MoveSpeed { get; set; } = 360f;

    [ExportGroup("闪避")]
    [Export] public float DodgeSpeed { get; set; } = 760f;
    [Export] public float DodgeDuration { get; set; } = 0.28f;
    [Export] public float DodgeCooldown { get; set; } = 0.65f;
    [Export] public float PenaltyDuration { get; set; }
    [Export] public float PenaltyInitialMultiplier { get; set; } = 0.2f;
    [Export] public Curve RecoveryCurve { get; set; } = GD.Load<Curve>("res://assets/data/player/curves/recovery_ease_out.tres");

    [ExportGroup("格挡")]
    [Export] public float BlockDuration { get; set; } = 0.5f;
    [Export] public bool BlockSingleHit { get; set; } = true;
    [Export] public bool GrantsBlockBonus { get; set; }
    [Export] public float BlockBonusMultiplier { get; set; } = 1.6f;
    [Export] public float BlockCooldown { get; set; } = 3f;
    [Export] public bool ResetBlockCooldownOnSuccess { get; set; } = true;
    [Export] public bool ResetAttackIntervalOnBlock { get; set; } = true;

    [ExportGroup("攻击：前摇、有效判定、后摇")]
    [Export] public float Damage { get; set; } = 10f;
    [Export] public float AttackWindup { get; set; } = 0.08f;
    [Export] public float AttackActive { get; set; } = 0.10f;
    [Export] public float AttackRecovery { get; set; } = 0.16f;
    [Export] public float AttackReach { get; set; } = 90f;
    [Export] public float DownAttackMultiplier { get; set; } = 1f;

    [ExportGroup("攻击后摇衔接")]
    [Export] public bool SoftAttackRecovery { get; set; }
    [Export] public float BlockCancelWindow { get; set; }
    [Export] public bool DodgeCancelRecovery { get; set; }

    [ExportGroup("跳跃：起跳时冻结配置")]
    [Export] public float JumpWindup { get; set; } = 0.05f;
    [Export] public float JumpSpeed { get; set; } = 600f;
    [Export] public float Gravity { get; set; } = 1600f;
    [Export] public bool ConstantDescent { get; set; } = true;
    [Export] public float DescentSpeed { get; set; } = 300f;
    [Export] public float TerminalSpeed { get; set; } = 880f;
    [Export] public float HoldAcceleration { get; set; } = 850f;
    [Export] public float HoldMaxTime { get; set; } = 0.24f;
    [Export] public float HoldHeightCap { get; set; } = 176f;
}
