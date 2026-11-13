using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Enemy.Bosses.Keeper;
using SonOfMud.Enemy.Bosses.Keeper.States;
using SonOfMud.Enemy.States;

/// <summary>
/// 敌人美术图按命名自动载入（EnemyAnimator、EnemySprite）：
/// ① 正式美术：三张改名后的待机图被祈祷泥人、虔诚泥人、塔庙泥卫载入，塔庙泥匠和守坑人没有图仍是色块；显示节点只有一个，就是场景里的 Sprite；
/// ② 显示节点：场景里没有 Sprite 时兜底新建（只有一个）；手动填的 SpriteFrames 原样使用、不被替换；
/// ③ 测试图（tests/enemy/fixtures/art）：命名查找、帧数、缩放到 ArtCanvas、脚底对齐、翻转、按动作时长拉伸、缺图是色块、死亡停在最后一帧、
///    有图时不画占位（色块、攻击红框、蝎尾预警），状态颜色染在图上，变身闪白；
/// ④ 实际播放时长：手动做的 6 FPS、帧时长 1/2/1 的动画按目标时长播完（数帧）；
/// ⑤ 编辑器显示：EnemySprite 显示 idle 图，保存前还原（自动帧不存进场景），手动帧照常保存、重开还在。
/// </summary>
public partial class EnemyArtTest : LabSuiteBase
{
    private const string Fixture = "res://tests/enemy/fixtures/art";
    private const string NoArt = "res://tests/enemy/fixtures/no_art_here";
    private static readonly string[] Kinds = { "prayer", "devout", "mason", "temple_guard", "keeper" };

    protected override string ReportName => "enemy_art_results";

    private Node2D _stage;

    protected override async Task RunChecks()
    {
        _stage = new Node2D { Name = "Stage" };
        AddChild(_stage);
        TableChecks();
        await RepoArtChecks();
        await SpriteNodeChecks();
        await FixtureChecks();
        await GridChecks();
        await DevoutArtChecks();
        await ProjectileArtChecks();
        await KeeperChecks();
        await PlaybackChecks();
        EditorPreviewChecks();
    }

    private static PackedScene SceneOf(string kind) => GD.Load<PackedScene>($"res://assets/scenes/enemy/{kind}.tscn");

    /// <summary>实例化一个敌人放到场上，关掉它的物理（状态只由测试切换，不会自己往下走）。</summary>
    private async Task<EnemyBody> Spawn(string kind, System.Action<EnemyBody> beforeAdd = null)
    {
        var enemy = SceneOf(kind).Instantiate<EnemyBody>();
        enemy.Position = new Vector2(400, 300);
        beforeAdd?.Invoke(enemy);
        _stage.AddChild(enemy);
        enemy.SetPhysicsProcess(false);
        await ProcessFrames(1);
        return enemy;
    }

    private async Task ProcessFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Despawn(Node node)
    {
        node.QueueFree();
        await ProcessFrames(2);
    }

    private static List<AnimatedSprite2D> SpritesOf(Node enemy) => enemy.GetChildren().OfType<AnimatedSprite2D>().ToList();

    /// <summary>帧底边在敌人本地坐标里的 y（应当是 0，也就是脚底）。</summary>
    private static float BottomOf(AnimatedSprite2D sprite, int frameHeight) =>
        sprite.Position.Y + (sprite.Offset.Y + frameHeight * 0.5f) * sprite.Scale.Y;

    private static SpriteFrames ManualFrames(string animation, float fps, params float[] durations)
    {
        var frames = new SpriteFrames();
        frames.AddAnimation(animation);
        frames.SetAnimationLoop(animation, false);
        frames.SetAnimationSpeed(animation, fps);
        var texture = GD.Load<Texture2D>(Fixture + "/spr_enemy_prayer_idle.png");
        foreach (float d in durations) frames.AddFrame(animation, texture, d);
        return frames;
    }

    // ---------- 动作表、各状态的动作名和时长 ----------

    private void TableChecks()
    {
        var loops = new[] { "idle", "patrol", "chase", "flee", "dormant", "approach", "tail_aim", "staggered" };
        var once = new[] { "attack_windup", "attack_active", "attack_recover", "hurt", "dead", "transform", "sweep_windup", "sweep_strike", "sweep_recover", "tail_strike", "tail_recover" };
        Check(EnemyAnimator.Actions.Count == 19 && loops.All(a => EnemyAnimator.Actions[a]) && once.All(a => !EnemyAnimator.Actions[a]),
            "动作表：19 个动作，待机 / 巡逻 / 追击 / 逃跑 / 等待 / 逼近 / 蝎尾瞄准 / 僵直循环，其余不循环");

        var prayer = GD.Load<EnemyData>("res://assets/data/enemy/prayer.tres");
        var keeper = GD.Load<KeeperData>("res://assets/data/enemy/keeper/KeeperData.tres");
        Check(prayer.ArtCanvas == 128f && GD.Load<EnemyData>("res://assets/data/enemy/temple_guard.tres").ArtCanvas == 128f && keeper.ArtCanvas == 256f,
            $"显示尺寸：普通敌人一帧 128（源图标准 512），守坑人 256（源图标准 1024）（祈祷泥人 {prayer.ArtCanvas}，守坑人 {keeper.ArtCanvas}）");
    }

