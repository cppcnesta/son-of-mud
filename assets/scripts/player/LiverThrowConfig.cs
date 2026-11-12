using Godot;
using SonOfMud.Combat;

namespace SonOfMud.Player;

/// <summary>
/// 玩家扔泥肝的参数（assets/data/player_v2/liver_throw.tres，挂在玩家场景的 Throw 状态上）。
/// 伤害固定是 Hit 里的值（15），不吃形态倍率和泥格挡增伤；飞行参数和虔诚泥人共用 mud_liver_projectile.tres。
/// </summary>
[GlobalClass]
public partial class LiverThrowConfig : Resource
{
    [ExportGroup("道具")]
    /// <summary>消耗的计数道具编号：数量在进度变量 count.&lt;编号&gt; 里。</summary>
    [Export] public StringName ItemId { get; set; } = "泥肝";

    [ExportGroup("伤害与飞行")]
    /// <summary>单发伤害（15）。</summary>
    [Export] public HitSpec Hit { get; set; }
    /// <summary>重力、判定半径、寿命、颜色。</summary>
    [Export] public ProjectileSpec Projectile { get; set; }
    /// <summary>落回出手高度时飞过的水平距离（像素）。地形 1 格 64 像素，300 ≈ 4.7 格。</summary>
    [Export] public float Range { get; set; } = 300f;
    /// <summary>
    /// 落回出手高度用的时间（秒）。越短越平：0.45 秒、重力 1200 时最高点只比出手点高约 30 像素，
    /// 矮个子的敌人（祈祷泥人 56 高）在整段路上都打得到，不会从头顶飞过去。
    /// </summary>
    [Export] public float FlightTime { get; set; } = 0.45f;
    /// <summary>出手点相对玩家中心的位置（按朝右摆，朝左时左右翻转）。玩家中心离地 24 像素。</summary>
    [Export] public Vector2 LaunchOffset { get; set; } = new(20f, 0f);

    [ExportGroup("节奏")]
    /// <summary>投掷动作时长（秒），期间不能做别的动作。</summary>
    [Export] public float Duration { get; set; } = 0.2f;
    /// <summary>两次投掷之间的最短间隔（秒），从出手那一刻算。</summary>
    [Export] public float Interval { get; set; } = 0.4f;
}
