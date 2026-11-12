using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Camera;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Enemy.Bosses.Keeper;
using SonOfMud.Enemy.States;
using SonOfMud.Feel;
using SonOfMud.Player;

/// <summary>
/// 主角攻击的打击感 6 层（测试场 enemy_lab，真实玩家、真实按键，镜头换成 RoomCamera）：
/// ① 数据和接线：每种攻击的 HitFeel、下劈单独的打击感、下砸；每次结算只请求一次顿帧；
/// ② 打中 / 打死：顿帧时长（真实时间）、震屏、闪白、击退、命中粒子、音效都按各自的数值；挥空、打在无敌期间都不触发；
/// ③ 顿帧和暂停交错、连续命中只取最长、同一刀打中两个敌人；
/// ④ 震屏叠加不超过 MaxShakeOffset；
/// ⑤ 击退：逃跑 / 被动反击 / 受击硬直 / 不理会四种反应都被推开且反应不变，重复受击替换不叠加，悬崖边和墙边停住，守坑人不动，打死清掉击退；
/// ⑥ 全工程只有 FeedbackService 写 Engine.TimeScale。
/// </summary>
public partial class HitFeelTest : LabSuiteBase
{
    private const string LabPath = "res://assets/scenes/tests/enemy_lab.tscn";
    private const float FloorY = 600f;
    private const string P = "res://assets/data/player_v2/";

    protected override string ReportName => "hit_feel_results";

    private FeedbackService Service => FeedbackService.Instance;
    private RoomCamera _camera;
    private int _hitStopRequests;
    private readonly List<float> _hitStopSeconds = new();

    protected override async Task RunChecks()
    {
        await LoadLab(LabPath);
        foreach (string name in new[] { "Mason", "Prayer", "PrayerCity", "TempleGuard" })
            Lab.GetNodeOrNull(name)?.Free();
        Player.GetNodeOrNull<Camera2D>("Camera2D")?.Free();
        _camera = new RoomCamera { Name = "TestCamera", Settings = GD.Load<CameraSettings>("res://assets/data/camera/camera_settings.tres") };
        Lab.AddChild(_camera);
        _camera.SetTarget(Player);
        Events.Instance.HitStopRequested += OnHitStopRequested;
        await ProcessFrames(2);

        Check(Service != null && GetNodeOrNull(FeedbackService.JuiceePath) != null && _camera.ShakeRig != null,
            "前提：打击感服务（自动加载 Feedback）和 Juicee 都在，镜头有震动挂点");
        if (Service == null) return;

        DataChecks();
        await WaterHitChecks();
        await KillChecks();
        await MudHitChecks();
        await DownAttackChecks();
        await NoFeelChecks();
        await PauseChecks();
        await StackChecks();
        await ShakeCapChecks();
        await ReactionChecks();
        await RepeatHitChecks();
        await LedgeAndWallChecks();
        await BossChecks();
        await FlashChecks();
        ScanChecks();
        Events.Instance.HitStopRequested -= OnHitStopRequested;
        Service.CancelAll();
    }

    private void OnHitStopRequested(float seconds)
    {
        _hitStopRequests++;
        _hitStopSeconds.Add(seconds);
    }

    // ---------- 工具 ----------

    private async Task ProcessFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>把场上清干净：主角放到 x、指定形态，所有效果收尾。</summary>
    private async Task Clean(float x, int form = 0)
    {
        foreach (Node child in Lab.GetChildren())
            if (child is EnemyBody || child.Name.ToString().StartsWith("TestSolid")) child.Free();
        Service.CancelAll();
        PauseService.Reset();
        await Reset(new Vector2(x, FloorY - 24f), form);
        await ProcessFrames(2);
    }

    private EnemyBody Spawn(string kind, float x, float y = FloorY, System.Action<EnemyBody> configure = null)
    {
        var enemy = GD.Load<PackedScene>($"res://assets/scenes/enemy/{kind}.tscn").Instantiate<EnemyBody>();
        enemy.Position = new Vector2(x, y);
        enemy.Seed = 42;
        // 看不见主角：不会追上来打主角，打击感的检查不受干扰（反应规则和视野无关）
        enemy.Data = (EnemyData)enemy.Data.Duplicate();
        enemy.Data.VisionRange = 0f;
        if (enemy is KeeperBoss keeper) keeper.Arena = new Rect2(100000f, 0f, 10f, 10f);
        configure?.Invoke(enemy);
        Lab.AddChild(enemy);
        return enemy;
    }

