using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 正式游戏里的整套界面（game_ui.tscn 的根，放在 main_game.tscn 下）：HUD、大地图、暂停菜单。
/// 这里是唯一处理 menu（Esc）和 map（M / Tab）两个键的地方，负责界面互斥（规格第 2 节）：
/// - 剧情对话进行中（PauseService.IsRequestedBy(Narrative)），按这两个键都不会打开任何界面；
///   换房淡入淡出期间（InputLock 的 RoomTransition 来源）也不打开。已经开着的界面仍然可以关掉。
/// - 地图和暂停菜单同一时间只开一个：菜单开着时按地图键（或点菜单里的“地图”）= 关菜单、开地图；
///   地图开着时按 Esc 只关地图。同一帧两个键一起按，只处理 Esc。
/// 暂停只通过 PauseService 按来源请求；即使漏判，计数也保证关地图不会解除别的来源的暂停。
/// 按键用“按下沿”检测（每帧比较 IsActionPressed），ProcessMode = Always，暂停中也能收到。
/// </summary>
public partial class GameUi : Node
{
    public static readonly StringName MenuAction = "menu";
    public static readonly StringName MapAction = "map";

    [Export] public Hud Hud { get; set; }
    [Export] public MapScreen Map { get; set; }
    [Export] public PauseMenu Menu { get; set; }

    /// <summary>此刻能不能打开地图或暂停菜单。</summary>
    public static bool CanOpen =>
        !PauseService.IsRequestedBy(PauseService.Narrative) && !InputLock.IsRequestedBy(InputLock.RoomTransition);

    private bool _menuHeld;
    private bool _mapHeld;

    public override void _EnterTree()
    {
        if (Events.Instance != null) Events.Instance.MapToggleRequested += OnMapPressed;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.MapToggleRequested -= OnMapPressed;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (Menu != null) Menu.MapRequested += SwitchMenuToMap;
        // 进场时已经按着的键不算一次按下
        _menuHeld = IsHeld(MenuAction);
        _mapHeld = IsHeld(MapAction);
    }

    public override void _Process(double delta)
    {
        bool menu = IsHeld(MenuAction);
        bool map = IsHeld(MapAction);
        bool menuDown = menu && !_menuHeld;
        bool mapDown = map && !_mapHeld;
        _menuHeld = menu;
        _mapHeld = map;
        if (menuDown) OnMenuPressed();
        else if (mapDown) OnMapPressed();
    }

    private static bool IsHeld(StringName action) => InputMap.HasAction(action) && Input.IsActionPressed(action);

    /// <summary>Esc：地图开着就关地图；菜单开着就关菜单；都没开且允许时开菜单。</summary>
    public void OnMenuPressed()
    {
        if (Map == null || Menu == null) return;
        if (Map.IsOpen) { Map.Close(); return; }
        if (Menu.IsOpen) { Menu.Close(); return; }
        if (!CanOpen) return;
        Menu.Open();
    }

    /// <summary>M / Tab：地图开着就关；菜单开着就切到地图；都没开且允许时开地图。</summary>
    public void OnMapPressed()
    {
        if (Map == null || Menu == null) return;
        if (Map.IsOpen) { Map.Close(); return; }
        if (Menu.IsOpen) { SwitchMenuToMap(); return; }
        if (!CanOpen) return;
        Map.Open();
    }

    /// <summary>从暂停菜单进地图：结果等于先关菜单、再开地图。先登记地图的暂停来源再释放菜单的，中间不会有一帧恢复。</summary>
    public void SwitchMenuToMap()
    {
        if (Map == null || Menu == null || !Menu.IsOpen || !CanOpen) return;
        Map.Open();
        Menu.Close();
    }
}
