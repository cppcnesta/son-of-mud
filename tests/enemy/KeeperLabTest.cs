using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy.Bosses.Keeper;
using SonOfMud.Enemy.Bosses.Keeper.States;
using SonOfMud.Player.States;

/// <summary>
/// 计划第 6 节第 5 步：keeper_lab.tscn（S1_05 尺寸）里用真实玩家、真实按键、正式 KeeperData.tres 测守坑人，第 3 节每条规则一条断言。
/// 测试只做两种“搭建”：把玩家放到某个位置（等蝎尾充满的 20 秒里把玩家挪到远处，让守坑人追不上），不改任何冷却、伤害和血量。
/// ① 等待 / 开战：玩家进竞技场才开战；开战时横扫 5/5、蝎尾 0/20；中速 120。
/// ② 横扫：前摇 1 秒站定、冷却施法中；打中扣 40 且蝎尾减 5；收招 0.5 秒（受击不缩短）2 倍易伤；5 秒冷却在收招结束后才开始。
///    被真实格挡也让蝎尾减 5；挥空不减、照样按计时收招。
/// ③ 蝎尾：两个都就绪时蝎尾优先；瞄准 3 秒无敌（受击 Invulnerable）、不动、结束时锁定玩家 X；没挡住扣 60 + 眩晕 3 秒；
///    只跳躲不开；真实格挡：不掉血不眩晕，Boss 僵直 5 秒、2 倍伤害、每受一次伤减 1 秒，僵直结束后蝎尾才重新充能。
/// ④ 变身：血量归零后不再有任何判定，闪白 1 秒内 boss_keeper 还没写；闪白结束写 boss_keeper，BossDefeated 恰好一次。
/// </summary>
public partial class KeeperLabTest : LabSuiteBase
{
    private const string LabScene = "res://assets/scenes/tests/keeper_lab.tscn";
    /// <summary>站在 y=888 的地面上时身体中心（原点）的 y。</summary>
    private static readonly float FloorY = 888f - PlayerBody.HalfH;
    private const float Dt = 1f / 60f;
    private const ulong RoamSeed = 20261120;

    protected override string ReportName => "keeper_lab_results";

    private KeeperBoss _keeper;
    private readonly List<string> _encounters = new();
    private readonly List<string> _defeats = new();
    private int _healthEvents;
    private int _playerDamaged;
    private float _sweepChargedAtStart, _tailChargedAtStart, _tailRemainingAtStart;

    protected override async Task RunChecks()
    {
        Events.Instance.BossEncounterStarted += OnEncounter;
        Events.Instance.BossDefeated += OnDefeated;
        Events.Instance.BossHealthChanged += OnHealth;
        try
        {
            await EncounterChecks();
            await RoamChecks();
            await AttackGapChecks();
            await RoamCoverageChecks();
            await SweepHitAndBlockChecks();
            await SweepWhiffChecks();
            await TailUnblockedChecks();
            await TailJumpChecks();
            await TailBlockAndTransformChecks();
        }
        finally
        {
            RealKeys.ReleaseAll();
            Events.Instance.BossEncounterStarted -= OnEncounter;
            Events.Instance.BossDefeated -= OnDefeated;
            Events.Instance.BossHealthChanged -= OnHealth;
        }
    }

    private void OnEncounter(StringName id, string name, int maxHp)
    {
        _encounters.Add($"{id}|{name}|{maxHp}");
        if (_keeper == null) return;
        _sweepChargedAtStart = _keeper.Sweep.Charged;
        _tailChargedAtStart = _keeper.Tail.Charged;
        _tailRemainingAtStart = _keeper.Tail.Remaining;
    }

    private void OnDefeated(StringName id, Vector2 position) => _defeats.Add(id.ToString());
    private void OnHealth(StringName id, int hp, int max) => _healthEvents++;

    // ---------- 搭建 ----------

    private async Task Fresh(Vector2 playerAt)
    {
        RealKeys.ReleaseAll();
        _keeper = null;
        await LoadLab(LabScene);
        _keeper = Lab.GetNode<KeeperBoss>("Keeper");
        _keeper.SetRoamSeed(_seedOverride != 0 ? _seedOverride : RoamSeed); // 巡游的随机挑法固定种子，每次跑都一样
        _encounters.Clear();
        _defeats.Clear();
        _healthEvents = 0;
        _playerDamaged = 0;
        Player.HurtBox.Damaged += (hit, dealt) => _playerDamaged++;
        await Reset(playerAt);
    }

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

    private void PlacePlayer(float x)
    {
        Player.GlobalPosition = new Vector2(x, FloorY);
        Player.Velocity = Vector2.Zero;
    }

    private float Distance => Mathf.Abs(_keeper.GlobalPosition.X - Player.GlobalPosition.X);

