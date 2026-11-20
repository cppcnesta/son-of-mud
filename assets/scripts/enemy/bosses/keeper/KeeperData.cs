using Godot;
using SonOfMud.Combat;

namespace SonOfMud.Enemy.Bosses.Keeper;

/// <summary>
/// 守坑人的全部数值（assets/data/enemy/keeper/KeeperData.tres），逐条对应 boss.md 和计划 v1.2 第 3、5 节。
/// 血量、移速、身体尺寸用基类 EnemyData 的字段（MaxHealth 150、MoveSpeed 120 = Q7 的“中速”）。
/// 伤害按 HP 140 体系换算（Q1，待试玩）：横扫 40、蝎尾 60，写在两个 HitSpec 里。
/// </summary>
[GlobalClass]
public partial class KeeperData : EnemyData
{
    [ExportGroup("巡游（不出招时在场地里来回走，钳子始终朝玩家）")]
    /// <summary>贴近 / 横穿时停在离玩家（水平）这么远的地方。</summary>
    [Export] public float StopDistance { get; set; } = 96f;
    /// <summary>巡游速度（像素/秒）。</summary>
    [Export] public float RoamSpeed { get; set; } = 160f;
    /// <summary>走到目标点后停多久（秒，在两者之间随机）。</summary>
    [Export] public float RoamPauseMin { get; set; } = 0.4f;
    [Export] public float RoamPauseMax { get; set; } = 0.9f;
    /// <summary>三种挑法的权重：贴近玩家 / 退开 / 从玩家身下横穿到另一边。</summary>
    [Export] public float RoamApproachWeight { get; set; } = 50f;
    [Export] public float RoamRetreatWeight { get; set; } = 30f;
    [Export] public float RoamCrossWeight { get; set; } = 20f;
    /// <summary>退开时离玩家至少这么远（不够就退到场地边）。</summary>
    [Export] public float RetreatDistance { get; set; } = 600f;
    /// <summary>身体两边离场地边至少留这么多（目标点限制在场地里）。</summary>
    [Export] public float ArenaMargin { get; set; } = 40f;
    /// <summary>出招间隔：横扫收招、蝎尾收招、反制僵直结束后，至少巡游这么久才出下一招（秒）。</summary>
    [Export] public float AttackGap { get; set; } = 1.5f;
    /// <summary>转身死区：玩家水平距离小于它时不转身（玩家在头顶附近时不来回翻）。</summary>
    [Export] public float TurnDeadzone { get; set; } = 60f;

    [ExportGroup("芦苇杖横扫")]
    [Export] public HitSpec SweepSpec { get; set; }
    /// <summary>冷却 5 秒，开战 5/5 就绪。</summary>
    [Export] public float SweepCooldown { get; set; } = 5f;
    /// <summary>发动距离：2 地块（128px，水平距离）。</summary>
    [Export] public float SweepRange { get; set; } = 128f;
    [Export] public float SweepWindup { get; set; } = 1f;
    /// <summary>横扫判定持续时间（boss.md 没写，技术取值）。</summary>
    [Export] public float SweepActive { get; set; } = 0.2f;
    /// <summary>收招僵直 0.5 秒，受击不缩短（Q4）。</summary>
    [Export] public float SweepRecover { get; set; } = 0.5f;
    /// <summary>收招僵直期间的易伤倍率。</summary>
    [Export] public float SweepRecoverMultiplier { get; set; } = 2f;
    /// <summary>横扫命中（被格挡也算）时蝎尾剩余冷却减少的秒数。</summary>
    [Export] public float SweepReducesTail { get; set; } = 5f;

    [ExportGroup("蝎尾")]
    [Export] public HitSpec TailSpec { get; set; }
    /// <summary>冷却 20 秒，开战 0/20。</summary>
    [Export] public float TailCooldown { get; set; } = 20f;
    /// <summary>触发距离 6 格（384px，水平距离，Q2）。</summary>
    [Export] public float TailTriggerRange { get; set; } = 384f;
    /// <summary>瞄准 3 秒：无敌、不能移动，结束时锁定玩家当时的 X。</summary>
    [Export] public float TailAim { get; set; } = 3f;
    /// <summary>瞄准结束到判定之间的 0.1 秒过渡。</summary>
    [Export] public float TailDelay { get; set; } = 0.1f;
    /// <summary>蝎尾判定持续时间（技术取值）。</summary>
    [Export] public float TailActive { get; set; } = 0.1f;
    /// <summary>判定范围：锁定 X 处的竖带，宽 96px（Q2），高度覆盖整个房间。</summary>
    [Export] public float TailWidth { get; set; } = 96f;
    [Export] public float TailHeight { get; set; } = 2400f;
    /// <summary>没被挡住时的收招（Q6）。</summary>
    [Export] public float TailRecover { get; set; } = 0.5f;
    /// <summary>被格挡后的反制僵直：5 秒、2 倍、每受一次伤减 1 秒。</summary>
    [Export] public StaggerConfig Stagger { get; set; }

    [ExportGroup("变身")]
    /// <summary>血量归零后的闪白时长（Q11 占位演出）；闪白结束才写 boss_keeper 并发 BossDefeated。</summary>
    [Export] public float TransformFlash { get; set; } = 1f;
}
