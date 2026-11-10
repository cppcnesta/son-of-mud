using System;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace SonOfMud.Core;

/// <summary>
/// MetSys（GDScript 插件，autoload 名 MetSys）的唯一 C# 入口。
/// 其他代码不得用 GetNode("/root/MetSys") 或字符串 Call 直接调 MetSys。
/// 方法名与源码 addons/MetroidvaniaSystem/Scripts/MetroidvaniaSystem.gd 对应，由 Core 测试逐个调用验证。
/// </summary>
public static class MetSysApi
{
    private static class M
    {
        public static readonly StringName ResetState = "reset_state";
        public static readonly StringName GetSaveData = "get_save_data";
        public static readonly StringName SetSaveData = "set_save_data";
        public static readonly StringName SetPlayerPosition = "set_player_position";
        public static readonly StringName GetCurrentCoords = "get_current_coords";
        public static readonly StringName GetCurrentRoomInstance = "get_current_room_instance";
        public static readonly StringName GetCurrentRoomId = "get_current_room_id";
        public static readonly StringName GetCurrentRoomName = "get_current_room_name";
        public static readonly StringName RegisterStorableObject = "register_storable_object";
        public static readonly StringName StoreObject = "store_object";
        public static readonly StringName IsObjectIdStored = "is_object_id_stored";
        public static readonly StringName GetObjectId = "get_object_id";
        public static readonly StringName IsCellDiscovered = "is_cell_discovered";
        public static readonly StringName GetExploredRatio = "get_explored_ratio";
        public static readonly StringName GetGroupByName = "get_group_by_name";
        public static readonly StringName DiscoverCellGroup = "discover_cell_group";
        public static readonly StringName CurrentLayer = "current_layer";
        public static readonly StringName LastPlayerPosition = "last_player_position";
        public static readonly StringName Settings = "settings";
        public static readonly StringName InGameCellSize = "in_game_cell_size";
        public static readonly StringName SaveDataField = "save_data";
    }

    private static class S
    {
        public static readonly StringName CellChanged = "cell_changed";
        public static readonly StringName RoomChanged = "room_changed";
        public static readonly StringName MapUpdated = "map_updated";
    }

    public static Node Singleton =>
        (Engine.GetMainLoop() as SceneTree)?.Root?.GetNodeOrNull("MetSys");

    public static bool Available => Singleton != null;

    private static Node Require()
    {
        Node node = Singleton;
        if (node == null) throw new InvalidOperationException("MetSys autoload 不存在：检查 project.godot 的 [autoload]。");
        return node;
    }

    // ---- 状态与存档 ----

    /// <summary>新一局开始时调用。之后必须再调 SetSaveData 初始化存档数据。</summary>
    public static void ResetState() => Require().Call(M.ResetState);

    /// <summary>MetSys 自身的存档数据（已探索格、已储存对象等）。必须先 SetSaveData。</summary>
    public static GDictionary GetSaveData() => Require().Call(M.GetSaveData).AsGodotDictionary();

    /// <summary>传 null 或空字典 = 新存档；读档时必须传 GetSaveData 导出的完整字典。</summary>
    public static void SetSaveData(GDictionary data = null) => Require().Call(M.SetSaveData, data ?? new GDictionary());

    public static bool HasSaveData => Require().Get(M.SaveDataField).VariantType != Variant.Type.Nil;

    // ---- 玩家位置与房间 ----

    /// <summary>每个物理帧由 World 调用，传玩家在当前房间里的坐标。</summary>
    public static void SetPlayerPosition(Vector2 position) => Require().Call(M.SetPlayerPosition, position);

