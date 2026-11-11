using Godot;
using System.Collections.Generic;

namespace SonOfMud.Core;

public partial class MudInputDefaults : Node
{
    public override void _Ready()
    {
        // Preserve existing bindings, adding only actions absent from the project.
        var defaults = new Dictionary<string, Key>
        {
            ["switch_form"] = Key.Q, ["dodge"] = Key.Shift, ["block"] = Key.K,
            ["attack"] = Key.J, ["toggle_module"] = Key.B, ["debug_unlock"] = Key.F6,
            ["restart_flow"] = Key.F8, ["open_lab"] = Key.F2, ["retry"] = Key.R,
            ["menu"] = Key.Escape
        };
        foreach (var entry in defaults)
        {
            if (InputMap.HasAction(entry.Key)) continue;
            InputMap.AddAction(entry.Key);
            InputMap.ActionAddEvent(entry.Key, new InputEventKey { PhysicalKeycode = entry.Value });
        }
    }
}