    private void StateTimingChecks(EnemyBody prayer, KeeperBoss keeper)
    {
        EnemyData p = prayer.Data;
        KeeperData k = keeper.Keeper;
        T Of<T>(Node body) where T : Node => body.GetNode<StateMachine>("StateMachine").GetChildren().OfType<T>().First();
        var normal = new (EnemyState state, string name, float seconds)[]
        {
            (Of<IdleState>(prayer), "idle", 0f), (Of<PatrolState>(prayer), "patrol", 0f), (Of<ChaseState>(prayer), "chase", 0f),
            (Of<AttackWindupState>(prayer), "attack_windup", p.AttackWindup), (Of<AttackActiveState>(prayer), "attack_active", p.AttackActive),
            (Of<AttackRecoverState>(prayer), "attack_recover", p.AttackRecover), (Of<HurtState>(prayer), "hurt", p.HurtStun),
            (Of<FleeState>(prayer), "flee", 0f), (Of<DeadState>(prayer), "dead", 0f),
        };
        var boss = new (EnemyState state, string name, float seconds)[]
        {
            (Of<DormantState>(keeper), "dormant", 0f), (Of<ApproachState>(keeper), "approach", 0f),
            (Of<SweepWindupState>(keeper), "sweep_windup", k.SweepWindup), (Of<SweepStrikeState>(keeper), "sweep_strike", k.SweepActive),
            (Of<SweepRecoverState>(keeper), "sweep_recover", k.SweepRecover), (Of<TailAimState>(keeper), "tail_aim", 0f),
            (Of<TailStrikeState>(keeper), "tail_strike", k.TailDelay + k.TailActive), (Of<TailRecoverState>(keeper), "tail_recover", k.TailRecover),
            (Of<StaggeredState>(keeper), "staggered", 0f), (Of<TransformState>(keeper), "transform", k.TransformFlash), (Of<GoneState>(keeper), "", 0f),
        };
        var wrong = normal.Concat(boss).Where(e => e.state.AnimName != e.name || !Mathf.IsEqualApprox(e.state.AnimDuration, e.seconds))
            .Select(e => $"{e.state.Name}={e.state.AnimName}/{e.state.AnimDuration}").ToList();
        Check(wrong.Count == 0, "各状态的动作名和时长：前摇 / 判定 / 收招 / 受击 / 横扫 / 蝎尾 / 变身按数据里的时长，循环动作和死亡不拉伸，已击败不播"
            + (wrong.Count > 0 ? "：" + string.Join(", ", wrong) : ""));
    }

    // ---------- ① 正式美术 ----------

    private async Task RepoArtChecks()
    {
        var expectArt = new Dictionary<string, bool> { ["prayer"] = true, ["devout"] = true, ["temple_guard"] = true, ["mason"] = false, ["keeper"] = false };
        foreach (string kind in Kinds)
        {
            EnemyBody enemy = await Spawn(kind);
            EnemyAnimator anim = enemy.Animator;
            List<AnimatedSprite2D> sprites = SpritesOf(enemy);
            bool reused = sprites.Count == 1 && sprites[0] is EnemySprite && sprites[0] == anim?.Sprite && sprites[0].Name == "Sprite";
            Check(anim != null && reused && !anim.Manual, $"{kind}：只有一个显示节点，就是场景里的 Sprite（EnemySprite），按命名自动生成帧（{sprites.Count} 个）");
            bool art = expectArt[kind];
            if (art)
            {
                int h = EnemyAnimator.FrameHeight(anim.Sprite.SpriteFrames, "idle");
                float shown = h * anim.Sprite.Scale.Y;
                Check(anim.CurrentAnimation == "idle" && anim.CurrentIsArt && anim.Sprite.Visible && !enemy.DrawsPlaceholder
                      && Mathf.IsEqualApprox(shown, 128f) && Mathf.Abs(BottomOf(anim.Sprite, h)) < 0.01f,
                    $"{kind}：仓库里的 spr_enemy_{kind}_idle.png 被载入，待机显示美术图（源图帧高 {h}，游戏里 {shown:F1}），帧底边在脚底，本体不画色块");
            }
            else
            {
                Check(!anim.CurrentIsArt && !anim.Sprite.Visible && enemy.DrawsPlaceholder && anim.Sprite.SpriteFrames == null,
                    $"{kind}：仓库里没有它的图，Sprite 隐藏，本体照旧画色块");
            }
            await Despawn(enemy);
        }
        string prayerPath = EnemyAnimator.DefaultArtDirectory + "/spr_enemy_prayer_idle.png";
        string devoutPath = EnemyAnimator.DefaultArtDirectory + "/spr_enemy_devout_idle.png";
        string guardPath = EnemyAnimator.DefaultArtDirectory + "/spr_enemy_temple_guard_idle.png";
        Check(EnemyAnimator.WarnedSizes.Contains(prayerPath) && EnemyAnimator.WarnedSizes.Contains(devoutPath) && !EnemyAnimator.WarnedSizes.Contains(guardPath),
            "尺寸警告：祈祷泥人 373×485 比一格小、虔诚泥人 2219×2564 不是 512 的整数倍，都给了警告（照样显示）；塔庙泥卫 512×512 没有警告");
    }

