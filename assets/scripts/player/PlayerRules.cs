using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Player;

/// <summary>和形态无关的玩家规则（assets/data/player_v2/rules.tres）。</summary>
[GlobalClass]
public partial class PlayerRules : KeptResource
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
    /// <summary>起始血量上限（还没吃生命之面包时）。实际上限见 MaxHealthFor。</summary>
    [Export] public int MaxHealth { get; set; } = 70;
    /// <summary>吃下生命之面包后进度里成立的变量（剧本 m_pit 供桌“吃下”设置）。</summary>
    [Export] public StringName LifeBreadFlag { get; set; } = "item.生命之面包";
    /// <summary>生命之面包增加的血量上限；吃下的那一刻当前血量也加这么多。</summary>
    [Export] public int LifeBreadBonus { get; set; } = 70;
    /// <summary>受伤硬直。没有受伤无敌帧（规格第 9 节候选：不给，靠按实体去重防止同一次攻击连续扣血）。</summary>
    [Export] public float HurtDuration { get; set; } = 0.18f;
    /// <summary>受伤 / 眩晕时击退速度每秒衰减的比例。</summary>
    [Export] public float KnockbackDamping { get; set; } = 4f;

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

    /// <summary>按进度算出的血量上限：起始上限，吃过生命之面包再加 LifeBreadBonus。</summary>
    public int MaxHealthFor(ProgressStore progress) =>
        MaxHealth + (progress != null && progress.Flag(LifeBreadFlag) ? LifeBreadBonus : 0);
}