    public static bool HasPlayerPosition =>
        Require().Get(M.LastPlayerPosition).AsVector3I() != new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);

    public static Vector3I CurrentCoords => Require().Call(M.GetCurrentCoords).AsVector3I();
    public static string CurrentRoomId => Require().Call(M.GetCurrentRoomId).AsString();
    public static string CurrentRoomName => Require().Call(M.GetCurrentRoomName).AsString();

    public static MetSysRoom? CurrentRoom
    {
        get
        {
            GodotObject room = Require().Call(M.GetCurrentRoomInstance).AsGodotObject();
            return room == null ? null : new MetSysRoom(room);
        }
    }

    public static int CurrentLayer
    {
        get => Require().Get(M.CurrentLayer).AsInt32();
        set => Require().Set(M.CurrentLayer, value);
    }

    /// <summary>一个地图格对应的游戏内像素尺寸（MetSysSettings.in_game_cell_size）。</summary>
    public static Vector2 CellSize =>
        Require().Get(M.Settings).AsGodotObject().Get(M.InGameCellSize).AsVector2();

    // ---- 持久对象（打碎的墙、拿走的道具） ----

    /// <summary>登记可储存对象。返回 true 表示它之前已被储存（调用方应移除自己）。</summary>
    public static bool RegisterStorable(GodotObject obj, Action onAlreadyStored = null) =>
        Require().Call(M.RegisterStorableObject, obj,
            onAlreadyStored == null ? new Callable() : Callable.From(onAlreadyStored)).AsBool();

    public static void Store(GodotObject obj) => Require().Call(M.StoreObject, obj);
    public static bool IsStored(StringName id) => Require().Call(M.IsObjectIdStored, id).AsBool();
    public static StringName GetObjectId(GodotObject obj) => Require().Call(M.GetObjectId, obj).AsStringName();

    // ---- 地图 ----

    public static bool IsCellDiscovered(Vector3I coords, bool includeMapped = true) =>
        Require().Call(M.IsCellDiscovered, coords, includeMapped).AsBool();

    public static float GetExploredRatio(int layer = -1) => Require().Call(M.GetExploredRatio, layer).AsSingle();
    public static int GetGroupByName(string group) => Require().Call(M.GetGroupByName, group).AsInt32();
    public static void DiscoverCellGroup(int groupId) => Require().Call(M.DiscoverCellGroup, groupId);

    // ---- 地图控件（MetSys 模板 Template/Nodes/Minimap.tscn 的实例） ----
    // UI 的大地图要自己决定显示哪一格、哪一层（小地图则由控件自己跟随玩家，不需要这里）。

    private static class V
    {
        public static readonly StringName Center = "center";
        public static readonly StringName Layer = "layer";
    }

    /// <summary>把地图控件（track_position=false 的 Minimap 实例）的中心格设成 center、图层设成 layer。先设图层再设中心。</summary>
    public static void ConfigureMapView(Control view, Vector2I center, int layer)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));
        view.Set(V.Layer, layer);
        view.Set(V.Center, center);
    }

    /// <summary>地图控件当前显示的中心格。</summary>
    public static Vector2I GetMapViewCenter(Control view) => view.Get(V.Center).AsVector2I();

    /// <summary>地图控件当前显示的图层。</summary>
    public static int GetMapViewLayer(Control view) => view.Get(V.Layer).AsInt32();

    // ---- 信号桥接：第一次订阅时连接 MetSys 的 GDScript 信号，转成 C# 事件 ----

    private static Action<Vector3I> _cellChanged;
    private static Action<string> _roomChanged;
    private static Action _mapUpdated;
    private static ulong _bridgedInstance;

    public static event Action<Vector3I> CellChanged
    {
        add { EnsureBridge(); _cellChanged += value; }
        remove => _cellChanged -= value;
    }

    /// <summary>参数是房间 ID（场景路径或 UID）。</summary>
    public static event Action<string> RoomChanged
    {
        add { EnsureBridge(); _roomChanged += value; }
        remove => _roomChanged -= value;
    }

    public static event Action MapUpdated
    {
        add { EnsureBridge(); _mapUpdated += value; }
        remove => _mapUpdated -= value;
    }

    private static void EnsureBridge()
    {
        Node node = Require();
        if (_bridgedInstance == node.GetInstanceId()) return;
        node.Connect(S.CellChanged, Callable.From<Vector3I>(cell => _cellChanged?.Invoke(cell)));
        node.Connect(S.RoomChanged, Callable.From<string>(room => _roomChanged?.Invoke(room)));
        node.Connect(S.MapUpdated, Callable.From(() => _mapUpdated?.Invoke()));
        _bridgedInstance = node.GetInstanceId();
    }
}

/// <summary>MetSys RoomInstance 节点的包装。房间场景里必须放一个 RoomInstance，位置 (0,0)。</summary>
public readonly struct MetSysRoom
{
    private static readonly StringName RoomIdField = "room_id";
    private static readonly StringName AdjustCameraLimitsMethod = "adjust_camera_limits";
    private static readonly StringName GetSizeMethod = "get_size";
    private static readonly StringName GetLayerMethod = "get_layer";
    private static readonly StringName GetRoomPositionOffsetMethod = "get_room_position_offset";

    public GodotObject Instance { get; }

    public MetSysRoom(GodotObject instance) => Instance = instance;

    public string Id => Instance.Get(RoomIdField).AsString();
    public Vector2 Size => Instance.Call(GetSizeMethod).AsVector2();
    public int Layer => Instance.Call(GetLayerMethod).AsInt32();

    /// <summary>把镜头 limit 设成房间范围 0..Size。</summary>
    public void AdjustCameraLimits(Camera2D camera) => Instance.Call(AdjustCameraLimitsMethod, camera);

    /// <summary>从上一个房间切过来时，玩家坐标要减去的偏移（previousRoom 是上一个房间的 RoomInstance）。</summary>
    public Vector2 OffsetFrom(MetSysRoom previousRoom) =>
        Instance.Call(GetRoomPositionOffsetMethod, previousRoom.Instance).AsVector2();
}
