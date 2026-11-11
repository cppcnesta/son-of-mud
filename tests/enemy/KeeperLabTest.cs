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
    private const float FloorY = 864f;
    private const float Dt = 1f / 60f;

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
        float x0 = _keeper.GlobalPosition.X;
        await Frames(60);
        float speed = x0 - _keeper.GlobalPosition.X;
        Check(Mathf.Abs(speed - 120f) <= 3f && _keeper.State is ApproachState, $"逼近：以中速走向玩家（实测 {speed:F1}px/s，Q7：120）");
        Check(_keeper.Sweep.IsReady && _keeper.Tail.IsRecharging && Mathf.Abs(_keeper.Tail.Remaining - (20f - 64f * Dt)) < 0.1f,
            $"冷却：开战后蝎尾按时间回充（剩 {_keeper.Tail.Remaining:F2}）");
    }

    // ---------- ② 横扫 ----------

    private async Task SweepHitAndBlockChecks()
    {
        await Fresh(new Vector2(1600, FloorY));
        bool windup = await WaitFor(() => _keeper.State is SweepWindupState, 200);
        Check(windup && Distance <= 128f && _keeper.Sweep.IsCasting && _encounters.Count == 1,
            $"横扫：玩家进入 2 地块（{Distance:F0} ≤ 128）且横扫就绪 → 前摇，横扫冷却 BeginCast（施法中）");
        float x = _keeper.GlobalPosition.X;
        float prevTail = _keeper.Tail.Remaining;
        int hp = Player.Stats.Health;
        int windupFrames = 0;
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
        bool released = false;
        while (_keeper.State is SweepRecoverState && recoverFrames < 120)
        {
            await Frames(1);
            recoverFrames++;
            if (!released) { RealKeys.Up(RealKeys.Attack); released = true; }
        }
        Check(bossHp - _keeper.Stats.Health == 20, $"横扫收招：期间玩家水形态一刀打出 {bossHp - _keeper.Stats.Health}（10 × 2）");
        Check(Mathf.Abs(recoverFrames - 30) <= 1, $"横扫收招：固定 0.5 秒，受击不缩短（{recoverFrames} 帧，Q4）");
        Check(_keeper.State is ApproachState && _keeper.Sweep.IsRecharging && _keeper.Sweep.Remaining > 4.95f,
            $"横扫：5 秒 CD 在收招结束后才开始算（收招结束时剩 {_keeper.Sweep.Remaining:F2}）");
        int cdFrames = await CountFramesWhile(() => _keeper.State is not SweepWindupState && _keeper.State is not TailAimState, 600);
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
        Check(windup && strike.LastOutcome == null && Player.Stats.Health == 140 && _keeper.TailReductions == 0 && tailDrop < 0.5f,
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
            "蝎尾：开战约 20 秒后充满（期间玩家一直在 500px 外，横扫没发动、仍是就绪）");
        PlacePlayer(_keeper.GlobalPosition.X - 100f);
        bool bothReadyInRange = _keeper.Sweep.IsReady && _keeper.Tail.IsReady && Distance <= 128f;
        await Frames(1);
        ulong aimStart = Engine.GetPhysicsFrames();
        Check(bothReadyInRange && _keeper.State is TailAimState, $"蝎尾优先（Q3）：两个技能都就绪、玩家在 128px 内，守坑人选择蝎尾瞄准（{_keeper.State?.DisplayName}）");

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
        Check(hits.Count >= 2 && hits.All(h => h.dealt == 20), $"反制僵直：期间每刀 {string.Join("/", hits.Select(h => h.dealt))}（水 10 × 2）");
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
        Check(!flagEarly && !defeatedEarly && flashAtStart > 0.9f && flashAtStart <= 1f && Mathf.Abs(flashFrames - flashAtStart * 60f) <= 1.01f,
            $"变身：闪白 1 秒（进入变身后已过 {(1f - flashAtStart) * 60f:F0} 帧，再等 {flashFrames} 帧结束）期间 boss_keeper 一直没写、BossDefeated 没发");
        Check(during == null || during == HitOutcome.Ignored, $"变身：闪白期间再打它不结算伤害（结果 {during?.ToString() ?? "没有"}）");
        Check(_keeper.State is GoneState && GameSession.Progress.Flag("boss_keeper") && _defeats.SequenceEqual(new[] { "keeper" }) && !_keeper.Visible,
            "变身 ②：闪白结束写 boss_keeper = true，发 BossDefeated(keeper) → 已击败（身体隐藏）");
        await Frames(180);
        Check(_defeats.Count == 1 && Player.Stats.Health == playerHp && _healthEvents == healthEvents && _keeper.State is GoneState,
            "变身之后：BossDefeated 恰好一次，再也没有判定打到玩家，血量也不再变化");
    }
}
