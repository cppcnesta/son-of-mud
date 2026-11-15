using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative;
using SonOfMud.World;

/// <summary>
/// 「从这里试玩」（计划_从这里试玩 第 2 版）：编辑器按钮做的就是“写一次性请求 → 启动试玩场景 trial_game.tscn”，这里照做。
/// ① 补齐算法；② 残留的请求文件不影响正式入口；③ 试玩开局（房间、出生点、补齐变量、请求文件删掉、角上字样）；
/// ④ 复活：没存档回试玩起点，用过存档点回存档点，都不写存档文件；⑤ 试玩里重开回同一个试玩起点；
/// ⑥ 回主菜单后从菜单开新游戏是正常的；⑦ 白纸试玩门是关的；⑧ 试玩 S1_01：补齐不播开场，白纸播开场。
/// </summary>
public partial class TrialTest : NarrativeFlowBase
{
    protected override string SuiteName => "narrative_trial";

    private static readonly string[] Main3 = { "boss_keeper", "flood_done", "boss_dragon", "districts_done" };
    private static readonly string[] Tablets = Enumerable.Range(1, 7).Select(k => $"tablet.gate_{k}").ToArray();

    protected override async Task RunChecks()
    {
        Listen();
        try
        {
            FillChecks();
            await ResidualRequest();
            await TrialStart();
            await RespawnAndSave();
            await RestartInTrial();
            await BackToMenu();
            await BlankTrial();
            await OpeningChecks();
        }
        finally
        {
            Unlisten();
            DebugStart.DeleteRequestFile();
            GameSession.Trial = null;
            NarrativeDirector.AutoOpeningEnabled = NarrativeOpening;
        }
    }

    private static bool Flag(string key) => GameSession.Progress.Flag(key);
    private string Room => World?.CurrentRoomDef?.Id;
    private Vector2 SpawnAt(string room, string spawn) =>
        World.CurrentRoom.GetNode<Node2D>($"Spawns/{spawn}").GlobalPosition;

    /// <summary>像编辑器按钮那样：写请求、切到试玩场景，等开局结束。</summary>
    private async Task<bool> EnterTrial(string room, string spawn, bool fill)
    {
        DebugStart.WriteRequest(room, spawn, fill);
        // 每次按按钮都是编辑器新开的游戏进程，内存里没有上一次的试玩（同一进程里留着的话会被当成“试玩里重开”）
        GameSession.Trial = null;
        return await EnterScene(DebugStart.TrialScene);
    }

    private async Task<bool> EnterScene(string path)
    {
        ReleaseInputs();
        int before = Starts.Count;
        GameSession.PendingStart = StartMode.NewGame;
        if (GetTree().ChangeSceneToFile(path) != Error.Ok) return false;
        bool finished = await WaitUntil(() => Starts.Count > before, 600);
        BindMainGame();
        BindNarrative();
        // 试玩起点有“走进来自动播”的剧情（S4_02 七道门旁白）时先看完；S1_01 的开场留给开场检查自己处理
        await Frames(10);
        if (Narr != null && Narr.MainActive && Narr.MainLabel != "开场") await AdvanceUntil(() => !Narr.MainActive, 40);
        await WaitSettled();
        return finished && Starts[^1].success;
    }

