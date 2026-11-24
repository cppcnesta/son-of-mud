using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.World;

/// <summary>
/// BGM（从正式入口 main_game.tscn 开新游戏）：
/// ① 表：地图一、二、三各一首（assets/audio/bgm/bgm_s1～3），都设成循环；地图四没有。
/// ② 开局在地图一：放 bgm_s1；地图一里换房间，曲子不从头放。
/// ③ 换到地图二：换成 bgm_s2（淡入淡出后只剩新的一首在放）；回到地图一又换回来。
/// ④ 进地图四：淡出变安静。
/// </summary>
public partial class BgmTest : FlowTestBase
{
    protected override string SuiteName => "bgm";

    private BgmPlayer Music => MainGame?.GetNodeOrNull<BgmPlayer>("Music");

    protected override async Task RunChecks()
    {
        bool started = await EnterMainGame(StartMode.NewGame);
        await WaitSettled();
        BgmPlayer music = Music;
        Check(started && music != null && music.Table != null, "main_game.tscn 里有 Music（BGM 播放器）和 BGM 表");
        if (music == null) return;

        BgmTable table = music.Table;
        AudioStream s1 = table.StreamFor("S1"), s2 = table.StreamFor("S2"), s3 = table.StreamFor("S3");
        bool loops = new[] { s1, s2, s3 }.All(s => s is AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Forward });
        Check(s1?.ResourcePath.EndsWith("bgm/bgm_s1.wav") == true && s2?.ResourcePath.EndsWith("bgm/bgm_s2.wav") == true
              && s3?.ResourcePath.EndsWith("bgm/bgm_s3.wav") == true && table.StreamFor("S4") == null && loops,
            "BGM 表：地图一 / 二 / 三各一首，都设成循环；地图四没有");

        await Frames(80);
        Check(music.CurrentRegion == "S1" && music.IsPlaying(s1), $"开局在地图一：放 bgm_s1（{music.CurrentRegion}）");

        int starts = music.StartCount;
        World.LoadRoomImmediate("S1_02", "Default");
        await Frames(20);
        World.LoadRoomImmediate("S1_04", "Default");
        await Frames(20);
        Check(music.IsPlaying(s1) && music.StartCount == starts, $"地图一里换两个房间：还是 bgm_s1，没有从头放（开始次数 {starts} → {music.StartCount}）");

        World.LoadRoomImmediate("S2_01", "Default");
        await Frames(90);
        Check(music.CurrentRegion == "S2" && music.IsPlaying(s2) && music.StartCount == starts + 1 && PlayingCount(music) == 1,
            $"进地图二：换成 bgm_s2，淡出后只剩这一首在放（同时在放 {PlayingCount(music)} 首）");

        World.LoadRoomImmediate("S1_06", "Default");
        await Frames(90);
        Check(music.IsPlaying(s1) && PlayingCount(music) == 1, "回到地图一：换回 bgm_s1");

        World.LoadRoomImmediate("S4_01", "Default");
        await Frames(90);
        Check(music.CurrentRegion == "" && music.CurrentStream == null && PlayingCount(music) == 0, $"进地图四（没有曲子）：淡出后安静（还在放 {PlayingCount(music)} 首）");
    }

    private static int PlayingCount(BgmPlayer music) =>
        music.GetChildren().OfType<AudioStreamPlayer>().Count(p => p.Playing);
}
