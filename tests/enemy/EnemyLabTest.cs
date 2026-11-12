using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Enemy.States;
using SonOfMud.Player;
using SonOfMud.Player.States;
using EnemyIdle = SonOfMud.Enemy.States.IdleState;
using EnemyDead = SonOfMud.Enemy.States.DeadState;
using IdleState = SonOfMud.Player.States.IdleState;

/// <summary>
/// enemy_lab.tscn 里用真实玩家、真实按键（RealKeys）、正式 .tres 配置测普通敌人（新设定）。测试里不改任何伤害和血量。
/// ① 塔庙泥匠：会追、会前摇（站定）、会出手；没挡住扣 20 且同一次攻击只结算一次；能被真实格挡；挥空也按计时收招；
///    死亡时 EnemyKilled 恰好一次，死后再打是 Ignored；不逃跑、不掉落。
/// ② 祈祷泥人绝不主动出手：玩家在身边站 2 秒，它一直跪着、不出手。
/// ③ 祈祷泥人被打（水，固定种子 → 逃跑）：第一刀剩 20，逃跑，再补两刀才死；泥坑的掉落表固定种子 → 掉泥瓶。
/// ④ 祈祷泥人被打（泥，固定种子 → 反击）：第一刀剩 6，起身前摇 0.5 秒（看得出来）后泥手挥击，站着不动的玩家只扣一次 10；
///    反击完回去跪着，不追、不再出手；补一刀死，这次固定种子不掉落。
/// ⑤ 占卜之城的祈祷泥人（摆放点覆盖掉落表）：反击能被真实按 K 格挡住；打死 100% 掉泥瓶。
/// ⑥ 塔庙泥卫：看不见玩家时在出生点左右按巡逻距离来回走；看见了就冲锋——前摇时锁定方向，冲锋中途玩家换到身后也不转向；
///    可以被格挡；没挡住只扣一次 20。
/// </summary>
public partial class EnemyLabTest : LabSuiteBase
{
    private const string LabScene = "res://assets/scenes/tests/enemy_lab.tscn";

    protected override string ReportName => "enemy_lab_results";

    private EnemyBody _mason, _prayer, _prayerCity, _guard;
    private readonly List<string> _killed = new();
    private readonly List<string> _dropped = new();
    private int _playerDamaged;

