using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Enemy.States;
using SonOfMud.Player.States;
using IdleState = SonOfMud.Player.States.IdleState;

/// <summary>
/// 虔诚泥人（远程，投掷物）验收：devout_lab.tscn 里用真实玩家、真实按键、正式 .tres，测试里不改伤害和血量。
/// ① 看不见玩家时在出生点左右巡逻；② 看见玩家主动上前，走到约 300 像素停下，前摇 0.6 秒后抛出泥肝（抛物线）；
/// ③ 站着不动被泥肝打中掉 15 血，同一个泥肝只结算一次，结算后消失；④ 真实按 K 格挡能挡住；
/// ⑤ 真实按住方向键 + Shift 闪避能躲开，泥肝落地就碎；⑥ 隔着墙扔，泥肝撞墙就碎，打不到墙后的玩家；
/// ⑦ 真实攻击打死它：EnemyKilled 一次，固定掉落种子 → 掉泥肝。
/// </summary>
public partial class DevoutLabTest : LabSuiteBase
{
    private const string LabScene = "res://assets/scenes/tests/devout_lab.tscn";

    protected override string ReportName => "enemy_devout_lab_results";

    private EnemyBody _devout, _devoutWall;
    private readonly List<string> _killed = new();
    private readonly List<string> _dropped = new();
    private int _playerDamaged;
    private readonly List<Projectile> _thrown = new();

    protected override async Task RunChecks()
    {
        Events.Instance.EnemyKilled += OnKilled;
        Events.Instance.ItemDropped += OnDropped;
        try
        {
            await PatrolChecks();
            await ApproachAndHitChecks();
            await BlockChecks();
            await DodgeChecks();
            await WallChecks();
            await KillChecks();
        }
        finally
        {
            RealKeys.ReleaseAll();
            Events.Instance.EnemyKilled -= OnKilled;
            Events.Instance.ItemDropped -= OnDropped;
        }
    }

    private void OnKilled(StringName kind, Vector2 position) => _killed.Add(kind.ToString());
    private void OnDropped(StringName item, Vector2 position) => _dropped.Add(item.ToString());

    // ---------- 搭建与工具 ----------

    private async Task Fresh(Vector2 playerAt)
    {
        RealKeys.ReleaseAll();
        await LoadLab(LabScene);
        _devout = Lab.GetNode<EnemyBody>("Devout");
        _devoutWall = Lab.GetNode<EnemyBody>("DevoutWall");
        _killed.Clear();
        _dropped.Clear();
        _thrown.Clear();
        _playerDamaged = 0;
        Player.HurtBox.Damaged += (hit, dealt) => _playerDamaged++;
        _devout.ProjectileThrown += p => _thrown.Add(p);
        _devoutWall.ProjectileThrown += p => _thrown.Add(p);
        await Reset(playerAt);
    }

    private bool PlayerFree => Player.State is IdleState or RunState;

