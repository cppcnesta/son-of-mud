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
/// 计划第 6 节第 2–4 步：enemy_lab.tscn 里用真实玩家、真实按键（RealKeys）、正式 .tres 配置测普通敌人。测试里不改任何伤害和血量。
/// ① 工人：会追、会前摇（站定）、会出手；没挡住扣 20 且同一次攻击只结算一次；能被真实格挡；挥空也按计时收招；
///    死亡时 EnemyKilled 恰好一次，死后再打是 Ignored。
/// ② 朝圣者（E2）：不带增伤的基础攻击——水一刀剩 20、出现反应、再补两刀才死；泥一刀剩 6、出现反应、再补一刀死；
///    固定种子让两种反应（逃跑 / 举手）各出现一次，掉落也用固定种子；朝圣者从不出手。
/// ③ 跪拜泥人：一直不动，一刀死，100% 掉小泥偶。
/// ④ 守卫：前摇时锁定方向，冲锋中途玩家换到身后也不转向；可以被格挡；没挡住只扣一次 20。
/// </summary>
public partial class EnemyLabTest : LabSuiteBase
{
    private const string LabScene = "res://assets/scenes/tests/enemy_lab.tscn";

    protected override string ReportName => "enemy_lab_results";

    private EnemyBody _worker, _pilgrim, _kneeler, _guard;
    private readonly List<string> _killed = new();
    private readonly List<string> _dropped = new();
    private int _playerDamaged;