    private void Solid(Rect2 rect)
    {
        var body = new StaticBody2D { Name = "TestSolid" + Lab.GetChildCount(), CollisionLayer = Layers.World, CollisionMask = 0, Position = rect.GetCenter() };
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        Lab.AddChild(body);
    }

    /// <summary>按一下攻击，等到这一刀开始顿帧（或等满 maxFrames）。返回从按键到顿帧开始的处理帧数，-1 表示没打中。</summary>
    private async Task<int> AttackUntilHitStop(int maxFrames = 40, bool down = false)
    {
        // 上一刀还没收完时按键会被吞掉：先等主角回到自由状态
        for (int i = 0; i < 90 && Player.State is SonOfMud.Player.States.AttackState or SonOfMud.Player.States.AttackRecoveryState; i++) await Frames(1);
        Player.Timers.AttackInterval = 0f;
        if (down) Input.ActionPress(PlayerActions.Down);
        Input.ActionPress(PlayerActions.Attack);
        int frames = 0;
        bool released = false;
        while (frames < maxFrames)
        {
            await ProcessFrames(1);
            frames++;
            if (!released && frames >= 2)
            {
                Input.ActionRelease(PlayerActions.Attack);
                released = true;
            }
            if (Service.HitStopActive) break;
        }
        Input.ActionRelease(PlayerActions.Attack);
        if (down) Input.ActionRelease(PlayerActions.Down);
        return Service.HitStopActive ? frames : -1;
    }

    /// <summary>从现在起数处理帧，直到 TimeScale 回到 1；返回帧数。</summary>
    private async Task<int> FramesUntilTimeResumes(int limit = 120)
    {
        int frames = 0;
        while (Engine.TimeScale < 1.0 && frames < limit)
        {
            await ProcessFrames(1);
            frames++;
        }
        return frames;
    }

    private List<AudioStreamPlayer> Sounds() =>
        Service.GetChildren().OfType<AudioStreamPlayer>().Where(p => p.Name.ToString().StartsWith("_juicee_sound_") && !p.IsQueuedForDeletion()).ToList();

    private HashSet<ulong> SoundIds() => Sounds().Select(p => p.GetInstanceId()).ToHashSet();

    private AudioStreamPlayer NewSound(HashSet<ulong> before) => Sounds().FirstOrDefault(p => !before.Contains(p.GetInstanceId()));