    /// <summary>
    /// 等蝎尾充满：守坑人离玩家不到 500px 时，把玩家放到房间另一头（只是挪位置），它就一直在追、不会出手。
    /// </summary>
    private async Task<bool> KiteUntilTailReady(int maxFrames = 1500)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (_keeper.Tail.IsReady) return true;
            if (Distance < 500f) PlacePlayer(_keeper.GlobalPosition.X > 1152f ? 400f : 2000f);
            await Frames(1);
        }
        return _keeper.Tail.IsReady;
    }

    private async Task TapKey(Key key)
    {
        RealKeys.Down(key);
        await Frames(1);
        RealKeys.Up(key);
        await Frames(1);
    }

    // ---------- ① 等待 / 开战 ----------

    private async Task EncounterChecks()
    {
        await Fresh(new Vector2(200, FloorY));
        await Frames(60);
        Check(_keeper.State is DormantState && _encounters.Count == 0 && !_keeper.FightActive && _keeper.GlobalPosition.X == 1800f,
            "等待：玩家还在竞技场外（x=200），守坑人不动、不开战");
        RealKeys.Down(RealKeys.Right);
        bool started = await WaitFor(() => _encounters.Count > 0, 120);
        RealKeys.Up(RealKeys.Right);
        Check(started && _encounters.SequenceEqual(new[] { "keeper|守坑人：蝎|150" }) && _keeper.State is ApproachState,
            $"开战：玩家走进竞技场 → BossEncounterStarted(keeper, 守坑人：蝎, 150) 一次，进入逼近（玩家 x={Player.GlobalPosition.X:F0}）");
        Check(Mathf.IsEqualApprox(_sweepChargedAtStart, 5f) && _tailChargedAtStart == 0f && Mathf.IsEqualApprox(_tailRemainingAtStart, 20f),
            $"开战：横扫 {_sweepChargedAtStart}/5 就绪，蝎尾 {_tailChargedAtStart}/20 从头充");
        await Frames(2);
        // 巡游：走动时速度是 RoamSpeed（往哪边走看挑法），停下时为 0
        var roam = _keeper.Machine.Get<ApproachState>();
        bool speedOk = true;
        int moving = 0;
        for (int i = 0; i < 60 && _keeper.State is ApproachState; i++)
        {
            await Frames(1);
            if (_keeper.State is not ApproachState) break;
            float vx = Mathf.Abs(_keeper.Velocity.X);
            if (roam.Paused) speedOk &= vx == 0f;
            else if (vx > 0f) { moving++; speedOk &= Mathf.Abs(vx - _keeper.Keeper.RoamSpeed) < 0.5f; }
        }
        Check(speedOk && moving > 10, $"巡游：走动时速度 {_keeper.Keeper.RoamSpeed}，停下时为 0（走了 {moving} 帧）");
        Check(_keeper.Sweep.IsReady && _keeper.Tail.IsRecharging && Mathf.Abs(_keeper.Tail.Remaining - (20f - 64f * Dt)) < 0.1f,
            $"冷却：开战后蝎尾按时间回充（剩 {_keeper.Tail.Remaining:F2}）");
    }

    // ---------- ①b 巡游（计划 v2 测试 A） ----------

    /// <summary>不让它出招（巡游检查时用）：出招间隔一直没走完。</summary>
    private void HoldAttacks() => _keeper.AttackGapRemaining = 999f;

    /// <summary>指定下一次挑法并立刻重挑，等它走到并停下；途中记录往远离玩家的方向走的帧里朝向是否一直朝玩家。</summary>
    private async Task<(bool arrived, bool backingFacesPlayer, int backingFrames)> RoamTo(KeeperBoss.RoamKind kind, int maxFrames = 400)
    {
        var roam = _keeper.Machine.Get<ApproachState>();
        _keeper.ForceNextRoam = kind;
        roam.Pick();
        bool facing = true;
        int backing = 0;
        for (int i = 0; i < maxFrames; i++)
        {
            HoldAttacks();
            await Frames(1);
            if (roam.Paused) return (true, facing, backing);
            float dx = Player.GlobalPosition.X - _keeper.GlobalPosition.X;
            bool away = _keeper.Velocity.X != 0f && Mathf.Sign(_keeper.Velocity.X) == -Mathf.Sign(dx);
            if (away && Mathf.Abs(dx) >= _keeper.Keeper.TurnDeadzone + 10f)
            {
                backing++;
                facing &= _keeper.Facing == Mathf.Sign(dx) && _keeper.Animator.Sprite.FlipH == (_keeper.Facing > 0f);
            }
        }
        return (roam.Paused, facing, backing);
    }

    private async Task RoamChecks()
    {
        await Fresh(new Vector2(1000, FloorY));
        bool started = await WaitFor(() => _keeper.State is ApproachState, 60);
        Player.HurtBox.SetInvulnerable("roam_test", true);
        float min = _keeper.RoamMinX, max = _keeper.RoamMaxX, stop = _keeper.Keeper.StopDistance;
        Check(started && Mathf.IsEqualApprox(min, 660f) && Mathf.IsEqualApprox(max, 1964f),
            $"巡游：可走范围按场地扣半个身宽和两边各 40 像素余量 = 身体中心 {min:F0}～{max:F0}");

        // 贴近：停在玩家同一侧、离玩家 StopDistance
        var (arrived, _, _) = await RoamTo(KeeperBoss.RoamKind.Approach);
        float gap = _keeper.GlobalPosition.X - Player.GlobalPosition.X;
        Check(arrived && Mathf.Abs(gap - stop) <= 6f && _keeper.Facing < 0f,
            $"巡游·贴近：走到玩家右边 {stop} 处停下（实际离玩家 {gap:F0}），面朝玩家");

        // 退开：往远离玩家的方向走，钳子仍朝玩家
        var (retreated, backingFaces, backingFrames) = await RoamTo(KeeperBoss.RoamKind.Retreat);
        gap = _keeper.GlobalPosition.X - Player.GlobalPosition.X;
        Check(retreated && (gap >= _keeper.Keeper.RetreatDistance - 6f || Mathf.Abs(_keeper.GlobalPosition.X - max) <= 6f),
            $"巡游·退开：离玩家至少 {_keeper.Keeper.RetreatDistance}（实际 {gap:F0}）或退到场地边");
        Check(backingFrames > 20 && backingFaces,
            $"巡游·后退：往远离玩家的方向走的 {backingFrames} 帧里一直面朝玩家（图翻转也对），没有背对玩家");

        // 横穿：从玩家身下走到另一边，过了玩家以后转身
        int crossings = _keeper.Crossings;
        var (crossed, _, _) = await RoamTo(KeeperBoss.RoamKind.Cross, 600);
        gap = _keeper.GlobalPosition.X - Player.GlobalPosition.X;
        Check(crossed && gap < 0f && (gap <= -_keeper.Keeper.StopDistance + 6f || Mathf.Abs(_keeper.GlobalPosition.X - min) <= 6f) && _keeper.GlobalPosition.X >= min - 1f && _keeper.Crossings == crossings + 1 && _keeper.Facing > 0f && _keeper.Animator.Sprite.FlipH,
            $"巡游·横穿：从玩家身下走到左边、离玩家 {stop} 到场地边之间、场地不够就停在边上（离玩家 {gap:F0}，最左 {min:F0}），横穿记 1 次，转身面朝右边的玩家（图翻转）");

        // 转身死区：玩家在头顶附近左右小动，朝向不变
        float facing = _keeper.Facing;
        bool steady = true;
        for (int i = 0; i < 40; i++)
        {
            HoldAttacks();
            PlacePlayer(_keeper.GlobalPosition.X + (i % 10 < 5 ? 30f : -30f));
            await Frames(1);
            steady &= _keeper.Facing == facing;
        }
        Check(steady, $"转身死区：玩家在它头顶左右 30 像素来回动（死区 {_keeper.Keeper.TurnDeadzone}），朝向不变");

        // 悬崖按移动方向检测：右边是悬崖、左边有地，面朝左往右走 → 停住；往左走照常
        Vector2 at = _keeper.GlobalPosition;
        _keeper.GlobalPosition = new Vector2(2200f, at.Y);
        _keeper.Facing = -1f;
        bool towardLedge = _keeper.Move(1f, 160f);
        float vxLedge = _keeper.Velocity.X;
        bool awayFromLedge = _keeper.Move(-1f, 160f);
        float vxAway = _keeper.Velocity.X;
        _keeper.Stop();
        _keeper.GlobalPosition = at;
        Check(!towardLedge && vxLedge == 0f && awayFromLedge && vxAway < 0f && _keeper.Facing < 0f,
            "移动：悬崖按移动方向检测（面朝左、往右走到悬崖边停住；往左走照常），Move 不改朝向");
        Player.HurtBox.SetInvulnerable("roam_test", false);
    }

    /// <summary>出招间隔：收招 / 反制僵直刚结束（回到巡游）时调用。两个技能都弄成就绪、玩家放在横扫距离内，量它多久才出下一招。</summary>
    private async Task CheckAttackGap(string after)
    {
        float gapStart = _keeper.AttackGapRemaining;
        _keeper.Sweep.Reduce(99f);
        _keeper.Tail.Reduce(99f);
        PlacePlayer(_keeper.GlobalPosition.X + _keeper.Facing * 300f);
        int frames = await CountFramesWhile(() => _keeper.State is ApproachState, 200);
        int expected = Mathf.RoundToInt(_keeper.Keeper.AttackGap * 60f);
        Check(Mathf.Abs(gapStart - _keeper.Keeper.AttackGap) < 0.05f && Mathf.Abs(frames - expected) <= 3 && _keeper.State is SweepWindupState or TailAimState,
            $"出招间隔（{after}）：结束后重新计满 {_keeper.Keeper.AttackGap} 秒，技能就绪、玩家在范围内也要等满才出招（{frames} 帧后 {_keeper.State?.DisplayName}）");
    }

    private async Task AttackGapChecks()
    {
        // 横扫收招结束
        await Fresh(new Vector2(1600, FloorY));
        Player.HurtBox.SetInvulnerable("gap_test", true);
        bool windup = await WaitFor(() => _keeper.State is SweepWindupState, 120);
        // 出招中锁朝向：前摇里玩家绕到背后，朝向不变
        float locked = _keeper.Facing;
        PlacePlayer(_keeper.GlobalPosition.X - locked * 250f);
        await Frames(10);
        Check(windup && _keeper.State is SweepWindupState && _keeper.Facing == locked,
            "出招中锁朝向：横扫前摇里玩家绕到背后（在死区外），朝向不变");
        await WaitFor(() => _keeper.State is ApproachState, 200);
        await CheckAttackGap("横扫收招");

        // 蝎尾收招结束（没挡住）
        await WaitFor(() => _keeper.State is ApproachState, 400);
        _keeper.Machine.TransitionTo<TailAimState>();
        await WaitFor(() => _keeper.State is ApproachState, 400);
        await CheckAttackGap("蝎尾收招");

        // 反制僵直结束（不经过普通收招）
        await WaitFor(() => _keeper.State is ApproachState, 400);
        _keeper.Machine.TransitionTo<StaggeredState>();
        await Frames(2);
        _keeper.Stagger.End();
        await WaitFor(() => _keeper.State is ApproachState, 10);
        await CheckAttackGap("反制僵直");
        Player.HurtBox.SetInvulnerable("gap_test", false);
    }

    /// <summary>巡游覆盖用的 8 个固定种子（一个种子 20 秒随机性太大，按 8 段一起统计）。</summary>
    private static readonly ulong[] CoverageSeeds = { 20261120, 1, 2, 3, 4, 5, 6, 7 };
    private ulong _seedOverride;

    /// <summary>
    /// 固定种子，玩家站在场地中间不动，每个种子观察 20 秒：走过的范围、三种挑法、不出场地、不出招时死区外朝玩家。
    /// 8 个种子一起统计：每段覆盖 ≥ 60%，平均 ≥ 75%；至少一半的种子两端都到过（离端点 ≤ 150）；三种挑法总数各 ≥ 5。
    /// </summary>
    private async Task RoamCoverageChecks()
    {
        var coverages = new List<float>();
        int bothEnds = 0;
        int[] totals = new int[3];
        bool inside = true, facing = true;
        foreach (ulong seed in CoverageSeeds)
        {
            _seedOverride = seed;
            await Fresh(new Vector2(1312, FloorY));
            _seedOverride = 0;
            Player.HurtBox.SetInvulnerable("coverage_test", true);
            await WaitFor(() => _keeper.State is ApproachState, 60);
            float min = _keeper.RoamMinX, max = _keeper.RoamMaxX;
            float lo = _keeper.GlobalPosition.X, hi = lo;
            int[] before = (int[])_keeper.RoamCounts.Clone();
            for (int i = 0; i < 1200; i++)
            {
                PlacePlayer(1312f);
                await Frames(1);
                float x = _keeper.GlobalPosition.X;
                lo = Mathf.Min(lo, x);
                hi = Mathf.Max(hi, x);
                inside &= x >= min - 1f && x <= max + 1f;
                float dx = Player.GlobalPosition.X - x;
                if (_keeper.State is ApproachState && Mathf.Abs(dx) >= _keeper.Keeper.TurnDeadzone + 10f) facing &= _keeper.Facing == Mathf.Sign(dx);
            }
            for (int k = 0; k < 3; k++) totals[k] += _keeper.RoamCounts[k] - before[k];
            coverages.Add((hi - lo) / (max - min));
            if (lo <= min + 150f && hi >= max - 150f) bothEnds++;
            Player.HurtBox.SetInvulnerable("coverage_test", false);
        }
        float average = coverages.Average();
        Metric("keeper_roam_coverage_avg", average);
        string each = string.Join("、", coverages.Select(c => $"{c:P0}"));
        Check(coverages.All(c => c >= 0.6f) && average >= 0.75f && inside,
            $"巡游覆盖（8 个固定种子各 20 秒）：每段 {each}，都 ≥ 60%、平均 {average:P0}（≥ 75%），没出场地");
        Check(bothEnds * 2 >= CoverageSeeds.Length, $"巡游覆盖：{bothEnds}/{CoverageSeeds.Length} 段两端都到过（离可走范围端点 ≤ 150）");
        Check(totals.All(c => c >= 5), $"巡游覆盖：三种挑法都用到了（贴近 {totals[0]}、退开 {totals[1]}、横穿 {totals[2]}）");
        Check(facing, "巡游覆盖：不出招时，玩家在死区外它始终面朝玩家");
    }

    // ---------- ② 横扫 ----------

    private async Task SweepHitAndBlockChecks()
    {
        await Fresh(new Vector2(1600, FloorY));
        bool windup = await WaitFor(() => _keeper.State is SweepWindupState, 200);
        Check(windup && Distance <= _keeper.Keeper.SweepRange && _keeper.Sweep.IsCasting && _encounters.Count == 1,
            $"横扫：玩家进入横扫距离（{Distance:F0} ≤ {_keeper.Keeper.SweepRange}）且横扫就绪 → 前摇，横扫冷却 BeginCast（施法中）");
        float x = _keeper.GlobalPosition.X;
        float prevTail = _keeper.Tail.Remaining;
        int hp = Player.Stats.Health;
        // 守坑人放大后发动距离变远，放好玩家的那几帧里它可能已经开始前摇了：把已经过去的帧数也算上
        int windupFrames = Mathf.RoundToInt((_keeper.Keeper.SweepWindup - _keeper.Machine.Get<SweepWindupState>().Remaining) * 60f);
        bool castingAll = true;
        while (_keeper.State is SweepWindupState && windupFrames < 120)
        {
            prevTail = _keeper.Tail.Remaining;
            await Frames(1);
            windupFrames++;
            castingAll &= _keeper.Sweep.IsCasting;
        }
        var strike = _keeper.Machine.Get<SweepStrikeState>();
        Check(Mathf.Abs(windupFrames - 60) <= 1 && Mathf.Abs(_keeper.GlobalPosition.X - x) < 0.5f && _keeper.State is SweepStrikeState,
            $"横扫：前摇站着不动 1 秒（{windupFrames} 帧）");
        await Frames(1);
        float tailDrop = prevTail - _keeper.Tail.Remaining;
        Check(strike.LastOutcome == HitOutcome.Damaged && Player.Stats.Health == hp - 40 && _playerDamaged == 1,
            $"横扫：没挡住扣 40（{hp} → {Player.Stats.Health}，结果 {strike.LastOutcome}）");
        Check(Mathf.Abs(tailDrop - 5f) < 0.06f && _keeper.TailReductions == 1, $"横扫：打中 → 蝎尾剩余 CD 减 5（这一帧少了 {tailDrop:F3} 秒）");

        // 收招：0.5 秒、2 倍易伤、受击不缩短
        bool recover = await WaitFor(() => _keeper.State is SweepRecoverState, 30);
        Check(recover && _keeper.Stats.IncomingMultiplier == 2f && castingAll && _keeper.Sweep.IsCasting, "横扫收招：易伤 ×2，前摇到收招全程横扫冷却都是施法中（不回充）");
        int bossHp = _keeper.Stats.Health;
        RealKeys.Down(RealKeys.Attack);
        int recoverFrames = 0;
        ulong recoverStart = Engine.GetPhysicsFrames();
        bool released = false;
        while (_keeper.State is SweepRecoverState && recoverFrames < 120)
        {
            await Frames(1);
            recoverFrames++;
            if (!released) { RealKeys.Up(RealKeys.Attack); released = true; }
        }
        int waterHit = Mathf.RoundToInt(GD.Load<HitSpec>("res://assets/data/player_v2/water_attack.tres").Damage);
        Check(bossHp - _keeper.Stats.Health == waterHit * 2, $"横扫收招：期间玩家水形态一刀打出 {bossHp - _keeper.Stats.Health}（{waterHit} × 2）");
        // 期间那一刀会顿帧（游戏时间不走）：扣掉顿帧的帧
        int recoverFrozen = FrozenBetween(recoverStart, Engine.GetPhysicsFrames());
        Check(Mathf.Abs(recoverFrames - recoverFrozen - 30) <= 1, $"横扫收招：固定 0.5 秒，受击不缩短（{recoverFrames} 帧，其中顿帧 {recoverFrozen} 帧，Q4）");
        Check(_keeper.State is ApproachState && _keeper.Sweep.IsRecharging && _keeper.Sweep.Remaining > 4.95f,
            $"横扫：5 秒 CD 在收招结束后才开始算（收招结束时剩 {_keeper.Sweep.Remaining:F2}）");
        // 第二次横扫要真的砍到玩家：等冷却时让它一直挑「贴近」（横穿到玩家身下时横扫打不到身后，见计划）
        int cdFrames = await CountFramesWhile(() =>
        {
            _keeper.ForceNextRoam = KeeperBoss.RoamKind.Approach;
            return _keeper.State is not SweepWindupState && _keeper.State is not TailAimState;
        }, 600);
        _keeper.ForceNextRoam = null;
        Check(_keeper.State is SweepWindupState && cdFrames >= 298 && cdFrames <= 302, $"横扫：冷却 5 秒后才再次发动（{cdFrames} 帧）");

        // 第二次横扫：真实按 K 格挡
        bool late = await WaitFor(() => _keeper.State is SweepWindupState { Remaining: <= 0.10f }, 90);
        hp = Player.Stats.Health;
        int damaged = _playerDamaged;
        prevTail = _keeper.Tail.Remaining;
        RealKeys.Down(RealKeys.Block);
        await WaitFor(() => _keeper.State is SweepStrikeState, 30);
        await Frames(1);
        tailDrop = prevTail - _keeper.Tail.Remaining;
        bool blockState = Player.State is BlockState || strike.LastOutcome == HitOutcome.Blocked;
        await Frames(20);
        RealKeys.Up(RealKeys.Block);
        Check(late && blockState && strike.LastOutcome == HitOutcome.Blocked && Player.Stats.Health == hp && _playerDamaged == damaged,
            $"横扫：真实格挡住（结果 {strike.LastOutcome}），玩家不掉血");
        Check(tailDrop > 5f && tailDrop < 5.2f && _keeper.TailReductions == 2, $"横扫：被格挡也算命中，蝎尾剩余 CD 同样减 5（少了 {tailDrop:F3} 秒）");
    }

    private async Task SweepWhiffChecks()
    {
        await Fresh(new Vector2(1600, FloorY));
        bool windup = await WaitFor(() => _keeper.State is SweepWindupState, 200);
        RealKeys.Down(RealKeys.Left);
        float prevTail = _keeper.Tail.Remaining;
        while (_keeper.State is SweepWindupState)
        {
            prevTail = _keeper.Tail.Remaining;
            await Frames(1);
        }
        RealKeys.Up(RealKeys.Left);
        var strike = _keeper.Machine.Get<SweepStrikeState>();
        int strikeFrames = await CountFramesWhile(() => _keeper.State is SweepStrikeState, 60);
        float tailDrop = prevTail - _keeper.Tail.Remaining;
        int recoverFrames = await CountFramesWhile(() => _keeper.State is SweepRecoverState, 60);
        Check(windup && strike.LastOutcome == null && Player.Stats.Health == Player.Stats.MaxHealth && _keeper.TailReductions == 0 && tailDrop < 0.5f,
            $"横扫：玩家在前摇里跑开（距离 {Distance:F0}），挥空——没有结算回调，蝎尾不减（{strikeFrames + 1} 帧里少了 {tailDrop:F3} 秒）");
        Check(Mathf.Abs(strikeFrames - 12) <= 1 && Mathf.Abs(recoverFrames - 30) <= 1 && _keeper.Sweep.IsRecharging,
            $"横扫：挥空也按计时收招（判定 {strikeFrames} 帧、收招 {recoverFrames} 帧），冷却照常开始");
    }

    // ---------- ③ 蝎尾 ----------

    private async Task TailUnblockedChecks()
    {
        await Fresh(new Vector2(1600, FloorY));
        bool ready = await KiteUntilTailReady();
        await Frames(1);
        Check(ready && _keeper.Sweep.IsReady && _keeper.State is ApproachState && _encounters.Count == 1,
            $"蝎尾：开战约 20 秒后充满（期间玩家一直在 500px 外，横扫没发动、仍是就绪）（ready {ready} sweep {_keeper.Sweep.IsReady} state {_keeper.State?.DisplayName} enc {_encounters.Count} tail {_keeper.Tail.Remaining:F2}）");
        PlacePlayer(_keeper.GlobalPosition.X - 100f);
        bool bothReadyInRange = _keeper.Sweep.IsReady && _keeper.Tail.IsReady && Distance <= _keeper.Keeper.SweepRange;
        await Frames(1);
        ulong aimStart = Engine.GetPhysicsFrames();
        Check(bothReadyInRange && _keeper.State is TailAimState, $"蝎尾优先（Q3）：两个技能都就绪、玩家在横扫距离内，守坑人选择蝎尾瞄准（{_keeper.State?.DisplayName}）");

        // 瞄准：无敌、不动、冷却施法中
        float x = _keeper.GlobalPosition.X;
        int bossHp = _keeper.Stats.Health;
        HitOutcome? outcome = null;
        void OnLanded(HurtBox t, Hit h, int o) { if (t.Entity == _keeper) outcome = (HitOutcome)o; }
        Player.AttackHitBox.HitLanded += OnLanded;
        await TapKey(RealKeys.Attack);
        await WaitFor(() => outcome != null, 20);
        Player.AttackHitBox.HitLanded -= OnLanded;
        Check(outcome == HitOutcome.Invulnerable && _keeper.Stats.Health == bossHp && _keeper.Tail.IsCasting,
            $"蝎尾瞄准：期间受击返回 Invulnerable，不掉血（结果 {outcome}），蝎尾冷却 BeginCast");
        // 玩家在瞄准期间换个位置：往左走 20 帧再停下
        await Frames(10);
        RealKeys.Down(RealKeys.Left);
        await Frames(20);
        RealKeys.Up(RealKeys.Left);
        await CountFramesWhile(() => _keeper.State is TailAimState, 240);
        int aimFrames = (int)(Engine.GetPhysicsFrames() - aimStart);
        float playerX = Player.GlobalPosition.X;
        Check(_keeper.State is TailStrikeState && Mathf.Abs(_keeper.GlobalPosition.X - x) < 0.5f && aimFrames >= 178 && aimFrames <= 184,
            $"蝎尾瞄准：持续 3 秒（约 {aimFrames} 帧），期间不能移动");
        Check(Mathf.Abs(_keeper.LockedTailX - playerX) < 1f && _keeper.TailLocked, $"蝎尾瞄准：结束时锁定玩家当时的 X（{_keeper.LockedTailX:F0} / 玩家 {playerX:F0}）");

        // 没挡住：扣 60、眩晕 3 秒
        int hp = Player.Stats.Health;
        var tail = _keeper.Machine.Get<TailStrikeState>();
        int delayFrames = await CountFramesWhile(() => _keeper.State is TailStrikeState { Striking: false }, 30);
        await Frames(1);
        bool stunned = Player.State is StunnedState;
        float stun = (Player.State as StunnedState)?.Remaining ?? 0f;
        Check(Mathf.Abs(delayFrames - 6) <= 1 && tail.LastOutcome == HitOutcome.Damaged && Player.Stats.Health == hp - 60,
            $"蝎尾：0.1 秒过渡（{delayFrames} 帧）后判定，没挡住扣 60（{hp} → {Player.Stats.Health}）");
        Check(stunned && stun > 2.9f && stun <= 3f, $"蝎尾：没挡住眩晕 3 秒（剩 {stun:F2}）");
        int recoverFrames = await CountFramesWhile(() => _keeper.State is TailStrikeState or TailRecoverState, 120);
        Check(_keeper.State is not StaggeredState && _keeper.Tail.IsRecharging && _keeper.Tail.Remaining > 19.9f && recoverFrames >= 34 && recoverFrames <= 38,
            $"蝎尾：没挡住不进僵直，收招 0.5 秒后冷却从 0/20 重新充（剩 {_keeper.Tail.Remaining:F2}）");
    }

    private async Task TailJumpChecks()
    {
        await Fresh(new Vector2(1600, FloorY));
        bool ready = await KiteUntilTailReady();
        PlacePlayer(_keeper.GlobalPosition.X - 300f);
        bool aim = await WaitFor(() => _keeper.State is TailAimState, 10);
        bool late = await WaitFor(() => _keeper.State is TailAimState { Remaining: <= 0.05f }, 240);
        float floorY = Player.GlobalPosition.Y;
        float heightAtHit = 0f;
        bool airborne = false;
        void OnHurt(Hit hit, int dealt)
        {
            heightAtHit = floorY - Player.GlobalPosition.Y;
            airborne = !Player.IsOnFloor();
        }
        Player.HurtBox.Damaged += OnHurt;
        int hp = Player.Stats.Health;
        RealKeys.Down(RealKeys.Jump);
        await WaitFor(() => _keeper.State is not TailStrikeState and not TailAimState, 30);
        RealKeys.Up(RealKeys.Jump);
        Player.HurtBox.Damaged -= OnHurt;
        Check(ready && aim && late && airborne && heightAtHit > 10f && Player.Stats.Health == hp - 60,
            $"蝎尾：只跳躲不开——判定时玩家离地 {heightAtHit:F0}px，仍然扣 60（竖带覆盖整个房间高度）");
    }

    private async Task TailBlockAndTransformChecks()
    {
        await Fresh(new Vector2(1600, FloorY));
        await KiteUntilTailReady();
        PlacePlayer(_keeper.GlobalPosition.X - 110f);
        bool aim = await WaitFor(() => _keeper.State is TailAimState, 10);
        bool late = await WaitFor(() => _keeper.State is TailAimState { Remaining: <= 0.05f }, 240);
        int hp = Player.Stats.Health, damaged = _playerDamaged;
        RealKeys.Down(RealKeys.Block);
        bool staggered = await WaitFor(() => _keeper.State is StaggeredState, 30);
        var tail = _keeper.Machine.Get<TailStrikeState>();
        bool playerStunned = Player.State is StunnedState;
        await Frames(1);
        RealKeys.Up(RealKeys.Block);
        Check(aim && late && staggered && tail.LastOutcome == HitOutcome.Blocked && Player.Stats.Health == hp && _playerDamaged == damaged && !playerStunned,
            $"蝎尾：真实按 K 格挡住（结果 {tail.LastOutcome}）→ 玩家不掉血、不眩晕");
        Check(_keeper.Stagger.Active && _keeper.Stagger.Remaining > 4.9f && _keeper.Stats.IncomingMultiplier == 2f && _keeper.Tail.IsCasting && _keeper.Tail.Charged == 0f,
            $"反制僵直：Boss 僵直 5 秒（剩 {_keeper.Stagger.Remaining:F2}），易伤 ×2，蝎尾冷却还是施法中（不回充）");

        // 僵直中连续出刀：每刀 20，每刀让剩余时间多减 1 秒
        var hits = new List<(int dealt, float drop, bool ended, float before)>();
        float lastRemaining = _keeper.Stagger.Remaining;
        void OnBossHp(int oldHp, int newHp) => hits.Add((oldHp - newHp, lastRemaining - _keeper.Stagger.Remaining, !_keeper.Stagger.Active, lastRemaining));
        _keeper.Stats.HealthChanged += OnBossHp;
        bool attackDown = false;
        int frames = 0;
        while (_keeper.State is StaggeredState && frames < 400)
        {
            lastRemaining = _keeper.Stagger.Remaining;
            if (attackDown) { RealKeys.Up(RealKeys.Attack); attackDown = false; }
            else if (Player.State is IdleState or RunState && Player.Timers.AttackReady) { RealKeys.Down(RealKeys.Attack); attackDown = true; }
            await Frames(1);
            frames++;
        }
        RealKeys.Up(RealKeys.Attack);
        _keeper.Stats.HealthChanged -= OnBossHp;
        await Frames(1);
        int waterDamage = Mathf.RoundToInt(GD.Load<HitSpec>("res://assets/data/player_v2/water_attack.tres").Damage);
        Check(hits.Count >= 2 && hits.All(h => h.dealt == waterDamage * 2), $"反制僵直：期间每刀 {string.Join("/", hits.Select(h => h.dealt))}（水 {waterDamage} × 2）");
        bool dropsOk = hits.Count >= 2 && hits.All(h => h.ended ? h.before <= 1.03f : h.drop > 0.99f && h.drop < 1.03f);
        Check(dropsOk, $"反制僵直：每受一次伤剩余时间减 1 秒（每刀那一帧减少 {string.Join("/", hits.Select(h => h.drop.ToString("F3") + (h.ended ? "（减到 0 结束）" : "")))}）");
        Check(frames < 300 && _keeper.State is ApproachState or SweepWindupState && _keeper.Tail.IsRecharging && _keeper.Tail.Remaining > 19.9f && _keeper.Stats.IncomingMultiplier == 1f,
            $"反制僵直：被打缩短到 {frames} 帧结束；结束后蝎尾才开始重新充能（剩 {_keeper.Tail.Remaining:F2}/20），易伤解除");

        // ④ 打到血量归零 → 变身
        var bot = new KeeperBot(Player, _keeper, n => Frames(n));
        bool zero = await bot.FightUntil(() => _keeper.State is TransformState || Player.Stats.IsDepleted, 3600);
        bool inTransform = _keeper.State is TransformState;
        Check(zero && inTransform && _keeper.Stats.Health == 0 && !Player.Stats.IsDepleted,
            $"变身：真实按键打到血量归零（玩家剩 {Player.Stats.Health}，出刀 {bot.Attacks}、格挡 {bot.BlockPresses}），立刻进入变身");
        Check(!_keeper.SweepHitBox.Active && !_keeper.TailHitBox.Active && !_keeper.HurtBox.Receiving && !_keeper.FightActive && !_keeper.Stagger.Active
              && _keeper.Stats.IncomingMultiplier == 1f && !GameSession.Progress.Flag("boss_keeper") && _defeats.Count == 0,
            "变身 ①：所有判定和受击都关了，计时和易伤清掉；这时还没写 boss_keeper、没发 BossDefeated");
        int playerHp = Player.Stats.Health, healthEvents = _healthEvents;
        int flashFrames = 0;
        ulong flashStart = Engine.GetPhysicsFrames();
        float flashAtStart = _keeper.FlashRemaining;
        bool flagEarly = false, defeatedEarly = false;
        HitOutcome? during = null;
        void OnLanded(HurtBox t, Hit h, int o) { if (t.Entity == _keeper) during = (HitOutcome)o; }
        Player.AttackHitBox.HitLanded += OnLanded;
        while (_keeper.State is TransformState && flashFrames < 120)
        {
            if (flashFrames == 10) RealKeys.Down(RealKeys.Attack);
            if (flashFrames == 11) RealKeys.Up(RealKeys.Attack);
            flagEarly |= GameSession.Progress.Flag("boss_keeper");
            defeatedEarly |= _defeats.Count > 0;
            await Frames(1);
            flashFrames++;
        }
        Player.AttackHitBox.HitLanded -= OnLanded;
        flashFrames -= FrozenBetween(flashStart, Engine.GetPhysicsFrames()); // 打死那一下的顿帧期间游戏时间不走
        Check(!flagEarly && !defeatedEarly && flashAtStart > 0.9f && flashAtStart <= 1f && Mathf.Abs(flashFrames - flashAtStart * 60f) <= 1.01f,
            $"变身：闪白 1 秒（进入变身后已过 {(1f - flashAtStart) * 60f:F0} 帧，再等 {flashFrames} 帧结束）期间 boss_keeper 一直没写、BossDefeated 没发");
        Check(during == null || during == HitOutcome.Ignored, $"变身：闪白期间再打它不结算伤害（结果 {during?.ToString() ?? "没有"}）");
        Check(_keeper.State is GoneState && GameSession.Progress.Flag("boss_keeper") && _defeats.SequenceEqual(new[] { "keeper" }) && !_keeper.Visible,
            "变身 ②：闪白结束写 boss_keeper = true，发 BossDefeated(keeper) → 已击败（身体隐藏）");
        await Frames(2);
        Node2D remains = _keeper.Remains;
        var body = remains?.GetNodeOrNull<Sprite2D>("Body");
        bool lying = body != null && Mathf.IsEqualApprox(Mathf.Abs(body.Rotation), Mathf.Pi * 0.5f);
        // 躺下后身体的底边贴地：转 90 度后的高 = 贴图宽 × 1/4，往上抬一半
        float bottom = body == null ? 99f : body.Position.Y + body.Texture.GetWidth() * body.Scale.X * 0.5f;
        Check(remains != null && remains.IsInsideTree() && remains.Visible && remains.GlobalPosition.IsEqualApprox(_keeper.GlobalPosition) && lying && Mathf.Abs(bottom) < 0.01f
              && body.Texture is AtlasTexture crop && crop.Atlas.ResourcePath.Contains("spr_enemy_devout_idle"),
            $"变身 ③：原地留下躺着的虔诚泥人（借虔诚泥人待机图第 1 帧，转 90 度，身体底边贴地 {bottom:F2}）");
        _keeper.ShowRemains();
        await Frames(1);
        Check(_keeper.Remains == remains && remains.GetParent().GetChildren().Count(n => n.Name.ToString().StartsWith("KeeperRemains")) == 1,
            "变身 ③：躺着的虔诚泥人只出现一次");
        await Frames(180);
        Check(_defeats.Count == 1 && Player.Stats.Health == playerHp && _healthEvents == healthEvents && _keeper.State is GoneState,
            "变身之后：BossDefeated 恰好一次，再也没有判定打到玩家，血量也不再变化");
    }
}
