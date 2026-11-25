using Godot;
using SonOfMud.Core;

namespace SonOfMud.Narrative.Data;

/// <summary>
/// 一张 CG（assets/data/narrative/cg/cg_&lt;Id&gt;.tres）。剧本里写 [CG Id 秒数]。
/// 正式图还没交付时 Texture 为空：CG 层铺一块 PlaceholderColor 色块，中间写 Title（技术占位）。
/// </summary>
[GlobalClass]
public partial class CgData : KeptResource
{
    [Export] public string Id { get; set; } = "";
    /// <summary>占位时显示的标题，比如 CG1「七夜」。黑屏这类纯色 CG 留空。</summary>
    [Export] public string Title { get; set; } = "";
    [Export] public Texture2D Texture { get; set; }
    [Export] public Color PlaceholderColor { get; set; } = new(0.12f, 0.1f, 0.09f);
    /// <summary>
    /// 动画 CG 的场景（根节点挂 AnimatedCg）。填了就是动画 CG：剧本遇到 [CG Id 秒数] 时收起对话框、从头播到尾（不能跳过，台词在场景的字幕里），
    /// 播完才继续往下走；不填就是静态图（Texture）。
    /// </summary>
    [Export] public PackedScene AnimatedScene { get; set; }
}