    // ---------- ② 显示节点 ----------

    private async Task SpriteNodeChecks()
    {
        EnemyBody bare = await Spawn("prayer", e =>
        {
            Node sprite = e.GetNode("Sprite");
            e.RemoveChild(sprite);
            sprite.Free();
        });
        List<AnimatedSprite2D> sprites = SpritesOf(bare);
        Check(sprites.Count == 1 && sprites[0].Name == "Sprite" && sprites[0] is not EnemySprite && sprites[0] == bare.Animator.Sprite && bare.Animator.CurrentIsArt,
            $"场景里没有 Sprite：兜底新建一个（只有 {sprites.Count} 个），照样显示待机图");
        await Despawn(bare);

        SpriteFrames manual = ManualFrames("idle", 6f, 1f, 2f, 1f);
        EnemyBody custom = await Spawn("prayer", e => e.GetNode<AnimatedSprite2D>("Sprite").SpriteFrames = manual);
        EnemyAnimator anim = custom.Animator;
        anim.UseArtDirectory(Fixture);
        await ProcessFrames(1);
        Check(anim.Manual && anim.Sprite.SpriteFrames == manual && anim.Generated == null && SpritesOf(custom).Count == 1 && anim.CurrentIsArt,
            "手动填了 SpriteFrames：原样使用，不被按命名生成的替换（换美术文件夹也不替换），显示节点仍只有一个");
        await Despawn(custom);
    }

    // ---------- ③ 测试图：普通敌人 ----------

    private async Task FixtureChecks()
    {
        EnemyBody enemy = await Spawn("prayer");
        EnemyAnimator anim = enemy.Animator;
        AnimatedSprite2D sprite = anim.Sprite;
        anim.UseArtDirectory(Fixture);
        await ProcessFrames(1);
        SpriteFrames frames = sprite.SpriteFrames;
        EnemyData data = enemy.Data;

        Check(frames.GetFrameCount("idle") == 2 && frames.GetFrameCount("attack_windup") == 3 && frames.GetFrameCount("dead") == 2 && frames.GetFrameCount("hurt") == 1
              && !frames.HasAnimation("attack_active") && !frames.HasAnimation("patrol"),
            "命名查找：有图的动作才有动画；待机先找美术名 pray（1024×512 → 2 帧，优先于 idle 的图），死亡美术名 death 没有图就退回 dead（→ 2 帧）；前摇 1536×512 → 3；比一格小的 300×400 → 整张 1 帧");
        Check(frames.GetAnimationLoop("idle") && !frames.GetAnimationLoop("attack_windup") && Mathf.IsEqualApprox((float)frames.GetAnimationSpeed("attack_windup"), 12f),
            "循环和速度：待机循环，前摇不循环，12 帧/秒");

        // 待机：缩放到 128、脚底对齐
        Check(anim.CurrentAnimation == "idle" && anim.CurrentIsArt && Mathf.IsEqualApprox(512f * sprite.Scale.Y, 128f) && Mathf.Abs(BottomOf(sprite, 512)) < 0.01f
              && sprite.Offset.X == 0f && sprite.Modulate == Colors.White && !enemy.DrawsPlaceholder,
            $"待机：512 的图显示为 128 高，帧底边在脚底（{BottomOf(sprite, 512):F2}），不染色，本体不画色块");

        // 翻转：敌人的图画朝左（主角相反）
        enemy.Facing = 1f;
        await ProcessFrames(1);
        bool right = sprite.FlipH;
        enemy.Facing = -1f;
        await ProcessFrames(1);
        Check(right && !sprite.FlipH, "敌人的图画朝左：朝右时水平翻转，朝左时不翻");

        // 前摇：3 帧，拉伸到 AttackWindup；前摇偏黄染在图上；本体不画
        enemy.Machine.TransitionTo<AttackWindupState>();
        await ProcessFrames(1);
        float expected = 3f / (12f * data.AttackWindup);
        Color yellow = Colors.White.Lerp(new Color(1f, 0.85f, 0.3f), 0.5f);
        Check(anim.CurrentAnimation == "attack_windup" && anim.CurrentIsArt && Mathf.Abs(anim.CurrentSpeedScale - expected) < 0.0001f,
            $"前摇：3 帧整段拉伸到 {data.AttackWindup} 秒，速度倍率 {expected:F3}（实际 {anim.CurrentSpeedScale:F3}）");
        Check(sprite.Modulate.IsEqualApprox(yellow) && !enemy.DrawsPlaceholder, $"前摇偏黄染在图上（{sprite.Modulate}），本体不画色块");

        // 判定：没有图 → 色块，攻击红框照旧画
        enemy.Machine.TransitionTo<AttackActiveState>();
        await ProcessFrames(1);
        Check(!anim.CurrentIsArt && !sprite.Visible && enemy.DrawsPlaceholder && enemy.AttackHitBox.Active,
            "攻击判定：这个动作没有图 → Sprite 隐藏，本体画色块和攻击红框（判定开着）");

        // 有图时打开判定：不画红框（借前摇的图，手动打开判定）
        enemy.Machine.TransitionTo<AttackWindupState>();
        enemy.OpenAttack(enemy.AttackHitBox, data.AttackSpec, "test");
        await ProcessFrames(1);
        Check(enemy.AttackHitBox.Active && anim.CurrentIsArt && !enemy.DrawsPlaceholder, "当前动作有图时判定开着也不画攻击红框（本体什么都不画）");
        enemy.CloseAttacks();

        // 受击：竖长单张图 300×400，非标准尺寸 → 缩放到 128，脚底对齐，按 HurtStun 拉伸
        enemy.Machine.TransitionTo<HurtState>();
        await ProcessFrames(1);
        float hurtScale = 1f / (12f * data.HurtStun);
        Check(anim.CurrentAnimation == "hurt" && anim.CurrentIsArt && Mathf.IsEqualApprox(400f * sprite.Scale.Y, 128f) && Mathf.Abs(BottomOf(sprite, 400)) < 0.01f
              && Mathf.Abs(anim.CurrentSpeedScale - hurtScale) < 0.0001f && EnemyAnimator.WarnedSizes.Contains(Fixture + "/spr_enemy_prayer_hurt.png"),
            $"受击：竖长单张图也缩放到 128 高、脚底对齐，按 {data.HurtStun} 秒拉伸；不是标准尺寸，给了警告");

        // 死亡：不拉伸，播完停在最后一帧；有图时不画红泥
        enemy.Machine.TransitionTo<DeadState>();
        await ProcessFrames(15);
        Check(anim.CurrentAnimation == "dead" && anim.CurrentIsArt && anim.CurrentSpeedScale == 1f && sprite.Frame == 1 && !sprite.IsPlaying() && !enemy.DrawsPlaceholder,
            $"死亡：12 帧/秒播完 2 帧后停在最后一帧（第 {sprite.Frame} 帧，在播 {sprite.IsPlaying()}），有图时不画红泥");

        // 再次进入同一个不循环的动作：从第 1 帧重新播
        enemy.Machine.TransitionTo<AttackWindupState>();
        await ProcessFrames(15);
        enemy.Machine.TransitionTo<AttackRecoverState>();
        enemy.Machine.TransitionTo<AttackWindupState>();
        Check(sprite.Frame == 0 && sprite.IsPlaying(), "再次进入前摇：从第 1 帧重新播");

        // 没有任何图的文件夹：全部色块，死亡画红泥
        anim.UseArtDirectory(NoArt);
        enemy.Machine.TransitionTo<DeadState>();
        await ProcessFrames(1);
        Check(sprite.SpriteFrames == null && !anim.CurrentIsArt && !sprite.Visible && enemy.DrawsPlaceholder,
            "没有图的文件夹：全部是色块，死亡画红泥（和接入美术前一样）");
        await Despawn(enemy);
    }

