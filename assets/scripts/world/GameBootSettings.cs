using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>正式游戏入口用到的场景路径。主菜单是 UI 的新主菜单 title_menu.tscn（旧的 main_menu.tscn 只从调试入口进）。</summary>
[GlobalClass]
public partial class GameBootSettings : KeptResource
{
    public const string DefaultPath = "res://assets/data/world/game_boot_settings.tres";

    /// <summary>回主菜单（QuitToMenuRequested、开局失败）时切到的场景。</summary>
    [Export(PropertyHint.File, "*.tscn")] public string MenuScene { get; set; } = "res://assets/scenes/ui/title_menu.tscn";

    /// <summary>正式游戏场景；重开一局（RestartRunRequested）时整个重新载入它。</summary>
    [Export(PropertyHint.File, "*.tscn")] public string GameScene { get; set; } = "res://assets/scenes/world/main_game.tscn";
}
