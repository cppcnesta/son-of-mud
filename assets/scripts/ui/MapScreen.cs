using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 大地图界面。打开时 PauseService.Request(Map, true)，关闭时释放；ProcessMode = Always。
/// 里面是一个 MetSys Minimap.tscn 实例（track_position=false，大范围），打开时以玩家当前格为中心，
/// 方向键（ui_left/right/up/down）平移；只显示已探索的格子（MetSys 默认行为）。
/// 中心格和图层只经过 MetSysApi.ConfigureMapView 设置。开关的时机由 GameUi 决定（互斥规则在那里）。
/// </summary>
public partial class MapScreen : Control
{
    private static readonly StringName[] PanActions = { "ui_left", "ui_right", "ui_up", "ui_down" };
    private static readonly Vector2I[] PanDirections = { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down };

    [Export] public UiSettings Settings { get; set; }
    /// <summary>MetSys Minimap.tscn 的实例。</summary>
    [Export] public Control MapView { get; set; }

    public bool IsOpen { get; private set; }
    public Vector2I Center => MapView != null && MetSysApi.Available ? MetSysApi.GetMapViewCenter(MapView) : Vector2I.Zero;
    public int Layer => MapView != null && MetSysApi.Available ? MetSysApi.GetMapViewLayer(MapView) : 0;

    private readonly bool[] _held = new bool[4];
    private float _panAccumulator;

    public override void _Ready()
    {
        Settings ??= UiSettings.LoadDefault();
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
    }

    public override void _ExitTree()
    {
        // 场景被直接释放（没经过 GameBoot）时也不留下地图的暂停来源
        if (IsOpen) PauseService.Request(PauseService.Map, false);
        IsOpen = false;
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        PauseService.Request(PauseService.Map, true);
        Visible = true;
        System.Array.Clear(_held);
        _panAccumulator = 0f;
        CenterOnPlayer();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        PauseService.Request(PauseService.Map, false);
    }

    /// <summary>以玩家当前格为中心（MetSys 还没有玩家位置时保持原样）。</summary>
    public void CenterOnPlayer()
    {
        if (MapView == null || !MetSysApi.Available || !MetSysApi.HasPlayerPosition) return;
        Vector3I coords = MetSysApi.CurrentCoords;
        MetSysApi.ConfigureMapView(MapView, new Vector2I(coords.X, coords.Y), coords.Z);
    }

    public void Pan(Vector2I offset)
    {
        if (MapView == null || !MetSysApi.Available || offset == Vector2I.Zero) return;
        MetSysApi.ConfigureMapView(MapView, Center + offset, Layer);
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        Vector2I step = Vector2I.Zero;
        Vector2I held = Vector2I.Zero;
        for (int i = 0; i < PanActions.Length; i++)
        {
            bool pressed = InputMap.HasAction(PanActions[i]) && Input.IsActionPressed(PanActions[i]);
            if (pressed && !_held[i]) step += PanDirections[i];
            if (pressed) held += PanDirections[i];
            _held[i] = pressed;
        }
        if (step != Vector2I.Zero)
        {
            // 按下的那一刻先移一格，之后按住按速度连续平移
            _panAccumulator = 0f;
            Pan(step);
            return;
        }
        if (held == Vector2I.Zero)
        {
            _panAccumulator = 0f;
            return;
        }
        _panAccumulator += (float)delta * Mathf.Max(Settings.MapPanCellsPerSecond, 0f);
        while (_panAccumulator >= 1f)
        {
            _panAccumulator -= 1f;
            Pan(held);
        }
    }
}
