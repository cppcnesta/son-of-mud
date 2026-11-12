using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 取代 MetSys 模板里的 MetSysGame + RoomTransitions：挂当前房间、每个物理帧上报玩家坐标、处理走边缘换房和过门。
/// 玩家不是本系统的类型，只当 Node2D 用，通过 Events.PlayerSpawned 或 SetPlayer 交给这里。
/// 房间都放在 Rooms（Node2D，位于原点）下面，玩家坐标就是当前房间里的坐标。
///
/// 事件契约（规格第 4 节）：
/// - 每次换房在卸载旧房间之前发 RoomExiting(旧房间)；换完发 RoomEntered(新房间)。
/// - 只有过门 / 传送 / 复活时发 PlayerTeleported，走边缘换房不发。
/// - 过门：锁输入 → 淡出并等到同一 id 的 FadeCompleted → 换房 → 淡入并等完成 → 解锁。
/// </summary>
[GlobalClass]
public partial class WorldGame : Node
{
    /// <summary>等待淡入淡出完成的保险时间。超时只是兜底，会报错并继续；测试里出现超时即算失败。</summary>
    public const double FadeTimeoutSeconds = 3.0;

    [Export] public WorldLayout Layout { get; set; }

    public Node2D Player { get; private set; }
    public RoomRoot CurrentRoom { get; private set; }
    public RoomDef CurrentRoomDef { get; private set; }
    public Node2D RoomsContainer => _rooms;

    /// <summary>过门 / 传送进行中。为 true 时忽略新的传送请求和 MetSys 的走边缘换房信号。</summary>
    public bool IsTransitioning { get; private set; }

    /// <summary>淡入淡出等待超时的次数（应当始终为 0）。</summary>
    public int FadeTimeouts { get; private set; }

    private Node2D _rooms;
    private int _nextFadeId;
    private int _awaitFadeId = -1;
    private bool _awaitFadeOut;
    private bool _awaitFadeDone;
    private string _pendingEdgeRoom;
    private bool _metSysHooked;

    /// <summary>从任意节点往上找所属的 WorldGame（房间里的门用它）。</summary>
    public static WorldGame FindFrom(Node node)
    {
        for (Node n = node; n != null; n = n.GetParent())
            if (n is WorldGame game) return game;
        return null;
    }

