using Godot;
using SonOfMud.Core;

namespace SonOfMud.Combat;

/// <summary>
/// 通用投掷物：一个会飞的 HitBox。命中结算完全走 HitBox → HurtBox.Receive 那一套（去重、无敌、格挡、扣血都一样），
/// 所以玩家格挡它和格挡近战攻击是同一个判定：HurtBox.Guard 覆盖、Hit.Blockable 为真就是 Blocked。
/// Hit.SourcePosition 每帧更新成投掷物当前的位置，单向格挡（Omnidirectional = false）按它判断从哪边来。
///
/// 用法：Projectile.Launch(父节点, 发射位置, 初速度, Hit 或 HitSpec, 所属一方, 飞行参数)。
/// - 每个物理帧：按重力推进（恒定重力下的精确位移），用圆形扫掠（CastMotion）查世界碰撞防止穿墙，
///   再在新位置用自身的圆形查一次受击框；
/// - 只结算一次：第一次得到不是 Ignored 的结果（Blocked / Damaged / Killed / Invulnerable / Duplicate）就结束并消失；
///   受击方不接受攻击（Ignored，比如玩家调试飞行）时穿过去继续飞；
/// - 撞到世界碰撞（墙、地面）就结束（ProjectileEnd.World）并消失；飞得超过 MaxLifetime 也消失；
/// - 结束时发 Finished(原因, 位置) 信号（原因是 ProjectileEnd 的整数值），然后 QueueFree。
/// 父节点通常是房间（敌人传自己的父节点），房间卸载时投掷物跟着释放。
/// </summary>
[GlobalClass]
public partial class Projectile : HitBox
{
    [Signal] public delegate void FinishedEventHandler(int reason, Vector2 position);

    public const float DefaultRadius = 10f;

    public Vector2 Velocity { get; set; }
    public float Gravity { get; set; } = 1200f;
    public float Radius { get; private set; } = DefaultRadius;
    public float MaxLifetime { get; set; } = 4f;
    public ProjectileTeam Team { get; private set; } = ProjectileTeam.Enemy;
    public Color Tint { get; set; } = new(0.55f, 0.2f, 0.16f);

    /// <summary>已经飞了多久（秒）。</summary>
    public float Age { get; private set; }
    public ProjectileEnd EndReason { get; private set; } = ProjectileEnd.None;
    public bool Ended => EndReason != ProjectileEnd.None;
    /// <summary>结算次数（不算 Ignored）。按规则最多是 1。</summary>
    public int Resolutions { get; private set; }
    /// <summary>那一次结算的结果；没打到东西是 null。</summary>
    public HitOutcome? Outcome { get; private set; }
    /// <summary>被结算的受击框；没打到东西是 null。</summary>
    public HurtBox Target { get; private set; }
    /// <summary>发射时带的那次攻击（结束后 HitBox.Current 会清空，这里保留）。</summary>
    public Hit Hit { get; private set; }

    private Hit _pendingHit;
    private CircleShape2D _shape;

    /// <summary>
    /// 生成并发射一个投掷物。parent：挂在哪个节点下（一般是房间）；position：发射点（全局坐标）；velocity：初速度（像素 / 秒）；
    /// hit：伤害（用 Hit.From(HitSpec, 攻击编号, 位置, 倍率, 攻击方) 生成，每个投掷物一个新的攻击编号）；team：哪一方；
    /// spec：重力、半径、寿命、颜色，不填用默认值。生成的同一帧就会查一次受击框。
    /// </summary>
    public static Projectile Launch(Node parent, Vector2 position, Vector2 velocity, Hit hit, ProjectileTeam team, ProjectileSpec spec = null)
    {
        var projectile = new Projectile
        {
            Name = "Projectile",
            Velocity = velocity,
            Gravity = spec?.Gravity ?? 1200f,
            MaxLifetime = spec?.MaxLifetime ?? 4f,
            Tint = spec?.Tint ?? new Color(0.55f, 0.2f, 0.16f),
        };
        projectile.Setup(team, spec?.Radius ?? DefaultRadius, hit);
        parent.AddChild(projectile);
        projectile.GlobalPosition = position;
        projectile.Fire();
        return projectile;
    }

