using Godot;
using GDictionary = Godot.Collections.Dictionary;

public partial class MudRoomTravel : Node
{
    public static MudRoomTravel Instance { get; private set; }
    private bool _isChangingRoom;
    public float ArrivalLock { get; set; }
    public GDictionary Transient { get; private set; } = new();
    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }
    public override void _Process(double delta) => ArrivalLock = Mathf.Max(0f, ArrivalLock - (float)delta);
    public string RoomPath(string roomId) => roomId == "TEST" ? "res://assets/scenes/whitebox/tests/form_lab.tscn" :
        $"res://assets/scenes/whitebox/levels/{roomId}.tscn";
    public void EnterRoom(string roomId, string spawnName = "Start", bool carry = true)
    {
        if (!carry) Transient.Clear();
        GoToRoom(RoomPath(roomId), spawnName, carry);
    }
    public void GoToRoom(string scenePath, StringName spawnName, bool carry = true)
    {
        if (_isChangingRoom) return;
        if (string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(spawnName.ToString()) || !ResourceLoader.Exists(scenePath, "PackedScene"))
        { GD.PushError($"房间路径/出生点无效 {scenePath} / {spawnName}"); return; }
        _isChangingRoom = true;
        if (carry && GetTree().GetFirstNodeInGroup("player") is MudPlayer current) Transient = current.ExportTransient();
        Callable.From(() => PerformChange(scenePath, spawnName)).CallDeferred();
    }
    private async void PerformChange(string scenePath, StringName spawnName)
    {
        try
        {
            var packed = GD.Load<PackedScene>(scenePath);
            // The packed-scene overload preserves the deferred scene lifecycle.
            var error = GetTree().ChangeSceneToPacked(packed);
            if (error != Error.Ok) { GD.PushError($"切场失败 {error}"); return; }
            await ToSignal(GetTree(), SceneTree.SignalName.SceneChanged);
            var next = GetTree().CurrentScene;
            var player = next.GetNodeOrNull<MudPlayer>("Player");
            var spawn = next.GetNodeOrNull<Marker2D>("Spawns/" + spawnName);
            if (player == null || spawn == null)
            { GD.PushError($"缺少Player或出生点 {scenePath} / {spawnName}"); return; }
            player.GlobalPosition = spawn.GlobalPosition;
            player.Velocity = Vector2.Zero;
            if (Transient.Count > 0) player.RestoreTransient(Transient);
            else player.ClearAirContext();
            Transient.Clear();
            player.ResetPhysicsInterpolation();
            var camera = player.GetNodeOrNull<Camera2D>("Camera2D");
            if (camera != null) { camera.ResetSmoothing(); camera.ForceUpdateScroll(); }
            ArrivalLock = 0.45f;
            if (next is MudWhiteboxRoom room && room.RoomId != "TEST")
            {
                MudProgress.Instance.LastRoom = room.RoomId;
                MudProgress.Instance.LastSpawn = spawnName.ToString();
                MudProgress.Instance.SaveGame();
            }
            GD.Print($"房间载入 {scenePath.GetFile()} / {spawnName}");
        }
        catch (System.Exception exception) { GD.PushError($"房间切换失败: {exception}"); }
        finally { _isChangingRoom = false; }
    }
    public void Retry()
    {
        Transient.Clear();
        var progress = MudProgress.Instance;
        if (progress.TestMode) EnterRoom("TEST", "Start", false);
        else EnterRoom(progress.CheckpointRoom, progress.CheckpointSpawn, false);
    }
}
