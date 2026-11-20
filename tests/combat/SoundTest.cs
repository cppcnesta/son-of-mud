using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Enemy.Bosses.Keeper;
using SonOfMud.Enemy.Bosses.Keeper.States;
using AttackWindupState = SonOfMud.Enemy.States.AttackWindupState;
using SonOfMud.Player;
using SonOfMud.Player.States;

/// <summary>
/// 音效（《NZZ音效对照表》）：
/// ① SoundBank 按文件名分组（含导出后的 .import / .remap 名字），仓库里交付的 5 组版本数对，没交付的 8 组不响、不报错；
/// ② 随机：只把挑中的一个交给 SoundRequested，同一组不连着两次一样，所有版本都会出现；
/// ③ 各个时机（换成测试音效文件夹，13 组都有文件）：脚步（按走过的距离，停下 / 贴墙 / 空中 / 直接改位置都不算）、
///    起跳、蹬墙跳、闪避、格挡成功、扔泥肝、下劈、主角受击 / 死亡、小怪受击（近战、泥肝、G5 没被打断）/ 死亡、
///    守坑人横扫 / 尾刺 / 死亡；不受伤（无敌、重复判定）不响；致死的一下只响一个。
/// </summary>
public partial class SoundTest : LabSuiteBase
{
    private const string LabPath = "res://assets/scenes/tests/enemy_lab.tscn";
    private const string FixtureDir = "res://tests/combat/fixtures/sfx";
    private const float FloorY = 600f;

    protected override string ReportName => "sound_results";

    private readonly List<string> _played = new();
    private readonly List<int> _requestSizes = new();
    private readonly List<AudioStream> _requested = new();
    private readonly List<AudioStream> _playedStreams = new();

    protected override async Task RunChecks()
    {
        SoundBank.Played += OnPlayed;
        Events.Instance.SoundRequested += OnRequested;
        try
        {
            GroupChecks();
            RandomChecks();
            await LoadLab(LabPath);
            foreach (string name in new[] { "Mason", "Prayer", "PrayerCity", "TempleGuard" })
                Lab.GetNodeOrNull(name)?.Free();
            SoundBank.UseDirectory(FixtureDir);
            Check(Group().Count == 13 && SoundBank.Variants(SoundBank.MinionDeath).Count == 2,
                $"测试音效文件夹：13 组都有文件（{Group().Count} 组）");
            await FootstepChecks();
            await PlayerActionChecks();
            await PlayerHitChecks();
            await EnemyChecks();
            await KeeperChecks();
        }
        finally
        {
            SoundBank.UseDirectory(null);
            SoundBank.Played -= OnPlayed;
            if (Events.Instance != null) Events.Instance.SoundRequested -= OnRequested;
        }
    }

    private static Dictionary<string, string[]> Group() =>
        SoundBank.Group(DirAccess.GetFilesAt(SoundBank.Directory));

    private void OnPlayed(string group, AudioStream stream)
    {
        _played.Add(group);
        _playedStreams.Add(stream);
    }

    private void OnRequested(Godot.Collections.Array streams)
    {
        _requestSizes.Add(streams.Count);
        _requested.Add(streams.Count > 0 ? streams[0].AsGodotObject() as AudioStream : null);
    }

    private int Mark => _played.Count;
    private List<string> Since(int mark) => _played.Skip(mark).ToList();
    private int Count(int mark, string group) => Since(mark).Count(g => g == group);
    private static string Show(IEnumerable<string> groups) => groups.Any() ? string.Join("、", groups) : "无";

    // ---------- ① 分组 ----------

