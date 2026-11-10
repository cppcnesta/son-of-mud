using Godot;
using SonOfMud.Core;
using GDictionary = Godot.Collections.Dictionary;

namespace SonOfMud.Save;

/// <summary>
/// 唯一的存档服务（规格第 3 节），三个操作各自独立：
/// - SaveToDisk(savePointId)：在存档点交互时写盘，同时更新“最近存档点”。
/// - LoadFromDisk(slot)：清空内存 → 读盘 → 恢复 MetSys 和进度 → 进入存档点所在房间。
/// - RespawnAtCheckpoint()：玩家死亡（D4）。不读盘，保留内存里的进度，回到最近存档点。
/// 只依赖 Core：换房通过 Events.TravelRequested 交给 World，当前房间从 Events.RoomEntered 得知。
/// </summary>
[GlobalClass]
public partial class SaveService : Node
{
    [Export] public string SaveDirectory { get; set; } = "user://";
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

    public StringName CheckpointSpawn => string.IsNullOrEmpty(CheckpointSavePoint) ? NewGameSpawn : SavePointSpawn;

    public string SlotPath(int slot) => SaveDirectory.PathJoin($"save_{slot}.sav");

    public override void _EnterTree()
    {
        CheckpointRoom ??= NewGameRoom;
        if (Events.Instance == null) return;
        Events.Instance.SaveRequested += OnSaveRequested;
        Events.Instance.PlayerDied += RespawnAtCheckpoint;
        Events.Instance.RoomEntered += OnRoomEntered;
        Events.Instance.NewGameRequested += StartNewGame;
        Events.Instance.ContinueRequested += OnContinueRequested;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.SaveRequested -= OnSaveRequested;
        Events.Instance.PlayerDied -= RespawnAtCheckpoint;
        Events.Instance.RoomEntered -= OnRoomEntered;
        Events.Instance.NewGameRequested -= StartNewGame;
        Events.Instance.ContinueRequested -= OnContinueRequested;
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
        Events.Instance?.EmitSignal(Events.SignalName.TravelRequested, NewGameRoom, NewGameSpawn);
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
        if (data == null) return Fail(error);

        GameSession.Reset(InitialAbilities);
        if (MetSysApi.Available)
        {
            MetSysApi.ResetState();
            MetSysApi.SetSaveData(data.MetSys);
        }
        GameSession.Progress.FromSave(data.Progress);
        CheckpointRoom = data.Room;
        CheckpointSavePoint = data.SavePoint;
        LastError = "";
        Events.Instance?.EmitSignal(Events.SignalName.TravelRequested, CheckpointRoom, CheckpointSpawn);
        Events.Instance?.EmitSignal(Events.SignalName.GameLoaded, CheckpointRoom);
        return true;
    }

    /// <summary>死亡复活：不读盘，进度原样保留（D4）。Player 收到 CheckpointRespawned 后回满血、回 Idle。</summary>
    public void RespawnAtCheckpoint()
    {
        Events.Instance?.EmitSignal(Events.SignalName.TravelRequested, CheckpointRoom, CheckpointSpawn);
        Events.Instance?.EmitSignal(Events.SignalName.CheckpointRespawned);
    }

    private bool Fail(string error)
    {
        LastError = error;
        GD.PushError("SaveService：" + error);
        return false;
    }
}