    // ① 补齐算法
    private void FillChecks()
    {
        WorldLayout layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        string Got(string room) => string.Join(",", DebugStart.FillFlags(layout, room));
        Check(Got("S1_01") == "seen.opening", $"补齐 S1_01：只有 seen.opening（{Got("S1_01")}）");
        Check(Got("S1_06") == "boss_keeper,seen.opening" && Got("S1_08") == "boss_keeper,seen.opening",
            $"补齐 S1_06、回溯房间 S1_08：boss_keeper + seen.opening（{Got("S1_06")} / {Got("S1_08")}）");
        Check(Got("S4_02") == string.Join(",", Main3.Append("seen.opening")),
            $"补齐 S4_02：前三张地图的主线条件，没有七块泥板（{Got("S4_02")}）");
        Check(Got("S4_03") == string.Join(",", Main3.Concat(Tablets).Append("seen.opening")),
            $"补齐 S4_03：再加七块泥板 tablet.gate_1–7（{Got("S4_03")}）");
        Check(DebugStart.FillFlags(layout, "NO_SUCH_ROOM").Count == 0, "补齐不存在的房间：什么都不设");

        // 检查器里“试玩出生点”下拉框的选项：这个房间的全部出生点，Default 排第一
        var tower = GD.Load<PackedScene>("res://assets/scenes/world/s4/S4_04.tscn").Instantiate<RoomRoot>();
        List<string> spawns = tower.TrialSpawns();
        bool listed = tower.GetPropertyList().Any(p => p["name"].AsString() == "trial_spawn" && p["hint_string"].AsString() == string.Join(",", spawns));
        tower.Free();
        Check(spawns.FirstOrDefault() == "Default" && spawns.Contains("Save") && spawns.Contains("From_S4_03") && spawns.Contains("From_S4_05") && listed,
            $"检查器：S4_04 的试玩出生点下拉框列出全部出生点，Default 排第一（{string.Join(",", spawns)}）");
    }

    // ② 残留请求不影响正式入口
    private async Task ResidualRequest()
    {
        DebugStart.WriteRequest("S4_02", "From_S4_03", true);
        bool started = await EnterMainGame(StartMode.NewGame);
        await WaitSettled();
        Check(started && Room == "S1_01" && GameSession.Trial == null && Boot?.TrialBadge == null && DebugStart.HasRequestFile && !Flag("boss_keeper"),
            $"留着一个试玩请求文件，从正式入口开新游戏：照旧在 S1_01、不是试玩、没有试玩字样，请求文件没有被读（在 {Room}）");
        DebugStart.DeleteRequestFile();
    }

    // ③ 试玩开局
    private async Task TrialStart()
    {
        bool started = await EnterTrial("S4_02", "From_S4_03", true);
        Vector2 at = Room == "S4_02" ? SpawnAt("S4_02", "From_S4_03") : Vector2.Zero;
        Check(started && Room == "S4_02" && Player.GlobalPosition.DistanceTo(at) < 40f,
            $"试玩 S4_02 / From_S4_03：开局成功，人在 S4_02 的 From_S4_03（{Room} {Player?.GlobalPosition}，出生点 {at}）");
        Check(Main3.All(Flag) && !Tablets.Any(Flag) && Flag("seen.opening") && !DebugStart.HasRequestFile,
            "补齐：boss_keeper、flood_done、boss_dragon、districts_done、seen.opening 为真，七块泥板为假；请求文件已删");
        Check(GameSession.Trial != null && Boot?.TrialBadge?.Text == "试玩：S4_02 / From_S4_03（补齐进度）" && Boot.TrialBadge.IsVisibleInTree(),
            $"屏幕角上显示“{Boot?.TrialBadge?.Text}”");
    }

    private async Task<bool> Die()
    {
        int respawned = 0;
        void OnRespawn() => respawned++;
        Events.Instance.CheckpointRespawned += OnRespawn;
        Events.Instance.EmitSignal(Events.SignalName.PlayerDied);
        bool ok = await WaitUntil(() => respawned > 0 && !World.IsTransitioning, 400);
        Events.Instance.CheckpointRespawned -= OnRespawn;
        await WaitSettled();
        return ok;
    }

