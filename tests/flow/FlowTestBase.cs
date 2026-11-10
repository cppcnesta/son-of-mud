using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Camera;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Save;
using SonOfMud.World;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>
/// 开局流程测试的公共底座：一律从正式入口 main_game.tscn 启动，测试里不手动创建玩家、不手动解锁能力。
/// 驱动节点先把自己挂到 root 下、从 CurrentScene 摘掉（和旧 MudRuntimeSuite 一样），所以切场景时它不会被释放。
/// 存档槽 0 的原有文件在开始时备份、结束时恢复；测试自己产生的存档在结束时删除。
/// 结果写到 tests/output/flow_&lt;名字&gt;_results.json（UI 测试用 ui_ 前缀），失败时退出码为 1。
/// </summary>
public abstract partial class FlowTestBase : Node
{
    public const string MainGameScene = "res://assets/scenes/world/main_game.tscn";
    /// <summary>主菜单场景：以 GameBoot 的设置为准（回主菜单、开局失败都切到这里）。</summary>
    public static string MenuScene => GD.Load<GameBootSettings>(GameBootSettings.DefaultPath).MenuScene;
    public const int Slot = 0;

    protected abstract string SuiteName { get; }
    /// <summary>结果文件名前缀：tests/output/&lt;前缀&gt;_&lt;名字&gt;_results.json。</summary>
    protected virtual string ResultPrefix => "flow";
    protected abstract Task RunChecks();

    private int _checks;
    private int _failures;
    private readonly GArray _lines = new();
    private byte[] _savedBackup;

    /// <summary>本测试收到的全部 GameStartFinished（按顺序）。</summary>
    protected readonly List<(bool success, string error)> Starts = new();

    protected Node MainGame;
    protected WorldGame World;
    protected SaveService Saves;
    protected RoomCamera Cam;
    protected GameBoot Boot;
    protected PlayerCharacter Player;

    protected static string SavePath => SavePaths.SlotPath(Slot);

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private async void Run()
    {
        try
        {
            if (GetParent() != GetTree().Root) Reparent(GetTree().Root);
            if (GetTree().CurrentScene == this) GetTree().CurrentScene = null;
            BackupSave();
            Check(Events.Instance != null && MetSysApi.Available, "前提：Events 和 MetSys autoload 都在");
            Events.Instance.GameStartFinished += OnGameStartFinished;
            await RunChecks();
        }
        catch (Exception exception)
        {
            Check(false, "未捕获异常：" + exception);
        }
        ReleaseInputs();
        if (Events.Instance != null) Events.Instance.GameStartFinished -= OnGameStartFinished;
        RestoreSave();
        Finish();
    }

    private void OnGameStartFinished(bool success, string error) => Starts.Add((success, error));

    protected void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) _failures++;
        string line = $"Tests: {(condition ? "PASS" : "FAIL")} {message}";
        _lines.Add(line);
        GD.Print(line);
    }

    private void Finish()
    {
        string dir = "res://tests/output";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        using (FileAccess report = FileAccess.Open($"{dir}/{ResultPrefix}_{SuiteName}_results.json", FileAccess.ModeFlags.Write))
        {
            var data = new GDictionary { ["suite"] = SuiteName, ["checks"] = _checks, ["failures"] = _failures, ["results"] = _lines };
            report?.StoreString(Json.Stringify(data, "\t"));
        }
        GD.Print($"Tests: {(_failures == 0 ? "PASS" : "FAIL")} {ResultPrefix}_{SuiteName} 总计{_checks}项，失败{_failures}项");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---------- 存档文件 ----------

    private void BackupSave()
    {
        _savedBackup = FileAccess.FileExists(SavePath) ? FileAccess.GetFileAsBytes(SavePath) : null;
        DeleteSave();
    }

    private void RestoreSave()
    {
        DeleteSave();
        if (_savedBackup == null) return;
        using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        file?.StoreBuffer(_savedBackup);
    }

    protected static void DeleteSave()
    {
        foreach (string path in new[] { SavePath, SavePath + ".tmp" })
            if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }

    protected static void WriteSaveBytes(byte[] bytes)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SavePaths.DefaultDirectory));
        using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        file.StoreBuffer(bytes);
    }

    protected static byte[] ReadSaveBytes() => FileAccess.FileExists(SavePath) ? FileAccess.GetFileAsBytes(SavePath) : null;

    protected static bool SameBytes(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);

    // ---------- 进入正式入口 ----------

    /// <summary>像主菜单那样：设好 PendingStart，切到 main_game.tscn，等到收到 GameStartFinished（最多 maxFrames 帧）。</summary>
    protected async Task<bool> EnterMainGame(StartMode mode, int maxFrames = 600)
    {
        ReleaseInputs();
        int before = Starts.Count;
        GameSession.PendingStart = mode;
        Error error = GetTree().ChangeSceneToFile(MainGameScene);
        if (error != Error.Ok)
        {
            Check(false, $"切到 {MainGameScene} 失败：{error}");
            return false;
        }
        bool finished = await WaitUntil(() => Starts.Count > before, maxFrames);
        BindMainGame();
        return finished;
    }

    /// <summary>从当前场景取正式入口里的各个节点（场景不是 main_game 时全部为 null）。</summary>
    protected void BindMainGame()
    {
        Node scene = GetTree().CurrentScene;
        bool isMain = scene != null && scene.SceneFilePath == MainGameScene;
        MainGame = isMain ? scene : null;
        World = MainGame?.GetNodeOrNull<WorldGame>("Game/WorldGame");
        Saves = MainGame?.GetNodeOrNull<SaveService>("Game/SaveService");
        Cam = MainGame?.GetNodeOrNull<RoomCamera>("Game/RoomCamera");
        Boot = MainGame?.GetNodeOrNull<GameBoot>("GameBoot");
        Player = MainGame?.GetNodeOrNull<PlayerCharacter>("Player");
    }

    /// <summary>等开局的换房（淡入淡出）全部结束、输入锁解除。</summary>
    protected async Task<bool> WaitSettled(int maxFrames = 400)
    {
        bool ok = await WaitUntil(() => World != null && !World.IsTransitioning && !InputLock.IsLocked, maxFrames);
        await Frames(10);
        return ok;
    }

    // ---------- 通用 ----------

    protected async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    protected async Task<bool> WaitUntil(Func<bool> condition, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition()) return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return condition();
    }

    /// <summary>按下再松开，返回时这次按键已经被处理过（见 LabSuiteBase.Tap 的说明）。</summary>
    protected async Task Tap(StringName action)
    {
        Input.ActionPress(action);
        await Frames(1);
        Input.ActionRelease(action);
        await Frames(1);
    }

    protected static void ReleaseInputs()
    {
        foreach (StringName action in InputMap.GetActions()) Input.ActionRelease(action);
    }

    /// <summary>整棵场景树里所有的 PlayerCharacter。</summary>
    protected List<PlayerCharacter> AllPlayers()
    {
        var found = new List<PlayerCharacter>();
        void Walk(Node node)
        {
            if (node is PlayerCharacter p) found.Add(p);
            foreach (Node child in node.GetChildren()) Walk(child);
        }
        Walk(GetTree().Root);
        return found;
    }

    protected static string AbilityList() =>
        string.Join(",", GameSession.Progress.Abilities.Select(a => a.ToString()).OrderBy(a => a, StringComparer.Ordinal));

    protected bool NoPauseOrLock() => !PauseService.IsPaused && !GetTree().Paused && !InputLock.IsLocked;

    protected static T FindChild<T>(Node node) where T : class
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is T found) return found;
            T deeper = FindChild<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }
}