    // ---------- ③ 严格按 512 格子切：网格、空格子、不是整数倍；美术名对照表 ----------

    private async Task GridChecks()
    {
        // 纯切图：3 列 × 2 行、最后一格空 → 5 帧按行排；1300×600 → 2 列 × 1 行，丢掉多出来的部分并警告
        var walk = GD.Load<Texture2D>(Fixture + "/spr_enemy_mason_walk.png");
        Rect2[] cells = SpriteStrip.GridRegions(walk, 512, "测试");
        var expected = new[] { new Vector2(0, 0), new Vector2(512, 0), new Vector2(1024, 0), new Vector2(0, 512), new Vector2(512, 512) };
        Check(cells.Length == 5 && cells.Select(c => c.Position).SequenceEqual(expected) && cells.All(c => c.Size == new Vector2(512, 512)),
            $"网格切图：1536×1024 按 512 切成 3 列 × 2 行，最后一格整格透明跳过 → 5 帧，从左到右、从上到下（{string.Join(" ", cells.Select(c => c.Position))}）");
        var odd = GD.Load<Texture2D>(Fixture + "/spr_enemy_mason_attack.png");
        Rect2[] oddCells = SpriteStrip.GridRegions(odd, 512, "测试");
        Check(oddCells.Length == 2 && oddCells[1].Position == new Vector2(512, 0) && oddCells.All(c => c.Size == new Vector2(512, 512)),
            "网格切图：1300×600 不是 512 的整数倍 → 严格切成 2 列 × 1 行，每格仍是 512×512，右边 / 下边多出来的不用");

        // 泥匠：走路用美术名 walk（巡逻和追击同一张），攻击生效用 attack
        EnemyBody mason = await Spawn("mason");
        mason.Animator.UseArtDirectory(Fixture);
        await ProcessFrames(1);
        SpriteFrames frames = mason.Animator.Sprite.SpriteFrames;
        bool shared = frames != null && frames.GetFrameCount("patrol") == 5 && frames.GetFrameCount("chase") == 5
                      && ((AtlasTexture)frames.GetFrameTexture("patrol", 0)).Atlas == walk && ((AtlasTexture)frames.GetFrameTexture("chase", 0)).Atlas == walk;
        Check(shared && frames.GetFrameCount("attack_active") == 2 && EnemyAnimator.WarnedSizes.Contains(odd.ResourcePath),
            "美术名对照表：泥匠巡逻、追击都找 spr_enemy_mason_walk.png（5 帧），攻击生效找 spr_enemy_mason_attack.png（2 帧，尺寸不整给了警告）");

        mason.Machine.TransitionTo<PatrolState>();
        await ProcessFrames(1);
        AnimatedSprite2D sprite = mason.Animator.Sprite;
        Check(mason.Animator.CurrentAnimation == "patrol" && mason.Animator.CurrentIsArt && Mathf.IsEqualApprox(512f * sprite.Scale.Y, 128f) && Mathf.Abs(BottomOf(sprite, 512)) < 0.01f,
            "网格图的每一格同样缩放到 128、格子底边在脚底");
        await Despawn(mason);

        Check(EnemyAnimator.ArtFileNames("prayer", "idle", new Dictionary<string, string> { ["idle"] = "pray" }).SequenceEqual(new[] { "spr_enemy_prayer_pray.png", "spr_enemy_prayer_idle.png" })
              && EnemyAnimator.ArtFileNames("prayer", "hurt", null).SequenceEqual(new[] { "spr_enemy_prayer_hurt.png" }),
            "找图顺序：先美术名，再动作名；没有对照的动作只找动作名");
        var tables = new Dictionary<string, (string action, string art)[]>
        {
            ["prayer"] = new[] { ("idle", "pray"), ("attack_windup", "counter_windup"), ("attack_active", "counter"), ("attack_recover", "counter_recover"), ("dead", "death") },
            ["devout"] = new[] { ("patrol", "walk"), ("chase", "walk"), ("attack_windup", "throw_windup"), ("attack_active", "throw"), ("attack_recover", "throw_recover"), ("dead", "death") },
            ["temple_guard"] = new[] { ("patrol", "walk"), ("chase", "walk"), ("attack_windup", "charge_windup"), ("attack_active", "charge"), ("attack_recover", "charge_recover"), ("dead", "death") },
            ["mason"] = new[] { ("patrol", "walk"), ("chase", "walk"), ("attack_active", "attack"), ("dead", "death") },
        };
        var wrong = new List<string>();
        foreach (var (kind, pairs) in tables)
        {
            var data = GD.Load<EnemyData>($"res://assets/data/enemy/{kind}.tres");
            foreach (var (action, art) in pairs)
                if (!data.ArtNames.TryGetValue(action, out string got) || got != art) wrong.Add($"{kind}.{action}");
        }
        Check(wrong.Count == 0, "对照表：四种敌人的数据里按美术表填好了（祈祷泥人 pray / counter…，虔诚泥人 throw…，泥卫 charge…，泥匠 attack，走路 walk，死亡 death）"
            + (wrong.Count > 0 ? "：缺 " + string.Join(", ", wrong) : ""));
    }

