using Godot;
using Godot.Collections;

namespace SonOfMud.Core;

/// <summary>门、泥板等的前置条件：需要的能力全部拥有，且需要的变量全部为 bool true。</summary>
[Tool]
[GlobalClass]
public partial class GateRequirement : KeptResource
{
    [Export] public Array<StringName> Abilities { get; set; } = new();
    [Export] public Array<StringName> Flags { get; set; } = new();
    [Export] public string LockedText { get; set; } = "";
}
