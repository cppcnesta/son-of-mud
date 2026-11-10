using Godot;

namespace SonOfMud.World;

/// <summary>正式游戏入口用到的场景路径。本轮主菜单先指向旧的 main_menu.tscn，UI 那一轮改成新主菜单。</summary>
[GlobalClass]
public partial class GameBootSettings : Resource
{
    public const string DefaultPath = "res://assets/data/world/game_boot_settings.tres";

    /// <summary>回主菜单（QuitToMenuRequested、开局失败）时切到的场景。</summary>
    [Export(PropertyHint.File, "*.tscn")] public string MenuScene { get; set; } = "res://assets/scenes/ui/main_menu.tscn";

    /// <summary>正式游戏场景；重开一局（RestartRunRequested）时整个重新载入它。</summary>
    [Export(PropertyHint.File, "*.tscn")] public string GameScene { get; set; } = "res://assets/scenes/world/main_game.tscn";
}
