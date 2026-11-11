using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>
/// Core 第 1 轮的验收测试：暂停嵌套与释放、输入锁独立、变量类型往返与比较、状态机切换顺序、MetSys 桥接。
/// 结果写到 tests/output/core_results.json，失败时进程退出码为 1。
/// </summary>
public partial class CoreSuite : Node
{
    private int _checks;
    private int _failures;
    private readonly GArray _lines = new();

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private void Check(bool condition, string message)
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
            Check(Events.Instance != null, "Events autoload 存在且 Instance 已设置");
            PauseChecks();
            InputLockChecks();
            PauseWriteScan();
            StoryValueChecks();
            ProgressChecks();
            await StateMachineChecks();
            MetSysChecks();
        }
        catch (Exception exception)
        {
            Check(false, "未捕获异常：" + exception);
        }
        Finish();
    }

    // ---------- 暂停 ----------

    private void PauseChecks()
    {
        PauseService.Reset();
        var events = new List<bool>();
        Callable recorder = Callable.From<bool>(paused => events.Add(paused));
        Events.Instance.Connect(Events.SignalName.PauseChanged, recorder);

        PauseService.Request(PauseService.Narrative, true);
        Check(GetTree().Paused && PauseService.IsPaused, "暂停：剧情请求后 SceneTree.Paused=true");
        PauseService.Request(PauseService.Map, true);
        Check(GetTree().Paused, "暂停：剧情中再开地图，仍暂停");
        PauseService.Request(PauseService.Map, false);
        Check(GetTree().Paused, "暂停：关地图后剧情仍未结束，保持暂停（审查反例）");
        PauseService.Request(PauseService.Map, false);
        Check(GetTree().Paused, "暂停：同一来源重复释放，不会解除别的来源");
        PauseService.Request(PauseService.Narrative, true);
        PauseService.Request(PauseService.Narrative, false);
        Check(!GetTree().Paused && !PauseService.IsPaused, "暂停：同一来源重复请求只算一次，释放一次即解除");
        Check(events.Count == 2 && events[0] && !events[1], $"暂停：PauseChanged 只在整体状态变化时发出（实际 {events.Count} 次）");

        PauseService.Request(PauseService.PauseMenu, true);
        PauseService.Request(PauseService.Map, true);
        PauseService.Reset();
        Check(!GetTree().Paused && !PauseService.IsRequestedBy(PauseService.PauseMenu), "暂停：Reset 清掉所有来源并恢复");

        Events.Instance.Disconnect(Events.SignalName.PauseChanged, recorder);
    }

    private void InputLockChecks()
    {
        PauseService.Reset();
        InputLock.Reset();
        var events = new List<bool>();
        Callable recorder = Callable.From<bool>(locked => events.Add(locked));
        Events.Instance.Connect(Events.SignalName.InputLockChanged, recorder);

        InputLock.Request(InputLock.RoomTransition, true);
        Check(InputLock.IsLocked && !GetTree().Paused, "输入锁：锁输入不会暂停世界");
        PauseService.Request(PauseService.PauseMenu, true);
        InputLock.Request(InputLock.RoomTransition, false);
        Check(!InputLock.IsLocked && GetTree().Paused, "输入锁：解锁不影响暂停，两者互相独立");
        PauseService.Reset();
        InputLock.Request(InputLock.RoomTransition, true);
        InputLock.Request(InputLock.Narrative, true);
        InputLock.Request(InputLock.RoomTransition, false);
        Check(InputLock.IsLocked, "输入锁：多个来源时，释放其中一个仍保持锁定");
        InputLock.Request(InputLock.Narrative, false);
        Check(!InputLock.IsLocked, "输入锁：全部释放后解锁");
        Check(events.Count == 4, $"输入锁：InputLockChanged 只在整体状态变化时发出（实际 {events.Count} 次）");

        Events.Instance.Disconnect(Events.SignalName.InputLockChanged, recorder);
    }

    /// <summary>规格要求：只有 PauseService 能写 SceneTree.Paused。扫描全部 C# 源码。</summary>
    private void PauseWriteScan()
    {
        Check(WritesPaused("GetTree().Paused = true;") && WritesPaused("tree.Paused=false;"), "扫描器自检：真正写 Paused 的代码能被发现");
        Check(!WritesPaused("var p = GetTree().Paused == true; // x.Paused = y\nvar s = \"tree.Paused=true\";"), "扫描器自检：比较、注释、字符串里的 Paused= 不算");

        var offenders = new List<string>();
        foreach (string root in new[] { "res://assets/scripts", "res://tests" })
            ScanDir(root, path =>
            {
                if (path.EndsWith("/core/PauseService.cs")) return;
                if (WritesPaused(FileAccess.GetFileAsString(path))) offenders.Add(path);
            });
        Check(offenders.Count == 0, "暂停：除 PauseService 外没有代码写 Paused" + (offenders.Count > 0 ? "，违规：" + string.Join(", ", offenders) : ""));
    }

    private static readonly Regex StringsAndComments = new(@"@""(?:[^""]|"""")*""|""(?:[^""\\\n]|\\.)*""|//[^\n]*|/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex PausedWrite = new(@"\.Paused\s*=(?!=)");

    /// <summary>去掉字符串和注释后，检查是否有对 .Paused 的赋值。</summary>
    private static bool WritesPaused(string source) =>
        PausedWrite.IsMatch(StringsAndComments.Replace(source, ""));

    private static void ScanDir(string dir, Action<string> onFile)
    {
        foreach (string file in DirAccess.GetFilesAt(dir))
            if (file.EndsWith(".cs")) onFile(dir + "/" + file);
        foreach (string sub in DirAccess.GetDirectoriesAt(dir))
            ScanDir(dir + "/" + sub, onFile);
    }

    // ---------- 变量类型 ----------

    private void StoryValueChecks()
    {
        Variant yes = StoryValue.Parse("是");
        Check(yes.VariantType == Variant.Type.Bool && yes.AsBool(), "剧本值：「是」解析为 bool true");
        Check(StoryValue.Parse(" TRUE ").AsBool() && StoryValue.Parse("true").VariantType == Variant.Type.Bool, "剧本值：true 不区分大小写、去空白");
        Variant no = StoryValue.Parse("否");
        Check(no.VariantType == Variant.Type.Bool && !no.AsBool() && !StoryValue.Parse("False").AsBool(), "剧本值：「否」/ false 解析为 bool false");
        Variant n = StoryValue.Parse("42");
        Check(n.VariantType == Variant.Type.Int && n.AsInt64() == 42 && StoryValue.Parse("-3").AsInt64() == -3, "剧本值：整数解析为 int");
        Variant s = StoryValue.Parse("穆尔图库尔");
        Check(s.VariantType == Variant.Type.String && s.AsString() == "穆尔图库尔", "剧本值：其余解析为 string");
        Check(StoryValue.Parse("1.5").VariantType == Variant.Type.String, "剧本值：小数不当数字，按 string 处理");

        Check(StoryValue.AreEqual(StoryValue.Parse("是"), StoryValue.Parse("true"), out bool m1) && !m1, "比较：「是」与 true 相等");
        Check(StoryValue.AreEqual(StoryValue.Parse("是"), true, out _), "比较：剧本「是」与门写入的 bool true 相等");
        Check(!StoryValue.AreEqual("true", true, out bool m2) && m2, "比较：字符串 \"true\" 与 bool true 不相等，并标记类型不同");
        Check(!StoryValue.AreEqual(1, true, out bool m3) && m3, "比较：int 1 与 bool true 不相等，并标记类型不同");
        Check(!StoryValue.AreEqual("1", 1, out bool m4) && m4, "比较：字符串 \"1\" 与 int 1 不相等，并标记类型不同");
        Check(StoryValue.AreEqual(new StringName("a"), "a", out bool m5) && !m5, "比较：StringName 与 string 视为同类");
        Check(StoryValue.AreEqual(3, 3L, out _), "比较：int32 与 int64 视为同类");
    }

    private void ProgressChecks()
    {
        var store = new ProgressStore();
        var changes = new List<string>();
        Callable recorder = Callable.From<StringName, Variant>((key, value) => changes.Add(key + "=" + value));
        Events.Instance.Connect(Events.SignalName.VariableChanged, recorder);

        store.Unlock("block");
        store.Unlock("double_jump");
        Check(store.SetVar("truth", true) && store.SetVar("gate_count", 3) && store.SetVar("speaker", "？？？"), "进度：bool / int / string 均可写入");
        Check(!store.SetVar("bad_float", 1.5f) && !store.HasVar("bad_float"), "进度：float 被拒绝且未写入（会打印一条预期的错误）");
        Check(!store.SetVar("bad_vec", new Vector2(1, 2)) && !store.HasVar("bad_vec"), "进度：Vector2 被拒绝且未写入（会打印一条预期的错误）");
        store.SetVar("truth", true);
        Check(changes.Count == 3, $"进度：写入相同值不重复发 VariableChanged（实际 {changes.Count} 次）");
        store.SetVar("flag_as_text", "true");
        Check(store.Flag("truth") && !store.Flag("flag_as_text") && !store.Flag("missing"), "进度：Flag 只认 bool true，字符串 \"true\" 不算");

        string text = GD.VarToStr(store.ToSave());
        var loaded = new ProgressStore();
        loaded.FromSave(GD.StrToVar(text).AsGodotDictionary());
        Check(loaded.HasAbility("block") && loaded.HasAbility("double_jump") && !loaded.HasAbility("climb"), "往返：能力经 VarToStr 存档后完整恢复");
        Variant truth = loaded.GetVar("truth"), count = loaded.GetVar("gate_count"), speaker = loaded.GetVar("speaker"), asText = loaded.GetVar("flag_as_text");
        Check(truth.VariantType == Variant.Type.Bool && truth.AsBool(), "往返：bool 仍是 bool true");
        Check(count.VariantType == Variant.Type.Int && count.AsInt64() == 3, "往返：int 仍是 int 3");
        Check(speaker.VariantType == Variant.Type.String && speaker.AsString() == "？？？", "往返：string 仍是原字符串（含全角字符）");
        Check(asText.VariantType == Variant.Type.String && !loaded.Flag("flag_as_text"), "往返：字符串 \"true\" 不会被读成 bool");
        Check(StoryValue.AreEqual(loaded.GetVar("truth"), StoryValue.Parse("是"), out _), "往返：读档后的 truth 与剧本「是」比较成立");

        var corrupt = new GDictionary { ["abilities"] = new GArray { "block" }, ["vars"] = new GDictionary { ["ok"] = 1, ["bad"] = new Vector2(1, 1) } };
        var fromCorrupt = new ProgressStore();
        fromCorrupt.FromSave(corrupt);
        Check(fromCorrupt.HasVar("ok") && !fromCorrupt.HasVar("bad"), "往返：存档里的非法类型被跳过（会打印一条预期的错误）");

        var gate = new GateRequirement { LockedText = "需要二段跳" };
        gate.Abilities.Add("double_jump");
        gate.Flags.Add("truth");
        Check(loaded.Meets(gate, out string okReason) && okReason == "", "门条件：能力与旗标都满足时通过");
        loaded.SetVar("truth", false);
        Check(!loaded.Meets(gate, out string reason) && reason == "需要二段跳", "门条件：旗标为 false 时不通过并给出提示");
        Check(loaded.Meets(null, out _), "门条件：没有条件时直接通过");

        var table = new AbilityTable();
        table.Initial.Add("block");
        table.Initial.Add("switch_form");
        loaded.ResetNew(table);
        Check(loaded.HasAbility("switch_form") && !loaded.HasAbility("double_jump") && !loaded.HasVar("truth"), "新游戏：清空后只剩初始能力");

        Events.Instance.Disconnect(Events.SignalName.VariableChanged, recorder);
    }

    // ---------- 状态机 ----------

    private async Task StateMachineChecks()
    {
        var log = new List<string>();
        var owner = new Node2D { Name = "Owner" };
        var machine = new StateMachine { Name = "StateMachine" };
        var a = new CoreTestStateA { Name = "A", Log = log };
        var b = new CoreTestStateB { Name = "B", Log = log };
        var c = new CoreTestStateC { Name = "C", Log = log };
        owner.AddChild(machine);
        machine.Owner = owner;
        machine.AddChild(a);
        machine.AddChild(b);
        machine.AddChild(c);
        machine.Initial = a;

        var changes = new List<string>();
        machine.StateChanged += (from, to) => changes.Add(from + ">" + to);

        AddChild(owner);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check(machine.IsIn<CoreTestStateA>() && log.Count == 1 && log[0] == "A.enter", "状态机：拥有者就绪后进入初始状态，只进一次");

        log.Clear();
        var payload = new object();
        machine.TransitionTo<CoreTestStateB>(StateMsg.With(payload));
        Check(string.Join(",", log) == "A.exit,B.enter", "状态机：切换顺序 A.exit → B.enter");
        Check(ReferenceEquals(b.LastPayload, payload), "状态机：StateMsg.Payload 原样传给新状态");
        Check(changes.Count >= 2 && changes[^1] == "A>B", "状态机：StateChanged(from=A, to=B)");

        log.Clear();
        b.ChainToC = true;
        machine.TransitionTo<CoreTestStateA>();
        machine.TransitionTo<CoreTestStateB>();
        Check(string.Join(",", log) == "B.exit,A.enter,A.exit,B.enter,B.exit,C.enter", "状态机：Enter 里再切换会排队，顺序不乱（…B.enter → B.exit → C.enter）");
        Check(machine.IsIn<CoreTestStateC>(), "状态机：排队的切换完成后停在 C");

        machine.TransitionTo<CoreTestStateMissing>();
        Check(machine.IsIn<CoreTestStateC>(), "状态机：切到未登记的状态被拒绝，停留原状态（会打印一条预期的错误）");
        Check(machine.Get<CoreTestStateB>() == b && machine.Get<CoreTestStateMissing>() == null, "状态机：Get<T> 按类型取状态");

        owner.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // ---------- MetSys ----------

    private void MetSysChecks()
    {
        Check(MetSysApi.Available, "MetSys：autoload 可用");
        if (!MetSysApi.Available) return;

        MetSysApi.ResetState();
        Check(!MetSysApi.HasSaveData, "MetSys：ResetState 后没有存档数据");
        MetSysApi.SetSaveData();
        Check(MetSysApi.HasSaveData, "MetSys：SetSaveData() 初始化新存档");
        Check(MetSysApi.CellSize == new Vector2(576, 324), $"MetSys：格子尺寸为半屏 576×324（实际 {MetSysApi.CellSize}）");

        MetSysApi.CurrentLayer = 0;
        Check(MetSysApi.CurrentLayer == 0, "MetSys：CurrentLayer 读写");
        MetSysApi.SetPlayerPosition(new Vector2(10, 10));
        Check(!MetSysApi.HasPlayerPosition, "MetSys：没有当前房间时上报坐标不会产生格子位置");
        Check(MetSysApi.CurrentRoom == null && MetSysApi.CurrentRoomId == "", "MetSys：没有当前房间时 CurrentRoom 为 null、CurrentRoomId 为空");
        Check(!MetSysApi.IsCellDiscovered(new Vector3I(0, 0, 0)), "MetSys：空地图上格子未发现");
        _ = MetSysApi.GetExploredRatio();
        Check(MetSysApi.GetGroupByName("不存在的组") == -1, "MetSys：GetGroupByName 查不到返回 -1（MetSys 会打印一条预期的错误）");

        var wall = new Node { Name = "TestWall" };
        wall.SetMeta("object_id", "core_test_wall");
        AddChild(wall);
        bool calledBack = false;
        Check(!MetSysApi.RegisterStorable(wall, () => calledBack = true) && !calledBack, "MetSys：首次登记的对象未被储存");
        Check(MetSysApi.GetObjectId(wall) == "core_test_wall", "MetSys：GetObjectId 读 object_id 元数据");
        MetSysApi.Store(wall);
        Check(MetSysApi.IsStored("core_test_wall"), "MetSys：Store 后 IsStored 为 true");

        GDictionary saved = MetSysApi.GetSaveData();
        string text = GD.VarToStr(saved);
        MetSysApi.ResetState();
        MetSysApi.SetSaveData(GD.StrToVar(text).AsGodotDictionary());
        Check(MetSysApi.IsStored("core_test_wall"), "MetSys：储存的对象经 VarToStr 存读档后仍在");
        calledBack = false;
        Check(MetSysApi.RegisterStorable(wall, () => calledBack = true) && calledBack, "MetSys：读档后再次登记，回调触发（墙应移除自己）");
        wall.QueueFree();

        string receivedRoom = null;
        Vector3I receivedCell = default;
        int mapUpdates = 0;
        Action<string> onRoom = room => receivedRoom = room;
        Action<Vector3I> onCell = cell => receivedCell = cell;
        Action onMap = () => mapUpdates++;
        MetSysApi.RoomChanged += onRoom;
        MetSysApi.CellChanged += onCell;
        MetSysApi.MapUpdated += onMap;
        MetSysApi.Singleton.EmitSignal("room_changed", "res://test_room.tscn");
        MetSysApi.Singleton.EmitSignal("cell_changed", new Vector3I(1, 2, 0));
        MetSysApi.Singleton.EmitSignal("map_updated");
        Check(receivedRoom == "res://test_room.tscn", "MetSys：room_changed 信号桥接到 C# 事件");
        Check(receivedCell == new Vector3I(1, 2, 0), "MetSys：cell_changed 信号桥接到 C# 事件");
        Check(mapUpdates == 1, "MetSys：map_updated 信号桥接到 C# 事件");
        MetSysApi.RoomChanged -= onRoom;
        MetSysApi.Singleton.EmitSignal("room_changed", "res://other.tscn");
        Check(receivedRoom == "res://test_room.tscn", "MetSys：取消订阅后不再收到事件");
        MetSysApi.CellChanged -= onCell;
        MetSysApi.MapUpdated -= onMap;

        MetSysApi.ResetState();
    }

    private void Finish()
    {
        string dir = "res://tests/output";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        using var report = FileAccess.Open(dir + "/core_results.json", FileAccess.ModeFlags.Write);
        var data = new GDictionary { ["checks"] = _checks, ["failures"] = _failures, ["results"] = _lines };
        report?.StoreString(Json.Stringify(data, "\t"));
        GD.Print($"Tests: {(_failures == 0 ? "PASS" : "FAIL")} Core 总计{_checks}项，失败{_failures}项");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }
}
