using System;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.Narrative.Data;

/// <summary>
/// 一个角色的资料：剧本里写的名字、别名、名牌颜色、立绘。
/// 立绘：HasPortrait 为 true 时说话会在对话框上方显示立绘。Portrait 是默认立绘；剧本里写「名字（表情）：」时，
/// 先在 ExpressionPortraits 里按表情名找图，找不到用默认立绘；都没有就画 PortraitColor 色块加名字（技术占位）。
/// 建议的文件名：assets/art/narrative/portrait/por_&lt;Id&gt;.png、por_&lt;Id&gt;_&lt;表情&gt;.png。
/// </summary>
[GlobalClass]
public partial class CharacterData : KeptResource
{
    /// <summary>程序里用的名字（英文小写），拼资源文件名用，比如 mul_tukul。</summary>
    [Export] public string Id { get; set; } = "";
    /// <summary>剧本里写的名字，也是名牌默认显示的名字。改名（[改名]）后名牌改显示变量 name.&lt;这个名字&gt;。</summary>
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>剧本里的其他写法，查找时当成同一个人。</summary>
    [Export] public string[] Aliases { get; set; } = Array.Empty<string>();
    [Export] public Color NameColor { get; set; } = new(0.95f, 0.85f, 0.6f);
    /// <summary>说话时在对话框上方显示立绘。</summary>
    [Export] public bool HasPortrait { get; set; }
    /// <summary>默认立绘；为空时用 PortraitColor 色块占位。</summary>
    [Export] public Texture2D Portrait { get; set; }
    /// <summary>表情立绘：键是剧本里括号中的表情名（如“惊讶”），值是图。没列的表情用默认立绘。</summary>
    [Export] public Godot.Collections.Dictionary<string, Texture2D> ExpressionPortraits { get; set; } = new();
    [Export] public Color PortraitColor { get; set; } = new(0.35f, 0.3f, 0.25f);

    /// <summary>这个表情用的立绘：表情图 → 默认立绘 → null（色块占位）。</summary>
    public Texture2D PortraitFor(string expression)
    {
        if (!string.IsNullOrEmpty(expression) && ExpressionPortraits != null
            && ExpressionPortraits.TryGetValue(expression, out Texture2D byExpression) && byExpression != null)
            return byExpression;
        return Portrait;
    }

    /// <summary>这个名字是不是指这个角色（Id、显示名、别名都算）。</summary>
    public bool Matches(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name == Id || name == DisplayName) return true;
        return Aliases != null && Array.IndexOf(Aliases, name) >= 0;
    }
}
