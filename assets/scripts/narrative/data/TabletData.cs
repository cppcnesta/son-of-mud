using Godot;

namespace SonOfMud.Narrative.Data;

/// <summary>
/// 一块泥板（或一段铭文）的文字。泥板放在 assets/data/narrative/tablets/tablet_&lt;Id&gt;.tres，
/// 铭文放在 assets/data/narrative/inscriptions/inscription_&lt;Id&gt;.tres。
/// 只有策划案写了完整版的泥板才填 FullText；变量 FullAfterVar（默认 truth）为 true 后显示完整版。铭文只有一个版本。
/// </summary>
[GlobalClass]
public partial class TabletData : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string Title { get; set; } = "";
    /// <summary>放在哪个房间（只是记录，摆放以房间场景为准）。</summary>
    [Export] public string Room { get; set; } = "";
    /// <summary>残缺版（铭文就是唯一的版本）。</summary>
    [Export(PropertyHint.MultilineText)] public string PartialText { get; set; } = "";
    /// <summary>完整版；空表示这块泥板没有完整版。</summary>
    [Export(PropertyHint.MultilineText)] public string FullText { get; set; } = "";
    [Export] public StringName FullAfterVar { get; set; } = "truth";

    public bool HasFull => !string.IsNullOrEmpty(FullText);

    public string TextFor(bool full) => full && HasFull ? FullText : PartialText;
}