    private static bool Near(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;

    /// <summary>刚打中时的击退速度：初速度，最多已经按衰减走了一个物理帧（结算和敌人的物理帧在同一帧）。</summary>
    private static bool FreshKnock(float knock, HitFeel feel, float sign = 1f) =>
        knock * sign <= feel.Knockback + 0.5f && knock * sign >= feel.Knockback - feel.KnockbackDecay / 60f - 0.5f;

    // ---------- ① 数据和接线 ----------

    private void DataChecks()
    {
        var water = GD.Load<FormConfig>(P + "water.tres");
        var mud = GD.Load<FormConfig>(P + "mud.tres");
        var rules = Player.Rules;
        HitFeel w = water.AttackSpec.Feel, m = mud.AttackSpec.Feel;
        Check(w != null && m != null && w.ResourcePath.EndsWith("feel_water.tres") && m.ResourcePath.EndsWith("feel_mud.tres"),
            "数据：水 / 泥攻击各有自己的打击感（feel_water / feel_mud）");
        Check(water.DownAttackFeel?.ResourcePath.EndsWith("feel_water_down.tres") == true && mud.DownAttackFeel?.ResourcePath.EndsWith("feel_mud_down.tres") == true
              && water.DownAttackFeel.Knockback == 0f && mud.DownAttackFeel.Knockback == 0f,
            "数据：下劈单独配打击感（不往旁边推）");
        Check(rules.SlamSpec?.Feel?.ResourcePath.EndsWith("feel_slam.tres") == true && Hit.From(rules.SlamSpec, "x", Vector2.Zero).Feel == rules.SlamSpec.Feel,
            "数据：下砸有自己的打击感，Hit.From 把 HitSpec.Feel 带进本次 Hit");
        Check(w.HitStop < w.KillHitStop && m.HitStop < m.KillHitStop && w.HitStop < m.HitStop && w.Knockback < m.Knockback && w.HitSounds.Count > 0 && m.KillSounds.Count > 0,
            $"数据：打死比打中重、泥比水重（水顿帧 {w.HitStop}/{w.KillHitStop}，泥 {m.HitStop}/{m.KillHitStop}；击退 {w.Knockback}/{m.Knockback}）");
    }

    // ---------- ② 打中 ----------

    private async Task WaterHitChecks()
    {
        await Clean(800f);
        HitFeel feel = Player.Forms.Current.AttackSpec.Feel;
        EnemyBody enemy = Spawn("mason", 870f);
        enemy.Stats.MaxHealth = 999; enemy.Stats.Refill();
        await Frames(4);
        int requests = _hitStopRequests;
        int sparksBefore = Service.ActiveSparks();
        HashSet<ulong> soundsBefore = SoundIds();
        int toHit = await AttackUntilHitStop();
        Check(toHit > 0 && Engine.TimeScale == 0.0 && _hitStopRequests == requests + 1 && Near(_hitStopSeconds[^1], feel.HitStop),
            $"水攻击打中：立刻顿帧（TimeScale {Engine.TimeScale}），只请求一次，时长取 feel_water 的 {feel.HitStop} 秒（请求 {_hitStopRequests - requests} 次）");
        await ProcessFrames(1);
        bool shaking = _camera.IsShaking;
        bool flashing = enemy.FlashAmount == 1f && enemy.HitFlashRemaining > 0f;
        float knock = enemy.KnockbackVelocity;
        bool sparked = Service.ActiveSparks() > sparksBefore;
        AudioStream sound = NewSound(soundsBefore)?.Stream;
        int resume = await FramesUntilTimeResumes();
        int expected = Mathf.RoundToInt(feel.HitStop * 60f);
        Check(Mathf.Abs(resume + 1 - expected) <= 1, $"顿帧按真实时间 {feel.HitStop} 秒（约 {expected} 帧）后恢复（{resume + 1} 帧）");
        Check(shaking, "震屏：打中时镜头在震");
        Check(flashing, "闪白：被打中的敌人在闪白");
        Check(FreshKnock(knock, feel), $"击退：敌人被沿远离主角的方向推开，初速度 {feel.Knockback}（{knock:0}）");
        Check(sparked, "粒子：命中点出现泥点");
        Check(sound != null && feel.HitSounds.Contains(sound), "音效：播了 feel_water 里的打中音效之一");
        float before = enemy.GlobalPosition.X;
        await Frames(20);
        Check(enemy.GlobalPosition.X > before + 5f && enemy.KnockbackVelocity < knock, $"击退在顿帧结束后真的把敌人推远并减速（移动 {enemy.GlobalPosition.X - before:0} 像素）");
        await Frames(40);
        Check(enemy.KnockbackVelocity == 0f && !_camera.IsShaking && _camera.Offset == Vector2.Zero && enemy.FlashAmount == 0f,
            "之后：击退减到 0，镜头回原位，闪白结束");
    }

    private async Task KillChecks()
    {
        await Clean(800f);
        HitFeel feel = Player.Forms.Current.AttackSpec.Feel;
        EnemyBody enemy = Spawn("mason", 870f);
        await Frames(4);
        // 先打一下让它带着击退，再在击退没结束时打死：死亡要清掉击退
        enemy.Stats.MaxHealth = 15; enemy.Stats.Refill();
        await AttackUntilHitStop();
        await FramesUntilTimeResumes();
        await Frames(2);
        bool sliding = enemy.KnockbackVelocity != 0f;
        Player.Timers.AttackInterval = 0f;
        Player.GlobalPosition = new Vector2(enemy.GlobalPosition.X - 70f, Player.GlobalPosition.Y);
        HashSet<ulong> soundsBefore = SoundIds();
        int toHit = await AttackUntilHitStop();
        Check(toHit > 0 && enemy.IsDead && Near(_hitStopSeconds[^1], feel.KillHitStop), $"打死：顿帧取打死档 {feel.KillHitStop} 秒（toHit {toHit} dead {enemy.IsDead} hp {enemy.Stats.Health} last {(_hitStopSeconds.Count > 0 ? _hitStopSeconds[^1] : -1)} state {enemy.State?.Name} player {Player.State?.Name}）");
        AudioStream sound = NewSound(soundsBefore)?.Stream;
        Check(sound != null && feel.KillSounds.Contains(sound), "打死：播打死音效");
        Check(enemy.KnockbackVelocity == 0f && enemy.FlashAmount == 1f, $"打死：不被推开，照样闪白（之前那一下击退 {(sliding ? "有" : "没有")}）");
        float x = enemy.GlobalPosition.X;
        await FramesUntilTimeResumes();
        await Frames(10);
        Check(Near(enemy.GlobalPosition.X, x, 0.5f), "打死后原地不动");

        // 还在滑的时候被打死：击退立即清零
        EnemyBody second = Spawn("mason", 1300f);
        await Frames(4);
        second.Stats.MaxHealth = 3; second.Stats.Refill();
        second.HurtBox.Receive(new Hit { AttackId = "slide", Damage = 1f, SourcePosition = second.GlobalPosition - new Vector2(50f, 0f), Feel = feel, Source = Player });
        await Frames(2);
        bool stillSliding = second.KnockbackVelocity > 0f;
        second.HurtBox.Receive(new Hit { AttackId = "finish", Damage = 5f, SourcePosition = second.GlobalPosition - new Vector2(50f, 0f), Feel = feel, Source = Player });
        float xDead = second.GlobalPosition.X;
        await Frames(10);
        Check(stillSliding && second.IsDead && second.KnockbackVelocity == 0f && Near(second.GlobalPosition.X, xDead, 0.5f),
            "还在被击退时被打死：没结束的击退立即清零，尸体不再滑");
    }

    private async Task MudHitChecks()
    {
        await Clean(800f, 1);
        HitFeel feel = Player.Forms.Current.AttackSpec.Feel;
        EnemyBody enemy = Spawn("mason", 870f);
        enemy.Stats.MaxHealth = 999; enemy.Stats.Refill();
        await Frames(4);
        int toHit = await AttackUntilHitStop();
        await ProcessFrames(1);
        Check(toHit > 0 && Near(_hitStopSeconds[^1], feel.HitStop) && FreshKnock(enemy.KnockbackVelocity, feel) && feel.ResourcePath.EndsWith("feel_mud.tres"),
            $"泥攻击：顿帧 {feel.HitStop} 秒、击退 {feel.Knockback}（{enemy.KnockbackVelocity:0}）按 feel_mud");
        int resume = await FramesUntilTimeResumes();
        Check(Mathf.Abs(resume + 1 - Mathf.RoundToInt(feel.HitStop * 60f)) <= 1, $"泥攻击顿帧按 {feel.HitStop} 秒恢复（{resume + 1} 帧）");
    }

    private async Task DownAttackChecks()
    {
        await Clean(800f);
        HitFeel downFeel = Player.Forms.Current.DownAttackFeel;
        EnemyBody enemy = Spawn("mason", 800f);
        enemy.Stats.MaxHealth = 999; enemy.Stats.Refill();
        await Frames(4);
        Hit landed = null;
        void OnDamaged(Hit hit, int dealt) => landed = hit;
        enemy.HurtBox.Damaged += OnDamaged;
        Player.GlobalPosition = new Vector2(800f, FloorY - 64f - 70f);
        Player.Velocity = Vector2.Zero;
        await ProcessFrames(1);
        int toHit = await AttackUntilHitStop(40, down: true);
        enemy.HurtBox.Damaged -= OnDamaged;
        Check(toHit > 0 && landed?.Feel == downFeel && Near(_hitStopSeconds[^1], downFeel.HitStop) && enemy.KnockbackVelocity == 0f,
            $"下劈：本次 Hit 带的是下劈的打击感（feel_water_down），顿帧 {downFeel.HitStop} 秒，不往旁边推（{landed?.Feel?.ResourcePath}）");
        await FramesUntilTimeResumes();
    }

    private async Task NoFeelChecks()
    {
        await Clean(800f);
        // 挥空
        int requests = _hitStopRequests;
        Input.ActionPress(PlayerActions.Attack);
        await Frames(2);
        Input.ActionRelease(PlayerActions.Attack);
        await Frames(30);
        Check(_hitStopRequests == requests && Engine.TimeScale == 1.0 && !_camera.IsShaking && Service.ActiveSparks() == 0, "挥空：不顿帧、不震、没有泥点");

        // 打在无敌期间：没有结算成受伤，什么都不触发
        EnemyBody enemy = Spawn("mason", 870f);
        await Frames(4);
        enemy.HurtBox.SetInvulnerable("test", true);
        Player.Timers.AttackInterval = 0f;
        Input.ActionPress(PlayerActions.Attack);
        await Frames(2);
        Input.ActionRelease(PlayerActions.Attack);
        await Frames(20);
        Check(_hitStopRequests == requests && enemy.FlashAmount == 0f && enemy.KnockbackVelocity == 0f && Engine.TimeScale == 1.0,
            "打在无敌期间：不顿帧、不闪白、不击退");
        enemy.HurtBox.SetInvulnerable("test", false);

        // 没配打击感的攻击：顿帧退回 Rules.HitStopSeconds，其余不触发
        _hitStopSeconds.Clear();
        var hit = new Hit { AttackId = "nofeel", Damage = 1f, SourcePosition = Player.GlobalPosition, Source = Player };
        enemy.HurtBox.Receive(hit);
        await ProcessFrames(1);
        Check(enemy.FlashAmount == 0f && enemy.KnockbackVelocity == 0f, "没配打击感的攻击：敌人不闪白、不被推");
    }

    // ---------- ③ 顿帧和暂停、连续命中 ----------

    private async Task PauseChecks()
    {
        foreach (StringName source in new[] { PauseService.PauseMenu, PauseService.Narrative })
        {
            await Clean(800f);
            EnemyBody enemy = Spawn("mason", 870f);
            enemy.Stats.MaxHealth = 999; enemy.Stats.Refill();
            await Frames(4);
            await AttackUntilHitStop();
            PauseService.Request(source, true);
            Vector2 player = Player.GlobalPosition, foe = enemy.GlobalPosition;
            await ProcessFrames(20);
            bool resumedButPaused = Engine.TimeScale == 1.0 && GetTree().Paused && PauseService.IsPaused;
            bool still = Player.GlobalPosition == player && enemy.GlobalPosition == foe;
            PauseService.Request(source, false);
            await Frames(10);
            Check(resumedButPaused && still && !GetTree().Paused && enemy.GlobalPosition != foe,
                $"顿帧中暂停（{source}）：顿帧到期 TimeScale 回到 1，但游戏仍然暂停、谁都没动；取消暂停后正常（击退继续）");
        }

        // 暂停中结算了一次命中：顿帧照常到期，暂停不受影响
        await Clean(800f);
        PauseService.Request(PauseService.Narrative, true);
        Events.Instance.EmitSignal(Events.SignalName.HitStopRequested, 0.05f);
        bool frozen = Engine.TimeScale == 0.0;
        await ProcessFrames(10);
        Check(frozen && Engine.TimeScale == 1.0 && GetTree().Paused, "暂停中请求顿帧：照常到期恢复，暂停状态不受影响");
        PauseService.Request(PauseService.Narrative, false);
    }

    private async Task StackChecks()
    {
        await Clean(800f);
        Events.Instance.EmitSignal(Events.SignalName.HitStopRequested, 0.1f);
        await ProcessFrames(3);
        double left = Service.HitStopRemaining;
        Events.Instance.EmitSignal(Events.SignalName.HitStopRequested, 0.03f);
        bool shorterIgnored = Mathf.Abs(Service.HitStopRemaining - left) < 0.0001;
        Events.Instance.EmitSignal(Events.SignalName.HitStopRequested, 0.2f);
        int frames = await FramesUntilTimeResumes();
        Check(shorterIgnored && Mathf.Abs(frames + 3 - 12) <= 1 && Engine.TimeScale == 1.0,
            $"连续顿帧：短的不打断长的；更长的从第一次开始算总时长取较长的、不累加（0.2 秒 ≈ 12 帧，实际 {frames + 3}），结束后不会卡在 0");

        // 同一刀打中两个敌人：各结算一次，但只按最长的顿帧
        await Clean(800f);
        EnemyBody a = Spawn("mason", 860f), b = Spawn("mason", 880f);
        a.Stats.MaxHealth = b.Stats.MaxHealth = 999; a.Stats.Refill(); b.Stats.Refill();
        await Frames(4);
        int requests = _hitStopRequests;
        int starts = Service.HitStopStarts;
        await AttackUntilHitStop();
        await FramesUntilTimeResumes();
        float hs = Player.Forms.Current.AttackSpec.Feel.HitStop;
        Check(_hitStopRequests == requests + 2 && Service.HitStopStarts == starts + 1 && a.HitFlashRemaining >= 0f && b.HitFlashRemaining >= 0f,
            $"一刀打中两个敌人：两个都结算、请求两次顿帧，但只开始了一次 {hs} 秒的顿帧（开始 {Service.HitStopStarts - starts} 次，请求 {_hitStopRequests - requests} 次）");
    }

    // ---------- ④ 震屏上限 ----------

    private async Task ShakeCapChecks()
    {
        await Clean(800f);
        float cap = _camera.Settings.MaxShakeOffset;
        for (int i = 0; i < 10; i++) Events.Instance.EmitSignal(Events.SignalName.CameraShakeRequested, 24f, 0.4f);
        Events.Instance.EmitSignal(Events.SignalName.CameraShakeRequested, Player.Rules.SlamShakeStrength, Player.Rules.SlamShakeDuration);
        // 单次就比上限大的请求（数据调错时）：也要被截断
        Events.Instance.EmitSignal(Events.SignalName.CameraShakeRequested, 60f, 0.4f);
        float max = 0f;
        bool moved = false;
        for (int i = 0; i < 40; i++)
        {
            await ProcessFrames(1);
            max = Mathf.Max(max, _camera.Offset.Length());
            moved |= _camera.Offset != Vector2.Zero;
        }
        await ProcessFrames(10);
        Check(moved && max <= cap + 0.001f && _camera.Offset == Vector2.Zero && !_camera.IsShaking,
            $"震屏叠加 11 次、外加一次 60 像素的请求：镜头偏移始终不超过 {cap} 像素（最大 {max:0.0}），结束后回到 0");
    }

    // ---------- ⑤ 击退 ----------

    private async Task<(EnemyBody enemy, float knock)> HitOnce(string kind, System.Action<EnemyBody> configure)
    {
        await Clean(800f);
        EnemyBody enemy = Spawn(kind, 870f, FloorY, configure);
        await Frames(4);
        enemy.Stats.MaxHealth = 999; enemy.Stats.Refill();
        await AttackUntilHitStop();
        await ProcessFrames(1);
        float knock = enemy.KnockbackVelocity;
        await FramesUntilTimeResumes();
        return (enemy, knock);
    }

    private async Task ReactionChecks()
    {
        HitFeel w = GD.Load<HitFeel>(P + "feel_water.tres");

        var (flee, k1) = await HitOnce("mason", e => e.Data.FleeChance = 1f);
        await Frames(2);
        Check(flee.LastReaction == "flee" && flee.State is FleeState && FreshKnock(k1, w), $"逃跑反应：照常逃跑，同时被推开（{k1:0}）");
        await Frames(60);
        Check(flee.KnockbackVelocity == 0f && flee.State is FleeState && Mathf.Abs(flee.Velocity.X) > 1f, "击退结束后逃跑照常移动，速度是状态自己的");

        var (counter, k2) = await HitOnce("prayer", e => e.Data.FleeChance = 0f);
        Check(counter.LastReaction == "counter" && counter.State is AttackWindupState or AttackActiveState or AttackRecoverState && FreshKnock(k2, w),
            $"被动反击（祈祷泥人）：照常起身反击，同时被推开（{k2:0}，{counter.LastReaction} {counter.State?.Name}）");

        var (hurt, k3) = await HitOnce("mason", e => e.Data.FleeChance = 0f);
        Check(hurt.LastReaction == "hurt" && hurt.State is HurtState && FreshKnock(k3, w), $"攻击型：照常受击硬直，同时被推开（{k3:0}）");

        var (none, k4) = await HitOnce("prayer", e => { e.Data.FleeChance = 0f; e.Data.AttackSpec = null; });
        Check(none.LastReaction == "none" && none.State is IdleState && FreshKnock(k4, w), $"被动且没配攻击：照常不理会，同时被推开（{k4:0}，{none.LastReaction} {none.State?.Name}）");
    }

    private async Task RepeatHitChecks()
    {
        await Clean(800f, 1);
        float force = Player.Forms.Current.AttackSpec.Feel.Knockback;
        EnemyBody enemy = Spawn("mason", 870f);
        await Frames(4);
        enemy.Stats.MaxHealth = 999; enemy.Stats.Refill();
        var hit = new Hit { AttackId = "r1", Damage = 1f, SourcePosition = enemy.GlobalPosition - new Vector2(50f, 0f), Feel = Player.Forms.Current.AttackSpec.Feel, Source = Player };
        enemy.HurtBox.Receive(hit);
        await Frames(3);
        float afterFirst = enemy.KnockbackVelocity;
        var back = new Hit { AttackId = "r2", Damage = 1f, SourcePosition = enemy.GlobalPosition + new Vector2(50f, 0f), Feel = hit.Feel, Source = Player };
        enemy.HurtBox.Receive(back);
        float afterSecond = enemy.KnockbackVelocity;
        Check(afterFirst > 0f && afterFirst < force && Near(afterSecond, -force, 0.5f),
            $"重复受击：第二下替换第一下（方向换成新的、速度是单次的 {force}，不叠加；第一下剩 {afterFirst:0}，第二下后 {afterSecond:0}）");
        Service.CancelAll();
    }

    private async Task LedgeAndWallChecks()
    {
        // 悬崖：高台右边缘
        await Clean(2500f);
        Solid(new Rect2(2600f, 400f, 300f, 20f));
        float force = GD.Load<HitFeel>(P + "feel_mud.tres").Knockback;
        EnemyBody enemy = Spawn("mason", 2860f, 400f);
        await Frames(10);
        enemy.Stats.MaxHealth = 999; enemy.Stats.Refill();
        float y = enemy.GlobalPosition.Y;
        enemy.HurtBox.Receive(new Hit { AttackId = "ledge", Damage = 1f, SourcePosition = enemy.GlobalPosition - new Vector2(60f, 0f), Feel = GD.Load<HitFeel>(P + "feel_mud.tres"), Source = Player });
        await Frames(60);
        Check(enemy.IsOnFloor() && Near(enemy.GlobalPosition.Y, y, 1f) && enemy.GlobalPosition.X <= 2900f && enemy.KnockbackVelocity == 0f,
            $"悬崖边：被推到高台边缘停住，没有掉下去（x {enemy.GlobalPosition.X:0}，边缘 2900，击退 {force}）");

        // 墙：左边的墙（x = -400）
        await Clean(-300f);
        EnemyBody wall = Spawn("mason", -370f);
        await Frames(10);
        wall.Stats.MaxHealth = 999; wall.Stats.Refill();
        wall.HurtBox.Receive(new Hit { AttackId = "wall", Damage = 1f, SourcePosition = wall.GlobalPosition + new Vector2(60f, 0f), Feel = GD.Load<HitFeel>(P + "feel_mud.tres"), Source = Player });
        await Frames(4);
        Check(wall.KnockbackVelocity == 0f && wall.GlobalPosition.X >= -400f, $"贴墙：被推到墙上就清零（x {wall.GlobalPosition.X:0}）");
    }

    private async Task BossChecks()
    {
        await Clean(800f, 1);
        var keeper = (KeeperBoss)Spawn("keeper", 900f);
        await Frames(6);
        keeper.Stats.MaxHealth = 9999; keeper.Stats.Refill();
        float x = keeper.GlobalPosition.X;
        keeper.HurtBox.Receive(new Hit { AttackId = "boss", Damage = 1f, SourcePosition = keeper.GlobalPosition - new Vector2(80f, 0f), Feel = Player.Forms.Current.AttackSpec.Feel, Source = Player });
        await Frames(20);
        Check(keeper.KnockbackVelocity == 0f && Near(keeper.GlobalPosition.X, x, 0.5f) && keeper.HitFlashRemaining >= 0f, "守坑人：被打中不被推开，位置不变");
    }

    private async Task FlashChecks()
    {
        await Clean(800f);
        HitFeel feel = Player.Forms.Current.AttackSpec.Feel;
        EnemyBody mason = Spawn("mason", 870f, FloorY, e => e.Data.FleeChance = 0f); // 没有美术图：色块
        // 有图：用测试图（祈祷泥人有待机和反击前摇的图），被打中后进反击前摇，仍在播图
        EnemyBody devout = Spawn("prayer", 1000f, FloorY, e => e.Data.FleeChance = 0f);
        await Frames(4);
        devout.Animator.UseArtDirectory("res://tests/enemy/fixtures/art");
        mason.Stats.MaxHealth = devout.Stats.MaxHealth = 999; mason.Stats.Refill(); devout.Stats.Refill();
        foreach (EnemyBody e in new[] { mason, devout })
            e.HurtBox.Receive(new Hit { AttackId = "flash" + e.Name, Damage = 1f, SourcePosition = e.GlobalPosition - new Vector2(50f, 0f), Feel = feel, Source = Player });
        await ProcessFrames(1);
        bool placeholder = mason.DrawsPlaceholder && mason.FlashAmount == 1f;
        bool art = !devout.DrawsPlaceholder && (float)devout.Animator.FlashMaterial.GetShaderParameter("flash") == 1f;
        await ProcessFrames(Mathf.CeilToInt(feel.FlashSeconds * 60f) + 2);
        Check(placeholder && art && mason.FlashAmount == 0f && (float)devout.Animator.FlashMaterial.GetShaderParameter("flash") == 0f,
            $"闪白：色块敌人整块画白，有图的敌人用着色器闪白，{feel.FlashSeconds} 秒后都回到 0（色块 {placeholder} 有图 {art} devout {devout.State?.Name} art {devout.Animator.CurrentIsArt} 之后 {mason.FlashAmount}/{devout.FlashAmount}）");

        var keeper = (KeeperBoss)Spawn("keeper", 1400f);
        await Frames(4);
        keeper.FlashRemaining = 0.05f; // 变身闪白的「不白」半拍
        bool stateOff = keeper.FlashAmount == 0f;
        keeper.HurtBox.Receive(new Hit { AttackId = "kf", Damage = 1f, SourcePosition = keeper.GlobalPosition - new Vector2(80f, 0f), Feel = feel, Source = Player });
        Check(stateOff && keeper.FlashAmount == 1f, "守坑人：变身闪白和被打中的闪白取较大的");
        keeper.FlashRemaining = 0f;
        Service.CancelAll();
    }

    // ---------- ⑥ 只有 FeedbackService 写 TimeScale ----------

    private void ScanChecks()
    {
        var offenders = new List<string>();
        Scan("res://assets/scripts", offenders);
        Check(offenders.Count == 0, "全工程只有 FeedbackService（经 Juicee）写 Engine.TimeScale" + (offenders.Count > 0 ? "：" + string.Join(", ", offenders) : ""));
    }

    private static void Scan(string dir, List<string> offenders)
    {
        foreach (string f in DirAccess.GetFilesAt(dir))
        {
            if (!f.EndsWith(".cs") || f == "FeedbackService.cs") continue;
            string text = FileAccess.GetFileAsString(dir + "/" + f);
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"Engine\.TimeScale\s*=[^=]") || text.Contains("\"time_scale\"")) offenders.Add(f);
        }
        foreach (string d in DirAccess.GetDirectoriesAt(dir)) Scan(dir + "/" + d, offenders);
    }
}