    protected override async Task RunChecks()
    {
        Events.Instance.EnemyKilled += OnKilled;
        Events.Instance.ItemDropped += OnDropped;
        try
        {
            await WorkerChecks();
            await PilgrimWaterChecks();
            await PilgrimMudChecks();
            await KneelerChecks();
            await GuardChecks();
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
        _worker = Lab.GetNode<EnemyBody>("Worker");
        _pilgrim = Lab.GetNode<EnemyBody>("Pilgrim");
        _kneeler = Lab.GetNode<EnemyBody>("Kneeler");
        _guard = Lab.GetNode<EnemyBody>("Guard");
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

    // ---------- ① 工人 ----------

    private async Task WorkerChecks()
    {
        await Fresh(new Vector2(600, 576));
        // 反应随机数：前 6 次都不逃跑（逃跑概率 25%），这样工人每次受伤都进受击硬直；掉落随机数：第一次就掉
        _worker.SetSeeds(FindSeed(r => Enumerable.Range(0, 6).All(_ => r.Randf() >= 0.25f)), FindSeed(r => r.Randf() < 0.3f));
        float startX = _worker.GlobalPosition.X;
        bool windup = await WaitFor(() => _worker.State is AttackWindupState, 300);
        float dx = Mathf.Abs(_worker.GlobalPosition.X - Player.GlobalPosition.X);
        Check(windup && _worker.GlobalPosition.X < startX - 50f && dx <= 90f, $"工人：看见玩家就追过来（{startX:F0} → {_worker.GlobalPosition.X:F0}），进入攻击距离（{dx:F0} ≤ 90）开始前摇");

        float windupX = _worker.GlobalPosition.X;
        int hp = Player.Stats.Health, damagedBefore = _playerDamaged, attacks = _worker.AttacksStarted;
        int windupFrames = await CountFramesWhile(() => _worker.State is AttackWindupState, 120);
        Check(Mathf.Abs(_worker.GlobalPosition.X - windupX) < 0.5f && Mathf.Abs(windupFrames - 30) <= 1 && _worker.State is AttackActiveState,
            $"工人：前摇站定 0.5 秒（{windupFrames} 帧）后出手");
        var active = _worker.Machine.Get<AttackActiveState>();
        int activeFrames = await CountFramesWhile(() => _worker.State is AttackActiveState, 120);
        Check(_worker.AttacksStarted == attacks + 1 && active.LastOutcome == HitOutcome.Damaged && Mathf.Abs(activeFrames - 9) <= 1,
            $"工人：出手打中站着不动的玩家（结果 {active.LastOutcome}），判定 0.15 秒（{activeFrames} 帧）按计时收招");
        Check(Player.Stats.Health == hp - 20 && _playerDamaged == damagedBefore + 1,
            $"工人：同一次攻击只结算一次——判定开了 {activeFrames} 帧，玩家只扣一次 20（{hp} → {Player.Stats.Health}）");

        // 真实格挡：下一次前摇快结束时按住 K（水格挡 0.5 秒）
        bool second = await WaitFor(() => _worker.State is AttackWindupState { Remaining: <= 0.12f } && PlayerFree, 600);
        hp = Player.Stats.Health;
        damagedBefore = _playerDamaged;
        RealKeys.Down(RealKeys.Block);
        await Frames(2);
        bool blocking = Player.State is BlockState;
        await WaitFor(() => _worker.State is AttackRecoverState, 60);
        RealKeys.Up(RealKeys.Block);
        Check(second && blocking && active.LastOutcome == HitOutcome.Blocked && Player.Stats.Health == hp && _playerDamaged == damagedBefore,
            $"工人：真实按 K 格挡住出手（结果 {active.LastOutcome}），玩家不掉血");

        // 挥空：前摇一开始就按住 A 往后跑，出手时玩家已经不在判定里
        bool third = await WaitFor(() => _worker.State is AttackWindupState && PlayerFree, 600);
        hp = Player.Stats.Health;
        attacks = _worker.AttacksStarted;
        RealKeys.Down(RealKeys.Left);
        await WaitFor(() => _worker.State is AttackActiveState, 60);
        RealKeys.Up(RealKeys.Left);
        int whiffActive = await CountFramesWhile(() => _worker.State is AttackActiveState, 120);
        bool recovering = _worker.State is AttackRecoverState;
        int recoverFrames = await CountFramesWhile(() => _worker.State is AttackRecoverState, 200);
        Check(third && _worker.AttacksStarted == attacks + 1 && active.LastOutcome == null && Player.Stats.Health == hp && Mathf.Abs(whiffActive - 9) <= 1
              && recovering && Mathf.Abs(recoverFrames - 36) <= 1,
            $"工人：挥空（没有任何结算回调）也按计时收招——判定 {whiffActive} 帧、收招 {recoverFrames} 帧，之后回到 {_worker.State?.DisplayName}");

        // 打死：水形态基础攻击 4 刀
        var hps = new List<int>();
        var outcomes = new List<HitOutcome?>();
        for (int i = 0; i < 4 && !_worker.IsDead; i++)
        {
            outcomes.Add(await StrikeHit(_worker));
            hps.Add(_worker.Stats.Health);
        }
        Check(hps.SequenceEqual(new[] { 30, 20, 10, 0 }) && outcomes.Last() == HitOutcome.Killed,
            $"工人：水形态每刀只扣 10（同一刀只结算一次），4 刀打死（血量 {string.Join("→", hps)}，最后一刀 {outcomes.LastOrDefault()}）");
        HitOutcome? after = await StrikeHit(_worker);
        await Frames(30);
        Check(_worker.IsDead && _worker.State is EnemyDead && _killed.Count(k => k == "worker") == 1 && _worker.KilledEmits == 1 && after == HitOutcome.Ignored,
            $"工人：死亡时 EnemyKilled 恰好一次（{_killed.Count}），死后再打结果是 {after}，不再发");
        Check(_dropped.SequenceEqual(new[] { "石头" }) && !_worker.AttackHitBox.Active, "工人：固定掉落种子 → 掉一块石头；死后判定关闭");
    }

    // ---------- ② 朝圣者 ----------

    private async Task PilgrimWaterChecks()
    {
        await Fresh(new Vector2(1500, 576));
        Check(Player.Forms.Form == FormController.Water && !Player.Timers.Bonus.Ready, "朝圣者（水）：玩家是水形态，没有泥增伤（只用基础攻击）");
        _pilgrim.SetSeeds(FindSeed(r => r.Randf() < 0.5f), FindSeed(r => r.Randf() < 0.3f));
        HitOutcome? first = await StrikeHit(_pilgrim);
        await Frames(3);
        Check(first == HitOutcome.Damaged && _pilgrim.Stats.Health == 20 && !_pilgrim.IsDead, $"朝圣者（水）：第一刀打不死，剩 {_pilgrim.Stats.Health} 血");
        Check(_pilgrim.LastReaction == "flee" && _pilgrim.State is FleeState, $"朝圣者（水）：受击后出现反应——逃跑（固定种子，实际 {_pilgrim.LastReaction}）");
        float fleeStart = _pilgrim.GlobalPosition.X;
        await Frames(20);
        Check(_pilgrim.GlobalPosition.X > fleeStart + 40f, $"朝圣者（水）：背对玩家跑开（{fleeStart:F0} → {_pilgrim.GlobalPosition.X:F0}）");
        HitOutcome? second = await StrikeHit(_pilgrim);
        Check(second == HitOutcome.Damaged && _pilgrim.Stats.Health == 10 && !_pilgrim.IsDead, $"朝圣者（水）：补第二刀还活着（剩 {_pilgrim.Stats.Health}）");
        HitOutcome? third = await StrikeHit(_pilgrim);
        await Frames(5);
        Check(third == HitOutcome.Killed && _pilgrim.IsDead && _killed.SequenceEqual(new[] { "pilgrim" }), "朝圣者（水）：补第三刀才死，EnemyKilled 一次");
        Check(_dropped.SequenceEqual(new[] { "小泥偶" }), $"朝圣者：固定掉落种子 → 掉一个小泥偶（{string.Join(",", _dropped)}）");
        Check(_pilgrim.AttacksStarted == 0 && _playerDamaged == 0 && Player.Stats.Health == 140 && _pilgrim.GetNodeOrNull("HitBox") == null,
            "朝圣者：从不出手（没有攻击判定，全程玩家没掉血）");
    }

    private async Task PilgrimMudChecks()
    {
        await Fresh(new Vector2(1500, 576));
        await Frames(2);
        RealKeys.Down(RealKeys.SwitchForm);
        await Frames(1);
        RealKeys.Up(RealKeys.SwitchForm);
        await Frames(3);
        Check(Player.Forms.Form == FormController.Mud && !Player.Timers.Bonus.Ready, "朝圣者（泥）：真实按 Q 切到泥形态，没有泥增伤");
        _pilgrim.SetSeeds(FindSeed(r => r.Randf() >= 0.5f), FindSeed(r => r.Randf() >= 0.3f));
        HitOutcome? first = await StrikeHit(_pilgrim);
        await Frames(3);
        Check(first == HitOutcome.Damaged && _pilgrim.Stats.Health == 6 && !_pilgrim.IsDead, $"朝圣者（泥）：第一刀打不死，剩 {_pilgrim.Stats.Health} 血");
        float x = _pilgrim.GlobalPosition.X;
        await Frames(30);
        Check(_pilgrim.LastReaction == "cower" && _pilgrim.State is CowerState && Mathf.Abs(_pilgrim.GlobalPosition.X - x) < 0.5f,
            $"朝圣者（泥）：受击后出现反应——举起双手、原地不动（固定种子，实际 {_pilgrim.LastReaction}）");
        HitOutcome? second = await StrikeHit(_pilgrim);
        await Frames(5);
        Check(second == HitOutcome.Killed && _pilgrim.IsDead && _killed.SequenceEqual(new[] { "pilgrim" }) && _dropped.Count == 0,
            "朝圣者（泥）：补一刀就死；固定种子下这次没掉落");
        Check(_pilgrim.AttacksStarted == 0 && _playerDamaged == 0, "朝圣者（泥）：从不出手");
    }

    // ---------- ③ 跪拜泥人 ----------

    private async Task KneelerChecks()
    {
        await Fresh(new Vector2(2150, 576));
        Vector2 at = _kneeler.GlobalPosition;
        RealKeys.Down(RealKeys.Right);
        await Frames(15);
        RealKeys.Up(RealKeys.Right);
        await Frames(120);
        Check(_kneeler.GlobalPosition.DistanceTo(at) < 0.5f && _kneeler.State is EnemyIdle && _kneeler.AttacksStarted == 0 && _playerDamaged == 0,
            $"跪拜泥人：玩家走到身边 2 秒，一动不动、不出手（玩家距离 {Mathf.Abs(_kneeler.GlobalPosition.X - Player.GlobalPosition.X):F0}）");
        HitOutcome? hit = await StrikeHit(_kneeler);
        await Frames(20);
        Check(hit == HitOutcome.Killed && _kneeler.IsDead && _kneeler.GlobalPosition.DistanceTo(at) < 0.5f && _kneeler.LastReaction == "",
            "跪拜泥人：被打也没有反应（不逃不举手、不移动），血 10 一刀死");
        Check(_killed.SequenceEqual(new[] { "kneeler" }) && _dropped.SequenceEqual(new[] { "小泥偶" }), "跪拜泥人：EnemyKilled 一次，100% 掉小泥偶（Q9）");
    }

    // ---------- ④ 守卫 ----------

    private async Task GuardChecks()
    {
        await Fresh(new Vector2(3000, 576));
        bool windup = await WaitFor(() => _guard.State is AttackWindupState, 400);
        float locked = _guard.LockedDirection;
        Check(windup && locked < 0f && _guard.Facing < 0f, "守卫：看见左边的玩家，追到冲锋距离开始前摇，锁定方向朝左");
        // 前摇中玩家换到守卫身后（右边 150px）
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
            $"守卫：冲锋中途玩家换到身后也不转向，沿锁定方向冲出 {chargeStart - _guard.GlobalPosition.X:F0}px，打空");

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
            $"守卫：重新锁定朝向玩家冲锋，被真实格挡挡住（结果 {active.LastOutcome}），不掉血");

        // 不挡：冲锋判定连续多帧压在玩家身上，只扣一次 20
        bool third = await WaitFor(() => _guard.State is AttackWindupState && PlayerFree, 600);
        hp = Player.Stats.Health;
        damaged = _playerDamaged;
        await WaitFor(() => _guard.State is AttackRecoverState, 120);
        await Frames(5);
        Check(third && active.LastOutcome == HitOutcome.Damaged && Player.Stats.Health == hp - 20 && _playerDamaged == damaged + 1,
            $"守卫：没挡住的冲锋只结算一次，扣 20（{hp} → {Player.Stats.Health}）");
    }
}
