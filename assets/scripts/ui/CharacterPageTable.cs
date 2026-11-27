using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>收集页的人物页（assets/data/ui/character_pages.tres），按遇到的先后排；以后加人物只改这个文件、放立绘。</summary>
[GlobalClass]
public partial class CharacterPageTable : KeptResource
{
    public const string DefaultPath = "res://assets/data/ui/character_pages.tres";

    [Export] public Godot.Collections.Array<CharacterPage> Pages { get; set; } = new();
}
