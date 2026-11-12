using Godot;
using SonOfMud.Core;

namespace SonOfMud.Narrative.Data;

/// <summary>剧情系统的可调参数和数据位置（assets/data/narrative/settings.tres）。</summary>
[GlobalClass]
public partial class NarrativeSettings : KeptResource
{
    public const string DefaultPath = "res://assets/data/narrative/settings.tres";

    [ExportGroup("台词节奏")]
    /// <summary>对话框、字幕条每秒出几个字。</summary>
    [Export] public float CharsPerSecond { get; set; } = 30f;
    /// <summary>同一个人连着说的几句：一句打完字后停多少秒自动出下一句。</summary>
    [Export] public float LineGap { get; set; } = 0.5f;
    /// <summary>同一个人的话中间有空行时，停下来等玩家按推进键。</summary>
    [Export] public bool PauseAtBlankLines { get; set; } = true;
    /// <summary>字幕条：一句打完字后至少停留几秒。</summary>
    [Export] public float SubtitleBaseSeconds { get; set; } = 1.2f;
    /// <summary>字幕条：每个字再多停留几秒。</summary>
    [Export] public float SubtitleSecondsPerChar { get; set; } = 0.12f;

    [ExportGroup("保护")]
    /// <summary>一帧里最多执行几条不需要等待的指令。</summary>
    [Export] public int MaxStepsPerFrame { get; set; } = 200;
    /// <summary>连续执行这么多条都没有等待，就当成死循环停下来报错。</summary>
    [Export] public int MaxStepsWithoutWaiting { get; set; } = 5000;

    [ExportGroup("数据")]
    [Export] public CharacterDatabase Characters { get; set; }
    [Export(PropertyHint.Dir)] public string StoryDirectory { get; set; } = "res://assets/story";
    [Export(PropertyHint.Dir)] public string TabletDirectory { get; set; } = "res://assets/data/narrative/tablets";
    [Export(PropertyHint.Dir)] public string InscriptionDirectory { get; set; } = "res://assets/data/narrative/inscriptions";
    [Export(PropertyHint.Dir)] public string CgDirectory { get; set; } = "res://assets/data/narrative/cg";
    [Export(PropertyHint.Dir)] public string SfxDirectory { get; set; } = "res://assets/audio/sfx";

    [ExportGroup("开场")]
    /// <summary>进入这个房间、并且 OpeningSeenVar 还没设过时播放开场。</summary>
    [Export] public string OpeningRoom { get; set; } = "S1_01";
    [Export] public string OpeningScript { get; set; } = "m_pit";
    [Export] public string OpeningLabel { get; set; } = "开场";
    [Export] public StringName OpeningSeenVar { get; set; } = "seen.opening";

    [ExportGroup("画面")]
    /// <summary>剧情层的 CanvasLayer 层级（World 的黑幕是 100，剧情在它下面）。</summary>
    [Export] public int CanvasLayer { get; set; } = 90;

    public string TabletPath(string id) => $"{TabletDirectory}/tablet_{id}.tres";
    public string InscriptionPath(string id) => $"{InscriptionDirectory}/inscription_{id}.tres";
    public string CgPath(string id) => $"{CgDirectory}/cg_{id}.tres";
}
