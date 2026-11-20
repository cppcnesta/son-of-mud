using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 正式游戏里的整套界面（game_ui.tscn 的根，放在 main_game.tscn 下）：HUD、大地图、收集页、暂停菜单。
/// 这里是唯一处理 menu（Esc）、map（M）、collection（Tab）三个键的地方，负责界面互斥（规格第 2 节）：
/// - 剧情对话进行中（PauseService.IsRequestedBy(Narrative)），按这几个键都不会打开任何界面；
///   换房淡入淡出期间（InputLock 的 RoomTransition 来源）也不打开。已经开着的界面仍然可以关掉。
/// - 地图、收集页、暂停菜单同一时间只开一个：
///   菜单开着时按 M（或点菜单里的“地图”）= 关菜单、开地图；按 Tab = 关菜单、开收集页；
///   地图开着时按 Tab = 关地图、开收集页；收集页开着时按 M = 关收集页、开地图；
///   地图或收集页开着时按 Esc 只关它。同一帧几个键一起按，只处理一个（Esc 优先，其次 M）。
/// - 切换时先开新的、再关旧的：新界面先登记暂停来源，旧的再释放，中间不会有一帧恢复。
/// 暂停只通过 PauseService 按来源请求；即使漏判，计数也保证关一个界面不会解除别的来源的暂停。
/// 按键用“按下沿”检测（每帧比较 IsActionPressed），ProcessMode = Always，暂停中也能收到。
/// </summary>
public partial class GameUi : Node
{
    public static readonly StringName MenuAction = "menu";
    public static readonly StringName MapAction = "map";
    public static readonly StringName CollectionAction = "collection";

    [Export] public Hud Hud { get; set; }
    [Export] public MapScreen Map { get; set; }
    [Export] public PauseMenu Menu { get; set; }
    [Export] public CollectionScreen Collection { get; set; }

    /// <summary>此刻能不能打开地图、收集页或暂停菜单。</summary>
    public static bool CanOpen =>
        !PauseService.IsRequestedBy(PauseService.Narrative) && !InputLock.IsRequestedBy(InputLock.RoomTransition);

    private bool _menuHeld;
    private bool _mapHeld;
    private bool _collectionHeld;

    private bool CollectionOpen => Collection != null && Collection.IsOpen;

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
        _collectionHeld = IsHeld(CollectionAction);
    }

    public override void _Process(double delta)
    {
        bool menu = IsHeld(MenuAction);
        bool map = IsHeld(MapAction);
        bool collection = IsHeld(CollectionAction);
        bool menuDown = menu && !_menuHeld;
        bool mapDown = map && !_mapHeld;
        bool collectionDown = collection && !_collectionHeld;
        _menuHeld = menu;
        _mapHeld = map;
        _collectionHeld = collection;
        if (menuDown) OnMenuPressed();
        else if (mapDown) OnMapPressed();
        else if (collectionDown) OnCollectionPressed();
    }

    private static bool IsHeld(StringName action) => InputMap.HasAction(action) && Input.IsActionPressed(action);

    /// <summary>Esc：地图或收集页开着就关它；菜单开着就关菜单；都没开且允许时开菜单。</summary>
    public void OnMenuPressed()
    {
        if (Map == null || Menu == null) return;
        if (Map.IsOpen) { Map.Close(); return; }
        if (CollectionOpen) { Collection.Close(); return; }
        if (Menu.IsOpen) { Menu.Close(); return; }
        if (!CanOpen) return;
        Menu.Open();
    }

    /// <summary>M：地图开着就关；菜单或收集页开着就切到地图；都没开且允许时开地图。</summary>
    public void OnMapPressed()
    {
        if (Map == null || Menu == null) return;
        if (Map.IsOpen) { Map.Close(); return; }
        if (Menu.IsOpen) { SwitchMenuToMap(); return; }
        if (CollectionOpen)
        {
            if (!CanOpen) return;
            Map.Open();
            Collection.Close();
            return;
        }
        if (!CanOpen) return;
        Map.Open();
    }

    /// <summary>Tab：收集页开着就关；地图或菜单开着就切到收集页；都没开且允许时开收集页。</summary>
    public void OnCollectionPressed()
    {
        if (Collection == null) return;
        if (Collection.IsOpen) { Collection.Close(); return; }
        if (!CanOpen) return;
        bool mapOpen = Map != null && Map.IsOpen;
        bool menuOpen = Menu != null && Menu.IsOpen;
        // 先开收集页（登记暂停来源），再关旧的，中间不会有一帧恢复
        Collection.Open();
        if (mapOpen) Map.Close();
        if (menuOpen) Menu.Close();
    }

    /// <summary>从暂停菜单进地图：结果等于先关菜单、再开地图。先登记地图的暂停来源再释放菜单的，中间不会有一帧恢复。</summary>
    public void SwitchMenuToMap()
    {
        if (Map == null || Menu == null || !Menu.IsOpen || !CanOpen) return;
        Map.Open();
        Menu.Close();
    }
}
