using Godot;
using SonOfMud.Core;
using GDictionary = Godot.Collections.Dictionary;

namespace SonOfMud.Save;

/// <summary>
/// 唯一的存档服务（规格第 3 节），三个操作各自独立：
/// - SaveToDisk(savePointId)：在存档点交互时写盘，同时更新“最近存档点”。
/// - LoadFromDisk(slot)：清空内存 → 读盘 → 恢复 MetSys 和进度 → 补齐这个版本的初始能力 → 进入存档点所在房间。
/// - RespawnAtCheckpoint()：玩家死亡（D4）。不读盘，保留内存里的进度，回到最近存档点。
/// 只依赖 Core：换房通过 Events.TravelRequested 交给 World，当前房间从 Events.RoomEntered 得知。
/// 新游戏清空进度、读档恢复进度之后各发一次 Events.ProgressRestored（在换房请求之前）。
/// 开局结果（新游戏、读档）用 Events.GameStartFinished 报告：等 World 回执“已到达”之后才报成功；
/// 读不出存档、版本不支持、目标房间不存在、换房失败都报失败并带原因。新游戏和失败的读档都不碰存档文件。
/// </summary>
[GlobalClass]
public partial class SaveService : Node
{
    [Export] public string SaveDirectory { get; set; } = SavePaths.DefaultDirectory;
    /// <summary>新游戏起点，也是第一个存档点之前的复活点。</summary>
    [Export] public string NewGameRoom { get; set; } = "S1_01";
    [Export] public StringName NewGameSpawn { get; set; } = "Default";
    /// <summary>存档点所在房间里，复活 / 读档使用的出生点（生成器放在存档点脚下）。</summary>
    [Export] public StringName SavePointSpawn { get; set; } = "Save";
    [Export] public AbilityTable InitialAbilities { get; set; }

    /// <summary>最近存档点所在房间；还没存过档时是新游戏起点。</summary>
    public string CheckpointRoom { get; private set; }
    /// <summary>最近存档点编号；空表示还没存过档。</summary>
    public string CheckpointSavePoint { get; private set; } = "";
    public string CurrentRoom { get; private set; } = "";
    public string LastError { get; private set; } = "";
    /// <summary>正在进行的复活换房请求编号；-1 表示没有。</summary>
    public int PendingRespawnId { get; private set; } = -1;
    /// <summary>正在进行的读档换房请求编号；-1 表示没有。</summary>
    public int PendingLoadId { get; private set; } = -1;
    /// <summary>正在进行的新游戏换房请求编号；-1 表示没有。</summary>
    public int PendingNewGameId { get; private set; } = -1;

    public StringName CheckpointSpawn => string.IsNullOrEmpty(CheckpointSavePoint) ? NewGameSpawn : SavePointSpawn;

    public string SlotPath(int slot) => SavePaths.SlotPath(SaveDirectory, slot);

    public override void _EnterTree()
    {
        CheckpointRoom ??= NewGameRoom;
        if (Events.Instance == null) return;
        Events.Instance.SaveRequested += OnSaveRequested;
        Events.Instance.PlayerDied += RespawnAtCheckpoint;
        Events.Instance.RoomEntered += OnRoomEntered;
        Events.Instance.NewGameRequested += StartNewGame;
        Events.Instance.ContinueRequested += OnContinueRequested;
        Events.Instance.TravelFinished += OnTravelFinished;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.SaveRequested -= OnSaveRequested;
        Events.Instance.PlayerDied -= RespawnAtCheckpoint;
        Events.Instance.RoomEntered -= OnRoomEntered;
        Events.Instance.NewGameRequested -= StartNewGame;
        Events.Instance.ContinueRequested -= OnContinueRequested;
        Events.Instance.TravelFinished -= OnTravelFinished;
    }

    private void OnRoomEntered(string roomId) => CurrentRoom = roomId;
    private void OnSaveRequested(StringName savePointId) => SaveToDisk(savePointId);
    private void OnContinueRequested() => LoadFromDisk(0);

    /// <summary>新游戏：清空进度和 MetSys 状态，检查点回到起点，进入起点房间。</summary>
    public void StartNewGame()
    {
        GameSession.Reset(InitialAbilities);
        if (MetSysApi.Available)
        {
            MetSysApi.ResetState();
            MetSysApi.SetSaveData();
        }
        CheckpointRoom = NewGameRoom;
        CheckpointSavePoint = "";
        PendingRespawnId = -1;
        PendingLoadId = -1;
        Events.Instance?.EmitSignal(Events.SignalName.ProgressRestored);
        // GameStartFinished 等 World 回执“已到达”之后才发（见 OnTravelFinished）。
        PendingNewGameId = Events.NewRequestId();
        Events.Instance?.EmitSignal(Events.SignalName.TravelRequested, PendingNewGameId, NewGameRoom, NewGameSpawn);
    }

