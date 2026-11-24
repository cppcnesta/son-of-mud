using Godot;
using Godot.Collections;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 每个地图（区域）放哪首 BGM（assets/data/audio/bgm_table.tres，编辑器里直接改）。
/// 区域就是房间编号下划线前面那段（S1_03 → S1）。表里没有的区域不放音乐（进去后淡出变安静）。
/// 音乐文件放在 assets/audio/bgm/，导入设置要勾循环（.import 里 edit/loop_mode=2）。
/// </summary>
[GlobalClass]
public partial class BgmTable : KeptResource
{
    public const string DefaultPath = "res://assets/data/audio/bgm_table.tres";

    [Export] public Array<BgmTrack> Tracks { get; set; } = new();
    /// <summary>BGM 音量（分贝，0 = 原音量）。</summary>
    [Export] public float VolumeDb { get; set; } = -6f;
    /// <summary>换地图时旧曲淡出、新曲淡入各用的秒数。</summary>
    [Export] public float FadeSeconds { get; set; } = 1f;

    public AudioStream StreamFor(string region)
    {
        foreach (BgmTrack track in Tracks)
            if (track != null && track.Region == region) return track.Stream;
        return null;
    }
}