    private void GroupChecks()
    {
        SoundBank.UseDirectory(null);
        var listing = new[]
        {
            "SFX_Player_Jump_02.wav.import", "SFX_Player_Jump_01.wav", "SFX_Player_Jump_01.wav.import", "SFX_Player_Jump_03.ogg.remap",
            "SFX_Player_Attack_DownSlash_01.wav", "sfx_narrative_door.wav", "SFX_Bad.wav", "SFX_Player_Jump_x.wav", "readme.txt",
        };
        Dictionary<string, string[]> groups = SoundBank.Group(listing);
        Check(groups.Count == 2
              && groups["Player_Jump"].SequenceEqual(new[] { "SFX_Player_Jump_01.wav", "SFX_Player_Jump_02.wav", "SFX_Player_Jump_03.ogg" })
              && groups["Player_Attack_DownSlash"].SequenceEqual(new[] { "SFX_Player_Attack_DownSlash_01.wav" }),
            $"分组：去掉序号就是组名（组名里可以有下划线），.import / .remap 也认、不重复，不合规的文件不要（{string.Join("；", groups.Select(g => g.Key + "=" + g.Value.Length))}）");

        var expected = new Dictionary<string, int>
        {
            [SoundBank.MinionHit] = 3, [SoundBank.PlayerFootstep] = 4, [SoundBank.PlayerJump] = 3, [SoundBank.PlayerDodge] = 3, [SoundBank.PlayerBlock] = 3,
        };
        Dictionary<string, string[]> real = Group();
        bool counts = real.Count == expected.Count && expected.All(e => SoundBank.Variants(e.Key).Count == e.Value);
        bool loadable = expected.Keys.SelectMany(SoundBank.Variants).All(p => ResourceLoader.Exists(p) && GD.Load<AudioStream>(p) != null);
        Check(counts && loadable, $"仓库里的音效：5 组、版本数对得上对照表（{string.Join("；", real.Select(g => g.Key + "×" + g.Value.Length))}），都能载入");

        string[] missing = { SoundBank.MinionDeath, SoundBank.BossSweep, SoundBank.BossTailStab, SoundBank.BossDeath, SoundBank.PlayerDeath,
                             SoundBank.PlayerRangedAttack, SoundBank.PlayerDownSlash, SoundBank.PlayerHit };
        int mark = Mark, requests = _requestSizes.Count;
        bool silent = missing.All(g => !SoundBank.Has(g) && !SoundBank.Play(g)) && !SoundBank.Play("") && !SoundBank.Play(null);
        Check(silent && Mark == mark && _requestSizes.Count == requests, "还没交付的 8 组（和空组名）：什么都不播、不报错");
        bool fallback = SoundBank.PlayFirst(SoundBank.MinionDeath, SoundBank.MinionHit) && Since(mark).SequenceEqual(new[] { SoundBank.MinionHit });
        Check(fallback, "PlayFirst：死亡声没有文件时用受击声，只播一个");
    }

    // ---------- ② 随机 ----------

    private void RandomChecks()
    {
        int mark = Mark, requests = _requestSizes.Count;
        for (int i = 0; i < 30; i++) SoundBank.Play(SoundBank.PlayerJump);
        List<AudioStream> picks = _playedStreams.Skip(mark).ToList();
        List<AudioStream> sent = _requested.Skip(requests).ToList();
        bool single = _requestSizes.Skip(requests).All(n => n == 1) && sent.Count == 30 && sent.SequenceEqual(picks);
        bool noRepeat = picks.Zip(picks.Skip(1)).All(p => p.First != p.Second);
        int distinct = picks.Select(p => p.ResourcePath).Distinct().Count();
        Check(single, "随机：每次只把挑中的那一个交给 SoundRequested（通道不会再挑一次）");
        Check(noRepeat && distinct == 3, $"随机：连播 30 次跳跃声，3 个版本都出现（{distinct} 个），没有连着两次一样");
    }

    // ---------- ③ 脚步 ----------

    private RunState Run => Player.Machine.Get<RunState>();

