using Godot;
using SonOfMud.Combat;

namespace SonOfMud.Player;

/// <summary>和形态无关的玩家规则（assets/data/player_v2/rules.tres）。</summary>
[GlobalClass]
public partial class PlayerRules : Resource
{
    [ExportGroup("形态切换")]
    [Export(PropertyHint.Enum, "水,泥")] public int InitialForm { get; set; }
    [Export] public float SwitchCooldown { get; set; } = 0.15f;
    [Export] public bool AllowAirSwitch { get; set; } = true;

    [ExportGroup("输入宽容")]
    [Export] public float CoyoteTime { get; set; } = 0.10f;
    [Export] public float JumpBuffer { get; set; } = 0.12f;
    /// <summary>攻击 / 格挡 / 闪避的输入缓冲。</summary>
    [Export] public float ActionBuffer { get; set; } = 0.15f;

    [ExportGroup("生命与受击")]
    /// <summary>7 颗泥珠 × 20（D1、D10）。</summary>
    [Export] public int MaxHealth { get; set; } = 140;
    [Export] public float HurtDuration { get; set; } = 0.18f;
    /// <summary>受伤后的无敌时间：候选为 0，靠按实体去重防止同一次攻击连续扣血。</summary>
    [Export] public float HurtInvulnerability { get; set; }

    [ExportGroup("重压下砸")]
    [Export] public float SlamSpeed { get; set; } = 1000f;
    [Export] public HitSpec SlamSpec { get; set; }
    [Export] public float SlamRadius { get; set; } = 150f;
    /// <summary>水形态下砸落地后的站定后摇（泥形态用 1.5 秒可移动后摇）。</summary>
    [Export] public float HardSlamRecovery { get; set; } = 0.25f;

    [ExportGroup("反馈")]
    [Export] public float HitStopSeconds { get; set; } = 0.05f;
    [Export] public float SlamShakeStrength { get; set; } = 6f;
    [Export] public float SlamShakeDuration { get; set; } = 0.2f;

    [ExportGroup("调试")]
    [Export] public float DebugFlySpeed { get; set; } = 600f;
}