    /// <summary>仓库里虔诚泥人的正式序列帧（美术交来的这一套，文件名已改对）：严格按 512 切出来的帧数。</summary>
    private async Task DevoutArtChecks()
    {
        EnemyBody devout = await Spawn("devout");
        SpriteFrames frames = devout.Animator.Sprite.SpriteFrames;
        var expected = new Dictionary<string, int>
        {
            ["idle"] = 17, ["patrol"] = 20, ["chase"] = 20, ["attack_windup"] = 30, ["attack_active"] = 2, ["attack_recover"] = 18, ["hurt"] = 11,
        };
        var got = expected.Keys.ToDictionary(a => a, a => frames != null && frames.HasAnimation(a) ? frames.GetFrameCount(a) : 0);
        Check(expected.All(e => got[e.Key] == e.Value) && frames != null && !frames.HasAnimation("dead"),
            "虔诚泥人正式图：严格按 512 切，空格子跳过 → " + string.Join("、", got.Select(g => $"{g.Key} {g.Value}"))
            + "（walk 用于巡逻和追击；还没有 death 图，死亡仍是色块）");
        devout.Machine.TransitionTo<HurtState>();
        await ProcessFrames(1);
        float scale = 11f / (12f * devout.Data.HurtStun);
        Check(devout.Animator.CurrentIsArt && Mathf.Abs(devout.Animator.CurrentSpeedScale - scale) < 0.0001f,
            $"虔诚泥人受伤：11 帧整段压进 {devout.Data.HurtStun} 秒（速度倍率 {devout.Animator.CurrentSpeedScale:F2}）");
        await Despawn(devout);
    }