    // ④ 复活和存档
    private async Task RespawnAndSave()
    {
        await Teleport("S4_02", "Default");
        bool back = await Die();
        Check(back && Room == "S4_02" && Player.GlobalPosition.DistanceTo(SpawnAt("S4_02", "From_S4_03")) < 40f,
            $"试玩里还没用过存档点就死亡：回到试玩起点 S4_02 / From_S4_03（{Room} {Player.GlobalPosition}）");

        await Teleport("S4_04", "Save");
        int saved = 0;
        void OnSaved() => saved++;
        Events.Instance.GameSaved += OnSaved;
        Events.Instance.EmitSignal(Events.SignalName.SaveRequested, "S4_04");
        await Frames(2);
        Events.Instance.GameSaved -= OnSaved;
        Check(saved == 1 && Saves.CheckpointRoom == "S4_04" && ReadSaveBytes() == null,
            "试玩里用 S4_04 的存档点：照常提示已存档、检查点更新成 S4_04，但没有写存档文件");
        await Teleport("S4_02", "Default");
        bool atSave = await Die();
        Check(atSave && Room == "S4_04" && Player.GlobalPosition.DistanceTo(SpawnAt("S4_04", "Save")) < 40f && ReadSaveBytes() == null,
            $"用过存档点后再死亡：回到 S4_04 的存档点（{Room}），存档文件仍然没有");
    }

    // ⑤ 试玩里重开
    private async Task RestartInTrial()
    {
        GameSession.Progress.SetVar("tablet.gate_1", true);
        int before = Starts.Count;
        Events.Instance.EmitSignal(Events.SignalName.RestartRunRequested);
        bool restarted = await WaitUntil(() => Starts.Count > before, 600);
        BindMainGame();
        BindNarrative();
        await WaitSettled();
        Check(restarted && GetTree().CurrentScene?.SceneFilePath == DebugStart.TrialScene && Room == "S4_02"
              && Player.GlobalPosition.DistanceTo(SpawnAt("S4_02", "From_S4_03")) < 40f && !Flag("tablet.gate_1") && Main3.All(Flag) && Boot?.TrialBadge != null,
            $"试玩里重开（回去睡）：重新载入试玩场景，回到同一个试玩起点，进度重新补齐（{Room}）");
    }

    // ⑥ 回主菜单后正常
    private async Task BackToMenu()
    {
        Events.Instance.EmitSignal(Events.SignalName.QuitToMenuRequested);
        await WaitUntil(() => GetTree().CurrentScene?.SceneFilePath == MenuScene, 300);
        Check(GameSession.Trial == null, "回主菜单：试玩结束");
        bool started = await EnterMainGame(StartMode.NewGame);
        await WaitSettled();
        Check(started && Room == "S1_01" && Boot?.TrialBadge == null && !Flag("boss_keeper"), $"之后从菜单开新游戏：正常在 S1_01，没有试玩字样（{Room}）");
    }

    // ⑦ 白纸
    private async Task BlankTrial()
    {
        bool started = await EnterTrial("S1_05", "Default", false);
        GateBarrier gate = World?.CurrentRoom?.GetNodeOrNull<GateBarrier>("Objects/GateToS1_06");
        Check(started && Room == "S1_05" && !Flag("boss_keeper") && !Flag("seen.opening") && gate != null && !gate.IsOpen
              && Boot?.TrialBadge?.Text == "试玩：S1_05 / Default（白纸）",
            $"白纸试玩 S1_05：boss_keeper 为假，守坑人的门关着（{Room}）");
        bool filled = await EnterTrial("S1_06", "Default", true);
        Check(filled && Room == "S1_06" && Flag("boss_keeper"), "补齐试玩 S1_06：boss_keeper 为真");
    }

    // ⑧ S1_01 的开场
    private async Task OpeningChecks()
    {
        NarrativeDirector.AutoOpeningEnabled = true;
        int before = Started.Count;
        bool filled = await EnterTrial("S1_01", "Default", true);
        await Frames(30);
        Check(filled && Room == "S1_01" && Started.Count == before && !Narr.MainActive, "试玩 S1_01、补齐：当作开场看过了，不播开场");

        before = Started.Count;
        bool blank = await EnterTrial("S1_01", "Default", false);
        bool opening = await WaitUntil(() => Narr != null && Narr.MainActive && Narr.MainLabel == "开场", 120);
        Check(blank && Room == "S1_01" && opening, "试玩 S1_01、白纸：和新游戏一样播开场");
        if (Narr != null && Narr.MainActive) await AdvanceUntil(() => !Narr.MainActive, 60);
        NarrativeDirector.AutoOpeningEnabled = NarrativeOpening;
    }
}
