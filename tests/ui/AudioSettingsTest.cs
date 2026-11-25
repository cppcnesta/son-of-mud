using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Views;
using SonOfMud.World;

/// <summary>
/// 音乐 / 音效音量（主菜单“设置”）：
/// ① 音轨：Master 下面有 Music、SFX。
/// ② 三条播放路径都接到音轨上：BGM 两台播放器走 Music；动作音效（SoundBank → FeedbackService → Juicee）、剧情音效（NarrativeAudio）走 SFX；
///    场景树里没有留在 Master 上的播放器。
/// ③ 音效设成 0：SFX 静音，真实的动作音效、剧情音效都在 SFX 上；BGM 照常在 Music 上放。
/// ④ 音乐设成 0：换地图交叉淡入淡出时，两台 BGM 播放器都在静音的 Music 上，不漏音。
/// ⑤ 重启后自动恢复：存下音量后另起一个 Godot 进程，不打开设置，音轨已经是存下的音量。
/// </summary>
public partial class AudioSettingsTest : UiTestBase
{
    protected override string SuiteName => "audio_settings";
    public const string ProbeScene = "res://tests/ui/audio_settings_probe.tscn";
    public const string ProbeResult = "user://audio_settings_probe.txt";

    protected override async Task RunChecks()
    {
        int master = AudioServer.GetBusIndex("Master"), music = AudioServer.GetBusIndex(AudioSettings.MusicBus), sfx = AudioServer.GetBusIndex(AudioSettings.SfxBus);
        Check(music > 0 && sfx > 0 && AudioServer.GetBusSend(music) == "Master" && AudioServer.GetBusSend(sfx) == "Master" && master == 0,
            "音轨：Master 下面有 Music、SFX");

        AudioSettings.SetMusic(1f);
        AudioSettings.SetSfx(1f);
        bool started = await EnterMainGame(StartMode.NewGame);
        await WaitSettled();
        var bgm = MainGame?.GetNodeOrNull<BgmPlayer>("Music");
        Check(started && bgm != null, "开新游戏，有 BGM 播放器");
        if (bgm == null) return;
        await Frames(80);

        // ② 每条路径真实放一次，看实际在放的播放器在哪条音轨上
        var bgmPlayers = bgm.GetChildren().OfType<AudioStreamPlayer>().ToList();
        Check(bgmPlayers.Count == 2 && bgmPlayers.All(p => p.Bus == AudioSettings.MusicBus) && bgmPlayers.Any(p => p.Playing),
            $"BGM 两台播放器都走 Music，正在放（{string.Join(",", bgmPlayers.Select(p => p.Bus))}）");
        var before = AllPlayers().ToHashSet();
        bool played = SoundBank.Play(SoundBank.PlayerJump);
        await Frames(2);
        var actionPlayers = AllPlayers().Where(p => !before.Contains(p)).ToList();
        Check(played && actionPlayers.Count > 0 && actionPlayers.All(p => p.Bus == AudioSettings.SfxBus),
            $"动作音效（跳跃）：实际放声的播放器走 SFX（{string.Join(",", actionPlayers.Select(p => p.Bus))}）");
        var narrative = FindChild<NarrativeAudio>(MainGame);
        var narrativePlayer = narrative?.GetNodeOrNull<AudioStreamPlayer>("Sfx");
        Check(narrativePlayer != null && narrativePlayer.Bus == AudioSettings.SfxBus, $"剧情音效播放器走 SFX（{narrativePlayer?.Bus}）");
        var onMaster = AllPlayers().Where(p => p.Bus == "Master").Select(p => p.GetPath().ToString()).ToList();
        Check(onMaster.Count == 0, "场景树里没有留在 Master 上的播放器" + (onMaster.Count > 0 ? "：" + string.Join("、", onMaster) : ""));

        // ③ 音效 0
        AudioSettings.SetSfx(0f);
        before = AllPlayers().ToHashSet();
        SoundBank.Play(SoundBank.PlayerJump);
        await Frames(2);
        actionPlayers = AllPlayers().Where(p => !before.Contains(p)).ToList();
        var sfxState = AudioSettings.BusState(AudioSettings.SfxBus);
        var musicState = AudioSettings.BusState(AudioSettings.MusicBus);
        Check(sfxState.muted && actionPlayers.All(p => p.Bus == AudioSettings.SfxBus) && narrativePlayer?.Bus == AudioSettings.SfxBus
              && !musicState.muted && bgm.IsPlaying(bgm.CurrentStream),
            "音效设成 0：SFX 静音，动作音效、剧情音效都在 SFX 上；BGM 不受影响，照常在放");
        AudioSettings.SetSfx(1f);

        // ④ 音乐 0 + 换地图交叉淡入淡出
        AudioSettings.SetMusic(0f);
        World.LoadRoomImmediate("S2_01", "Default");
        bool allOnMusic = true;
        int bothPlaying = 0;
        for (int i = 0; i < 40; i++)
        {
            await Frames(1);
            var players = bgm.GetChildren().OfType<AudioStreamPlayer>().ToList();
            allOnMusic &= players.All(p => p.Bus == AudioSettings.MusicBus);
            if (players.Count(p => p.Playing) == 2) bothPlaying++;
        }
        Check(AudioSettings.BusState(AudioSettings.MusicBus).muted && allOnMusic && bothPlaying > 0,
            $"音乐设成 0、换到地图二：交叉淡入淡出期间（两台同时在放 {bothPlaying} 帧）都在静音的 Music 上，不漏音");

        // ⑤ 存下音量，另起一个进程检查
        AudioSettings.SetMusic(0.3f);
        AudioSettings.SetSfx(0.6f);
        if (FileAccess.FileExists(ProbeResult)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ProbeResult));
        var output = new Godot.Collections.Array();
        int code = OS.Execute(OS.GetExecutablePath(), new[] { "--headless", "--path", ProjectSettings.GlobalizePath("res://"), "--scene", ProbeScene, "--audio-driver", "Dummy" }, output);
        string result = FileAccess.FileExists(ProbeResult) ? FileAccess.GetFileAsString(ProbeResult).Trim() : "(没有结果)";
        Check(code == 0 && result == "music=0.30 sfx=0.60 musicMuted=False sfxMuted=False",
            $"重启后自动恢复：新进程启动时没打开设置，音轨已经是存下的音量（{result}，退出码 {code}）");
        AudioSettings.SetMusic(1f);
        AudioSettings.SetSfx(1f);
    }

    private IEnumerable<AudioStreamPlayer> AllPlayers() =>
        GetTree().Root.FindChildren("*", nameof(AudioStreamPlayer), true, false).OfType<AudioStreamPlayer>();
}