    public override void _EnterTree()
    {
        Layout ??= GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        _rooms = GetNodeOrNull<Node2D>("Rooms");
        if (_rooms == null)
        {
            _rooms = new Node2D { Name = "Rooms" };
            AddChild(_rooms);
        }
        if (Events.Instance != null)
        {
            Events.Instance.PlayerSpawned += OnPlayerSpawned;
            Events.Instance.TravelRequested += OnTravelRequested;
            Events.Instance.FadeCompleted += OnFadeCompleted;
        }
        if (MetSysApi.Available)
        {
            MetSysApi.RoomChanged += OnMetSysRoomChanged;
            _metSysHooked = true;
        }
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null)
        {
            Events.Instance.PlayerSpawned -= OnPlayerSpawned;
            Events.Instance.TravelRequested -= OnTravelRequested;
            Events.Instance.FadeCompleted -= OnFadeCompleted;
        }
        if (_metSysHooked) MetSysApi.RoomChanged -= OnMetSysRoomChanged;
        _metSysHooked = false;
    }

    public void SetPlayer(Node2D player) => Player = player;

    private void OnPlayerSpawned(Node2D player) => SetPlayer(player);

    public override void _PhysicsProcess(double delta)
    {
        if (IsTransitioning || CurrentRoom == null || !IsInstanceValid(Player) || !MetSysApi.Available) return;
        MetSysApi.SetPlayerPosition(Player.GlobalPosition - CurrentRoom.GlobalPosition);
    }

    // ---------- 直接载入（开局，不淡入淡出） ----------

    /// <summary>不经过淡入淡出，直接换到指定房间并把玩家放到出生点（开局、测试用）。按传送处理：发 PlayerTeleported。</summary>
    public bool LoadRoomImmediate(string roomIdOrPath, StringName spawnId)
    {
        if (IsTransitioning) return false;
        RoomDef def = Layout?.Resolve(roomIdOrPath);
        if (def == null)
        {
            GD.PushError($"WorldGame：清单里没有房间 {roomIdOrPath}。");
            return false;
        }
        return SwapRoom(def, spawnId);
    }

    // ---------- 过门 / 传送 ----------

    private readonly System.Collections.Generic.Queue<(int id, string room, StringName spawn)> _travelQueue = new();

    /// <summary>
    /// 经 Events 发来的换房请求（读档、复活、新游戏）。正在换房时不丢弃，排队等当前换房结束再执行；
    /// 每个请求都会收到一次 TravelFinished 回执。按键过门直接调 TeleportAsync，重复按键仍然被拒绝。
    /// </summary>
    private void OnTravelRequested(int requestId, string roomIdOrPath, StringName spawnId)
    {
        _travelQueue.Enqueue((requestId, roomIdOrPath, spawnId));
        ProcessTravelQueue();
    }

    private void ProcessTravelQueue()
    {
        if (IsTransitioning || _travelQueue.Count == 0 || !IsInsideTree()) return;
        var (id, room, spawn) = _travelQueue.Dequeue();
        _ = RunQueuedTravel(id, room, spawn);
    }

    private async Task RunQueuedTravel(int requestId, string roomIdOrPath, StringName spawnId)
    {
        bool reported = false;
        void Report(bool success, string roomId)
        {
            if (reported) return;
            reported = true;
            Events.Instance?.EmitSignal(Events.SignalName.TravelFinished, requestId, roomId ?? roomIdOrPath, success);
        }
        bool started = await TeleportAsync(roomIdOrPath, spawnId, Report);
        if (!started)
        {
            Report(false, roomIdOrPath);
            if (IsInstanceValid(this)) Callable.From(ProcessTravelQueue).CallDeferred();
        }
    }

    /// <summary>
    /// 过门、读档、复活共用的传送流程。正在换房时直接返回 false（重复按键不会启动第二次传送）。
    /// </summary>
    /// <param name="onArrived">玩家被放到目标出生点之后（或换房失败时）调用一次：(是否成功, 房间编号)。</param>
    public async Task<bool> TeleportAsync(string roomIdOrPath, StringName spawnId, System.Action<bool, string> onArrived = null)
    {
        if (IsTransitioning) return false;
        RoomDef def = Layout?.Resolve(roomIdOrPath);
        if (def == null)
        {
            GD.PushError($"WorldGame：传送目标 {roomIdOrPath} 不在清单里。");
            return false;
        }
        IsTransitioning = true;
        _pendingEdgeRoom = null;
        InputLock.Request(InputLock.RoomTransition, true);
        try
        {
            await Fade(true);
            if (!IsInstanceValid(this) || !IsInsideTree())
            {
                onArrived?.Invoke(false, def.Id);
                return false;
            }
            bool swapped = SwapRoom(def, spawnId);
            onArrived?.Invoke(swapped, def.Id);
            await Fade(false);
        }
        finally
        {
            InputLock.Request(InputLock.RoomTransition, false);
            if (IsInstanceValid(this))
            {
                IsTransitioning = false;
                // 换房期间排队的请求（例如过门途中死亡触发的复活），等这一次结束后再执行。
                Callable.From(ProcessTravelQueue).CallDeferred();
            }
        }
        return true;
    }

    private async Task Fade(bool fadeOut)
    {
        int id = ++_nextFadeId;
        _awaitFadeId = id;
        _awaitFadeOut = fadeOut;
        _awaitFadeDone = false;
        Events.Instance?.EmitSignal(Events.SignalName.FadeRequested, id, fadeOut);
        ulong start = Time.GetTicksMsec();
        while (!_awaitFadeDone)
        {
            if (Time.GetTicksMsec() - start > (ulong)(FadeTimeoutSeconds * 1000))
            {
                FadeTimeouts++;
                GD.PushError($"WorldGame：等待 FadeCompleted({id}, {fadeOut}) 超过 {FadeTimeoutSeconds} 秒，继续执行。");
                break;
            }
            SceneTree tree = GetTree();
            if (tree == null) break;
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!IsInstanceValid(this)) return;
        }
        _awaitFadeId = -1;
    }

    private void OnFadeCompleted(int requestId, bool fadeOut)
    {
        if (requestId == _awaitFadeId && fadeOut == _awaitFadeOut) _awaitFadeDone = true;
    }

    // ---------- 走边缘换房（MetSys room_changed） ----------

    private void OnMetSysRoomChanged(string metSysRoomId)
    {
        if (IsTransitioning) return;
        _pendingEdgeRoom = metSysRoomId;
        // 信号是在 SetPlayerPosition 里同步发出的，延后到本帧末尾再换房。
        Callable.From(ApplyPendingEdgeChange).CallDeferred();
    }

    private void ApplyPendingEdgeChange()
    {
        string pending = _pendingEdgeRoom;
        _pendingEdgeRoom = null;
        if (pending == null || IsTransitioning || CurrentRoomDef == null) return;
        RoomDef def = Layout?.FindByMetSysId(pending);
        if (def == null || def == CurrentRoomDef) return; // 传送后 MetSys 会补发一次当前房间，忽略
        SwapRoom(def, null);
    }

    // ---------- 换房 ----------

    /// <summary>spawnId 为 null 表示走边缘换房：按两个房间的格坐标差修正玩家坐标；否则放到出生点并发 PlayerTeleported。</summary>
    private bool SwapRoom(RoomDef def, StringName spawnId)
    {
        Node instance = GD.Load<PackedScene>(def.ScenePath)?.Instantiate();
        if (instance is not RoomRoot next)
        {
            instance?.Free();
            GD.PushError($"WorldGame：房间场景 {def.ScenePath} 不存在或根节点不是 RoomRoot。");
            return false;
        }
        if (MetSysApi.Available && !MetSysApi.HasSaveData) MetSysApi.SetSaveData();

        RoomRoot previous = CurrentRoom;
        RoomDef previousDef = CurrentRoomDef;
        if (previous != null) Events.Instance?.EmitSignal(Events.SignalName.RoomExiting, previousDef.Id);

        GodotObject previousInstance = previous?.GetNodeOrNull("RoomInstance");
        if (previous != null) _rooms.RemoveChild(previous);

        _rooms.AddChild(next);
        CurrentRoom = next;
        CurrentRoomDef = def;
        if (MetSysApi.Available) MetSysApi.CurrentLayer = def.Layer;

        bool teleported = spawnId != null;
        if (IsInstanceValid(Player))
        {
            if (!teleported && previousInstance != null)
            {
                var nextRoom = new MetSysRoom(next.GetNode("RoomInstance"));
                Player.GlobalPosition -= nextRoom.OffsetFrom(new MetSysRoom(previousInstance));
            }
            else if (teleported)
            {
                SpawnPoint spawn = next.FindSpawn(spawnId);
                if (spawn != null) Player.GlobalPosition = spawn.GlobalPosition;
                else GD.PushError($"WorldGame：房间 {def.Id} 没有出生点 {spawnId}，也没有 Default。");
            }
        }
        previous?.QueueFree();

        RevealRegion(def);
        if (teleported) Events.Instance?.EmitSignal(Events.SignalName.PlayerTeleported, spawnId);
        Events.Instance?.EmitSignal(Events.SignalName.RoomEntered, def.Id);
        return true;
    }

    // ---------- 区域地图（关卡几何计划 G2） ----------

    /// <summary>区域 = 房间编号里“_”前面的部分（S1_03 → S1）。</summary>
    public static string RegionOf(string roomId)
    {
        int at = roomId?.IndexOf('_') ?? -1;
        return at > 0 ? roomId[..at] : roomId ?? "";
    }

    /// <summary>
    /// 进入一个区域时，把这个区域所有房间的格子标成“已知”：地图上显示轮廓，去过的才算“已探索”。
    /// 已经知道或探索过的格子不再重复标记；结果存在 MetSys 的存档数据里，读档后照样保留。
    /// </summary>
    private void RevealRegion(RoomDef entered)
    {
        if (!MetSysApi.Available || !MetSysApi.HasSaveData || Layout == null) return;
        string region = RegionOf(entered.Id);
        foreach (RoomDef room in Layout.Rooms)
        {
            if (RegionOf(room.Id) != region) continue;
            for (int y = room.Origin.Y; y < room.Origin.Y + room.Size.Y; y++)
                for (int x = room.Origin.X; x < room.Origin.X + room.Size.X; x++)
                {
                    var cell = new Vector3I(x, y, room.Layer);
                    if (!MetSysApi.IsCellDiscovered(cell)) MetSysApi.DiscoverCell(cell);
                }
        }
    }
}
