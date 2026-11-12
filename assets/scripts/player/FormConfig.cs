using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Player;

/// <summary>
/// 一个形态（水 / 泥）的全部手感参数。水、泥各一个 .tres（assets/data/player_v2/）。
/// 数值沿用白盒 water.tres / mud.tres，伤害按机制文档：水 10、泥 24。
/// </summary>
[GlobalClass]
public partial class FormConfig : KeptResource
{
    [ExportGroup("形态")]
    [Export] public string DisplayName { get; set; } = "水";
    /// <summary>动画名前缀：water / mud。</summary>
    [Export] public string AnimPrefix { get; set; } = "water";
    [Export] public Color Tint { get; set; } = new("64d8ef");
    [Export] public float MoveSpeed { get; set; } = 360f;

    [ExportGroup("闪避")]
    [Export] public float DodgeSpeed { get; set; } = 760f;
    [Export] public float DodgeDuration { get; set; } = 0.28f;
    [Export] public float DodgeCooldown { get; set; } = 0.65f;
    /// <summary>闪避结束后的减速时长，0 表示不减速（水）。</summary>
    [Export] public float PenaltyDuration { get; set; }
    [Export] public float PenaltyInitialMultiplier { get; set; } = 0.2f;
    [Export] public Curve RecoveryCurve { get; set; }

    [ExportGroup("格挡")]
    [Export] public float BlockDuration { get; set; } = 0.5f;
    /// <summary>true：挡住一次就结束（水）；false：按住期间可挡多段（泥）。</summary>
    [Export] public bool BlockSingleHit { get; set; } = true;
    [Export] public bool GrantsBlockBonus { get; set; }
    [Export] public float BlockBonusMultiplier { get; set; } = 1.6f;
    [Export] public float BlockCooldown { get; set; } = 3f;
    [Export] public bool ResetBlockCooldownOnSuccess { get; set; } = true;
    [Export] public bool ResetAttackIntervalOnBlock { get; set; } = true;

    [ExportGroup("攻击：前摇、有效判定、后摇")]
    [Export] public HitSpec AttackSpec { get; set; }
    [Export] public float AttackWindup { get; set; } = 0.08f;
    [Export] public float AttackActive { get; set; } = 0.10f;
    [Export] public float AttackRecovery { get; set; } = 0.16f;
    [Export] public float AttackReach { get; set; } = 90f;
    /// <summary>横向攻击框的高度，以及攻击框离身体中心的额外前移距离（白盒 70 / 16）。</summary>
    [Export] public float AttackHeight { get; set; } = 70f;
    [Export] public float AttackForwardOffset { get; set; } = 16f;
    /// <summary>下劈攻击框的宽度（白盒 72），高度用 AttackReach。</summary>
    [Export] public float DownAttackWidth { get; set; } = 72f;
    /// <summary>空中下劈、下落中命中时的倍率（泥 1.8，水 1）。</summary>
    [Export] public float DownAttackMultiplier { get; set; } = 1f;

    [ExportGroup("攻击后摇衔接")]
    /// <summary>true：后摇可以移动、曲线恢复速度（泥 1.5 秒）。</summary>
    [Export] public bool SoftAttackRecovery { get; set; }
    [Export] public float BlockCancelWindow { get; set; }
    [Export] public bool DodgeCancelRecovery { get; set; }

    [ExportGroup("跳跃：起跳时冻结配置")]
    [Export] public JumpStyle JumpStyle { get; set; } = JumpStyle.ConstantDescent;
    [Export] public float JumpWindup { get; set; } = 0.05f;
    [Export] public float JumpSpeed { get; set; } = 600f;
    [Export] public float Gravity { get; set; } = 1600f;
    [Export] public float DescentSpeed { get; set; } = 300f;
    [Export] public float TerminalSpeed { get; set; } = 880f;
    [Export] public float WallClimbSpeed { get; set; } = 180f;
}
