using System.Threading.Tasks;
using Godot;
using SonOfMud.Player;

/// <summary>
/// 主角美术图按命名自动载入（PlayerAnimator）：
/// ① 仓库里没有主角美术图时，全部是色块占位，照旧按形态着色；
/// ② 换到测试图文件夹 tests/player/fixtures/art：分形态的图优先，没有就用共用图，都没有就是占位；
///    帧数 = 宽 ÷ 高（单张图 1 帧，宽不是整数倍时向下取整）；
/// ③ 美术图缩放到 128、帧底边对齐身体碰撞框底边、不染色；占位色块照旧居中、着色；
/// ④ 朝左时水平翻转；有固定时长的动作按时长缩放播放速度。
/// </summary>
public partial class PlayerArtTest : LabSuiteBase
{
    private const string LabPath = "res://assets/scenes/tests/player_lab.tscn";
    private const string FixtureArt = "res://tests/player/fixtures/art";
    /// <summary>不存在的文件夹：里面一张图也没有，用来验证“全部是占位”。</summary>
    private const string NoArt = "res://tests/player/fixtures/no_art_here";
    private const float FloorY = 576f;

    protected override string ReportName => "player_art_results";

    protected override async Task RunChecks()
    {
        await LoadLab(LabPath);
        PlayerAnimator anim = Player.Animator;
        AnimatedSprite2D sprite = anim?.Sprite;
        Check(anim != null && sprite != null, "玩家身上有 PlayerAnimator 和 AnimatedSprite2D");
        if (anim == null || sprite == null) return;

        // ① 默认文件夹：仓库里有哪些主角美术图就用哪些（现在只有待机图 spr_player_idle.png），没有图的动作是占位
        await Reset(new Vector2(900, FloorY), 0);
        bool repoIdle = ResourceLoader.Exists(PlayerAnimator.DefaultArtDirectory + "/spr_player_idle.png");
        Check(anim.ArtDirectory == PlayerAnimator.DefaultArtDirectory && anim.IsArt("water_idle") == repoIdle && anim.IsArt("mud_idle") == repoIdle
              && !anim.IsArt("mud_attack") && anim.CurrentIsArt == repoIdle,
            $"默认：美术文件夹是 {PlayerAnimator.DefaultArtDirectory}，仓库里{(repoIdle ? "有" : "没有")}待机图，待机{(repoIdle ? "用美术图" : "是占位")}；攻击没有图，是占位");

        // 一张图也没有的文件夹：全部是占位
        anim.UseArtDirectory(NoArt);
        await Reset(new Vector2(900, FloorY), 0);
        Check(!anim.IsArt("water_idle") && !anim.IsArt("mud_attack") && !anim.CurrentIsArt, "没有图的文件夹：全部是色块占位");
        Check(anim.CurrentAnimation == "water_idle" && sprite.Modulate == Player.Forms.Current.Tint && sprite.Scale == Vector2.One && sprite.Position == Vector2.Zero,
            $"占位色块按形态着色、不缩放、居中（{anim.CurrentAnimation} {sprite.Modulate}）");

        // 帧数规则
        Check(PlayerAnimator.FrameCountFor(512, 512) == 1 && PlayerAnimator.FrameCountFor(192, 64) == 3 && PlayerAnimator.FrameCountFor(100, 40) == 2
              && PlayerAnimator.FrameCountFor(40, 100) == 1 && PlayerAnimator.FrameCountFor(0, 64) == 0,
            "帧数 = 宽 ÷ 高：512×512 → 1，192×64 → 3，100×40 → 2（向下取整），竖长单张 → 1，空图 → 0");

        // ② 换到测试图
        anim.UseArtDirectory(FixtureArt);
        await Frames(2);
        SpriteFrames frames = sprite.SpriteFrames;
        Check(anim.IsArt("water_idle") && anim.IsArt("mud_idle") && frames.GetFrameCount("water_idle") == 1 && frames.GetFrameCount("mud_idle") == 1,
            "共用图 spr_player_idle.png（单张 512×512）：水、泥待机都用它，1 帧");
        Check(anim.IsArt("water_attack") && frames.GetFrameCount("water_attack") == 3 && !anim.IsArt("mud_attack") && frames.GetFrameCount("mud_attack") == 7,
            $"分形态图 spr_player_water_attack.png（3 帧）只给水用；泥没有图，仍是 7 帧占位（{frames.GetFrameCount("mud_attack")}）");
        Check(anim.IsArt("water_run") && frames.GetFrameCount("water_run") == 4 && anim.IsArt("mud_run") && frames.GetFrameCount("mud_run") == 2,
            "跑：泥有专用图 spr_player_mud_run.png（2 帧）优先用；水没有专用图，用共用图 spr_player_run.png（4 帧）");
        Check(anim.IsArt("water_dodge") && frames.GetFrameCount("water_dodge") == 2 && frames.GetFrameTexture("water_dodge", 1) is AtlasTexture atlas && atlas.Region == new Rect2(40, 0, 40, 40),
            "宽不是高的整数倍（100×40）：切成 2 个 40×40 的正方形帧，右边多出的部分不用");
        Check(frames.GetAnimationLoop("water_idle") && !frames.GetAnimationLoop("water_attack") && Mathf.IsEqualApprox((float)frames.GetAnimationSpeed("water_idle"), 6f)
              && Mathf.IsEqualApprox((float)frames.GetAnimationSpeed("water_run"), 12f),
            "循环和速度：待机循环、6 帧/秒；跑 12 帧/秒；攻击不循环");
        Check(!anim.IsArt("water_hurt") && frames.GetFrameCount("water_hurt") == 2, "没有图的动作（受伤）仍是占位");

        // ③ 待机：美术图缩放到 128、脚底对齐、不染色
        await Reset(new Vector2(900, FloorY), 0);
        float shownHeight = 512f * sprite.Scale.Y;
        float bottom = sprite.Position.Y + (sprite.Offset.Y + 256f) * sprite.Scale.Y;
        Check(anim.CurrentAnimation == "water_idle" && anim.CurrentIsArt && sprite.Modulate == Colors.White,
            $"水待机用美术图，不染形态色（{anim.CurrentAnimation} {sprite.Modulate}）");
        Check(Mathf.IsEqualApprox(shownHeight, 128f) && Mathf.IsEqualApprox(bottom, anim.FootY) && Mathf.IsEqualApprox(anim.FootY, 24f) && sprite.Offset.X == 0f,
            $"512 的图在游戏里显示为 128 高，帧底边正好在身体碰撞框底边（底边 {bottom:F1}，脚底 {anim.FootY:F1}）");

        // ④ 朝向翻转
        Player.Facing = -1f;
        await Frames(1);
        bool flippedLeft = sprite.FlipH;
        Player.Facing = 1f;
        await Frames(1);
        Check(flippedLeft && !sprite.FlipH, "朝左时水平翻转，朝右时不翻");

        // 水攻击：美术图 3 帧，按 0.34 秒缩放速度
        await Tap(PlayerActions.Attack);
        await Frames(1);
        FormConfig water = Player.Forms.Current;
        float waterSeconds = water.AttackWindup + water.AttackActive + water.AttackRecovery;
        float expected = PlayerAnimator.SpeedScaleFor(3, 12f, waterSeconds);
        Check(anim.CurrentAnimation == "water_attack" && anim.CurrentIsArt && Mathf.Abs(anim.CurrentSpeedScale - expected) < 0.001f && sprite.Modulate == Colors.White,
            $"水攻击：美术图 3 帧，播放速度按 {waterSeconds:F2} 秒缩放 = {expected:F3}（实际 {anim.CurrentAnimation} {anim.CurrentSpeedScale:F3}）");

        // 泥攻击：没有图 → 占位，按泥着色、不缩放、居中
        await Reset(new Vector2(900, FloorY), 1);
        Check(anim.CurrentAnimation == "mud_idle" && anim.CurrentIsArt && sprite.Modulate == Colors.White, "泥待机：共用图，不染色");
        await Tap(PlayerActions.Attack);
        await Frames(1);
        Check(anim.CurrentAnimation == "mud_attack" && !anim.CurrentIsArt && sprite.Modulate == Player.Forms.Current.Tint && sprite.Scale == Vector2.One
              && sprite.Position == Vector2.Zero && sprite.Offset == Vector2.Zero,
            $"泥攻击：没有图，用占位色块，按泥着色、不缩放、居中（{anim.CurrentAnimation} {sprite.Modulate}）");

        // 换回默认文件夹：又按仓库里的图显示
        await Reset(new Vector2(900, FloorY), 0);
        anim.UseArtDirectory(PlayerAnimator.DefaultArtDirectory);
        await Frames(2);
        Check(anim.CurrentIsArt == repoIdle && sprite.Modulate == (repoIdle ? Colors.White : Player.Forms.Current.Tint) && !anim.IsArt("mud_attack"),
            $"换回默认文件夹：又按仓库里的图显示（待机{(repoIdle ? "用美术图" : "是占位")}，攻击是占位）");
    }
}