    public bool SaveToDisk(StringName savePointId, int slot = 0)
    {
        if (string.IsNullOrEmpty(CurrentRoom)) return Fail("还没有进入任何房间，不能存档。");
        CheckpointRoom = CurrentRoom;
        CheckpointSavePoint = savePointId.ToString();
        var data = new SaveData
        {
            MetSys = MetSysApi.Available && MetSysApi.HasSaveData ? MetSysApi.GetSaveData() : new GDictionary(),
            Progress = GameSession.Progress.ToSave(),
            Room = CheckpointRoom,
            SavePoint = CheckpointSavePoint,
            Meta = new GDictionary
            {
                ["savedAtUnix"] = (long)Time.GetUnixTimeFromSystem(),
                ["engine"] = Engine.GetVersionInfo()["string"],
            },
        };
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SaveDirectory));
        if (!data.WriteAtomic(SlotPath(slot), out string error)) return Fail(error);
        LastError = "";
        Events.Instance?.EmitSignal(Events.SignalName.GameSaved);
        return true;
    }

    public bool LoadFromDisk(int slot)
    {
        SaveData data = SaveData.ReadFrom(SlotPath(slot), out string error);
        if (data == null) return FailStart("读档失败：" + error);

        GameSession.Reset(InitialAbilities);
        if (MetSysApi.Available)
        {
            MetSysApi.ResetState();
            MetSysApi.SetSaveData(data.MetSys);
        }
        GameSession.Progress.FromSave(data.Progress);
        // FromSave 完全按存档恢复能力；旧存档里没有这个版本新加的初始能力（例如攀墙），在这里补齐。存档格式和版本号不变。
        if (InitialAbilities != null) GameSession.Progress.EnsureAbilities(InitialAbilities.Initial);
        // 只在进度完全恢复之后发一次（不在上面的 Reset 之后发），玩家的血量上限一步到位，不会先按空进度算一遍。
        Events.Instance?.EmitSignal(Events.SignalName.ProgressRestored);
        CheckpointRoom = data.Room;
        CheckpointSavePoint = data.SavePoint;
        LastError = "";
        // GameLoaded 等 World 回执“已到达”之后才发（见 OnTravelFinished）。
        PendingNewGameId = -1;
        PendingLoadId = Events.NewRequestId();
        Events.Instance?.EmitSignal(Events.SignalName.TravelRequested, PendingLoadId, CheckpointRoom, CheckpointSpawn);
        return true;
    }

    /// <summary>
    /// 死亡复活：不读盘，进度原样保留（D4）。
    /// 只发换房请求；等 World 回执“玩家已放到检查点”之后才发 CheckpointRespawned（Player 收到后回满血、回 Idle）。
    /// 换房失败或被拒绝时不发 CheckpointRespawned，只记录错误。复活进行中再次死亡不重复请求。
    /// </summary>
    public void RespawnAtCheckpoint()
    {
        if (PendingRespawnId >= 0) return;
        PendingRespawnId = Events.NewRequestId();
        Events.Instance?.EmitSignal(Events.SignalName.TravelRequested, PendingRespawnId, CheckpointRoom, CheckpointSpawn);
    }

    private void OnTravelFinished(int requestId, string roomId, bool success)
    {
        if (requestId == PendingRespawnId)
        {
            PendingRespawnId = -1;
            if (success && roomId == CheckpointRoom)
            {
                LastError = "";
                Events.Instance?.EmitSignal(Events.SignalName.CheckpointRespawned);
            }
            else Fail($"复活失败：没能到达检查点 {CheckpointRoom}（回执房间 {roomId}，success={success}）。");
        }
        else if (requestId == PendingLoadId)
        {
            PendingLoadId = -1;
            if (success && roomId == CheckpointRoom)
            {
                Events.Instance?.EmitSignal(Events.SignalName.GameLoaded, roomId);
                FinishStart();
            }
            else FailStart($"读档后没能进入房间 {CheckpointRoom}（房间不存在或换房失败；回执房间 {roomId}，success={success}）。");
        }
        else if (requestId == PendingNewGameId)
        {
            PendingNewGameId = -1;
            if (success && roomId == NewGameRoom) FinishStart();
            else FailStart($"新游戏没能进入起点房间 {NewGameRoom}（房间不存在或换房失败；回执房间 {roomId}，success={success}）。");
        }
    }

    private void FinishStart()
    {
        LastError = "";
        Events.Instance?.EmitSignal(Events.SignalName.GameStartFinished, true, "");
    }

    /// <summary>开局失败：记录错误并报告 GameStartFinished(false)。不写盘，存档文件保持原样。</summary>
    private bool FailStart(string error)
    {
        Fail(error);
        Events.Instance?.EmitSignal(Events.SignalName.GameStartFinished, false, error);
        return false;
    }

    private bool Fail(string error)
    {
        LastError = error;
        GD.PushError("SaveService：" + error);
        return false;
    }
}