    /// <summary>同上，直接给 HitSpec：按它生成 Hit（攻击编号 attackId、攻击方 source、倍率 multiplier）。</summary>
    public static Projectile Launch(Node parent, Vector2 position, Vector2 velocity, HitSpec hitSpec, string attackId, Node source,
        ProjectileTeam team, ProjectileSpec spec = null, float multiplier = 1f) =>
        Launch(parent, position, velocity, Hit.From(hitSpec, attackId, position, multiplier, source), team, spec);

    /// <summary>
    /// 抛物线初速度：从 from 出发、水平速度 horizontalSpeed、重力 gravity 时，恰好经过 to 的速度。
    /// 飞行时间 = 水平距离 / 水平速度，但至少 minFlightTime 秒（太近时抛高一点，不会平着砸过去）。
    /// </summary>
    public static Vector2 ArcVelocity(Vector2 from, Vector2 to, float gravity, float horizontalSpeed, float minFlightTime = 0.35f)
    {
        float dx = to.X - from.X;
        float t = Mathf.Max(Mathf.Abs(dx) / Mathf.Max(horizontalSpeed, 1f), Mathf.Max(minFlightTime, 0.05f));
        return new Vector2(dx / t, (to.Y - from.Y - 0.5f * gravity * t * t) / t);
    }

    private void Setup(ProjectileTeam team, float radius, Hit hit)
    {
        Team = team;
        Radius = Mathf.Max(radius, 1f);
        _pendingHit = hit;
        _shape = new CircleShape2D { Radius = Radius };
        AddChild(new CollisionShape2D { Name = "CollisionShape2D", Shape = _shape });
        CollisionLayer = team == ProjectileTeam.Enemy ? Layers.EnemyHit : Layers.PlayerHit;
        CollisionMask = team == ProjectileTeam.Enemy ? Layers.PlayerHurt : Layers.EnemyHurt;
    }

    public override void _Ready()
    {
        base._Ready();
        HitLanded += OnResolved;
    }

    private void Fire()
    {
        Hit = _pendingHit ?? new Hit();
        Hit.SourcePosition = GlobalPosition;
        if (OverlapsWorld())
        {
            End(ProjectileEnd.World);
            return;
        }
        Activate(Hit);
    }

    public override void _PhysicsProcess(double delta) => Step((float)delta);

    /// <summary>推进一帧（节点在树里时每个物理帧自动调用）。</summary>
    public void Step(float delta)
    {
        if (Ended || !IsInsideTree()) return;
        Age += delta;
        if (Age >= MaxLifetime)
        {
            End(ProjectileEnd.Expired);
            return;
        }
        Vector2 motion = Velocity * delta + new Vector2(0f, 0.5f * Gravity * delta * delta);
        Velocity += new Vector2(0f, Gravity * delta);
        float safe = 1f;
        if (motion.LengthSquared() > 0f)
        {
            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = _shape,
                Transform = new Transform2D(0f, GlobalPosition),
                Motion = motion,
                CollisionMask = Layers.World,
                CollideWithBodies = true,
                CollideWithAreas = false,
            };
            float[] result = GetWorld2D().DirectSpaceState.CastMotion(query);
            if (result.Length >= 1) safe = result[0];
        }
        GlobalPosition += motion * safe;
        if (Hit != null) Hit.SourcePosition = GlobalPosition;
        Scan();
        if (Ended) return;
        if (safe < 1f) End(ProjectileEnd.World);
    }

    private bool OverlapsWorld()
    {
        if (!IsInsideTree()) return false;
        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = _shape,
            Transform = new Transform2D(0f, GlobalPosition),
            CollisionMask = Layers.World,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };
        return GetWorld2D().DirectSpaceState.IntersectShape(query, 1).Count > 0;
    }

    private void OnResolved(HurtBox target, Hit hit, int outcome)
    {
        if (Ended || (HitOutcome)outcome == HitOutcome.Ignored) return;
        Resolutions++;
        Outcome = (HitOutcome)outcome;
        Target = target;
        End(ProjectileEnd.Target);
    }

    private void End(ProjectileEnd reason)
    {
        if (Ended) return;
        EndReason = reason;
        Deactivate();
        SetPhysicsProcess(false);
        EmitSignal(SignalName.Finished, (int)reason, GlobalPosition);
        QueueFree();
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (!Ended) DrawCircle(Vector2.Zero, Radius, Tint);
    }
}