    private async Task<bool> WaitFor(Func<bool> condition, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition()) return true;
            await Frames(1);
        }
        return condition();
    }

    private static ulong FindSeed(Func<RandomNumberGenerator, bool> predicate)
    {
        for (ulong seed = 1; seed < 200000; seed++)
            if (predicate(new RandomNumberGenerator { Seed = seed })) return seed;
        throw new InvalidOperationException("找不到符合条件的种子");
    }

    /// <summary>等下一个泥肝扔出来（返回它；超时返回 null）。</summary>
    private async Task<Projectile> NextThrow(int maxFrames = 400)
    {
        int count = _thrown.Count;
        await WaitFor(() => _thrown.Count > count, maxFrames);
        return _thrown.Count > count ? _thrown[^1] : null;
    }

    /// <summary>投掷物飞完（结束并释放）为止，记下它的 HitLanded 次数、最高点和结束原因。</summary>
    private sealed class Flight
    {
        public int Landed;
        public float MinY = float.MaxValue;
        public ProjectileEnd End;
        public HitOutcome? Outcome;
        public int Resolutions;
        public Vector2 EndPosition;
        public bool Freed;
    }

    private async Task<Flight> Follow(Projectile p, Action<Projectile> eachFrame = null, int maxFrames = 300)
    {
        var flight = new Flight();
        if (p == null) return flight;
        p.HitLanded += (t, h, o) => flight.Landed++;
        // 结束的那一帧末尾就释放了，所以在 Finished 信号里把结果记下来
        p.Finished += (reason, position) =>
        {
            flight.EndPosition = position;
            flight.End = (ProjectileEnd)reason;
            flight.Outcome = p.Outcome;
            flight.Resolutions = p.Resolutions;
        };
        for (int i = 0; i < maxFrames && GodotObject.IsInstanceValid(p) && !p.Ended; i++)
        {
            flight.MinY = Mathf.Min(flight.MinY, p.GlobalPosition.Y);
            eachFrame?.Invoke(p);
            await Frames(1);
        }
        await Frames(2);
        flight.Freed = !GodotObject.IsInstanceValid(p);
        return flight;
    }

    private async Task<HitOutcome?> Strike(EnemyBody enemy, int maxFrames = 900)
    {
        HitOutcome? outcome = null;
        void OnLanded(HurtBox target, Hit hit, int value)
        {
            if (target.Entity == enemy) outcome = (HitOutcome)value;
        }
        Player.AttackHitBox.HitLanded += OnLanded;
        Key? held = null;
        void Hold(Key? key)
        {
            if (held == key) return;
            if (held is { } old) RealKeys.Up(old);
            held = key;
            if (key is { } k) RealKeys.Down(k);
        }
        bool pressed = false, attacking = false;
        for (int i = 0; i < maxFrames; i++)
        {
            if (pressed)
            {
                RealKeys.Up(RealKeys.Attack);
                attacking |= Player.State is AttackState;
                if (attacking && Player.State is not AttackState) break;
                await Frames(1);
                continue;
            }
            float dx = enemy.GlobalPosition.X - Player.GlobalPosition.X;
            bool facing = Mathf.Sign(dx) == Mathf.Sign(Player.Facing);
            if (Mathf.Abs(dx) > 75f) Hold(RealKeys.Toward(dx));
            else if (Mathf.Abs(dx) < 40f) Hold(RealKeys.Toward(-dx));
            else if (!facing) Hold(RealKeys.Toward(dx));
            else
            {
                Hold(null);
                if (PlayerFree && Player.Timers.AttackReady)
                {
                    RealKeys.Down(RealKeys.Attack);
                    pressed = true;
                }
            }
            await Frames(1);
        }
        Hold(null);
        RealKeys.Up(RealKeys.Attack);
        Player.AttackHitBox.HitLanded -= OnLanded;
        await Frames(2);
        return outcome;
    }

    // ---------- ① 巡逻 ----------

    private async Task PatrolChecks()
    {
        await Fresh(new Vector2(400, 576));
        float home = _devout.Home.X, distance = _devout.Data.PatrolDistance;
        float minX = home, maxX = home;
        bool patrolled = false;
        for (int i = 0; i < 300; i++)
        {
            minX = Mathf.Min(minX, _devout.GlobalPosition.X);
            maxX = Mathf.Max(maxX, _devout.GlobalPosition.X);
            patrolled |= _devout.State is PatrolState;
            await Frames(1);
        }
        Check(patrolled && minX <= home - distance + 8f && maxX >= home + distance - 8f && minX >= home - distance - 8f && maxX <= home + distance + 8f
              && _devout.ProjectilesThrown == 0,
            $"虔诚泥人：看不见玩家时在工地上走动（出生点 {home:F0} 左右 {distance:F0}，实际 {minX:F0}–{maxX:F0}），不扔东西");
    }

    // ---------- ② ③ 上前、停在 300 外、前摇、扔中 ----------

    private async Task ApproachAndHitChecks()
    {
        await Fresh(new Vector2(1550, 576));
        await Frames(30);
        bool unseen = !_devout.CanSeeTarget() && _devout.ProjectilesThrown == 0;
        // 玩家往右走几步进入视野
        RealKeys.Down(RealKeys.Right);
        bool seen = await WaitFor(() => _devout.State is ChaseState or AttackWindupState, 120);
        RealKeys.Up(RealKeys.Right);
        float seenX = _devout.GlobalPosition.X;
        bool windup = await WaitFor(() => _devout.State is AttackWindupState, 300);
        float dx = Mathf.Abs(_devout.GlobalPosition.X - Player.GlobalPosition.X);
        Check(unseen && seen && windup && _devout.GlobalPosition.X < seenX - 20f && dx <= 301f && dx >= 280f,
            $"虔诚泥人：玩家在 450 外时不理会；进入视野后主动走上前（{seenX:F0} → {_devout.GlobalPosition.X:F0}），在约 300 像素处停下（距离 {dx:F0}）开始前摇");

        float windupX = _devout.GlobalPosition.X;
        int windupFrames = 0;
        while (_devout.State is AttackWindupState && windupFrames < 120)
        {
            await Frames(1);
            windupFrames++;
        }
        Projectile p = _thrown.Count > 0 ? _thrown[^1] : null;
        Check(Mathf.Abs(windupFrames - 36) <= 1 && p != null && _devout.ProjectilesThrown == 1 && Mathf.Abs(_devout.GlobalPosition.X - windupX) < 0.5f,
            $"虔诚泥人：前摇站定 0.6 秒（{windupFrames} 帧）后扔出一个泥肝");
        Check(p != null && GodotObject.IsInstanceValid(p) && p.Team == ProjectileTeam.Enemy && p.CollisionLayer == Layers.EnemyHit && p.CollisionMask == Layers.PlayerHurt && p.GetParent() == Lab
              && p.Hit?.Damage == 15f && p.Hit.Blockable && p.Hit.Source == _devout,
            "泥肝：敌方投掷物（EnemyHit 层，只打玩家受击框），挂在房间（测试场）下，伤害 15、可格挡，攻击方是虔诚泥人");

        int hp = Player.Stats.Health;
        float launchY = p != null && GodotObject.IsInstanceValid(p) ? p.GlobalPosition.Y : 0f;
        Flight flight = await Follow(p);
        Check(flight.MinY < launchY - 30f, $"泥肝：抛物线飞行（出手高度 {launchY:F0}，最高到 {flight.MinY:F0}）");
        Check(flight.End == ProjectileEnd.Target && flight.Outcome == HitOutcome.Damaged && Player.Stats.Health == hp - 15 && _playerDamaged == 1,
            $"泥肝：站着不动的玩家被打中掉 15 血（{hp} → {Player.Stats.Health}，结果 {flight.Outcome}）");
        Check(flight.Resolutions == 1 && flight.Landed == 1 && flight.Freed,
            $"泥肝：同一个泥肝只结算一次（结算 {flight.Resolutions} 次、HitLanded {flight.Landed} 次），结算后立刻消失");
    }

    // ---------- ④ 格挡 ----------

    private async Task BlockChecks()
    {
        await Fresh(new Vector2(1650, 576));
        Projectile p = await NextThrow();
        bool blocking = false;
        int hp = Player.Stats.Health;
        bool pressed = false;
        Flight flight = await Follow(p, proj =>
        {
            if (!pressed && Mathf.Abs(proj.GlobalPosition.X - Player.GlobalPosition.X) < 110f && PlayerFree && Player.Timers.BlockReady)
            {
                RealKeys.Down(RealKeys.Block);
                pressed = true;
            }
            blocking |= Player.State is BlockState;
        });
        RealKeys.Up(RealKeys.Block);
        Check(p != null && pressed && blocking && flight.End == ProjectileEnd.Target && flight.Outcome == HitOutcome.Blocked && Player.Stats.Health == hp && _playerDamaged == 0
              && flight.Resolutions == 1 && flight.Freed,
            $"泥肝：泥肝飞近时真实按 K 格挡，挡住了（结果 {flight.Outcome}），玩家不掉血，泥肝碎掉");
    }

    // ---------- ⑤ 闪避 ----------

    private async Task DodgeChecks()
    {
        await Fresh(new Vector2(1650, 576));
        Projectile p = await NextThrow();
        float radius = p != null && GodotObject.IsInstanceValid(p) ? p.Radius : 0f;
        bool dodged = false, pressed = false;
        int hp = Player.Stats.Health;
        Flight flight = await Follow(p, proj =>
        {
            if (!pressed && Mathf.Abs(proj.GlobalPosition.X - Player.GlobalPosition.X) < 160f && PlayerFree && Player.Timers.DodgeReady)
            {
                RealKeys.Down(RealKeys.Left);
                RealKeys.Down(RealKeys.Dodge);
                pressed = true;
            }
            else if (pressed)
            {
                RealKeys.Up(RealKeys.Dodge);
                RealKeys.Up(RealKeys.Left);
            }
            dodged |= Player.State is DodgeState;
        });
        RealKeys.Up(RealKeys.Dodge);
        RealKeys.Up(RealKeys.Left);
        Check(p != null && pressed && dodged && flight.End == ProjectileEnd.World && flight.Outcome == null && Player.Stats.Health == hp && _playerDamaged == 0 && flight.Freed,
            $"泥肝：真实按住 A + Shift 闪避躲开，泥肝落地就碎（结束原因 {flight.End}，落点 {flight.EndPosition.X:F0},{flight.EndPosition.Y:F0}），玩家不掉血");
        Check(p != null && flight.EndPosition.Y >= 600f - radius - 2f, $"泥肝：碎在地面上（落点 y {flight.EndPosition.Y:F0}，地面 600）");
    }

    // ---------- ⑥ 撞墙 ----------

    private async Task WallChecks()
    {
        await Fresh(new Vector2(3200, 576));
        Projectile p = await NextThrow();
        int hp = Player.Stats.Health;
        bool fromWallDevout = p != null && GodotObject.IsInstanceValid(p) && p.Hit?.Source == _devoutWall;
        float radius = fromWallDevout ? p.Radius : 0f;
        Flight flight = await Follow(p);
        Check(fromWallDevout && flight.End == ProjectileEnd.World && flight.Outcome == null && flight.EndPosition.X >= 3362f && flight.EndPosition.X <= 3362f + radius + 12f
              && Player.Stats.Health == hp && flight.Freed,
            $"泥肝：隔着墙朝玩家扔，撞到墙（x 3330–3362）就碎（落点 x {flight.EndPosition.X:F0}），没有穿墙打到玩家");
    }

    // ---------- ⑦ 打死 ----------

    private async Task KillChecks()
    {
        await Fresh(new Vector2(1650, 576));
        _devout.SetSeeds(1, FindSeed(r => r.Randf() < 0.3f));
        var reactions = new List<string>();
        for (int i = 0; i < 8 && !_devout.IsDead; i++)
        {
            HitOutcome? outcome = await Strike(_devout);
            if (outcome == HitOutcome.Damaged) reactions.Add(_devout.LastReaction);
        }
        await Frames(5);
        Check(_devout.IsDead && _killed.SequenceEqual(new[] { "devout" }) && reactions.All(r => r == "hurt"),
            $"虔诚泥人：真实攻击打死它（水形态 4 刀），被打时只是受击硬直、不逃跑（{string.Join(",", reactions)}），EnemyKilled 一次");
        Check(_dropped.SequenceEqual(new[] { "泥肝" }), $"虔诚泥人：固定掉落种子 → 掉一个泥肝（{string.Join(",", _dropped)}）");
    }
}