    /// <summary>虔诚泥人扔出去的泥肝用美术的图；主角扔的泥肝不受影响。</summary>
    private async Task ProjectileArtChecks()
    {
        var devoutData = GD.Load<EnemyData>("res://assets/data/enemy/devout.tres");
        var playerSpec = GD.Load<SonOfMud.Combat.ProjectileSpec>("res://assets/data/combat/mud_liver_projectile.tres");
        Check(devoutData.ThrowSpec?.Texture?.ResourcePath == "res://assets/art/enemy/spr_enemy_devout_projectile.png" && playerSpec.Texture == null
              && devoutData.ThrowSpec != playerSpec && Mathf.IsEqualApprox(devoutData.ThrowSpec.Gravity, playerSpec.Gravity) && Mathf.IsEqualApprox(devoutData.ThrowSpec.Radius, playerSpec.Radius),
            "泥肝：虔诚泥人用自己的投掷物设置（带图 spr_enemy_devout_projectile.png，飞行参数和原来一样），主角的泥肝设置没有图、不受影响");

        Hit hit = Hit.From(devoutData.AttackSpec, "test_throw", new Vector2(100, 100), 1f, null);
        var flying = SonOfMud.Combat.Projectile.Launch(_stage, new Vector2(100, 100), new Vector2(300, -200), hit, SonOfMud.Combat.ProjectileTeam.Enemy, devoutData.ThrowSpec);
        var plain = SonOfMud.Combat.Projectile.Launch(_stage, new Vector2(300, 100), new Vector2(300, -200), hit, SonOfMud.Combat.ProjectileTeam.Enemy, playerSpec);
        await ProcessFrames(1);
        Check(flying.DrawsTexture && !plain.DrawsTexture, "泥肝：虔诚泥人扔出的泥肝飞行时画图，用主角设置扔出的仍画圆");
        flying.QueueFree();
        plain.QueueFree();
        await ProcessFrames(1);
    }

    // ---------- ③ 测试图：守坑人 ----------

    private async Task KeeperChecks()
    {
        EnemyBody prayer = await Spawn("prayer");
        var keeper = (KeeperBoss)await Spawn("keeper");
        StateTimingChecks(prayer, keeper);
        await Despawn(prayer);

        EnemyAnimator anim = keeper.Animator;
        AnimatedSprite2D sprite = anim.Sprite;
        anim.UseArtDirectory(Fixture);
        await ProcessFrames(1);
        Check(anim.CurrentAnimation == "dormant" && anim.CurrentIsArt && Mathf.IsEqualApprox(1024f * sprite.Scale.Y, 256f) && Mathf.Abs(BottomOf(sprite, 1024)) < 0.01f,
            "守坑人：1024 的图显示为 256 高，帧底边在脚底");

        // 蝎尾瞄准：有图 → 不画预警竖带
        keeper.Machine.TransitionTo<TailAimState>();
        await ProcessFrames(1);
        Check(keeper.Aiming && anim.CurrentAnimation == "tail_aim" && anim.CurrentIsArt && sprite.SpriteFrames.GetAnimationLoop("tail_aim") && !keeper.DrawsPlaceholder,
            "蝎尾瞄准：有图时循环播瞄准动作，不画预警竖带");

        // 横扫前摇：3 帧按 SweepWindup 拉伸，偏黄
        keeper.Machine.TransitionTo<SweepWindupState>();
        await ProcessFrames(1);
        float expected = 3f / (12f * keeper.Keeper.SweepWindup);
        Check(anim.CurrentAnimation == "sweep_windup" && Mathf.Abs(anim.CurrentSpeedScale - expected) < 0.0001f
              && sprite.Modulate.IsEqualApprox(Colors.White.Lerp(new Color(1f, 0.85f, 0.3f), 0.4f)),
            $"横扫前摇：按 {keeper.Keeper.SweepWindup} 秒拉伸（{anim.CurrentSpeedScale:F3}），偏黄染在图上");

        // 变身：闪白（每 0.2 秒里 0.1 秒白）
        keeper.Machine.TransitionTo<TransformState>();
        keeper.FlashRemaining = 0.15f;
        await ProcessFrames(1);
        float on = (float)anim.FlashMaterial.GetShaderParameter("flash");
        keeper.FlashRemaining = 0.05f;
        await ProcessFrames(1);
        float off = (float)anim.FlashMaterial.GetShaderParameter("flash");
        Check(anim.CurrentAnimation == "transform" && anim.CurrentIsArt && sprite.Material == anim.FlashMaterial && on == 1f && off == 0f,
            $"变身：图上闪白（白 {on}，不白 {off}）");

        // 换回仓库的图（守坑人没有图）：瞄准时预警竖带照旧画
        anim.UseArtDirectory(EnemyAnimator.DefaultArtDirectory);
        keeper.Machine.TransitionTo<TailAimState>();
        await ProcessFrames(1);
        Check(keeper.Aiming && !anim.CurrentIsArt && keeper.DrawsPlaceholder, "没有瞄准图时：色块和预警竖带照旧画");
        await Despawn(keeper);
    }

    // ---------- ④ 实际播放时长 ----------

