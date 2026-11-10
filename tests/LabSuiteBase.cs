using System;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>
/// 新玩家 / 战斗测试的公共底座：在真实物理帧里加载测试场（player_lab / combat_lab），
/// 用 Input.ActionPress 模拟按键，结果写到 tests/output/&lt;ReportName&gt;.json，失败时退出码为 1。
/// </summary>
public abstract partial class LabSuiteBase : Node
{
    public const float Dt = 1f / 60f;

    private int _checks;
    private int _failures;
    private readonly GArray _lines = new();
    private readonly GDictionary _metrics = new();

    protected Node Lab { get; private set; }
    protected PlayerCharacter Player { get; private set; }

    protected abstract string ReportName { get; }
    protected abstract Task RunChecks();

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private async void Run()
    {
        try
        {
            await RunChecks();
        }
        catch (Exception exception)
        {
            Check(false, "未捕获异常：" + exception);
        }
        ReleaseInputs();
        Finish();
    }

    protected void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) _failures++;
        string line = $"Tests: {(condition ? "PASS" : "FAIL")} {message}";
        _lines.Add(line);
        GD.Print(line);
    }

    protected void Metric(string key, Variant value) => _metrics[key] = value;

    protected async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    protected static int FramesFor(float seconds) => Mathf.RoundToInt(seconds * 60f);

    protected static void ReleaseInputs()
    {
        foreach (StringName action in InputMap.GetActions()) Input.ActionRelease(action);
    }

    protected static void Press(StringName action) => Input.ActionPress(action);
    protected static void Release(StringName action) => Input.ActionRelease(action);

    /// <summary>
    /// 按下再松开，返回时这次按键已经被处理过。
    /// 测试协程在物理帧开头恢复执行；在这里按下的键，Godot 在下一个物理帧才报告 IsActionJustPressed，
    /// 所以等两帧：第一帧后松开，第二帧（按键被处理的那一帧）结束后返回。
    /// </summary>
    protected async Task Tap(StringName action)
    {
        Input.ActionPress(action);
        await Frames(1);
        Input.ActionRelease(action);
        await Frames(1);
    }

    protected async Task LoadLab(string path)
    {
        if (Lab != null)
        {
            Lab.QueueFree();
            await Frames(2);
        }
        ReleaseInputs();
        Lab = GD.Load<PackedScene>(path).Instantiate();
        AddChild(Lab);
        Player = Lab.GetNode<PlayerCharacter>("Player");
        await Frames(4);
    }

    /// <summary>复位玩家（位置、形态、计时、动作），清掉输入锁，等几帧让物理稳定。</summary>
    protected async Task Reset(Vector2 at, int form = 0, int settle = 4)
    {
        ReleaseInputs();
        InputLock.Reset();
        Player.ResetForLab(at, form);
        await Frames(settle);
    }

    protected static Hit MakeHit(string id, float damage, Vector2 source, float stun = 0f, bool blockable = true, bool counter = false) => new()
    {
        AttackId = id, Damage = damage, SourcePosition = source, StunSeconds = stun, Blockable = blockable, CounterOnBlock = counter,
    };

    private void Finish()
    {
        string dir = "res://tests/output";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        using (var report = FileAccess.Open($"{dir}/{ReportName}.json", FileAccess.ModeFlags.Write))
        {
            var data = new GDictionary { ["checks"] = _checks, ["failures"] = _failures, ["metrics"] = _metrics, ["results"] = _lines };
            report?.StoreString(Json.Stringify(data, "\t"));
        }
        GD.Print($"Tests: {(_failures == 0 ? "PASS" : "FAIL")} {ReportName} 总计{_checks}项，失败{_failures}项");
        InputLock.Reset();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }
}
