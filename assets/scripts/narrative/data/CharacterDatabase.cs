using Godot;
using Godot.Collections;
using SonOfMud.Core;

namespace SonOfMud.Narrative.Data;

/// <summary>全部角色（assets/data/narrative/characters.tres）。HiddenNames 里的说话人（旁白）不显示名牌。</summary>
[GlobalClass]
public partial class CharacterDatabase : KeptResource
{
    [Export] public Array<CharacterData> Characters { get; set; } = new();
    [Export] public string[] HiddenNames { get; set; } = { "旁白" };

    /// <summary>按 Id、显示名或别名找角色；找不到返回 null。</summary>
    public CharacterData Find(string name)
    {
        foreach (CharacterData character in Characters)
            if (character != null && character.Matches(name)) return character;
        return null;
    }

    public bool IsHiddenName(string name) =>
        string.IsNullOrEmpty(name) || (HiddenNames != null && System.Array.IndexOf(HiddenNames, name) >= 0);

    /// <summary>剧本里写的名字是否已登记（角色或旁白）。剧本检查用。</summary>
    public bool Knows(string name) => IsHiddenName(name) || Find(name) != null;
}