    private async Task FootstepChecks()
    {
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        int mark = Mark, steps = Run.Steps;
        float x0 = Player.GlobalPosition.X;
        Press(PlayerActions.Right);
        await Frames(60);
        float travel = Player.GlobalPosition.X - x0;
        int footsteps = Count(mark, SoundBank.PlayerFootstep);
        int expected = Mathf.FloorToInt((travel + RunState.StepDistance * 0.5f) / RunState.StepDistance);
        Check(footsteps >= 3 && Mathf.Abs(footsteps - expected) <= 1 && footsteps == Run.Steps - steps,
            $"脚步：在地上跑 1 秒走了 {travel:0} 像素，响了 {footsteps} 步（每 {RunState.StepDistance} 像素一步，应约 {expected} 步）");

        // 跑着的时候直接改位置（换房、复活、过门）不算走路
        mark = Mark;
        Player.GlobalPosition += new Vector2(400f, 0f);
        await Frames(5);
        int afterTeleport = Count(mark, SoundBank.PlayerFootstep);
        Check(afterTeleport <= 1, $"脚步：直接把主角挪 400 像素不会连响（之后 5 帧响了 {afterTeleport} 步）");
        Release(PlayerActions.Right);
        await Frames(10);
        mark = Mark;
        await Frames(40);
        Check(Count(mark, SoundBank.PlayerFootstep) == 0 && Player.State is IdleState, "脚步：停下不响");

        // 贴墙跑：按着方向键但走不动
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        var wall = new StaticBody2D { Name = "TestWall", CollisionLayer = Layers.World, CollisionMask = 0, Position = new Vector2(700f + PlayerBody.HalfW + 20f, FloorY - 100f) };
        wall.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(40f, 200f) } });
        Lab.AddChild(wall);
        Press(PlayerActions.Right);
        await Frames(20);
        mark = Mark;
        float xWall = Player.GlobalPosition.X;
        await Frames(60);
        bool pushing = Player.State is RunState && Mathf.Abs(Player.GlobalPosition.X - xWall) < 1f;
        Check(pushing && Count(mark, SoundBank.PlayerFootstep) == 0, $"脚步：贴墙跑走不动不响（状态 {Player.State?.Name}）");
        Release(PlayerActions.Right);
        wall.Free();

        // 空中不响
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        Press(PlayerActions.Right);
        await Tap(PlayerActions.Jump);
        for (int i = 0; i < 20 && Player.IsOnFloor(); i++) await Frames(1);
        mark = Mark;
        int airFrames = 0;
        while (!Player.IsOnFloor() && airFrames < 90)
        {
            await Frames(1);
            airFrames++;
        }
        Check(airFrames > 10 && Count(mark, SoundBank.PlayerFootstep) == 0, $"脚步：空中 {airFrames} 帧不响");
        Release(PlayerActions.Right);
    }

    // ---------- ③ 主角动作 ----------

    private async Task PlayerActionChecks()
    {
        // 起跳：前摇里不响，离地那一刻响一次
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        int mark = Mark;
        Press(PlayerActions.Jump);
        bool silentWindup = true;
        int frames = 0;
        while (Player.State is not RiseState && frames < 30)
        {
            if (Player.State is JumpWindupState && Count(mark, SoundBank.PlayerJump) > 0) silentWindup = false;
            await Frames(1);
            frames++;
        }
        Release(PlayerActions.Jump);
        Check(Player.State is RiseState && silentWindup && Count(mark, SoundBank.PlayerJump) == 1, "起跳：前摇里不响，离地那一刻响一次跳跃声");

        // 蹬墙跳（同一个起跳前摇，带 wall）
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        mark = Mark;
        Player.Machine.TransitionTo<JumpWindupState>(new StateMsg().Set(JumpWindupState.WallKey, true));
        for (int i = 0; i < 30 && Player.State is not RiseState; i++) await Frames(1);
        Check(Player.State is RiseState && Count(mark, SoundBank.PlayerJump) == 1, "蹬墙跳：离墙那一刻响一次跳跃声");

        // 闪避
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        mark = Mark;
        await Tap(PlayerActions.Dodge);
        Check(Player.State is DodgeState && Since(mark).SequenceEqual(new[] { SoundBank.PlayerDodge }), $"闪避：开始时响一次（播过 {Show(Since(mark))}）");

        // 格挡：按住不响，挡住攻击那一下响
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        mark = Mark;
        Press(PlayerActions.Block);
        await Frames(6);
        bool blocking = Player.State is BlockState;
        bool quiet = Count(mark, SoundBank.PlayerBlock) == 0;
        HitOutcome outcome = Player.HurtBox.Receive(MakeHit("sound_block", 5f, Player.GlobalPosition + new Vector2(Player.Facing * 60f, 0f)));
        Check(blocking && quiet && outcome == HitOutcome.Blocked && Since(mark).SequenceEqual(new[] { SoundBank.PlayerBlock }),
            $"格挡：只按格挡不响；挡住攻击那一下响一次（{outcome}，播过 {Show(Since(mark))}）");
        Release(PlayerActions.Block);

        // 扔泥肝
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        ThrowState throwState = Player.Machine.Get<ThrowState>();
        ItemCounts.Set(throwState.ItemId, 3);
        mark = Mark;
        await Tap(PlayerActions.ThrowItem);
        Check(throwState.Thrown > 0 && ItemCounts.Get(throwState.ItemId) == 2 && Count(mark, SoundBank.PlayerRangedAttack) == 1,
            $"扔泥肝（远程普攻）：出手响一次（播过 {Show(Since(mark))}）");

        // 下劈声：普通第 1 下不响；连招第 2 下、空中下劈出刀时响
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        mark = Mark;
        await Tap(PlayerActions.Attack);
        await Frames(30);
        Check(Count(mark, SoundBank.PlayerDownSlash) == 0, "普通攻击第 1 下：不响下劈声");

        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        mark = Mark;
        Player.Machine.TransitionTo<AttackState>(new StateMsg().Set(AttackState.ComboStepKey, 2));
        bool silentBefore = Count(mark, SoundBank.PlayerDownSlash) == 0;
        for (int i = 0; i < 40 && (Player.State as AttackState)?.Current != AttackState.Phase.Active; i++) await Frames(1);
        await Frames(1);
        Check(silentBefore && Count(mark, SoundBank.PlayerDownSlash) == 1, "连招第 2 下（播下劈动画）：出刀那一刻响一次下劈声");

        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        await Tap(PlayerActions.Jump);
        for (int i = 0; i < 20 && Player.IsOnFloor(); i++) await Frames(1);
        mark = Mark;
        Player.Machine.TransitionTo<AttackState>(new StateMsg().Set(AttackState.DownKey, true));
        for (int i = 0; i < 40 && (Player.State as AttackState)?.Current != AttackState.Phase.Active; i++) await Frames(1);
        await Frames(1);
        Check(Count(mark, SoundBank.PlayerDownSlash) == 1, "空中下劈：出刀那一刻响一次下劈声");
    }

    // ---------- ③ 主角受击 ----------

    private async Task PlayerHitChecks()
    {
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        Vector2 source = Player.GlobalPosition + new Vector2(60f, 0f);
        int mark = Mark;
        HitOutcome first = Player.HurtBox.Receive(MakeHit("sound_hurt", 1f, source));
        HitOutcome again = Player.HurtBox.Receive(MakeHit("sound_hurt", 1f, source));
        Check(first == HitOutcome.Damaged && again == HitOutcome.Duplicate && Since(mark).SequenceEqual(new[] { SoundBank.PlayerHit }),
            $"主角挨打掉血：响一次受击声；同一招重复判定不再响（播过 {Show(Since(mark))}）");

        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        mark = Mark;
        Player.HurtBox.SetInvulnerable("sound_test", true);
        HitOutcome invulnerable = Player.HurtBox.Receive(MakeHit("sound_inv", 1f, source));
        Player.HurtBox.SetInvulnerable("sound_test", false);
        Check(invulnerable == HitOutcome.Invulnerable && Mark == mark, "主角无敌时挨打：不响");

        mark = Mark;
        HitOutcome lethal = Player.HurtBox.Receive(MakeHit("sound_die", Player.Stats.Health + 100, source));
        await Frames(2);
        Check(lethal == HitOutcome.Killed && Player.State is DeadState && Since(mark).SequenceEqual(new[] { SoundBank.PlayerDeath }),
            $"主角被打死：只响死亡声，不再叠受击声（播过 {Show(Since(mark))}）");
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        Player.Stats.Refill();
    }

    // ---------- ③ 小怪 ----------

    private EnemyBody Spawn(string kind, float x, int health = 999)
    {
        var enemy = GD.Load<PackedScene>($"res://assets/scenes/enemy/{kind}.tscn").Instantiate<EnemyBody>();
        enemy.Position = new Vector2(x, FloorY);
        enemy.Seed = 42;
        enemy.Data = (EnemyData)enemy.Data.Duplicate();
        enemy.Data.VisionRange = 0f;
        if (enemy is KeeperBoss keeper) keeper.Arena = new Rect2(100000f, 0f, 10f, 10f);
        Lab.AddChild(enemy);
        enemy.Stats.MaxHealth = health;
        enemy.Stats.Refill();
        return enemy;
    }

    private async Task EnemyChecks()
    {
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        EnemyBody mason = Spawn("mason", 1300f);
        await Frames(4);
        Vector2 source = mason.GlobalPosition - new Vector2(60f, 0f);
        int mark = Mark;
        HitOutcome hit = mason.HurtBox.Receive(MakeHit("sound_m1", 1f, source));
        HitOutcome dup = mason.HurtBox.Receive(MakeHit("sound_m1", 1f, source));
        Check(hit == HitOutcome.Damaged && dup == HitOutcome.Duplicate && Since(mark).SequenceEqual(new[] { SoundBank.MinionHit }),
            $"小怪被打中：响一次小怪受击声；重复判定不响（播过 {Show(Since(mark))}）");

        mark = Mark;
        mason.HurtBox.SetInvulnerable("sound_test", true);
        HitOutcome inv = mason.HurtBox.Receive(MakeHit("sound_m2", 1f, source));
        mason.HurtBox.SetInvulnerable("sound_test", false);
        Check(inv == HitOutcome.Invulnerable && Mark == mark, "小怪无敌时被打：不响");

        // G5：出招中被不能打断的攻击打中，招照常出完，但掉了血也响
        await Frames(30);
        mason.Machine.TransitionTo<AttackWindupState>();
        await Frames(1);
        mark = Mark;
        int uninterrupted = mason.UninterruptedHits;
        Hit water = MakeHit("sound_m3", 1f, source);
        water.InterruptsActions = false;
        HitOutcome g5 = mason.HurtBox.Receive(water);
        Check(g5 == HitOutcome.Damaged && mason.UninterruptedHits == uninterrupted + 1 && Since(mark).SequenceEqual(new[] { SoundBank.MinionHit }),
            $"G5：出招中被水打中没被打断，掉了血也响受击声（播过 {Show(Since(mark))}）");

        mark = Mark;
        HitOutcome killed = mason.HurtBox.Receive(MakeHit("sound_m4", 99999f, source));
        Check(killed == HitOutcome.Killed && Since(mark).SequenceEqual(new[] { SoundBank.MinionDeath }),
            $"小怪被打死：只响小怪死亡声（文件到了就替代受击声，不叠加；播过 {Show(Since(mark))}）");
        mason.Free();

        // 泥肝打中小怪
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        EnemyBody target = Spawn("mason", 950f);
        await Frames(4);
        ThrowState throwState = Player.Machine.Get<ThrowState>();
        ItemCounts.Set(throwState.ItemId, 1);
        int health = target.Stats.Health;
        mark = Mark;
        await Tap(PlayerActions.ThrowItem);
        for (int i = 0; i < 60 && Count(mark, SoundBank.MinionHit) == 0; i++) await Frames(1);
        Check(target.Stats.Health < health && Count(mark, SoundBank.MinionHit) == 1, $"泥肝打中小怪：响一次小怪受击声（播过 {Show(Since(mark))}）");
        target.Free();
    }

    // ---------- ③ 守坑人 ----------

    private async Task KeeperChecks()
    {
        await Reset(new Vector2(700f, FloorY - PlayerBody.HalfH));
        var keeper = (KeeperBoss)Spawn("keeper", 2400f);
        await Frames(4);
        Check(keeper.Data.HitSound == SoundBank.MinionHit && keeper.Data.DeathSound == SoundBank.BossDeath, "守坑人：受击用小怪受击声，死亡用 Boss01_Death");

        int mark = Mark;
        keeper.Machine.TransitionTo<SweepStrikeState>();
        await Frames(1);
        Check(Count(mark, SoundBank.BossSweep) == 1, $"守坑人横扫出手：响一次横扫声（播过 {Show(Since(mark))}）");

        mark = Mark;
        keeper.Machine.TransitionTo<TailStrikeState>();
        await Frames(1);
        Check(Count(mark, SoundBank.BossTailStab) == 1, $"守坑人蝎尾出手：响一次尾刺声（播过 {Show(Since(mark))}）");

        keeper.Machine.TransitionTo<DormantState>();
        await Frames(2);
        Vector2 source = keeper.GlobalPosition - new Vector2(200f, 0f);
        mark = Mark;
        HitOutcome hit = keeper.HurtBox.Receive(MakeHit("sound_k1", 1f, source));
        Check(hit == HitOutcome.Damaged && Since(mark).SequenceEqual(new[] { SoundBank.MinionHit }), $"守坑人被打中：响小怪受击声（所有受击同一个声音；{hit}）");

        mark = Mark;
        HitOutcome killed = keeper.HurtBox.Receive(MakeHit("sound_k2", 99999f, source));
        await Frames(2);
        Check(killed == HitOutcome.Killed && keeper.State is TransformState && Since(mark).SequenceEqual(new[] { SoundBank.BossDeath }),
            $"守坑人被打败：只响一次 Boss01 死亡声（播过 {Show(Since(mark))}）");
        keeper.Free();
    }
}
