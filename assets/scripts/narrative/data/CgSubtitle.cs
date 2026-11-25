using Godot;
using SonOfMud.Core;

namespace SonOfMud.Narrative.Data;

/// <summary>动画 CG 里的一句字幕：谁说的（旁白留「旁白」）、说什么、在总时间轴上第几秒出现、第几秒消失。</summary>
[GlobalClass]
public partial class CgSubtitle : KeptResource
{
    public const string Narrator = "旁白";

    [Export] public string Speaker { get; set; } = Narrator;
    [Export(PropertyHint.MultilineText)] public string Text { get; set; } = "";
    [Export] public float Start { get; set; }
    [Export] public float End { get; set; }

    public bool IsNarration => string.IsNullOrEmpty(Speaker) || Speaker == Narrator;
}
