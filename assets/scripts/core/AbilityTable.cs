using Godot;
using Godot.Collections;

namespace SonOfMud.Core;

/// <summary>全部能力的清单，以及新游戏时自带的能力。</summary>
[GlobalClass]
public partial class AbilityTable : Resource
{
    /// <summary>正式的能力表（正式入口 game.tscn 的 SaveService 用的就是这一份）。</summary>
    public const string DefaultPath = "res://assets/data/core/ability_table.tres";

    [Export] public Array<AbilityDef> All { get; set; } = new();
    [Export] public Array<StringName> Initial { get; set; } = new();

    public AbilityDef Find(StringName id)
    {
        foreach (AbilityDef def in All)
            if (def != null && def.Id == id) return def;
        return null;
    }
}
