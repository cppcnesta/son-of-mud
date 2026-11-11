using System;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Camera;
using SonOfMud.Core;
using SonOfMud.Save;
using SonOfMud.World;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>
/// World / Save 测试的公共部分：断言计数、结果写到 tests/output/world_&lt;名字&gt;_results.json、搭建 game.tscn + TestMover。
/// 子类重写 SuiteName 和 RunChecks。失败时进程退出码为 1。
/// </summary>
public partial class WorldTestBase : Node
{
    public const string GameScene = "res://assets/scenes/world/game.tscn";

    protected virtual string SuiteName => "world_base";

    private int _checks;
    private int _failures;
    private readonly GArray _lines = new();

    protected Node Game;
    protected WorldGame World;
    protected SaveService Saves;
    protected RoomCamera Cam;
    protected TestMover Mover;

    public override void _Ready() => Callable.From(Run).CallDeferred();

    protected virtual Task RunChecks() => Task.CompletedTask;

    protected void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) _failures++;
        string line = $"Tests: {(condition ? "PASS" : "FAIL")} {message}";
        _lines.Add(line);
        GD.Print(line);
    }

    private async void Run()
    {
        try
        {
            Check(Events.Instance != null && MetSysApi.Available, "前提：Events 和 MetSys autoload 都在");
            await RunChecks();
        }
        catch (Exception exception)
        {
            Check(false, "未捕获异常：" + exception);
        }
        Finish();
    }

    private void Finish()
    {
        string dir = "res://tests/output";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        using var report = FileAccess.Open($"{dir}/world_{SuiteName}_results.json", FileAccess.ModeFlags.Write);
        var data = new GDictionary { ["suite"] = SuiteName, ["checks"] = _checks, ["failures"] = _failures, ["results"] = _lines };
        report?.StoreString(Json.Stringify(data, "\t"));
        GD.Print($"Tests: {(_failures == 0 ? "PASS" : "FAIL")} {SuiteName} 总计{_checks}项，失败{_failures}项");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---------- 搭建 ----------

    /// <summary>清空进度和 MetSys，载入 game.tscn，再放一个 TestMover（它在 _Ready 里发 PlayerSpawned）。</summary>
    protected async Task SetUpGame(bool withFadeOverlay = true)
    {
        GameSession.Reset();
        MetSysApi.ResetState();
        MetSysApi.SetSaveData();
        Game = GD.Load<PackedScene>(GameScene).Instantiate();
        if (!withFadeOverlay) Game.GetNode("FadeOverlay").Free();
        AddChild(Game);
        World = Game.GetNode<WorldGame>("WorldGame");
        Saves = Game.GetNode<SaveService>("SaveService");
        Cam = Game.GetNode<RoomCamera>("RoomCamera");
        Mover = new TestMover { Name = "Mover" };
        Game.AddChild(Mover);
        await Frames(1);
    }

    protected async Task TearDownGame()
    {
        Game?.QueueFree();
        Game = null;
        await Frames(2);
    }

    protected async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>每个物理帧检查一次条件，最多等 maxFrames 帧。</summary>
    protected async Task<bool> WaitUntil(Func<bool> condition, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition()) return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return condition();
    }

    /// <summary>玩家在整张地图上的像素坐标（房间左上角格 × 格子尺寸 + 房间内坐标）。</summary>
    protected Vector2 MapPixel()
    {
        Vector2 cell = MetSysApi.CellSize;
        Vector2I origin = World.CurrentRoomDef.Origin;
        return new Vector2(origin.X * cell.X, origin.Y * cell.Y) + Mover.GlobalPosition;
    }

    protected SpawnPoint Spawn(string id) => World.CurrentRoom.FindSpawn(id);

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
