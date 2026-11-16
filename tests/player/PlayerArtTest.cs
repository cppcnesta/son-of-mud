using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
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
    /// <summary>站在 y=600 的地面上时身体中心（原点）的 y。</summary>
    private static readonly float FloorY = 600f - PlayerBody.HalfH;

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
        // 仓库里有没有泥形态攻击图（spr_player_mud_attack[_列x行].png）；水形态攻击还没有图
        bool repoMudAttack = SpriteStrip.Find(PlayerAnimator.DefaultArtDirectory, "spr_player_mud_attack", "主角美术图", out _, out _) != null;
        Check(anim.ArtDirectory == PlayerAnimator.DefaultArtDirectory && anim.IsArt("water_idle") == repoIdle && anim.IsArt("mud_idle") == repoIdle
              && anim.IsArt("mud_attack") == repoMudAttack && anim.IsArt("water_attack") == repoMudAttack && anim.CurrentIsArt == repoIdle,
            $"默认：美术文件夹是 {PlayerAnimator.DefaultArtDirectory}，仓库里{(repoIdle ? "有" : "没有")}待机图，待机{(repoIdle ? "用美术图" : "是占位")}；泥攻击{(repoMudAttack ? "用美术图" : "是占位")}；水攻击没有自己的图，{(repoMudAttack ? "先用泥形态的图" : "是占位")}");

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
        Check(Mathf.IsEqualApprox(shownHeight, 128f) && Mathf.IsEqualApprox(bottom, anim.FootY) && Mathf.IsEqualApprox(anim.FootY, PlayerBody.HalfH) && sprite.Offset.X == 0f,
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
        Check(anim.CurrentIsArt == repoIdle && sprite.Modulate == (repoIdle ? Colors.White : Player.Forms.Current.Tint)
              && anim.IsArt("mud_attack") == repoMudAttack && anim.IsArt("water_attack") == repoMudAttack,
            $"换回默认文件夹：又按仓库里的图显示（待机{(repoIdle ? "用美术图" : "是占位")}，泥攻击{(repoMudAttack ? "用美术图" : "是占位")}，水攻击{(repoMudAttack ? "先用泥形态的图" : "是占位")}）");

        // 手动做的 SpriteFrames（6 FPS、帧时长 1/2/1）：速度倍率按实际 FPS 和各帧相对时长算，整段刚好播满攻击时长
        var manual = new SpriteFrames();
        manual.AddAnimation("water_attack");
        manual.SetAnimationLoop("water_attack", false);
        manual.SetAnimationSpeed("water_attack", 6f);
        Texture2D block = GD.Load<Texture2D>(FixtureArt + "/spr_player_idle.png");
        manual.AddFrame("water_attack", block, 1f);
        manual.AddFrame("water_attack", block, 2f);
        manual.AddFrame("water_attack", block, 1f);
        manual.AddAnimation("water_idle");
        manual.AddFrame("water_idle", block);
        sprite.SpriteFrames = manual;
        await Reset(new Vector2(900, FloorY), 0);
        await Tap(PlayerActions.Attack);
        await Frames(1);
        FormConfig form = Player.Forms.Current;
        float seconds = form.AttackWindup + form.AttackActive + form.AttackRecovery;
        float manualExpected = 4f / (6f * seconds);
        Check(anim.CurrentAnimation == "water_attack" && Mathf.Abs(anim.CurrentSpeedScale - manualExpected) < 0.001f,
            $"手动帧（6 FPS、1/2/1）：速度倍率 = 4 ÷ (6 × {seconds:F2}) = {manualExpected:F3}（实际 {anim.CurrentSpeedScale:F3}），不是按帧数 3 算的 {3f / (6f * seconds):F3}");
        await LayoutChecks(anim, sprite);
        anim.UseArtDirectory(PlayerAnimator.DefaultArtDirectory);
    }

    private const string ConflictArt = "res://tests/player/fixtures/conflict";

    /// <summary>新的切图规则：文件名写行列数、平均切、补边、固定比例、选图规则、导出版的文件名。</summary>
    private async Task LayoutChecks(PlayerAnimator anim, AnimatedSprite2D sprite)
    {
        // 文件名解析
        var a = SpriteStrip.ParseLayout("spr_enemy_devout_idle_6x5.png");
        var b = SpriteStrip.ParseLayout("spr_player_idle.png");
        var c = SpriteStrip.ParseLayout("spr_player_idle_0x1.png");
        Check(a?.baseName == "spr_enemy_devout_idle" && a?.cols == 6 && a?.rows == 5 && b?.baseName == "spr_player_idle" && b?.cols == 0
              && c?.baseName == "spr_player_idle_0x1" && c?.cols == 0 && SpriteStrip.ParseLayout("x.png.import") == null,
            "文件名：_6x5 解析成 6 列 5 行；不写行列数（或写成 0 列）按旧规则；不是 .png 的不算");

        // 选图：带行列数优先；多张带行列数的报冲突、按文件名排序取第一张，和目录列表顺序无关；导出版的 .import / .remap 名字也认
        var listing = new[] { "spr_player_idle_4x1.png", "spr_player_idle_2x2.png", "spr_player_idle.png", "spr_player_run.png" };
        var pick1 = SpriteStrip.Choose(listing, "spr_player_idle");
        var pick2 = SpriteStrip.Choose(listing.Reverse(), "spr_player_idle");
        Check(pick1?.file == "spr_player_idle_2x2.png" && pick2?.file == pick1?.file && pick1?.error == true && pick1?.message.Contains("spr_player_idle_4x1.png") == true,
            $"冲突：同时有 idle_4x1 和 idle_2x2 → 报错，按文件名排序用 {pick1?.file}；列表顺序反过来结果一样");
        var mixed = SpriteStrip.Choose(new[] { "spr_player_run.png", "spr_player_run_2x1.png" }, "spr_player_run");
        var plain = SpriteStrip.Choose(new[] { "spr_player_run.png" }, "spr_player_run");
        Check(mixed?.file == "spr_player_run_2x1.png" && mixed?.error == false && mixed?.message != null && plain?.file == "spr_player_run.png" && plain?.cols == 0 && plain?.message == null
              && SpriteStrip.Choose(new[] { "spr_player_runner.png" }, "spr_player_run") == null,
            "并存：带行列数的和不带的都有 → 用带的并给警告；只有不带的 → 按旧规则；名字只是开头相同的不算");
        var exported = SpriteStrip.Choose(new[] { "spr_player_idle_4x1.png.import", "spr_player_mud_run.png.remap", "readme.txt" }, "spr_player_idle");
        var exportedRun = SpriteStrip.Choose(new[] { "spr_player_mud_run.png.remap" }, "spr_player_mud_run");
        Check(exported?.file == "spr_player_idle_4x1.png" && exported?.cols == 4 && exportedRun?.file == "spr_player_mud_run.png",
            "导出版：目录里列出来的 xxx.png.import / xxx.png.remap 还原成原文件名再选图");

        // 格子边界：round(i × 宽 ÷ 列)，相邻格子连续、不重叠、不漏像素
        bool continuous = true;
        foreach (var (size, count) in new[] { (739, 2), (1025, 2), (2219, 6), (2564, 5), (3077, 6), (1801, 3) })
        {
            if (SpriteStrip.Edge(0, size, count) != 0 || SpriteStrip.Edge(count, size, count) != size) continuous = false;
            for (int i = 0; i < count; i++)
            {
                int width = SpriteStrip.Edge(i + 1, size, count) - SpriteStrip.Edge(i, size, count);
                if (width < size / count || width > size / count + 1) continuous = false;
            }
        }
        var sheet = GD.Load<Texture2D>(FixtureArt + "/spr_player_attack_down_2x2.png");
        Rect2I[] regions = SpriteStrip.EvenGridRegions(sheet, 2, 2);
        bool regionsOk = regions.Length == 3 && regions[0] == new Rect2I(0, 0, 370, 513) && regions[1] == new Rect2I(370, 0, 369, 513) && regions[2] == new Rect2I(0, 513, 370, 512);
        Check(continuous && regionsOk, $"平均切：边界取整后相邻格子连续不重叠；739×1025 按 2x2 → 3 格有内容（最后一格透明跳过）：{string.Join(" ", regions)}");

        // 测试图：大画布的蓄力跳 3x1（格子 600/601 × 641）、非整数格子的下劈 2x2
        anim.UseArtDirectory(FixtureArt);
        await Frames(2);
        SpriteFrames frames = sprite.SpriteFrames;
        bool windup = anim.IsArt("water_jump_windup") && anim.IsArt("mud_jump_windup") && frames.GetFrameCount("water_jump_windup") == 3
                      && Enumerable.Range(0, 3).All(i => frames.GetFrameTexture("water_jump_windup", i).GetWidth() == 601 && frames.GetFrameTexture("water_jump_windup", i).GetHeight() == 641);
        bool down = frames.GetFrameCount("water_attack_down") == 3
                    && Enumerable.Range(0, 3).All(i => frames.GetFrameTexture("water_attack_down", i).GetWidth() == 370 && frames.GetFrameTexture("water_attack_down", i).GetHeight() == 513);
        Check(windup && down, "测试图：蓄力跳 3x1 切 3 帧、补边成 601×641；下劈 2x2 切 3 帧、补边成 370×513（每帧一样大）");

        // 实际播放蓄力跳（泥形态 0.24 秒），逐帧检查：固定比例 1/4、每帧脚底都在身体碰撞框底边
        await Reset(new Vector2(900, FloorY), 1);
        await Tap(PlayerActions.Jump);
        var seen = new HashSet<int>();
        float worst = 0f;
        bool scaleOk = true;
        for (int i = 0; i < 30 && Player.State is SonOfMud.Player.States.JumpWindupState or SonOfMud.Player.States.IdleState; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (anim.CurrentAnimation != "mud_jump_windup") continue;
            Texture2D tex = sprite.SpriteFrames.GetFrameTexture(sprite.Animation, sprite.Frame);
            float bottom = sprite.Position.Y + (sprite.Offset.Y + tex.GetHeight() * 0.5f) * sprite.Scale.Y;
            worst = Mathf.Max(worst, Mathf.Abs(bottom - anim.FootY));
            scaleOk &= Mathf.IsEqualApprox(sprite.Scale.Y, 0.25f);
            seen.Add(sprite.Frame);
        }
        Check(seen.Count >= 2 && worst < 0.01f && scaleOk,
            $"蓄力跳实际播放：见到 {seen.Count} 个不同的帧，固定比例 1/4（641 高的画布显示 160 高，角色大小不变），每帧脚底都在碰撞框底边（最大偏差 {worst:F4}）");

        // 冲突目录：idle 有 4x1、2x2、不带的三张 → 报错并用 2x2（4 帧）；run 有 2x1 和不带的 → 用 2x1 并警告
        anim.UseArtDirectory(ConflictArt);
        await Frames(2);
        frames = sprite.SpriteFrames;
        bool reportedConflict = SpriteStrip.ReportedMessages.Any(m => m.Contains("spr_player_idle_2x2.png") && m.Contains("spr_player_idle_4x1.png"));
        bool reportedMixed = SpriteStrip.ReportedMessages.Any(m => m.Contains("spr_player_run_2x1.png"));
        Check(frames.GetFrameCount("water_idle") == 4 && frames.GetFrameTexture("water_idle", 0).GetWidth() == 512 && frames.GetFrameCount("water_run") == 2 && reportedConflict && reportedMixed,
            "冲突目录：idle 用文件名排序第一的 idle_2x2（4 帧、每帧 512）并报错；run 用带行列数的 run_2x1（2 帧）并给警告");
    }
}
