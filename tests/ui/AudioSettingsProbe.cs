using Godot;
using SonOfMud.Core;

/// <summary>
/// 给 AudioSettingsTest 另起进程用：启动后什么都不做（不打开设置），直接记下 Music / SFX 音轨现在的音量，写进结果文件后退出。
/// 音量是 Events autoload 启动时从 user://settings.cfg 读出来套上的。
/// </summary>
public partial class AudioSettingsProbe : Node
{
    public override void _Ready()
    {
        var music = AudioSettings.BusState(AudioSettings.MusicBus);
        var sfx = AudioSettings.BusState(AudioSettings.SfxBus);
        using var file = FileAccess.Open(AudioSettingsTest.ProbeResult, FileAccess.ModeFlags.Write);
        file?.StoreString($"music={music.volume:0.00} sfx={sfx.volume:0.00} musicMuted={music.muted} sfxMuted={sfx.muted}");
        GetTree().Quit();
    }
}
