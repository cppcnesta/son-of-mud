using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 音乐 / 音效音量（主菜单「设置」里调）。音轨在 default_bus_layout.tres：Master 下面的 Music、SFX。
/// - 音乐：BGM 播放器（BgmPlayer 的两台）走 Music；音效：战斗 / 脚步音效（FeedbackService 经 Juicee）和剧情音效（NarrativeAudio）走 SFX。
/// - 音量是 0～1（线性），存在 user://settings.cfg；游戏启动时（Events autoload 进场景树）读出来套到音轨上。0 时音轨直接静音。
/// </summary>
public static class AudioSettings
{
    public const string MusicBus = "Music";
    public const string SfxBus = "SFX";
    public const string FilePath = "user://settings.cfg";
    private const string Section = "audio";

    public static float MusicVolume { get; private set; } = 1f;
    public static float SfxVolume { get; private set; } = 1f;

    public static void SetMusic(float volume, bool save = true)
    {
        MusicVolume = Mathf.Clamp(volume, 0f, 1f);
        Apply(MusicBus, MusicVolume);
        if (save) Save();
    }

    public static void SetSfx(float volume, bool save = true)
    {
        SfxVolume = Mathf.Clamp(volume, 0f, 1f);
        Apply(SfxBus, SfxVolume);
        if (save) Save();
    }

    /// <summary>读存下的音量（没有文件就是 1）并套到音轨上。</summary>
    public static void LoadAndApply()
    {
        var file = new ConfigFile();
        bool loaded = file.Load(FilePath) == Error.Ok;
        MusicVolume = loaded ? Mathf.Clamp((float)file.GetValue(Section, "music", 1f), 0f, 1f) : 1f;
        SfxVolume = loaded ? Mathf.Clamp((float)file.GetValue(Section, "sfx", 1f), 0f, 1f) : 1f;
        Apply(MusicBus, MusicVolume);
        Apply(SfxBus, SfxVolume);
    }

    public static void Save()
    {
        var file = new ConfigFile();
        file.Load(FilePath);   // 保留文件里别的设置
        file.SetValue(Section, "music", MusicVolume);
        file.SetValue(Section, "sfx", SfxVolume);
        file.Save(FilePath);
    }

    /// <summary>音轨现在的音量（线性）和是否静音，测试用。找不到音轨时返回 (-1, false)。</summary>
    public static (float volume, bool muted) BusState(string bus)
    {
        int index = AudioServer.GetBusIndex(bus);
        return index < 0 ? (-1f, false) : (Mathf.DbToLinear(AudioServer.GetBusVolumeDb(index)), AudioServer.IsBusMute(index));
    }

    private static void Apply(string bus, float volume)
    {
        int index = AudioServer.GetBusIndex(bus);
        if (index < 0) return;
        AudioServer.SetBusMute(index, volume <= 0f);
        AudioServer.SetBusVolumeDb(index, volume <= 0f ? -80f : Mathf.LinearToDb(volume));
    }
}