    private async Task<int> FramesUntilFinished(AnimatedSprite2D sprite, int limit)
    {
        bool finished = false;
        void OnFinished() => finished = true;
        sprite.AnimationFinished += OnFinished;
        int count = 0;
        while (!finished && count < limit)
        {
            await ProcessFrames(1);
            count++;
        }
        sprite.AnimationFinished -= OnFinished;
        return count;
    }

    private async Task PlaybackChecks()
    {
        // 公式本身：各帧相对时长之和 ÷ (实际 FPS × 时长)
        SpriteFrames uneven = ManualFrames("a", 6f, 1f, 2f, 1f);
        float scale = SpriteStrip.SpeedScaleFor(uneven, "a", 0.5f);
        Check(Mathf.Abs(scale - 4f / 3f) < 0.0001f && SpriteStrip.SpeedScaleFor(uneven, "missing", 0.5f) == 1f && SpriteStrip.SpeedScaleFor(uneven, "a", 0f) == 1f,
            $"速度倍率 = 各帧相对时长之和 ÷ (实际 FPS × 时长)：6 FPS、1/2/1、0.5 秒 → {scale:F3}（4/3）；没有这个动画或时长为 0 → 1");

        // 直接播：0.5 秒 = 30 帧
        var sprite = new AnimatedSprite2D { SpriteFrames = uneven };
        _stage.AddChild(sprite);
        sprite.Play("a");
        sprite.SpeedScale = scale;
        int frames = await FramesUntilFinished(sprite, 120);
        Check(Mathf.Abs(frames - 30) <= 1, $"实际播放：6 FPS、帧时长 1/2/1 的手动动画按 0.5 秒播完（{frames} 帧，60 帧/秒下应为 30±1）");
        sprite.QueueFree();

        // 敌人手动帧：前摇按 AttackWindup 播完
        EnemyBody enemy = await Spawn("prayer", e => e.GetNode<AnimatedSprite2D>("Sprite").SpriteFrames = ManualFrames("attack_windup", 6f, 1f, 2f, 1f));
        enemy.Machine.TransitionTo<AttackWindupState>();
        int expectFrames = Mathf.RoundToInt(enemy.Data.AttackWindup * 60f);
        int windup = await FramesUntilFinished(enemy.Animator.Sprite, 300);
        Check(enemy.Animator.Manual && Mathf.Abs(windup - expectFrames) <= 1,
            $"敌人手动帧（6 FPS、1/2/1）：前摇动画按 {enemy.Data.AttackWindup} 秒播完（{windup} 帧，应为 {expectFrames}±1）");
        await Despawn(enemy);

        // 敌人自动帧（12 FPS 等长）：前摇 3 帧按 AttackWindup 播完
        EnemyBody auto = await Spawn("prayer");
        auto.Animator.UseArtDirectory(Fixture);
        auto.Machine.TransitionTo<AttackWindupState>();
        int autoFrames = await FramesUntilFinished(auto.Animator.Sprite, 300);
        Check(!auto.Animator.Manual && Mathf.Abs(autoFrames - expectFrames) <= 1,
            $"敌人自动帧（12 FPS、3 帧）：前摇动画按 {auto.Data.AttackWindup} 秒播完（{autoFrames} 帧，应为 {expectFrames}±1）");
        await Despawn(auto);
    }

    // ---------- ⑤ 编辑器显示和保存 ----------

    /// <summary>打包后场景里 Sprite 节点存了哪些属性。</summary>
    private static HashSet<string> StoredSpriteProperties(PackedScene packed)
    {
        SceneState state = packed.GetState();
        for (int i = 0; i < state.GetNodeCount(); i++)
        {
            if (state.GetNodeName(i) != "Sprite") continue;
            var names = new HashSet<string>();
            for (int j = 0; j < state.GetNodePropertyCount(i); j++) names.Add(state.GetNodePropertyName(i, j));
            return names;
        }
        return null;
    }

