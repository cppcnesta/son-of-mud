using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.Tools;

/// <summary>
/// 战斗验收测试（combat_lab.tscn）：计划 v2.1 第 3、4、5、6、7 步，以及第 8 步的 edge_attack_test。
/// World 不在本分支，换房用直接发 Events.RoomExiting 模拟。结果写到 tests/output/combat_results.json。
/// </summary>
public partial class CombatSuite : LabSuiteBase
{
    private const float FloorY = 576f;
    private static readonly Vector2 Near = new(470, 552);
    private static readonly Vector2 Far = new(2400, 552);

    protected override string ReportName => "combat_results";

    private TrainingDummy _a;
    private TrainingDummy _b;
    private readonly List<HitOutcome> _playerOutcomes = new();
    private readonly List<(int current, int max)> _health = new();
    private int _died;
    private int _shakes;
    private int _hitStops;
    private HitSpec _plain;
    private HitSpec _tail;

    protected override async Task RunChecks()
    {
        Events.Instance.PlayerHealthChanged += (c, m) => _health.Add((c, m));
        Events.Instance.PlayerDied += () => _died++;
        Events.Instance.CameraShakeRequested += (_, _) => _shakes++;
        Events.Instance.HitStopRequested += _ => _hitStops++;
        _plain = GD.Load<HitSpec>("res://assets/data/combat/lab/plain_strike.tres");
        _tail = GD.Load<HitSpec>("res://assets/data/combat/lab/tail_strike.tres");

        await LoadLab("res://assets/scenes/tests/combat_lab.tscn");
        _a = Lab.GetNode<TrainingDummy>("DummyA");
        _b = Lab.GetNode<TrainingDummy>("DummyB");
        Player.AttackHitBox.HitLanded += (_, _, o) => _playerOutcomes.Add((HitOutcome)o);
        Player.SlamHitBox.HitLanded += (_, _, o) => _playerOutcomes.Add((HitOutcome)o);

        StaticChecks();
        await DamageAndDedupChecks();
        await MissAndRecoveryChecks();
        await DownAttackAndSlamChecks();
        await BlockChecks();
        await BonusAndMultiplierChecks();
        await HurtStunDeathChecks();
        await StaggerChecks();
        await EdgeAttackChecks();
        await ProjectileChecks();
    }

    private async Task Arrange(Vector2 a, Vector2 b, int form = 0)
    {
        foreach (var (dummy, at) in new[] { (_a, a), (_b, b) })
        {
            dummy.GlobalPosition = at;
            dummy.Stats.Refill();
            dummy.Stagger?.End();
            foreach (HurtBox hb in dummy.HurtBoxes) hb.Guard = GuardMode.None;
        }
        _playerOutcomes.Clear();
        await Reset(new Vector2(400, FloorY), form);
    }

    private async Task WaitWhile(Func<bool> condition, int maxFrames)
    {
        for (int i = 0; i < maxFrames && condition(); i++) await Frames(1);
    }

    // ---------- 规则本身 ----------

    private void StaticChecks()
    {
        string[] names = Enum.GetNames(typeof(HitOutcome));
        Check(string.Join(",", names) == "Ignored,Duplicate,Invulnerable,Blocked,Damaged,Killed", $"HitOutcome 只有六种（{string.Join(",", names)}）");
        Check(DamageMath.Final(24f, 1.6f, 2f) == 48, "伤害公式：增伤 1.6 和易伤 ×2 同时生效时取最大值 ×2，24 → 48（不是 76）");
        Check(DamageMath.Final(24f, 1.6f) == 38 && DamageMath.Final(10f, 1.6f) == 16 && DamageMath.Final(24f, 1.8f) == 43, "伤害公式：只取整一次，floor(24×1.6)=38、floor(10×1.6)=16、floor(24×1.8)=43");
        Check(DamageMath.Final(10f) == 10 && DamageMath.Final(10f, 1f, 1f) == 10, "伤害公式：没有倍率时按 1 倍");

        var bonus = new BlockBonus();
        bonus.Grant(1.6f);
        bool kept = true;
        foreach (HitOutcome o in new[] { HitOutcome.Ignored, HitOutcome.Duplicate, HitOutcome.Invulnerable, HitOutcome.Blocked })
            kept &= !bonus.OnOutcome(o) && bonus.Ready;
        Check(kept, "泥增伤：Ignored / Duplicate / Invulnerable / Blocked 都不消耗");
        bool killedConsumes = bonus.OnOutcome(HitOutcome.Killed) && !bonus.Ready;
        bonus.Grant(1.6f);
        Check(killedConsumes && bonus.OnOutcome(HitOutcome.Damaged) && !bonus.Ready, "泥增伤：Damaged 和 Killed 都会消耗");

        var ledger = new AttackLedger();
        bool ok = ledger.Accept("a", 1) && !ledger.Accept("a", 1) && ledger.Accept("a", 2) && !ledger.Accept("", 1);
        for (int i = 0; i < 100; i++) ledger.Accept("x" + i, 1);
        Check(ok && ledger.Count == AttackLedger.Capacity, $"去重记录：按 (AttackId, 实体) 判断，只保留最近 {AttackLedger.Capacity} 条");

        string source = FileAccess.GetFileAsString("res://assets/scripts/combat/HitBox.cs");
        Check(source.Contains("CollideWithAreas = true") && source.Contains("CollideWithBodies = false") && source.Contains("CollisionMask = CollisionMask"),
            "HitBox 查询参数：CollideWithAreas=true、CollideWithBodies=false、按碰撞层过滤");

        bool clean = true;
        foreach (string dir in new[] { "res://assets/scripts/combat", "res://assets/scripts/player", "res://assets/scripts/player/states" })
            foreach (string file in DirAccess.GetFilesAt(dir))
            {
                if (!file.EndsWith(".cs") || file.StartsWith("Mud")) continue;
                string text = FileAccess.GetFileAsString($"{dir}/{file}");
                clean &= !text.Contains("SonOfMud.World") && !text.Contains("SonOfMud.UI") && !text.Contains("MudPlayer") &&
                         !text.Contains("MudProgress") && !text.Contains("MudFormConfig") && !text.Contains("MudAttackLedger");
                if (dir.EndsWith("combat")) clean &= !text.Contains("SonOfMud.Player") && !text.Contains("SonOfMud.Tools");
            }
        Check(clean, "依赖方向：新 Combat 只依赖 Core；新 Player 不引用 World / UI / 旧 Mud* 类");
    }