    protected override async Task RunChecks()
    {
        Events.Instance.EnemyKilled += OnKilled;
        Events.Instance.ItemDropped += OnDropped;
        try
        {
            await MasonChecks();
            await PrayerNeverStartsChecks();
            await PrayerFleeChecks();
            await PrayerCounterChecks();
            await PrayerCityChecks();
            await GuardPatrolChecks();
            await GuardChargeChecks();
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

    // ---------- 搭建 ----------

    private async Task Fresh(Vector2 playerAt)
    {
        RealKeys.ReleaseAll();
        await LoadLab(LabScene);
        _mason = Lab.GetNode<EnemyBody>("Mason");
        _prayer = Lab.GetNode<EnemyBody>("Prayer");
        _prayerCity = Lab.GetNode<EnemyBody>("PrayerCity");
        _guard = Lab.GetNode<EnemyBody>("TempleGuard");
        _killed.Clear();
        _dropped.Clear();
        _playerDamaged = 0;
        Player.HurtBox.Damaged += (hit, dealt) => _playerDamaged++;
        await Reset(playerAt);
    }

    /// <summary>找一个随机种子，让 RandomNumberGenerator 掷出的前几个数满足条件（和敌人内部的掷法一致：每次 Randf 一个数）。</summary>
    private static ulong FindSeed(Func<RandomNumberGenerator, bool> predicate)
    {
        for (ulong seed = 1; seed < 200000; seed++)
            if (predicate(new RandomNumberGenerator { Seed = seed })) return seed;
        throw new InvalidOperationException("找不到符合条件的种子");
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

    private async Task<int> CountFramesWhile(Func<bool> condition, int maxFrames)
    {
        int n = 0;
        while (condition() && n < maxFrames)
        {
            await Frames(1);
            n++;
        }
        return n;
    }

    /// <summary>
    /// 用真实按键走到敌人前面（40–75px、面朝它），按一下 J，等这一刀结束。返回这一刀对它的结算结果（没打中是 null）。
    /// </summary>
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

    /// <summary>Strike，没打中（敌人走开了）就再来，最多 tries 次。</summary>
    private async Task<HitOutcome?> StrikeHit(EnemyBody enemy, int tries = 4)
    {
        for (int i = 0; i < tries; i++)
        {
            HitOutcome? outcome = await Strike(enemy);
            if (outcome != null) return outcome;
        }
        return null;
    }

    /// <summary>记下敌人每次切状态时的物理帧号（量前摇时长用）。</summary>
    private static List<(ulong frame, string state)> TrackStates(EnemyBody enemy)
    {
        var log = new List<(ulong, string)>();
        enemy.Machine.StateChanged += (from, to) => log.Add((Engine.GetPhysicsFrames(), to.ToString()));
        return log;
    }

    // ---------- ① 塔庙泥匠 ----------

    private async Task MasonChecks()
    {
        await Fresh(new Vector2(600, 576));
        _mason.SetSeeds(1, 1);
        float startX = _mason.GlobalPosition.X;
        bool windup = await WaitFor(() => _mason.State is AttackWindupState, 300);
        float dx = Mathf.Abs(_mason.GlobalPosition.X - Player.GlobalPosition.X);
        Check(windup && _mason.GlobalPosition.X < startX - 50f && dx <= 90f, $"塔庙泥匠：看见玩家就追过来（{startX:F0} → {_mason.GlobalPosition.X:F0}），进入攻击距离（{dx:F0} ≤ 90）开始前摇");

        float windupX = _mason.GlobalPosition.X;
        int hp = Player.Stats.Health, damagedBefore = _playerDamaged, attacks = _mason.AttacksStarted;
        int windupFrames = await CountFramesWhile(() => _mason.State is AttackWindupState, 120);
        Check(Mathf.Abs(_mason.GlobalPosition.X - windupX) < 0.5f && Mathf.Abs(windupFrames - 30) <= 1 && _mason.State is AttackActiveState,
            $"塔庙泥匠：前摇站定 0.5 秒（{windupFrames} 帧）后挥锤");
        var active = _mason.Machine.Get<AttackActiveState>();
        int activeFrames = await CountFramesWhile(() => _mason.State is AttackActiveState, 120);
        Check(_mason.AttacksStarted == attacks + 1 && active.LastOutcome == HitOutcome.Damaged && Mathf.Abs(activeFrames - 9) <= 1,
            $"塔庙泥匠：挥锤打中站着不动的玩家（结果 {active.LastOutcome}），判定 0.15 秒（{activeFrames} 帧）按计时收招");
        Check(Player.Stats.Health == hp - 20 && _playerDamaged == damagedBefore + 1,
            $"塔庙泥匠：同一次攻击只结算一次——判定开了 {activeFrames} 帧，玩家只扣一次 20（{hp} → {Player.Stats.Health}）");

        // 真实格挡：下一次前摇快结束时按住 K（水格挡 0.5 秒）
        bool second = await WaitFor(() => _mason.State is AttackWindupState { Remaining: <= 0.12f } && PlayerFree, 600);
        hp = Player.Stats.Health;
        damagedBefore = _playerDamaged;
        RealKeys.Down(RealKeys.Block);
        await Frames(2);
        bool blocking = Player.State is BlockState;
        await WaitFor(() => _mason.State is AttackRecoverState, 60);
        RealKeys.Up(RealKeys.Block);
        Check(second && blocking && active.LastOutcome == HitOutcome.Blocked && Player.Stats.Health == hp && _playerDamaged == damagedBefore,
            $"塔庙泥匠：真实按 K 格挡住挥锤（结果 {active.LastOutcome}），玩家不掉血");

        // 挥空：前摇一开始就按住 A 往后跑，出手时玩家已经不在判定里
        bool third = await WaitFor(() => _mason.State is AttackWindupState && PlayerFree, 600);
        hp = Player.Stats.Health;
        attacks = _mason.AttacksStarted;
        RealKeys.Down(RealKeys.Left);
        await WaitFor(() => _mason.State is AttackActiveState, 60);
        RealKeys.Up(RealKeys.Left);
        int whiffActive = await CountFramesWhile(() => _mason.State is AttackActiveState, 120);
        bool recovering = _mason.State is AttackRecoverState;
        int recoverFrames = await CountFramesWhile(() => _mason.State is AttackRecoverState, 200);
        Check(third && _mason.AttacksStarted == attacks + 1 && active.LastOutcome == null && Player.Stats.Health == hp && Mathf.Abs(whiffActive - 9) <= 1
              && recovering && Mathf.Abs(recoverFrames - 36) <= 1,
            $"塔庙泥匠：挥空（没有任何结算回调）也按计时收招——判定 {whiffActive} 帧、收招 {recoverFrames} 帧，之后回到 {_mason.State?.DisplayName}");

        // 打死：水形态基础攻击 4 刀；每次受伤都进受击硬直，从不逃跑
        var hps = new List<int>();
        var outcomes = new List<HitOutcome?>();
        var reactions = new List<string>();
        for (int i = 0; i < 4 && !_mason.IsDead; i++)
        {
            outcomes.Add(await StrikeHit(_mason));
            hps.Add(_mason.Stats.Health);
            if (!_mason.IsDead) reactions.Add(_mason.LastReaction);
        }
        Check(hps.SequenceEqual(new[] { 30, 20, 10, 0 }) && outcomes.Last() == HitOutcome.Killed,
            $"塔庙泥匠：水形态每刀只扣 10（同一刀只结算一次），4 刀打死（血量 {string.Join("→", hps)}，最后一刀 {outcomes.LastOrDefault()}）");
        Check(reactions.Count == 3 && reactions.All(r => r == "hurt"), $"塔庙泥匠：不逃跑，每次受伤都是受击硬直（{string.Join(",", reactions)}）");
        HitOutcome? after = await StrikeHit(_mason);
        await Frames(30);
        Check(_mason.IsDead && _mason.State is EnemyDead && _killed.Count(k => k == "mason") == 1 && _mason.KilledEmits == 1 && after == HitOutcome.Ignored,
            $"塔庙泥匠：死亡时 EnemyKilled 恰好一次（{_killed.Count}），死后再打结果是 {after}，不再发");
        Check(_dropped.Count == 0 && !_mason.AttackHitBox.Active, "塔庙泥匠：不掉落；死后判定关闭");
    }

    // ---------- ② 祈祷泥人绝不主动出手 ----------

    private async Task PrayerNeverStartsChecks()
    {
        await Fresh(new Vector2(1500, 576));
        Vector2 at = _prayer.GlobalPosition;
        RealKeys.Down(RealKeys.Right);
        bool near = await WaitFor(() => Mathf.Abs(_prayer.GlobalPosition.X - Player.GlobalPosition.X) < 70f, 120);
        RealKeys.Up(RealKeys.Right);
        bool stillIdle = true;
        for (int i = 0; i < 120; i++)
        {
            stillIdle &= _prayer.State is EnemyIdle && _prayer.AttackHitBox is { Active: false };
            await Frames(1);
        }
        Check(near && stillIdle && _prayer.GlobalPosition.DistanceTo(at) < 0.5f && _prayer.AttacksStarted == 0 && _playerDamaged == 0 && _prayer.Reactions == 0,
            $"祈祷泥人：玩家走到身边（距离 {Mathf.Abs(_prayer.GlobalPosition.X - Player.GlobalPosition.X):F0}）站 2 秒，它一直跪着祈祷、不动、绝不主动出手");
    }

    // ---------- ③ 被打后逃跑 ----------

    private async Task PrayerFleeChecks()
    {
        await Fresh(new Vector2(1500, 576));
        Check(Player.Forms.Form == FormController.Water && !Player.Timers.Bonus.Ready, "祈祷泥人（水）：玩家是水形态，没有泥增伤（只用基础攻击）");
        // 反应随机数：前两次都逃跑（< 0.5）；掉落随机数：第一次就掉（< 0.3）
        _prayer.SetSeeds(FindSeed(r => r.Randf() < 0.5f && r.Randf() < 0.5f), FindSeed(r => r.Randf() < 0.3f));
        HitOutcome? first = await StrikeHit(_prayer);
        await Frames(3);
        Check(first == HitOutcome.Damaged && _prayer.Stats.Health == 20 && !_prayer.IsDead, $"祈祷泥人（水）：第一刀打不死，剩 {_prayer.Stats.Health} 血");
        Check(_prayer.LastReaction == "flee" && _prayer.State is FleeState, $"祈祷泥人（水）：被惊动后逃跑（固定种子，实际 {_prayer.LastReaction}）");
        float fleeStart = _prayer.GlobalPosition.X;
        await Frames(20);
        Check(_prayer.GlobalPosition.X > fleeStart + 40f, $"祈祷泥人（水）：背对玩家跑开（{fleeStart:F0} → {_prayer.GlobalPosition.X:F0}）");
        HitOutcome? second = await StrikeHit(_prayer);
        Check(second == HitOutcome.Damaged && _prayer.Stats.Health == 10 && !_prayer.IsDead && _prayer.LastReaction == "flee",
            $"祈祷泥人（水）：补第二刀还活着（剩 {_prayer.Stats.Health}），又逃跑");
        HitOutcome? third = await StrikeHit(_prayer);
        await Frames(5);
        Check(third == HitOutcome.Killed && _prayer.IsDead && _killed.SequenceEqual(new[] { "prayer" }), "祈祷泥人（水）：补第三刀才死，EnemyKilled 一次");
        Check(_dropped.SequenceEqual(new[] { "泥瓶" }), $"祈祷泥人：泥坑的掉落表（30%）固定掉落种子 → 掉一个泥瓶（{string.Join(",", _dropped)}）");
        Check(_prayer.AttacksStarted == 0 && _playerDamaged == 0 && Player.Stats.Health == Player.Stats.MaxHealth,
            "祈祷泥人：两次都逃跑时从没出手，全程玩家没掉血");
    }

    // ---------- ④ 被打后反击 ----------

    private async Task PrayerCounterChecks()
    {
        await Fresh(new Vector2(1500, 576));
        await Frames(2);
        RealKeys.Down(RealKeys.SwitchForm);
        await Frames(1);
        RealKeys.Up(RealKeys.SwitchForm);
        await Frames(3);
        Check(Player.Forms.Form == FormController.Mud && !Player.Timers.Bonus.Ready, "祈祷泥人（泥）：真实按 Q 切到泥形态，没有泥增伤");
        _prayer.SetSeeds(FindSeed(r => r.Randf() >= 0.5f), FindSeed(r => r.Randf() >= 0.3f));
        var states = TrackStates(_prayer);
        int hp = Player.Stats.Health;
        float x = _prayer.GlobalPosition.X;
        HitOutcome? first = await StrikeHit(_prayer);
        // 打中时被击退（打击感）：等击退结束，记下停住的位置，之后反击全程不应再动
        await WaitFor(() => _prayer.KnockbackVelocity == 0f, 60);
        float pushedX = _prayer.GlobalPosition.X;
        Check(first == HitOutcome.Damaged && _prayer.Stats.Health == 6 && !_prayer.IsDead && _prayer.LastReaction == "counter",
            $"祈祷泥人（泥）：第一刀打不死（剩 {_prayer.Stats.Health}），被惊动后起身反击（固定种子，实际 {_prayer.LastReaction}）");
        bool done = await WaitFor(() => _prayer.State is EnemyIdle, 180);
        var windup = states.FirstOrDefault(s => s.state == "AttackWindup");
        var active = states.FirstOrDefault(s => s.state == "AttackActive");
        long windupFrames = windup.state != null && active.state != null ? (long)(active.frame - windup.frame) - FrozenBetween(windup.frame, active.frame) : -1;
        Check(done && Mathf.Abs(windupFrames - 30) <= 1 && states.Select(s => s.state).SequenceEqual(new[] { "AttackWindup", "AttackActive", "AttackRecover", "Idle" }),
            $"祈祷泥人（泥）：反击是看得出来的前摇 {windupFrames} 帧（0.5 秒）→ 挥击 → 收招 → 回去跪着（{string.Join("→", states.Select(s => s.state))}）");
        Check(_prayer.AttacksStarted == 1 && Player.Stats.Health == hp - 10 && _playerDamaged == 1 && pushedX > x && Mathf.Abs(_prayer.GlobalPosition.X - pushedX) < 0.5f,
            $"祈祷泥人（泥）：泥手挥击打中没格挡的玩家，只结算一次、扣 10（{hp} → {Player.Stats.Health}），被击退停住后反击时原地不动");

        bool quiet = true;
        for (int i = 0; i < 120; i++)
        {
            quiet &= _prayer.State is EnemyIdle;
            await Frames(1);
        }
        Check(quiet && _prayer.AttacksStarted == 1 && _playerDamaged == 1, "祈祷泥人（泥）：反击之后不追、不再主动出手（2 秒内一直跪着）");
        HitOutcome? second = await StrikeHit(_prayer);
        await Frames(5);
        Check(second == HitOutcome.Killed && _prayer.IsDead && _killed.SequenceEqual(new[] { "prayer" }) && _dropped.Count == 0,
            "祈祷泥人（泥）：补一刀就死；固定种子下这次没掉落");
    }

    // ---------- ⑤ 占卜之城：反击可格挡、100% 掉泥瓶 ----------

    private async Task PrayerCityChecks()
    {
        await Fresh(new Vector2(2150, 576));
        // 反应随机数：第一次反击（≥ 0.5）；掉落随机数取一个 30% 下不会掉的值，证明 100% 来自摆放点覆盖
        _prayerCity.SetSeeds(FindSeed(r => r.Randf() >= 0.5f), FindSeed(r => r.Randf() >= 0.3f));
        var active = _prayerCity.Machine.Get<AttackActiveState>();
        HitOutcome? first = await StrikeHit(_prayerCity);
        bool windup = await WaitFor(() => _prayerCity.State is AttackWindupState { Remaining: <= 0.15f } && PlayerFree, 60);
        int hp = Player.Stats.Health;
        RealKeys.Down(RealKeys.Block);
        await Frames(2);
        bool blocking = Player.State is BlockState;
        await WaitFor(() => _prayerCity.State is AttackRecoverState, 60);
        RealKeys.Up(RealKeys.Block);
        Check(first == HitOutcome.Damaged && _prayerCity.LastReaction == "counter" && windup && blocking && active.LastOutcome == HitOutcome.Blocked
              && Player.Stats.Health == hp && _playerDamaged == 0,
            $"祈祷泥人（占卜之城）：被打后起身反击，前摇里真实按 K 格挡住泥手挥击（结果 {active.LastOutcome}），玩家不掉血");
        await WaitFor(() => _prayerCity.State is EnemyIdle, 120);
        for (int i = 0; i < 3 && !_prayerCity.IsDead; i++) await StrikeHit(_prayerCity);
        await Frames(5);
        Check(_prayerCity.IsDead && _killed.SequenceEqual(new[] { "prayer" }) && _dropped.SequenceEqual(new[] { "泥瓶" }) && _prayerCity.DropsOverride != null,
            $"祈祷泥人（占卜之城）：摆放点覆盖掉落表，打死 100% 掉泥瓶（{string.Join(",", _dropped)}）");
    }

    // ---------- ⑥ 塔庙泥卫 ----------

    private async Task GuardPatrolChecks()
    {
        await Fresh(new Vector2(1500, 576));
        float home = _guard.Home.X, distance = _guard.Data.PatrolDistance;
        float minX = home, maxX = home;
        bool patrolled = false;
        for (int i = 0; i < 540; i++)
        {
            minX = Mathf.Min(minX, _guard.GlobalPosition.X);
            maxX = Mathf.Max(maxX, _guard.GlobalPosition.X);
            patrolled |= _guard.State is PatrolState;
            await Frames(1);
        }
        Check(!_guard.CanSeeTarget() && patrolled && minX <= home - distance + 8f && maxX >= home + distance - 8f
              && minX >= home - distance - 8f && maxX <= home + distance + 8f && _guard.AttacksStarted == 0,
            $"塔庙泥卫：看不见玩家时持矛巡逻，在出生点 {home:F0} 左右 {distance:F0} 之内来回走（实际 {minX:F0}–{maxX:F0}），不出手");
    }

    private async Task GuardChargeChecks()
    {
        await Fresh(new Vector2(3000, 576));
        bool windup = await WaitFor(() => _guard.State is AttackWindupState, 400);
        float locked = _guard.LockedDirection;
        Check(windup && locked < 0f && _guard.Facing < 0f, "塔庙泥卫：看见左边的玩家，追到冲锋距离开始前摇，锁定方向朝左");
        // 前摇中玩家换到泥卫身后（右边 150px）
        Player.GlobalPosition = new Vector2(_guard.GlobalPosition.X + 150f, Player.GlobalPosition.Y);
        int hp = Player.Stats.Health;
        await WaitFor(() => _guard.State is AttackActiveState, 60);
        float chargeStart = _guard.GlobalPosition.X;
        bool neverTurned = true;
        while (_guard.State is AttackActiveState)
        {
            neverTurned &= _guard.Facing < 0f && _guard.LockedDirection < 0f;
            await Frames(1);
        }
        Check(neverTurned && _guard.GlobalPosition.X < chargeStart - 100f && Player.Stats.Health == hp,
            $"塔庙泥卫：长矛突刺途中玩家换到身后也不转向，沿锁定方向冲出 {chargeStart - _guard.GlobalPosition.X:F0}px，打空");

        // 下一次冲锋：前摇快结束时真实按 K 格挡
        var active = _guard.Machine.Get<AttackActiveState>();
        bool second = await WaitFor(() => _guard.State is AttackWindupState { Remaining: <= 0.12f } && PlayerFree, 600);
        bool facingPlayer = Mathf.Sign(Player.GlobalPosition.X - _guard.GlobalPosition.X) == Mathf.Sign(_guard.LockedDirection);
        hp = Player.Stats.Health;
        int damaged = _playerDamaged;
        RealKeys.Down(RealKeys.Block);
        await WaitFor(() => _guard.State is AttackRecoverState, 90);
        RealKeys.Up(RealKeys.Block);
        Check(second && facingPlayer && active.LastOutcome == HitOutcome.Blocked && Player.Stats.Health == hp && _playerDamaged == damaged,
            $"塔庙泥卫：重新锁定朝向玩家冲锋，被真实格挡挡住（结果 {active.LastOutcome}），不掉血");

        // 不挡：冲锋判定连续多帧压在玩家身上，只扣一次 20
        bool third = await WaitFor(() => _guard.State is AttackWindupState && PlayerFree, 600);
        hp = Player.Stats.Health;
        damaged = _playerDamaged;
        await WaitFor(() => _guard.State is AttackRecoverState, 120);
        await Frames(5);
        Check(third && active.LastOutcome == HitOutcome.Damaged && Player.Stats.Health == hp - 20 && _playerDamaged == damaged + 1,
            $"塔庙泥卫：没挡住的突刺只结算一次，扣 20（{hp} → {Player.Stats.Health}）");
    }
}
