using Godot;
using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>
/// 巡游（类名沿用原来的「逼近」，场景和其他状态都接在它上面）：不出招时在整个场地里来回走，钳子始终朝玩家。
/// - 每次挑一个目标点走过去，到了停 RoamPauseMin～Max 秒再挑下一个。三种挑法按权重随机（RoamRng，跟着 Seed 走）：
///   贴近（玩家所在一侧、离玩家 StopDistance）、退开（退到自己这一侧的场地边，离玩家至少 RetreatDistance，场地不够就到边为止）、
///   横穿（从玩家身下走到另一侧，停在离玩家 StopDistance 到那一头场地边之间随机一点）。目标点限制在 RoamMinX～RoamMaxX。
/// - 走路用 Move：只移动不改朝向，往后退时也面朝玩家；悬崖按移动方向检测，到悬崖边、撞墙（0.25 秒没走动）就当作到了。
/// - 朝向：玩家水平距离在转身死区（TurnDeadzone）外就面朝玩家，死区内保持原朝向。
/// - 出招（和原来的逼近一样）：出招间隔（AttackGapRemaining）走完以后，蝎尾就绪且玩家在 TailTriggerRange 内 → 瞄准；
///   横扫就绪且距离 ≤ SweepRange → 横扫前摇；两个都就绪时蝎尾优先（Q3）。
/// 动画：走的时候播 approach（walk），停下时播 dormant（idle）。
/// </summary>
public partial class ApproachState : KeeperState
{
    private const float ArriveDistance = 4f;
    private const float StuckSeconds = 0.25f;

    /// <summary>当前目标点（全局 X）。</summary>
    public float TargetX { get; private set; }
    /// <summary>当前这次挑的是哪一种。</summary>
    public KeeperBoss.RoamKind Kind { get; private set; }
    /// <summary>到点后还要停多久；&gt; 0 表示正停着。</summary>
    public float PauseLeft { get; private set; }
    public bool Paused => PauseLeft > 0f;

    private float _lastX;
    private float _stuck;
    private float _lastSide;

    public override string DisplayName => Paused ? "巡游（停）" : Kind switch
    {
        KeeperBoss.RoamKind.Approach => "巡游（贴近）",
        KeeperBoss.RoamKind.Retreat => "巡游（退开）",
        _ => "巡游（横穿）",
    };
    public override string AnimName => Paused ? "dormant" : "approach";

    public override void Enter(StateMsg msg)
    {
        PauseLeft = 0f;
        _lastSide = SideOfTarget();
        Pick();
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        K.ApplyGravity(dt);
        if (!K.HasTarget)
        {
            K.Stop();
            return;
        }
        if (K.AttackGapRemaining <= 0f)
        {
            float distance = Mathf.Abs(K.DxToTarget);
            if (K.Tail.IsReady && distance <= D.TailTriggerRange)
            {
                Machine.TransitionTo<TailAimState>();
                return;
            }
            if (K.Sweep.IsReady && distance <= D.SweepRange)
            {
                Machine.TransitionTo<SweepWindupState>();
                return;
            }
        }
        K.FaceTargetOutsideDeadzone();
        CountCrossing();

        if (Paused)
        {
            K.Stop();
            PauseLeft -= dt;
            if (PauseLeft <= 0.000001f)
            {
                PauseLeft = 0f;
                Pick();
            }
            return;
        }

        float dx = TargetX - K.GlobalPosition.X;
        if (Mathf.Abs(dx) <= ArriveDistance || !K.Move(Mathf.Sign(dx), D.RoamSpeed))
        {
            Arrive();
            return;
        }
        // 撞墙：一直想走却没走动
        _stuck = Mathf.Abs(K.GlobalPosition.X - _lastX) < 0.5f ? _stuck + dt : 0f;
        _lastX = K.GlobalPosition.X;
        if (_stuck >= StuckSeconds) Arrive();
    }

    private void Arrive()
    {
        K.Stop();
        float min = Mathf.Min(D.RoamPauseMin, D.RoamPauseMax), max = Mathf.Max(D.RoamPauseMin, D.RoamPauseMax);
        PauseLeft = Mathf.Max(0.0001f, K.RoamRng.RandfRange(min, max));
    }

    /// <summary>守坑人在玩家哪一边：1 右边，-1 左边。</summary>
    private float SideOfTarget()
    {
        float dx = K.GlobalPosition.X - (K.HasTarget ? K.Target.GlobalPosition.X : K.GlobalPosition.X);
        return Mathf.Abs(dx) > 0.5f ? Mathf.Sign(dx) : -K.Facing;
    }

    /// <summary>从玩家身下走过去、换到另一边，记一次横穿。</summary>
    private void CountCrossing()
    {
        float side = SideOfTarget();
        if (side != _lastSide && Mathf.Abs(K.DxToTarget) > 0.5f)
        {
            K.Crossings++;
            _lastSide = side;
        }
    }

    /// <summary>挑下一个目标点。</summary>
    public void Pick()
    {
        _stuck = 0f;
        PauseLeft = 0f;
        _lastX = K.GlobalPosition.X;
        Kind = K.ForceNextRoam ?? RollKind();
        K.ForceNextRoam = null;
        K.RoamCounts[(int)Kind]++;
        float min = K.RoamMinX, max = K.RoamMaxX;
        float x = K.GlobalPosition.X;
        float player = K.HasTarget ? K.Target.GlobalPosition.X : x;
        float side = SideOfTarget();
        float target = Kind switch
        {
            KeeperBoss.RoamKind.Approach => player + side * D.StopDistance,
            // 横穿：走到玩家另一侧，离玩家 StopDistance 到那一头场地边之间随机一点（更常从一头晃到另一头）
            KeeperBoss.RoamKind.Cross => Mathf.Lerp(player - side * D.StopDistance, side > 0f ? min : max, K.RoamRng.Randf()),
            // 退开：退到自己这一侧的场地边（离玩家越远越好；场地边离玩家不到 RetreatDistance 时也就只能退到边上）
            _ => side > 0f ? max : min,
        };
        target = Mathf.Clamp(target, min, max);
        TargetX = target;
    }

    private KeeperBoss.RoamKind RollKind()
    {
        float a = Mathf.Max(0f, D.RoamApproachWeight), r = Mathf.Max(0f, D.RoamRetreatWeight), c = Mathf.Max(0f, D.RoamCrossWeight);
        float total = a + r + c;
        if (total <= 0f) return KeeperBoss.RoamKind.Approach;
        float roll = K.RoamRng.Randf() * total;
        if (roll < a) return KeeperBoss.RoamKind.Approach;
        if (roll < a + r) return KeeperBoss.RoamKind.Retreat;
        return KeeperBoss.RoamKind.Cross;
    }
}