    // ---------- 第 3 步：伤害数值与去重 ----------

    private async Task<int> SwingAt(TrainingDummy dummy, int form)
    {
        int before = dummy.Stats.Health;
        await Tap(PlayerActions.Attack);
        await WaitWhile(() => Player.State is AttackState, 30);
        return before - dummy.Stats.Health;
    }

    private async Task DamageAndDedupChecks()
    {
        await Arrange(Near, Far, 0);
        int hitStops = _hitStops;
        int dealt = await SwingAt(_a, 0);
        Check(_hitStops == hitStops + 1, "命中造成伤害时发出一次 Events.HitStopRequested");
        Check(dealt == 10 && _a.DamageEvents == 1, $"水形态打假人扣 10，一次挥击跨 6 个有效帧只扣一次（实际 {dealt}，扣血 {_a.DamageEvents} 次）");
        Check(_playerOutcomes.Count == 1 && _playerOutcomes[0] == HitOutcome.Damaged, $"命中回调只有一次，结果 Damaged（{string.Join(",", _playerOutcomes)}）");

        await Arrange(Near, Far, 1);
        int events = _a.DamageEvents;
        dealt = await SwingAt(_a, 1);
        Check(dealt == 24 && _a.DamageEvents == events + 1, $"泥形态打假人扣 24，跨 8 个有效帧只扣一次（实际 {dealt}）");

        await Arrange(Far, Near, 0);
        events = _b.DamageEvents;
        dealt = await SwingAt(_b, 0);
        Check(_b.HurtBoxes.Count == 2 && dealt == 10 && _b.DamageEvents == events + 1 && _playerOutcomes.Count == 1,
            $"同一实体有两个 HurtBox（头、躯干）都在攻击范围内：只扣一次 10（实际 {dealt}，回调 {_playerOutcomes.Count} 次）");

        Hit hit = MakeHit("dedup_two_boxes", 10f, Player.GlobalPosition);
        HitOutcome first = _b.HurtBoxes[0].Receive(hit);
        HitOutcome second = _b.HurtBoxes[1].Receive(hit);
        HitOutcome again = _b.HurtBoxes[0].Receive(hit);
        Check(first == HitOutcome.Damaged && second == HitOutcome.Duplicate && again == HitOutcome.Duplicate,
            $"跨 HurtBox 去重：同一 AttackId 打同一实体的第二个 HurtBox 返回 Duplicate（{first},{second},{again}）");
        Check(_a.HurtBoxes[0].Receive(hit) == HitOutcome.Damaged, "去重按实体：同一 AttackId 打另一个实体照常结算");

        _a.HurtBoxes[0].SetInvulnerable("test", true);
        HitOutcome invulnerable = _a.HurtBoxes[0].Receive(MakeHit("invulnerable_test", 10f, Player.GlobalPosition));
        _a.HurtBoxes[0].SetInvulnerable("test", false);
        Check(invulnerable == HitOutcome.Invulnerable && _a.HurtBoxes[0].Receive(MakeHit("invulnerable_test_2", 10f, Player.GlobalPosition)) == HitOutcome.Damaged,
            "无敌：按来源开启时返回 Invulnerable，关闭后照常结算");

        var wrongLayer = new HurtBox { Name = "PlayerLayerBox", CollisionLayer = Layers.PlayerHurt, CollisionMask = 0 };
        wrongLayer.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(48, 96) } });
        var holder = new Node2D { Name = "Holder", GlobalPosition = Near };
        holder.AddChild(new Stats { Name = "Stats", MaxHealth = 100 });
        holder.AddChild(wrongLayer);
        Lab.AddChild(holder);
        await Arrange(Far, Far, 0);
        await Frames(2);
        await SwingAt(_a, 0);
        Check(_playerOutcomes.Count == 0 && wrongLayer.Stats.Health == 100, "碰撞层过滤：玩家攻击（mask=EnemyHurt）不会打到 PlayerHurt 层的受击框");
        holder.QueueFree();
    }

    // ---------- 第 4 步：攻击自己收招、泥后摇 ----------

    private async Task MissAndRecoveryChecks()
    {
        await Arrange(Far, Far, 1);
        await Tap(PlayerActions.Attack);
        Check(Player.State is AttackState && Player.Timers.AttackInterval > 1.6f, "泥攻击开始：进入前摇并开始攻击间隔");
        int frames = 1;
        while (Player.State is AttackState && frames < 60) { frames++; await Frames(1); }
        ulong recoveryStart = Engine.GetPhysicsFrames();
        Check(_playerOutcomes.Count == 0 && Player.State is AttackRecoveryState && Mathf.Abs(frames * Dt - 0.26f) <= 2 * Dt,
            $"泥攻击挥空：没有任何命中回调，仍按自己的计时器在约 0.26 秒后收招进后摇（{frames} 帧）");
        Check(Mathf.Abs(Player.Timers.Recovery.Multiplier() - 0.2f) < 0.03f && Player.Timers.Recovery.RecoverySource == MovementRecovery.Source.Attack,
            $"泥后摇起步是 20% 移速（实际 {Player.Timers.Recovery.Multiplier():F3}）");
        float x = Player.GlobalPosition.X;
        Press(PlayerActions.Right);
        await Frames(15);
        Release(PlayerActions.Right);
        float moved = Player.GlobalPosition.X - x;
        Check(Player.State is AttackRecoveryState && moved > 8f && moved < 70f, $"泥后摇 1.5 秒内可以移动，但被减速（15 帧移动 {moved:F1}px）");
        await Tap(PlayerActions.Attack);
        Check(Player.State is AttackRecoveryState, "泥后摇中再按攻击无效");
        await WaitWhile(() => Player.State is AttackRecoveryState, 120);
        float recovery = (Engine.GetPhysicsFrames() - recoveryStart) * Dt;
        Check(Player.State is IdleState && Mathf.Abs(recovery - 1.5f) <= 2 * Dt, $"泥后摇按时结束回到 Idle（实测 {recovery:F3} 秒）");

        await Arrange(Far, Far, 0);
        await Tap(PlayerActions.Attack);
        frames = 1;
        while (Player.State is AttackState && frames < 60) { frames++; await Frames(1); }
        Check(_playerOutcomes.Count == 0 && Player.State is IdleState && Mathf.Abs(frames * Dt - 0.34f) <= 2 * Dt,
            $"水攻击挥空：前摇 0.08 + 判定 0.10 + 后摇 0.16 后按时回到 Idle（{frames} 帧）");
    }

    private async Task DownAttackAndSlamChecks()
    {
        await Arrange(Far, Far, 1);
        _a.GlobalPosition = new Vector2(470, 552);
        Player.ResetForLab(new Vector2(470, 410), 1);
        await Frames(3);
        int before = _a.Stats.Health;
        Press(PlayerActions.Down);
        await Tap(PlayerActions.Attack);
        Release(PlayerActions.Down);
        Check(Player.State is AttackState { Down: true }, "没有下砸能力时空中下+攻击是下劈");
        await WaitWhile(() => Player.State is AttackState, 40);
        Check(before - _a.Stats.Health == 43, $"泥形态下落中的下劈强化 ×1.8：floor(24×1.8)=43（实际 {before - _a.Stats.Health}）");

        GameSession.Progress.Unlock(PlayerAbilities.HeavySlam);
        await Arrange(Near, Far, 1);
        Player.ResetForLab(new Vector2(400, 300), 1);
        await Frames(3);
        before = _a.Stats.Health;
        int shakes = _shakes;
        Press(PlayerActions.Down);
        await Tap(PlayerActions.Attack);
        Release(PlayerActions.Down);
        await WaitWhile(() => Player.State is SlamState, 60);
        Check(before - _a.Stats.Health == 44 && _shakes == shakes + 1 && _playerOutcomes.Contains(HitOutcome.Damaged),
            $"下砸落地：圆形重击打到半径 150 内的假人，扣 44（实际 {before - _a.Stats.Health}）");
    }

    // ---------- 第 5 步：格挡 ----------

    private async Task HoldBlock()
    {
        Press(PlayerActions.Block);
        await Frames(2);
    }

    private async Task BlockChecks()
    {
        await Arrange(Near, Far, 0);
        Player.Timers.AttackInterval = 1f;
        await HoldBlock();
        Check(Player.State is BlockState && Mathf.Abs(Player.Timers.BlockCooldown - 3f) < 0.001f, "水格挡：按下进入格挡，并立即开始 3 秒冷却");
        _a.Strike(_plain);
        Check(_a.StrikeOutcomes[^1] == HitOutcome.Blocked && Player.Stats.Health == 70, "水格挡挡住假人一刀：结果 Blocked，不掉血");
        Check(Player.State is not BlockState && Player.Timers.BlockCooldown == 0f && Player.Timers.AttackInterval == 0f,
            "水格挡成功：结束格挡，清零格挡冷却和攻击间隔");
        await Frames(3);
        _a.Strike(_plain);
        Check(_a.StrikeOutcomes[^1] == HitOutcome.Damaged && Player.Stats.Health == 50 && Player.State is HurtState,
            "水格挡只挡一次：按住不续挡，下一刀照常受伤");
        Release(PlayerActions.Block);

        await Arrange(Near, Far, 1);
        await HoldBlock();
        _a.Strike(_plain);
        await Frames(2);
        _a.Strike(_plain);
        Check(_a.StrikeOutcomes[^1] == HitOutcome.Blocked && _a.StrikeOutcomes[^2] == HitOutcome.Blocked && Player.State is BlockState && Player.Stats.Health == 70,
            "泥格挡：按住可以连续挡多段");
        Check(Player.Timers.Bonus.Ready && Mathf.Abs(Player.Timers.Bonus.Multiplier - 1.6f) < 0.001f && Player.Timers.BlockCooldown > 2.8f,
            "泥格挡成功：增伤 1.6 就绪，不清格挡冷却");
        Release(PlayerActions.Block);
        await Frames(2);
        Check(Player.State is IdleState, "泥格挡松开即结束");

        await Arrange(Near, Far, 1);
        await HoldBlock();
        await Frames(122);
        Check(Player.State is not BlockState, "泥格挡按住最长 2 秒");
        Release(PlayerActions.Block);
    }

    // ---------- 第 5 步：泥增伤与倍率 ----------

    private async Task BonusAndMultiplierChecks()
    {
        await Arrange(Near, Far, 1);
        Player.Timers.Bonus.Grant(1.6f);
        int dealt = await SwingAt(_a, 1);
        Check(dealt == 38 && !Player.Timers.Bonus.Ready, $"泥增伤：下一刀 floor(24×1.6)=38，命中后消耗（实际 {dealt}）");

        await Arrange(Far, Far, 1);
        Player.Timers.Bonus.Grant(1.6f);
        await SwingAt(_a, 1);
        Check(Player.Timers.Bonus.Ready, "泥增伤：挥空不消耗");

        await Arrange(Near, Far, 1);
        Player.Timers.Bonus.Grant(1.6f);
        _a.HurtBoxes[0].Guard = GuardMode.Hold;
        await SwingAt(_a, 1);
        _a.HurtBoxes[0].Guard = GuardMode.None;
        Check(_playerOutcomes.Contains(HitOutcome.Blocked) && Player.Timers.Bonus.Ready, "泥增伤：被格挡不消耗");

        await Arrange(Near, Far, 1);
        Player.Timers.Bonus.Grant(1.6f);
        _a.Stats.SetHealth(10);
        await SwingAt(_a, 1);
        Check(_playerOutcomes.Contains(HitOutcome.Killed) && _a.Stats.Health == 0 && !Player.Timers.Bonus.Ready, "泥增伤：用增伤打死目标（Killed）也算消耗");

        await Arrange(Near, Far, 1);
        Player.Timers.Bonus.Grant(1.6f);
        _a.Stagger.Begin();
        dealt = await SwingAt(_a, 1);
        Check(dealt == 48, $"增伤 1.6 和易伤 ×2 同时生效：取 ×2，泥形态一刀 48（实际 {dealt}）");
        _a.Stagger.End();
    }

    // ---------- 第 6 步：受伤、眩晕、死亡、复活 ----------

    private async Task HurtStunDeathChecks()
    {
        await Arrange(Near, Far, 0);
        int events = _health.Count;
        _a.Strike(_plain);
        Check(Player.State is HurtState && Player.Stats.Health == 50 && _health.Count == events + 1 && _health[^1] == (50, 70),
            "受伤：扣血、进入 Hurt，并发出 PlayerHealthChanged(50, 70)");
        Check(Player.Velocity.X < -100f, $"受伤：从攻击来源方向被击退（vx={Player.Velocity.X:F0}）");
        await Frames(FramesFor(0.18f) + 1);
        Check(Player.State is IdleState or RunState, "受伤硬直 0.18 秒后回到自由状态");

        Hit same = MakeHit("same_attack", 10f, _a.GlobalPosition);
        HitOutcome o1 = Player.HurtBox.Receive(same);
        HitOutcome o2 = Player.HurtBox.Receive(same);
        Check(o1 == HitOutcome.Damaged && o2 == HitOutcome.Duplicate && Player.Stats.Health == 40, "没有受伤无敌帧：同一次攻击靠去重只扣一次");
        HitOutcome o3 = Player.HurtBox.Receive(MakeHit("other_attack", 10f, _a.GlobalPosition));
        Check(o3 == HitOutcome.Damaged && Player.Stats.Health == 30, "没有受伤无敌帧：受伤中挨另一次攻击照常扣血");

        await Arrange(Near, Far, 0);
        _a.Strike(_tail);
        Check(Player.State is StunnedState && Player.Stats.Health == 67, "眩晕：没挡住 StunSeconds=3 的攻击 → Stunned，扣 3");
        float x = Player.GlobalPosition.X;
        Press(PlayerActions.Right);
        await Tap(PlayerActions.Attack);
        await Tap(PlayerActions.Jump);
        await Tap(PlayerActions.Dodge);
        await Frames(FramesFor(2.5f));
        bool stillStunned = Player.State is StunnedState;
        Check(stillStunned && Mathf.Abs(Player.GlobalPosition.X - x) < 1f, "眩晕 3 秒内无法操作（移动 / 攻击 / 跳 / 闪避都无效）");
        _a.Strike(_plain);
        Check(Player.State is StunnedState && Player.Stats.Health == 47, "眩晕中还能继续挨打，且不会被普通受伤打断（连环惩罚）");
        Release(PlayerActions.Right);
        await WaitWhile(() => Player.State is StunnedState, 60);
        Check(Player.State is IdleState or RunState, "眩晕 3 秒后恢复");

        await Arrange(Near, Far, 0);
        events = _health.Count;
        int died = _died;
        int strikes = 0;
        for (int i = 0; i < 10 && Player.State is not DeadState; i++)
        {
            Player.HurtBox.Receive(MakeHit($"death_{i}", 20f, _a.GlobalPosition));
            strikes++;
            await Frames(1);
        }
        Check(strikes == 4 && Player.State is DeadState && Player.Stats.Health == 0, $"70 血被 20 伤害打 4 次打空 → Dead（{strikes} 次）");
        Check(_health.Count == events + 4 && _health[^1] == (0, 70), "每次掉血都发 PlayerHealthChanged（4 次，最后是 0/70）");
        Check(_died == died + 1, "血量归零发出一次 PlayerDied");
        Check(Player.HurtBox.Receive(MakeHit("after_death", 20f, _a.GlobalPosition)) == HitOutcome.Ignored, "死亡后不再受击（Ignored）");
        await Tap(PlayerActions.Jump);
        Check(Player.State is DeadState, "死亡中按键无效，只等 CheckpointRespawned");
        Events.Instance.EmitSignal(Events.SignalName.CheckpointRespawned);
        Check(Player.State is IdleState && Player.Stats.Health == 70 && _health[^1] == (70, 70) && Player.HurtBox.Receiving,
            "CheckpointRespawned：回满血 70（没吃生命之面包）、回 Idle、恢复受击");
    }

    // ---------- 第 7 步：反制僵直 ----------

    private async Task StaggerChecks()
    {
        await Arrange(Near, Far, 1);
        await HoldBlock();
        _a.Strike(_tail);
        Release(PlayerActions.Block);
        Check(_a.StrikeOutcomes[^1] == HitOutcome.Blocked && _a.Stagger.Active && Mathf.Abs(_a.Stagger.Remaining - 5f) < 0.001f && _a.Stats.IncomingMultiplier == 2f,
            "挡住带 CounterOnBlock 的攻击 → 假人反制僵直 5 秒，易伤 ×2");
        Check(Player.State is BlockState && Player.Stats.Health == 70, "挡住蝎尾：玩家不掉血、不眩晕");
        await WaitWhile(() => Player.State is BlockState, 10);
        float before = _a.Stagger.Remaining;
        ulong startFrame = Engine.GetPhysicsFrames();
        int dealt = await SwingAt(_a, 1);
        float ticked = (Engine.GetPhysicsFrames() - startFrame) * Dt;
        float reduced = before - _a.Stagger.Remaining - ticked;
        Check(dealt == 48, $"僵直中泥形态一刀：增伤 1.6 与易伤 ×2 取大 → 48（实际 {dealt}）");
        Check(Mathf.Abs(reduced - 1f) < 0.02f, $"每受一次伤剩余时间额外减 1 秒（扣除自然流逝 {ticked:F3} 秒后减少 {reduced:F3} 秒）");

        int hits = 1;
        while (_a.Stagger.Active && hits < 10)
        {
            _a.HurtBoxes[0].Receive(MakeHit($"stagger_{hits}", 10f, Player.GlobalPosition));
            hits++;
        }
        Check(!_a.Stagger.Active && hits == 5 && _a.Stats.IncomingMultiplier == 1f, $"僵直 5 秒被 5 次受伤减完后提前结束，易伤恢复 ×1（{hits} 次）");
        int hp = _a.Stats.Health;
        _a.HurtBoxes[0].Receive(MakeHit("after_stagger", 10f, Player.GlobalPosition));
        Check(hp - _a.Stats.Health == 10, "僵直结束后伤害恢复正常");

        _a.Stagger.Begin();
        await Frames(FramesFor(4.9f));
        bool midway = _a.Stagger.Active;
        await Frames(FramesFor(0.2f));
        Check(midway && !_a.Stagger.Active, "没受伤时僵直按 5 秒自然结束");
    }

    // ---------- 第 8 步：edge_attack_test（模拟 World 发 RoomExiting） ----------

    private async Task EdgeAttackChecks()
    {
        // 有效帧正好跨过房间边缘：判定开着时收到 RoomExiting。
        await Arrange(Far, Far, 1);
        Player.Timers.DodgeCooldown = 1.5f;
        Player.Timers.BlockCooldown = 2f;
        Player.Timers.Bonus.Grant(1.6f);
        await Tap(PlayerActions.Attack);
        await WaitWhile(() => !Player.AttackHitBox.Active, 20);
        var attack = Player.State as AttackState;
        bool wasActive = Player.AttackHitBox.Active && attack is { Current: AttackState.Phase.Active };
        float dodgeBefore = Player.Timers.DodgeCooldown;
        float intervalBefore = Player.Timers.AttackInterval;
        Player.Buffer.Press(PlayerActions.Jump);
        Events.Instance.EmitSignal(Events.SignalName.RoomExiting, "edge_old_room");
        Check(wasActive && !Player.AttackHitBox.Active && Player.State is AttackState { JudgementClosed: true },
            "edge_attack_test：有效帧中收到 RoomExiting → HitBox 立即关闭，这一刀标记为判定已结束");
        Check(Player.State == attack && Player.Timers.DodgeCooldown == dodgeBefore && Player.Timers.AttackInterval == intervalBefore &&
              Player.Timers.BlockCooldown > 1.5f && Player.Timers.Bonus.Ready && Player.Buffer.Has(PlayerActions.Jump, 1f),
            "edge_attack_test：攻击状态本身、冷却、攻击间隔、增伤、输入缓冲都保留");

        // 新房间：玩家位置变了，新房间的假人正好在攻击范围里。
        Player.GlobalPosition = new Vector2(1300, FloorY);
        _b.GlobalPosition = new Vector2(1370, 552);
        int hpB = _b.Stats.Health;
        int hpA = _a.Stats.Health;
        int frames = 0;
        bool reopened = false;
        while (Player.State is AttackState && frames < 30)
        {
            await Frames(1);
            frames++;
            reopened |= Player.AttackHitBox.Active;
        }
        Check(!reopened && _b.Stats.Health == hpB && _a.Stats.Health == hpA && _playerOutcomes.Count == 0,
            "edge_attack_test：剩余有效帧不再打开 HitBox，新房间里的假人不掉血");
        Check(Player.State is AttackRecoveryState && Player.Timers.Bonus.Ready, "edge_attack_test：攻击照常按自己的计时器收招进后摇（增伤没被消耗）");

        // 前摇中就换房：后面整段有效帧都不打开。
        await Arrange(Far, Near, 0);
        await Frames(1);
        Player.ResetForLab(new Vector2(1300, FloorY), 0);
        _b.GlobalPosition = new Vector2(1370, 552);
        await Frames(3);
        await Tap(PlayerActions.Attack);
        bool windup = Player.State is AttackState { Current: AttackState.Phase.Windup };
        Events.Instance.EmitSignal(Events.SignalName.RoomExiting, "edge_old_room");
        hpB = _b.Stats.Health;
        await WaitWhile(() => Player.State is AttackState, 30);
        Check(windup && _b.Stats.Health == hpB && _playerOutcomes.Count == 0, "edge_attack_test：前摇中收到 RoomExiting，这一刀的有效帧都不再打开");

        // 走边缘换房保留移动状态和速度。
        await Arrange(Far, Far, 0);
        Press(PlayerActions.Right);
        await Frames(5);
        Vector2 v = Player.Velocity;
        Events.Instance.EmitSignal(Events.SignalName.RoomExiting, "edge_old_room");
        Check(Player.State is RunState && Player.Velocity == v && v.X > 300f, "RoomExiting：奔跑状态和速度保留（无缝衔接）");
        Release(PlayerActions.Right);
        await Tap(PlayerActions.Jump);
        await WaitWhile(() => Player.State is not RiseState, 10);
        v = Player.Velocity;
        Events.Instance.EmitSignal(Events.SignalName.RoomExiting, "edge_old_room");
        Check(Player.State is RiseState && Player.Velocity == v && v.Y < 0f, "RoomExiting：空中上升状态和速度保留");

        // 下砸资格保留：下砸途中换房，落地照常砸。
        await Arrange(Near, Far, 1);
        Player.ResetForLab(new Vector2(400, 300), 1);
        await Frames(3);
        int shakes = _shakes;
        Press(PlayerActions.Down);
        await Tap(PlayerActions.Attack);
        Release(PlayerActions.Down);
        bool slamming = Player.State is SlamState;
        Events.Instance.EmitSignal(Events.SignalName.RoomExiting, "edge_old_room");
        hpA = _a.Stats.Health;
        await WaitWhile(() => Player.State is SlamState, 60);
        Check(slamming && _shakes == shakes + 1 && hpA - _a.Stats.Health == 44, $"RoomExiting：下砸资格保留，落地照常结算（扣 44）（下砸中 {slamming}，震动 {_shakes - shakes}，扣 {hpA - _a.Stats.Health}）");
    }

    // ---------- 通用投掷物（Combat.Projectile） ----------

    private sealed class ProjectileRun
    {
        public ProjectileEnd End;
        public HitOutcome? Outcome;
        public int Resolutions;
        public HurtBox Target;
        public Vector2 EndPosition;
        public bool Freed;
    }

    /// <summary>等投掷物结束（结束那一帧末尾就释放，所以结果在 Finished 信号里记下）。</summary>
    private async Task<ProjectileRun> Fly(Projectile p, int maxFrames = 240)
    {
        var run = new ProjectileRun();
        if (p == null) return run;
        if (p.Ended)
        {
            run.End = p.EndReason;
            run.Outcome = p.Outcome;
            run.Resolutions = p.Resolutions;
            run.Target = p.Target;
            run.EndPosition = p.GlobalPosition;
        }
        else
        {
            p.Finished += (reason, position) =>
            {
                run.End = (ProjectileEnd)reason;
                run.Outcome = p.Outcome;
                run.Resolutions = p.Resolutions;
                run.Target = p.Target;
                run.EndPosition = position;
            };
        }
        for (int i = 0; i < maxFrames && GodotObject.IsInstanceValid(p) && !p.Ended; i++) await Frames(1);
        await Frames(2);
        run.Freed = !GodotObject.IsInstanceValid(p);
        return run;
    }

    private int _projectileSeq;

    private Projectile Throw(Vector2 from, Vector2 velocity, ProjectileTeam team, float damage = 7f, float gravity = 0f, float lifetime = 4f) =>
        Projectile.Launch(Lab, from, velocity, new HitSpec { Damage = damage }, $"lab_projectile_{++_projectileSeq}", null, team,
            new ProjectileSpec { Gravity = gravity, Radius = 10f, MaxLifetime = lifetime });

    private async Task ProjectileChecks()
    {
        // 我方投掷物：穿过玩家自己，打中敌方受击框（假人），只结算一次
        await Arrange(Near, Far);
        int hpA = _a.Stats.Health, hpP = Player.Stats.Health;
        Projectile mine = Throw(new Vector2(300, 540), new Vector2(600, 0), ProjectileTeam.Player);
        bool layers = mine.CollisionLayer == Layers.PlayerHit && mine.CollisionMask == Layers.EnemyHurt;
        ProjectileRun run = await Fly(mine);
        Check(layers && run.End == ProjectileEnd.Target && run.Outcome == HitOutcome.Damaged && run.Target?.Entity == _a && hpA - _a.Stats.Health == 7
              && Player.Stats.Health == hpP && run.Resolutions == 1 && run.Freed,
            $"投掷物（我方）：PlayerHit 层只打敌方受击框——穿过玩家自己（不掉血），打中假人扣 7、只结算一次后消失（{run.End}/{run.Outcome}）");

        // 敌方投掷物：穿过敌方受击框（假人），打中玩家
        await Arrange(new Vector2(600, 552), Far);
        hpA = _a.Stats.Health;
        hpP = Player.Stats.Health;
        Projectile theirs = Throw(new Vector2(800, 560), new Vector2(-600, 0), ProjectileTeam.Enemy, 12f);
        run = await Fly(theirs);
        Check(run.End == ProjectileEnd.Target && run.Outcome == HitOutcome.Damaged && run.Target == Player.HurtBox && hpP - Player.Stats.Health == 12
              && _a.Stats.Health == hpA && run.Resolutions == 1,
            $"投掷物（敌方）：EnemyHit 层只打玩家受击框——穿过敌方假人（不掉血），打中玩家扣 12（{hpP} → {Player.Stats.Health}）");

        // 只结算一次：两个假人挨着站在一条线上，只打第一个
        await Arrange(new Vector2(800, 552), new Vector2(830, 552));
        hpA = _a.Stats.Health;
        int hpB = _b.Stats.Health;
        run = await Fly(Throw(new Vector2(650, 560), new Vector2(500, 0), ProjectileTeam.Player));
        Check(run.Resolutions == 1 && run.Target?.Entity == _a && hpA - _a.Stats.Health == 7 && _b.Stats.Health == hpB,
            $"投掷物：同一个投掷物只结算一次——两个假人重叠在弹道上，只有第一个扣血（A {hpA - _a.Stats.Health}，B {hpB - _b.Stats.Health}）");

        // 受击方不接受攻击（Ignored）时穿过去，打后面那个
        await Arrange(new Vector2(800, 552), new Vector2(1000, 552));
        foreach (HurtBox hb in _a.HurtBoxes) hb.Receiving = false;
        hpB = _b.Stats.Health;
        run = await Fly(Throw(new Vector2(650, 560), new Vector2(600, 0), ProjectileTeam.Player));
        foreach (HurtBox hb in _a.HurtBoxes) hb.Receiving = true;
        Check(run.Resolutions == 1 && run.Target?.Entity == _b && hpB - _b.Stats.Health == 7,
            "投掷物：受击方不接受攻击（Ignored）不算结算，投掷物穿过去打中后面的假人");

        // 格挡：走 HurtBox 同一套判定。单向格挡按投掷物当前所在的一侧判断
        await Arrange(Near, Far);
        HurtBox guard = _a.HurtBoxes[0];
        guard.Guard = GuardMode.Hold;
        guard.Omnidirectional = false;
        guard.GuardFacing = -1f;
        hpA = _a.Stats.Health;
        run = await Fly(Throw(new Vector2(300, 540), new Vector2(600, 0), ProjectileTeam.Player));
        bool blocked = run.Outcome == HitOutcome.Blocked && _a.Stats.Health == hpA && run.End == ProjectileEnd.Target && run.Freed;
        guard.GuardFacing = 1f;
        run = await Fly(Throw(new Vector2(300, 540), new Vector2(600, 0), ProjectileTeam.Player));
        bool notCovered = run.Outcome == HitOutcome.Damaged && hpA - _a.Stats.Health == 7;
        var unblockable = new HitSpec { Damage = 5f, Blockable = false };
        guard.GuardFacing = -1f;
        int hpA2 = _a.Stats.Health;
        run = await Fly(Projectile.Launch(Lab, new Vector2(300, 540), new Vector2(600, 0), unblockable, "lab_unblockable", null, ProjectileTeam.Player,
            new ProjectileSpec { Gravity = 0f }));
        bool pierced = run.Outcome == HitOutcome.Damaged && hpA2 - _a.Stats.Health == 5;
        guard.Guard = GuardMode.None;
        guard.Omnidirectional = true;
        Check(blocked && notCovered && pierced,
            "投掷物：格挡走 HurtBox 同一套判定——朝着来向的单向格挡挡住（Blocked，投掷物碎掉）；背对来向挡不住；Blockable=false 的投掷物挡不住");

        // 撞世界碰撞就消失：地面、墙
        await Arrange(Far, Far);
        run = await Fly(Throw(new Vector2(1000, 400), new Vector2(0, 600), ProjectileTeam.Enemy));
        bool floor = run.End == ProjectileEnd.World && Mathf.Abs(run.EndPosition.Y - 590f) < 2f && run.Outcome == null && run.Freed;
        float floorY = run.EndPosition.Y;
        run = await Fly(Throw(new Vector2(2600, 400), new Vector2(900, 0), ProjectileTeam.Enemy));
        bool wall = run.End == ProjectileEnd.World && Mathf.Abs(run.EndPosition.X - 2790f) < 2f && run.Freed;
        Projectile inside = Throw(new Vector2(1000, 620), new Vector2(100, 0), ProjectileTeam.Enemy);
        bool insideEnded = inside.EndReason == ProjectileEnd.World;
        await Frames(2);
        Check(floor && wall && insideEnded && !GodotObject.IsInstanceValid(inside),
            $"投掷物：撞到地面（y 600）、墙（x 2800）就碎，停在碰撞面外（y {floorY:F0}、x {run.EndPosition.X:F0}），不穿过去；生成在墙里的当场就碎");

        // 寿命到了自己消失
        run = await Fly(Throw(new Vector2(1000, 300), new Vector2(10, 0), ProjectileTeam.Enemy, lifetime: 0.25f), 60);
        Check(run.End == ProjectileEnd.Expired && run.Freed, "投掷物：飞得超过 MaxLifetime 就自己消失");

        // 抛物线初速度：按 ArcVelocity 扔，经过目标点（打中站在 300 像素外的假人身体中心）
        await Arrange(new Vector2(1300, 552), Far);
        Vector2 from = new(1000, 520), to = _a.GlobalPosition;
        Vector2 velocity = Projectile.ArcVelocity(from, to, 1200f, 400f);
        Vector2 expected = new(400f, (to.Y - from.Y - 0.5f * 1200f * 0.75f * 0.75f) / 0.75f);
        int events = _a.DamageEvents;
        Projectile arc = Throw(from, velocity, ProjectileTeam.Player, gravity: 1200f);
        float apex = from.Y;
        for (int i = 0; i < 120 && GodotObject.IsInstanceValid(arc) && !arc.Ended; i++)
        {
            apex = Mathf.Min(apex, arc.GlobalPosition.Y);
            await Frames(1);
        }
        await Frames(2);
        Check(velocity.DistanceTo(expected) < 0.01f && apex < from.Y - 40f && _a.DamageEvents == events + 1 && !GodotObject.IsInstanceValid(arc),
            $"投掷物：ArcVelocity 算出的初速度 {velocity}（水平 400，飞 0.75 秒），先升到 {apex:F0} 再落下，正好打中 300 像素外的假人");

        string source = FileAccess.GetFileAsString("res://assets/scripts/combat/Projectile.cs");
        Check(source.Contains("partial class Projectile : HitBox") && !source.Contains("SonOfMud.Player") && !source.Contains("SonOfMud.Enemy"),
            "投掷物：是 HitBox 的子类（结算走同一套），在 Combat 里，不认识 Player / Enemy");
    }
}