    private void EditorPreviewChecks()
    {
        // 自动帧：显示 idle，保存前还原，场景里不存帧和缩放
        var shown = new List<string>();
        foreach (string kind in Kinds)
        {
            Node2D enemy = SceneOf(kind).Instantiate<Node2D>();
            var sprite = enemy.GetNode<EnemySprite>("Sprite");
            sprite.ArtDirectory = Fixture;
            sprite.ShowPreview();
            float canvas = kind == "keeper" ? 256f : 128f;
            if (sprite.PreviewShown)
            {
                string preview = kind == "keeper" ? "dormant" : "idle";
                // 祈祷泥人的待机按美术名找到 pray（测试图 1024×512 → 2 帧）
                var art = GD.Load<Texture2D>(kind == "prayer" ? $"{Fixture}/spr_enemy_prayer_pray.png" : $"{Fixture}/spr_enemy_{kind}_{preview}.png");
                int h = EnemyAnimator.FrameHeight(sprite.SpriteFrames, sprite.Animation);
                Texture2D first = sprite.SpriteFrames.GetFrameTexture(sprite.Animation, 0);
                if (sprite.PreviewAction == preview && (first == art || (first as AtlasTexture)?.Atlas == art)
                    && Mathf.IsEqualApprox(h * sprite.Scale.Y, canvas) && Mathf.Abs(BottomOf(sprite, h)) < 0.01f) shown.Add(kind);
                else shown.Add(kind + "(尺寸不对)");
            }
            enemy.Free();
        }
        Check(shown.SequenceEqual(new[] { "prayer", "keeper" }),
            $"编辑器显示：测试图里祈祷泥人显示 idle、守坑人显示 dormant（256 高），尺寸和脚底对齐与游戏一致；没有图的不显示（{string.Join(", ", shown)}）");

        var repoShown = new List<string>();
        foreach (string kind in Kinds)
        {
            Node2D enemy = SceneOf(kind).Instantiate<Node2D>();
            var sprite = enemy.GetNode<EnemySprite>("Sprite");
            sprite.ShowPreview();
            if (sprite.PreviewShown) repoShown.Add(kind);
            enemy.Free();
        }
        Check(repoShown.SequenceEqual(new[] { "prayer", "devout", "temple_guard" }),
            $"编辑器显示（仓库的图）：祈祷泥人、虔诚泥人、塔庙泥卫显示 idle，泥匠和守坑人没有图不显示（{string.Join(", ", repoShown)}）");

        {
            // 朝向：默认朝左（InitialFacing = -1）不翻；初始朝右的翻过来
            Node2D left = SceneOf("prayer").Instantiate<Node2D>();
            var leftSprite = left.GetNode<EnemySprite>("Sprite");
            leftSprite.ArtDirectory = Fixture;
            leftSprite.ShowPreview();
            Check(leftSprite.PreviewShown && !leftSprite.FlipH, "编辑器显示：默认朝左的敌人不翻转（图本来就画朝左）");
            left.Free();
        }

        {
            Node2D enemy = SceneOf("prayer").Instantiate<Node2D>();
            enemy.Set("InitialFacing", 1f);
            var sprite = enemy.GetNode<EnemySprite>("Sprite");
            sprite.ArtDirectory = Fixture;
            sprite.ShowPreview();
            bool facedRight = sprite.FlipH;
            enemy.Set("InitialFacing", -1f);
            bool followsLeft = !sprite.PreviewFlip();
            enemy.Set("InitialFacing", 1f);
            Check(facedRight && followsLeft, "编辑器显示：初始朝右的敌人水平翻转；初始朝向改回朝左，算出来的就是不翻");
            bool generated = sprite.PreviewShown && sprite.PreviewFrames != null && sprite.SpriteFrames == sprite.PreviewFrames;
            sprite.Notification((int)Node.NotificationEditorPreSave);
            var packed = new PackedScene();
            Error error = packed.Pack(enemy);
            HashSet<string> stored = StoredSpriteProperties(packed);
            bool cleared = sprite.SpriteFrames == null && sprite.Scale == Vector2.One && sprite.Offset == Vector2.Zero && !sprite.FlipH && !sprite.PreviewShown;
            Check(generated && error == Error.Ok && cleared && stored != null && !stored.Contains("sprite_frames") && !stored.Contains("scale") && !stored.Contains("offset")
                  && !stored.Contains("animation") && !stored.Contains("flip_h"),
                $"保存前还原：自动生成的帧、缩放和翻转都拿掉，场景里 Sprite 只存了 {(stored == null ? "（没找到）" : string.Join(", ", stored))}");
            sprite.ShowPreview();
            Check(sprite.PreviewShown && sprite.SpriteFrames == sprite.PreviewFrames && sprite.FlipH, "保存后重新显示 idle，仍按初始朝右翻转");
            enemy.Free();
        }

        {
            // 手动帧：预览用手动帧，保存时不动，重开还在
            Node2D enemy = SceneOf("prayer").Instantiate<Node2D>();
            var sprite = enemy.GetNode<EnemySprite>("Sprite");
            SpriteFrames manual = ManualFrames("idle", 6f, 1f, 2f, 1f);
            sprite.SpriteFrames = manual;
            sprite.ArtDirectory = Fixture;
            sprite.ShowPreview();
            bool usedManual = sprite.PreviewShown && sprite.PreviewFrames == null && sprite.SpriteFrames == manual;
            sprite.Notification((int)Node.NotificationEditorPreSave);
            var packed = new PackedScene();
            packed.Pack(enemy);
            HashSet<string> stored = StoredSpriteProperties(packed);
            Node2D reopened = packed.Instantiate<Node2D>();
            var reSprite = reopened.GetNode<EnemySprite>("Sprite");
            SpriteFrames back = reSprite.SpriteFrames;
            Check(usedManual && sprite.SpriteFrames == manual && stored != null && stored.Contains("sprite_frames")
                  && back != null && back.GetFrameCount("idle") == 3 && Mathf.IsEqualApprox((float)back.GetAnimationSpeed("idle"), 6f)
                  && Mathf.IsEqualApprox(back.GetFrameDuration("idle", 1), 2f),
                "手动帧：预览用手动的 idle，保存前不清掉，存进场景，重新实例化后还是那份（3 帧、6 FPS、第 2 帧时长 2）");
            reopened.Free();
            enemy.Free();
        }
    }
}
