using Godot;

namespace SonOfMud.Player;

/// <summary>玩家用到的 InputMap 动作名（和 project.godot 一致）。</summary>
public static class PlayerActions
{
    public static readonly StringName Left = "move_left";
    public static readonly StringName Right = "move_right";
    public static readonly StringName Up = "move_up";
    public static readonly StringName Down = "move_down";
    public static readonly StringName Jump = "jump";
    public static readonly StringName Attack = "attack";
    public static readonly StringName Block = "block";
    public static readonly StringName Dodge = "dodge";
    public static readonly StringName SwitchForm = "switch_form";
    public static readonly StringName Interact = "interact";

    /// <summary>进输入缓冲的动作。</summary>
    public static readonly StringName[] Buffered = { Jump, Attack, Block, Dodge };
}
