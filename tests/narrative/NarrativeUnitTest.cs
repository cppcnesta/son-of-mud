using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative;
using SonOfMud.Narrative.Commands;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Parsing;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Narrative.World;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>
/// 剧情系统的无界面测试（计划第 4 节第 1、2 步 + 内容数据检查）：
/// ① 解析器：OrpheusVN 样例剧本 Prologue.orph 的条目和标签位置与 Unity 原版导入结果一致（去掉本作不支持的命令）；
///    故意写错的各种情况报出正确行号；assets/story 下全部 .orph 零错误、零警告，内容检查零问题。
/// ② 执行器（AutoAdvance，无界面）：原版 8 条测试的移植；跳转 / 设置 / 如果；[如果] 类型不同报警告；
///    [结束] 发 Finished；死循环保护报行号；Stop 后迟到的 done 被忽略；选项、泥板、改名、重开、字幕模式限制。
/// ③ 数据：18 块泥板、5 块有完整版、2 段铭文、9 张 CG、角色表；地图一房间里摆的触发器都指向存在的剧本标签；导出预设包含 *.orph。
/// 结果写到 tests/output/narrative_unit_results.json。
/// </summary>
public partial class NarrativeUnitTest : Node
{
    private int _checks;
    private int _failures;
    private readonly GArray _lines = new();
    private NarrativeSettings _settings;

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
            _settings = GD.Load<NarrativeSettings>(NarrativeSettings.DefaultPath);
            Check(_settings != null && _settings.Characters != null, "前提：settings.tres 和角色表能加载");
            ParserPrologueChecks();
            ParserErrorChecks();
            ParserChoiceChecks();
            StoryFileChecks();
            DataChecks();
            SceneTriggerChecks();
            ExportPresetCheck();
            PortedRunnerTests();
            await RunnerFlowChecks();
            await RunnerContentChecks();
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
        using (FileAccess report = FileAccess.Open($"{dir}/narrative_unit_results.json", FileAccess.ModeFlags.Write))
        {
            var data = new GDictionary { ["suite"] = "narrative_unit", ["checks"] = _checks, ["failures"] = _failures, ["results"] = _lines };
            report?.StoreString(Json.Stringify(data, "\t"));
        }
        GD.Print($"Tests: {(_failures == 0 ? "PASS" : "FAIL")} narrative_unit 总计{_checks}项，失败{_failures}项");
        // 测试里临时建的 Godot 数组 / 字典（存档往返等）在退出前回收掉：
        // 留到引擎关闭之后再由终结器释放，会在 godotsharp_array_destroy 里崩溃（退出码 139）。
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // =================================================================
    // ① 解析器
    // =================================================================

    /// <summary>
    /// Unity 版 OrpheusScriptImporter 导入 Prologue.orph 的结果（按导入器第 1–7 步逐行推出来）：
    /// 38 条指令（命令名, 行号），7 个标签。本作删掉了 [背景] [登场] [退场]，解析时报“本作不支持”并跳过，
    /// 所以本作的结果 = 原版结果去掉这些指令，标签位置相应前移。
    /// </summary>
    private static readonly (string command, int line)[] UnityPrologueEntries =
    {
        ("背景", 6), ("说", 9), ("接着说", 10), ("BGM", 12), ("说", 14), ("接着说", 15), ("接着说", 16), ("接着说", 17),
        ("背景", 19), ("登场", 20), ("说", 22), ("说", 26), ("接着说", 27), ("等点击", 28), ("接着说", 29), ("说", 32),
        ("玩法", 36), ("说", 40), ("接着说", 41), ("接着说", 42), ("转场", 46), ("退场", 47), ("背景", 48), ("转场", 49),
        ("背景", 50), ("说", 53), ("接着说", 54), ("效果", 56), ("说", 59), ("玩法", 63), ("如果", 64), ("跳转", 65),
        ("说", 69), ("接着说", 70), ("跳转", 71), ("说", 75), ("BGM", 78), ("转场", 79),
    };

    private static readonly (string name, int index)[] UnityPrologueLabels =
    {
        ("序幕", 0), ("祈祷", 16), ("第一幕", 20), ("结局谜题", 29), ("假结局", 32), ("真结局", 35), ("结束", 36),
    };

    private static readonly string[] Removed = { "背景", "登场", "退场" };

    private void ParserPrologueChecks()
    {
        string text = FileAccess.GetFileAsString("res://tests/narrative/fixtures/prologue.orph");
        OrphScript script = OrphParser.Parse(text, "prologue");

        var expected = UnityPrologueEntries.Where(e => !Removed.Contains(e.command)).ToList();
        var actual = script.Entries.Select(e => (e.Command, e.SourceLine)).ToList();
        Check(UnityPrologueEntries.Length == 38, "解析器：Unity 原版导入 Prologue.orph 是 38 条指令（对照数据自检）");
        Check(actual.Count == expected.Count && actual.SequenceEqual(expected),
            $"解析器：Prologue.orph 的指令序列（命令名 + 行号）和原版一致，去掉本作不支持的 6 条后共 {expected.Count} 条（实际 {actual.Count} 条）" +
            (actual.SequenceEqual(expected) ? "" : "：" + string.Join(" ", actual.Select(a => a.Command + a.SourceLine))));

        bool labelsOk = script.Labels.Count == UnityPrologueLabels.Length;
        var labelText = new List<string>();
        foreach (var (name, unityIndex) in UnityPrologueLabels)
        {
            int removedBefore = UnityPrologueEntries.Take(unityIndex).Count(e => Removed.Contains(e.command));
            int at = script.FindLabel(name);
            labelText.Add($"{name}={at}");
            labelsOk &= at == unityIndex - removedBefore;
        }
        Check(labelsOk, $"解析器：7 个标签的位置和原版一致（按删掉的指令前移）：{string.Join(" ", labelText)}");

        var unsupportedLines = script.Errors.Select(OrphParser.LineOf).ToArray();
        Check(script.Errors.Count == 6 && script.Errors.All(e => e.Contains("本作不支持")) && unsupportedLines.SequenceEqual(new[] { 6, 19, 20, 47, 48, 50 }),
            $"解析器：[背景] [登场] [退场] 报“本作不支持”，行号 6/19/20/47/48/50（实际 {string.Join("/", unsupportedLines)}）");
        Check(script.Warnings.Count == 0, "解析器：Prologue.orph 没有“台词前面没有说话人”的警告");

        OrphEntry blank = script.Entries.FirstOrDefault(e => e.SourceLine == 28);
        OrphEntry confused = script.Entries.FirstOrDefault(e => e.SourceLine == 22);
        OrphEntry sameBlock = script.Entries.FirstOrDefault(e => e.SourceLine == 15);
        Check(blank != null && blank.Command == "等点击" && blank.Args.SequenceEqual(new[] { "空行" }), "解析器：段中空行（第 28 行）变成 [等点击 空行]，插在下一句前面");
        Check(confused != null && confused.Args[0] == "俄尔甫斯" && confused.Args[1] == "困惑" && confused.Args[2] == "……这是？", "解析器：说话人行的名字和表情拆对（俄尔甫斯（困惑）：）");
        Check(sameBlock != null && sameBlock.Command == "接着说" && sameBlock.Args[0] == "？？？" && sameBlock.Args[1] == "", "解析器：同一个人的第二句是“接着说”，表情只在第一句");
        OrphEntry ifEntry = script.Entries.FirstOrDefault(e => e.SourceLine == 64);
        Check(ifEntry != null && ifEntry.Args.SequenceEqual(new[] { "notes", "calling", "真结局" }), "解析器：[如果 notes calling 真结局] 参数原样保留");
    }

    private void ParserErrorChecks()
    {
        string text = string.Join("\n",
            "# 开始",                         // 1
            "旁白：",                          // 2
            "第一句",                          // 3
            "[跳转 不存在的标签]",              // 4 找不到标签
            "[不存在的命令 x]",                 // 5 不认识的命令
            "[设置 只有一个参数]",              // 6 参数不够
            "[等待 1",                         // 7 少了右括号
            "# 开始",                          // 8 标签重复
            "[等待 abc]",                      // 9 秒数不是数字
            "[]",                              // 10 空命令
            "第二句");                          // 11
        OrphScript script = OrphParser.Parse(text, "errors");
        int[] lines = script.Errors.Select(OrphParser.LineOf).ToArray();
        Check(lines.SequenceEqual(new[] { 4, 5, 6, 7, 8, 9, 10 }), $"解析器：7 种写错的情况都报出来，行号 4/5/6/7/8/9/10 按行排序（实际 {string.Join("/", lines)}）");
        Check(script.Errors[0].Contains("找不到标签「不存在的标签」") && script.Errors[1].Contains("不认识的命令「不存在的命令」") &&
              script.Errors[2].Contains("参数不够") && script.Errors[3].Contains("右括号") && script.Errors[4].Contains("重复") &&
              script.Errors[5].Contains("秒数") && script.Errors[6].Contains("空的命令"),
            "解析器：错误说明分别是 找不到标签 / 不认识的命令 / 参数不够 / 少了右括号 / 标签重复 / 秒数不是数字 / 空命令");
        Check(script.Entries.Count == 2 && script.Entries.All(e => e.Command == "说" || e.Command == "接着说"), $"解析器：写错的指令都不写进剧本（剩 {script.Entries.Count} 条台词）");

        OrphScript noSpeaker = OrphParser.Parse("# a\n没有说话人的台词\n[结束]", "warn");
        Check(noSpeaker.Errors.Count == 0 && noSpeaker.Warnings.Count == 1 && OrphParser.LineOf(noSpeaker.Warnings[0]) == 2, "解析器：台词前面没有说话人 → 第 2 行警告（不算错误）");

        OrphScript unsupported = OrphParser.Parse("# a\n[背景 x]\n[登场 y]\n[退场 z]\n[存档 w]\n[结束]", "unsupported");
        Check(unsupported.Errors.Count == 4 && unsupported.Errors.All(e => e.Contains("本作不支持")) && unsupported.Entries.Count == 1,
            "解析器：[背景] [登场] [退场] [存档] 都报“本作不支持”，不写进剧本");
    }

    private void ParserChoiceChecks()
    {
        string good = string.Join("\n",
            "### 供桌",
            "？？？：",
            "别碰！",
            "[选项 choice.供桌]",
            "- 离开 -> 供桌·离开",
            "*备注可以夹在选项中间*",
            "－ 吃下 → 供桌·吃下",
            "### 供桌·离开",
            "[结束]",
            "### 供桌·吃下",
            "[结束]");
        OrphScript script = OrphParser.Parse(good, "choice");
        OrphEntry choice = script.Entries.FirstOrDefault(e => e.Command == "选项");
        Check(script.Errors.Count == 0 && choice != null && choice.SourceLine == 4 &&
              choice.Args.SequenceEqual(new[] { "choice.供桌", "离开", "供桌·离开", "吃下", "供桌·吃下" }),
            "选项语法：[选项 变量] + 「- 文本 -> 标签」（全角减号、→ 也行），参数存成 变量, 文本, 标签……");
        Check(script.Entries.IndexOf(choice) == 1 && script.Entries[0].Command == "说", "选项语法：选项打断台词段，紧跟在台词后面");

        string bad = string.Join("\n",
            "# a",                       // 1
            "[选项 x]",                  // 2 只有一项
            "- 只有一项 -> a",            // 3
            "- 没有箭头",                 // 4 格式错
            "[结束]",                     // 5
            "- 孤立 -> a",               // 6 前面没有 [选项]
            "[选项 x y]",                // 7 变量写了两个
            "[选项]",                     // 8 不写变量也可以，但标签不存在
            "- 一 -> a",                  // 9
            "- 二 -> 不存在");             // 10
        OrphScript errors = OrphParser.Parse(bad, "choice_bad");
        int[] lines = errors.Errors.Select(OrphParser.LineOf).ToArray();
        Check(lines.SequenceEqual(new[] { 2, 4, 6, 7, 8 }), $"选项语法：少于两项 / 选项行格式错 / 孤立的选项行 / 变量多写 / 标签不存在 都报对行号 2/4/6/7/8（实际 {string.Join("/", lines)}）");
        Check(errors.Errors.Any(e => e.Contains("至少要有两项")) && errors.Errors.Any(e => e.Contains("找不到标签「不存在」")) && errors.Errors.Any(e => e.Contains("前面没有 [选项]")),
            "选项语法：错误说明清楚（至少两项 / 找不到标签 / 前面没有 [选项]）");
        Check(errors.Entries.All(e => e.Command != "选项"), "选项语法：写错的选项不写进剧本");
    }

    private void StoryFileChecks()
    {
        string dir = _settings.StoryDirectory;
        string[] files = DirAccess.GetFilesAt(dir).Where(f => f.EndsWith(".orph")).OrderBy(f => f).ToArray();
        Check(files.SequenceEqual(new[] { "m_pit.orph", "pickups.orph", "tablets.orph" }), $"剧本：assets/story 下有 m_pit.orph、pickups.orph（第 4 轮：道具拾取台词）和 tablets.orph（实际 {string.Join(",", files)}）");
        var problems = new List<string>();
        foreach (string file in files)
        {
            string text = FileAccess.GetFileAsString($"{dir}/{file}");
            OrphScript script = OrphParser.Parse(text, file.GetBaseName());
            problems.AddRange(script.Errors.Select(e => $"{file} {e}"));
            problems.AddRange(script.Warnings.Select(w => $"{file} {w}"));
            problems.AddRange(OrphValidator.Check(script, _settings));
        }
        Check(problems.Count == 0, "剧本：全部 .orph 解析零错误、零警告；每个标签段都以 [结束]/[跳转]/[选项]/[重开] 收尾，说话人都在角色表里，CG / 泥板数据都存在" +
              (problems.Count > 0 ? "：" + string.Join("; ", problems.Take(8)) : ""));

        OrphScript pit = OrphParser.Parse(FileAccess.GetFileAsString($"{dir}/m_pit.orph"), "m_pit");
        string[] labels = { "开场", "苏醒", "倒影", "朝圣者之路", "小泥偶", "供桌", "供桌·离开", "供桌·吃下", "守坑人", "七夜", "两河源头", "重塑" };
        string[] missing = labels.Where(l => pit.FindLabel(l) < 0).ToArray();
        Check(missing.Length == 0 && pit.Labels.Count == labels.Length, $"地图一剧本：12 个标签齐全（缺 {string.Join(",", missing)}，共 {pit.Labels.Count} 个）");
        string[] subtitles = pit.Labels.Where(l => pit.Entries[l.Index].Command == "字幕").Select(l => l.Name).ToArray();
        Check(subtitles.SequenceEqual(new[] { "朝圣者之路", "小泥偶", "两河源头" }), $"地图一剧本：玩家能动时的插话（朝圣者之路、小泥偶、两河源头）走字幕条（实际 {string.Join(",", subtitles)}）");
        string say(int line) => pit.Entries.First(e => e.SourceLine == line).Args[2];
        OrphEntry wake = pit.Entries.FirstOrDefault(e => e.Command == "说" && e.Args[0] == "？？？" && e.Args[2] == "——我的儿子，醒来。");
        Check(wake != null && pit.Entries.IndexOf(wake) < pit.FindLabel("苏醒"), "地图一剧本：开场以“？？？：——我的儿子，醒来。”结束，接着播苏醒");
        OrphEntry setSeen = pit.Entries.FirstOrDefault(e => e.Command == "设置" && e.Args[0] == "seen.opening");
        Check(setSeen != null && setSeen.Args[1] == "是", "地图一剧本：苏醒播完写 seen.opening = 是");
        OrphEntry offering = pit.Entries.FirstOrDefault(e => e.Command == "选项");
        Check(offering != null && offering.Args.SequenceEqual(new[] { "choice.供桌", "离开", "供桌·离开", "吃下", "供桌·吃下" }), "地图一剧本：供桌选项 离开 / 吃下");
        Check(pit.Entries.Any(e => e.Command == "设置" && e.Args.SequenceEqual(new[] { "item.生命之面包", "是" })), "地图一剧本：吃下 → item.生命之面包 = 是");
        Check(pit.Entries.Any(e => e.Command == "设置" && e.Args.SequenceEqual(new[] { "talked.keeper", "是" })), "地图一剧本：守坑人第一次对话完 → talked.keeper = 是");
        Check(pit.Entries.Count(e => e.Command == "CG") == 6 && pit.Entries.Any(e => e.Command == "CG" && e.Args[0] == "seven_nights") && pit.Entries.Any(e => e.Command == "CG" && e.Args[0] == "reshape"),
            "地图一剧本：开场黑屏 CG、七夜 CG1、重塑 CG2 都在（共 6 条 [CG]，含 3 条关闭）");
    }

    // =================================================================
    // ③ 数据
    // =================================================================

    private static readonly string[] TabletIds =
    {
        "nameless_day", "hand_blade", "smile", "after_flood", "destiny", "hoped_world", "too_soft", "curse", "annals",
        "gate_1", "gate_2", "gate_3", "gate_4", "gate_5", "gate_6", "gate_7", "name", "star",
    };

    private void DataChecks()
    {
        string[] files = DirAccess.GetFilesAt(_settings.TabletDirectory).Where(f => f.EndsWith(".tres")).ToArray();
        Check(files.Length == 18, $"泥板：共 18 块（实际 {files.Length}）");
        var tablets = TabletIds.Select(id => (id, data: ResourceLoader.Exists(_settings.TabletPath(id)) ? GD.Load<TabletData>(_settings.TabletPath(id)) : null)).ToList();
        Check(tablets.All(t => t.data != null && t.data.Id == t.id && t.data.Title != "" && t.data.PartialText != ""), "泥板：18 块都能加载，编号、标题、残缺版文字齐全");
        string[] full = tablets.Where(t => t.data?.HasFull == true).Select(t => t.id).ToArray();
        Check(full.SequenceEqual(new[] { "nameless_day", "hand_blade", "destiny", "curse", "star" }), $"泥板：只有策划案写了完整版的 5 块有完整版（实际 {string.Join(",", full)}）");
        Check(tablets.All(t => t.data == null || t.data.FullAfterVar == "truth"), "泥板：完整版都在 truth 之后显示");
        TabletData first = tablets[0].data;
        Check(first.Room == "S1_02" && first.Title == "当天还没有名字" && first.FullText.EndsWith("祂说：我的儿子。") && first.PartialText.EndsWith("第七夜，……落下。"),
            "泥板「当天还没有名字」：S1_02，残缺版和完整版文字照抄策划案");
        TabletData star = tablets.Last().data;
        Check(star.Title == "星\U0001202D" && star.FullText == "他爱我。可是只有一颗星落下那么久。", "泥板「星𒀭」：标题带楔形文字，完整版照抄策划案");
        string[] rooms = tablets.Select(t => t.data?.Room).ToArray();
        Check(rooms.SequenceEqual(new[] { "S1_02", "S1_08", "S2_02", "S2_03", "S2_04", "S3_04", "S3_05", "S3_06", "S3_07", "S4_02", "S4_02", "S4_02", "S4_02", "S4_02", "S4_02", "S4_02", "S4_05", "S4_05" }),
            "泥板：所在房间和计划第 2 节一致（新地图编号）");

        TabletData mural = GD.Load<TabletData>(_settings.InscriptionPath("mural"));
        TabletData relief = GD.Load<TabletData>(_settings.InscriptionPath("relief"));
        Check(mural != null && mural.Room == "S2_02" && mural.PartialText == "祂把风灌进她的嘴里，海就停了。祂举起弯刃，海就分成了两半。" && !mural.HasFull,
            "铭文：S2_02 壁画铭文，只有一个版本");
        Check(relief != null && relief.Room == "S3_02" && relief.PartialText == "祂睡在泥里。祂是破碎的。祂没有醒。" && !relief.HasFull, "铭文：S3_02 浮雕铭文，只有一个版本");

        string[] cgs = { "black", "seven_nights", "reshape", "sea_war", "meteor", "door", "patricide", "third_world", "slumber" };
        Check(cgs.All(id => ResourceLoader.Exists(_settings.CgPath(id)) && GD.Load<CgData>(_settings.CgPath(id)).Id == id), "CG：黑屏 + CG1–8 共 9 份数据");

        CharacterDatabase db = _settings.Characters;
        Check(db.Find("？？？") != null && db.Find("穆尔图库尔")?.HasPortrait == true && db.Find("伊吉")?.HasPortrait == true && db.Find("守坑人")?.HasPortrait == true &&
              db.Find("伊姆杜") != null && db.Find("塔姆图") != null && db.IsHiddenName("旁白") && db.Find("图库尔")?.DisplayName == "穆尔图库尔",
            "角色表：主角、？？？、伊姆杜、塔姆图、伊吉、守坑人……；主角、伊吉、守坑人有立绘（占位）；旁白不显示名牌；别名能找到本人");
    }

    private void SceneTriggerChecks()
    {
        var library = new OrphLibrary();
        library.LoadDirectory(_settings.StoryDirectory);
        var found = new List<string>();
        var problems = new List<string>();
        for (int i = 1; i <= 8; i++)
        {
            string room = $"S1_0{i}";
            Node scene = GD.Load<PackedScene>($"res://assets/scenes/world/s1/{room}.tscn").Instantiate();
            foreach (Node node in All(scene))
            {
                (string script, string label, string what) target = node switch
                {
                    StoryTrigger t => (t.ScriptId, t.Label, $"{room}:{(t.Mode == TriggerMode.Enter ? "进入" : "按E")}:{t.Label}"),
                    NpcTalk n => (n.ScriptId, n.FirstLabel, $"{room}:NPC:{n.FirstLabel}"),
                    TabletPickup p => (p.ScriptId, p.LabelOrId, $"{room}:泥板:{p.TabletId}"),
                    _ => (null, null, null),
                };
                if (target.what == null) continue;
                found.Add(target.what);
                if (!library.TryGet(target.script, out OrphScript script) || script.FindLabel(target.label) < 0)
                    problems.Add($"{target.what} 指向不存在的 {target.script}「{target.label}」");
                if (node.GetParent()?.Name != "Objects") problems.Add($"{target.what} 不在 Objects 下");
            }
            scene.Free();
        }
        string expected = "S1_02:泥板:nameless_day,S1_02:按E:倒影,S1_03:进入:朝圣者之路,S1_04:按E:供桌,S1_05:NPC:守坑人,S1_06:按E:七夜,S1_07:进入:两河源头,S1_08:按E:重塑,S1_08:泥板:hand_blade";
        Check(string.Join(",", found) == expected, $"房间：地图一 S1_02–S1_08 摆好了 9 个剧情节点（{string.Join(",", found)}）");
        Check(problems.Count == 0, "房间：每个剧情节点都在 Objects 下，指向存在的剧本标签" + (problems.Count > 0 ? "：" + string.Join("; ", problems) : ""));
    }

    private static IEnumerable<Node> All(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (Node n in All(child)) yield return n;
    }

    private void ExportPresetCheck()
    {
        var config = new ConfigFile();
        Error error = config.Load("res://export_presets.cfg");
        bool ok = error == Error.Ok;
        int presets = 0;
        if (ok)
        {
            foreach (string section in config.GetSections().Where(s => s.StartsWith("preset.") && !s.EndsWith(".options")))
            {
                presets++;
                ok &= config.GetValue(section, "include_filter", "").AsString().Split(',').Select(s => s.Trim()).Contains("*.orph");
            }
        }
        Check(ok && presets > 0, $"导出：export_presets.cfg 的每个导出预设都把 *.orph 加进了 include_filter（{presets} 个预设）");
    }

    // =================================================================
    // ② 执行器：原版 8 条测试
    // =================================================================

    private void PortedRunnerTests()
    {
        // 原版 1：GameplayAcceptsOnlyTheFirstResultOfTheCurrentSegment
        var segment = new TestSegment();
        VNRegistry.Register(segment);
        try
        {
            var mem = new MemoryStoryVariables();
            var ctx = new VNContext { Vars = mem };
            var start = new GameplayCommand();
            int completions = 0;
            start.Execute(new[] { segment.Type }, ctx, () => completions++);
            bool locked = ctx.InputLocked;
            Action<string> old = segment.Callback;
            old("fail");
            old("pass");
            bool first = mem.Get(segment.Type).AsString() == "fail" && completions == 1 && !ctx.InputLocked;
            start.Execute(new[] { segment.Type }, ctx, () => completions++);
            old("stale");
            bool staleIgnored = ctx.ActiveSegment == segment && completions == 1;
            segment.Callback("pass");
            Check(locked && first && staleIgnored && mem.Get(segment.Type).AsString() == "pass" && completions == 2,
                "原版测试 1：玩法期间锁推进键；只认当前这一场的第一个结果，旧回调和重复回调都忽略");

            // 原版 2：GameplayStoresResultInNamedVariable
            var mem2 = new MemoryStoryVariables();
            new GameplayCommand().Execute(new[] { segment.Type, "第一幕", "第一幕旋律" }, new VNContext { Vars = mem2 }, () => { });
            segment.Callback("pass");
            Check(mem2.Get("第一幕旋律").AsString() == "pass" && !mem2.Has(segment.Type), "原版测试 2：玩法结果存进指定的变量名");

            // 原版 3：NewSessionCancelsGameplayAndIgnoresLateResult
            var ctx3 = new VNContext { Vars = new MemoryStoryVariables() };
            int completions3 = 0;
            int cancelsBefore = segment.Cancellations;
            new GameplayCommand().Execute(new[] { segment.Type }, ctx3, () => completions3++);
            Action<string> cancelled = segment.Callback;
            ctx3.BeginSession();
            cancelled("late");
            Check(segment.Cancellations == cancelsBefore + 1 && ctx3.ActiveSegment == null && !ctx3.InputLocked && completions3 == 0,
                "原版测试 3：新的一场取消正在进行的玩法，迟到的结果被忽略");
        }
        finally
        {
            VNRegistry.Unregister(segment);
        }

        // 原版 4：InputLocksAreCounted
        var lockCtx = new VNContext();
        lockCtx.LockInput();
        lockCtx.LockInput();
        lockCtx.UnlockInput();
        bool stillLocked = lockCtx.InputLocked;
        lockCtx.UnlockInput();
        Check(stillLocked && !lockCtx.InputLocked, "原版测试 4：剧情输入锁按计数，一方解锁不会解开另一方的锁");

        // 原版 5：ImportValidationAndWaits
        var waitCtx = new VNContext { Settings = new NarrativeSettings { PauseAtBlankLines = false } };
        Check(new SetCommand().Validate(new[] { "x" }) != null && !WaitClickCommand.WillWait(new[] { "空行" }, waitCtx) && WaitClickCommand.WillWait(Array.Empty<string>(), waitCtx),
            "原版测试 5：参数不够被拒绝；PauseAtBlankLines 关掉时空行不等，手写的 [等点击] 仍然等");

        // 原版 6：StageStateFollowsCommands —— 本作没有舞台（背景 / 登场 / 退场），改为：舞台命令报“本作不支持”，表情按命令记录
        OrphScript stage = OrphParser.Parse("# a\n[背景 stage]\n[登场 演员甲 - 中]\n[退场 全部]\n[表情 穆尔图库尔 痛]\n[结束]", "stage");
        var exprCtx = new VNContext { Characters = _settings.Characters, Vars = new MemoryStoryVariables() };
        new ExpressionCommand().Execute(new[] { "图库尔", "痛" }, exprCtx, () => { });
        Check(stage.Errors.Count == 3 && stage.Entries.Select(e => e.Command).SequenceEqual(new[] { "表情", "结束" }) && exprCtx.Expressions.TryGetValue("穆尔图库尔", out string expr) && expr == "痛",
            "原版测试 6（改写）：舞台命令本作不支持并报错；[表情] 按角色的规范名记录（别名也算同一人）");

        // 原版 7：SaveFileRoundTrip —— 本作剧情不自己存档，改为：剧情变量放进 ProgressStore，存档往返后保留原类型
        var store = new ProgressStore();
        store.SetVar("choice.供桌", StoryValue.Parse("吃下"));
        store.SetVar("item.生命之面包", StoryValue.Parse("是"));
        store.SetVar("count", StoryValue.Parse("3"));
        store.SetVar("name.？？？", "穆尔图库尔");
        var restored = new ProgressStore();
        restored.FromSave((GDictionary)GD.StrToVar(GD.VarToStr(store.ToSave())));
        bool sameTypes = restored.GetVar("choice.供桌").VariantType == Variant.Type.String && restored.Flag("item.生命之面包")
                         && StoryValue.AreEqual(restored.GetVar("count"), 3L, out _) && restored.GetVar("name.？？？").AsString() == "穆尔图库尔";
        Check(sameTypes, "原版测试 7（改写）：剧情变量写进 ProgressStore，经过存档格式（VarToStr / StrToVar）往返后 string / bool / int 类型都保留");

        // 原版 8：ReplayStopsAtFirstGameplayAfterLabel
        var replay = new OrphScript("replay");
        replay.AddLabel("retry", 1);
        replay.Add("玩法", new[] { "rhythm", "第一幕" }, 1);
        replay.Add("说", new[] { "旁白", "", "must not replay" }, 2);
        Check(replay.FindNext("玩法", replay.FindLabel("retry")) == 0, "原版测试 8：从标签往后找第一条 [玩法]（PlayUntil 用）");
    }

    // =================================================================
    // ② 执行器：流程
    // =================================================================

    private (VNRunner runner, VNContext ctx, MemoryStoryVariables vars) NewRunner(RunnerMode mode = RunnerMode.Dialog)
    {
        var vars = new MemoryStoryVariables();
        var ctx = new VNContext { Vars = vars, Settings = _settings, Characters = _settings.Characters, Mode = mode };
        var runner = new VNRunner { AutoAdvance = true };
        AddChild(runner);
        runner.Attach(ctx);
        return (runner, ctx, vars);
    }

    private async Task RunnerFlowChecks()
    {
        // 跳转 / 设置 / 如果，类型不同报警告
        string branching = string.Join("\n",
            "### 入口",            // 1
            "[设置 a 3]",          // 2
            "[如果 a 3 三]",       // 3
            "[设置 path 错]",      // 4
            "[结束]",              // 5
            "### 三",              // 6
            "[设置 path 对]",      // 7
            "[如果 a 是 错误分支]", // 8  int 和 bool 比较 → 警告，按不相等
            "[设置 b 是]",         // 9
            "[如果 b 是 布尔]",    // 10
            "[结束]",              // 11
            "### 错误分支",        // 12
            "[设置 path 错]",      // 13
            "[结束]",              // 14
            "### 布尔",            // 15
            "[设置 path2 对]",     // 16
            "[如果 未设置 是 错误分支]", // 17 没设置过：不相等，不报警告
            "[结束]");             // 18
        var (runner, _, vars) = NewRunner();
        int finished = 0;
        runner.Finished += () => finished++;
        int warningsBefore = NarrativeLog.WarningCount;
        runner.Play(OrphParser.Parse(branching, "branching"), "入口");
        Check(vars.Get("path").AsString() == "对" && vars.Get("path2").AsString() == "对" && vars.Get("a").VariantType == Variant.Type.Int && vars.Flag("b"),
            "执行器：[设置] 按原类型写入（3 → int，是 → bool）；[如果] 相等时跳转，不相等时往下走");
        Check(NarrativeLog.WarningCount == warningsBefore + 1 && NarrativeLog.LastWarning.Contains("第8行") && NarrativeLog.LastWarning.Contains("类型不同"),
            $"执行器：[如果] 的变量和值类型不同 → 报警告（带第 8 行），按不相等处理；没设置过的变量不报警告（{NarrativeLog.LastWarning}）");
        Check(finished == 1 && !runner.IsPlaying, "执行器：[结束] 发 Finished 恰好一次，之后不再播放");

        // 死循环保护
        var (loopRunner, _, _) = NewRunner();
        loopRunner.MaxStepsWithoutWaiting = 50;
        string failure = null;
        int loopFinished = 0;
        loopRunner.Failed += message => failure = message;
        loopRunner.Finished += () => loopFinished++;
        loopRunner.Play(OrphParser.Parse("### 圈\n[设置 x 1]\n[跳转 圈]", "loop"), "圈");
        Check(failure != null && failure.Contains("死循环") && failure.Contains("第3行") && loopFinished == 0 && !loopRunner.IsPlaying,
            $"执行器：死循环保护停下来、报行号，不发 Finished（{failure}）");

        // Stop 之后，迟到的回调被忽略
        var (stopRunner, _, stopVars) = NewRunner();
        stopRunner.AutoAdvance = false;
        int stopFinished = 0;
        stopRunner.Finished += () => stopFinished++;
        stopRunner.Play(OrphParser.Parse("### w\n[等待 0.05]\n[设置 after 是]\n[结束]", "wait"), "w");
        bool waiting = stopRunner.IsPlaying;
        stopRunner.Stop();
        await Frames(15);
        Check(waiting && !stopVars.Has("after") && stopFinished == 0, "执行器：Stop 之后，[等待] 的回调被忽略，不继续执行、不发 Finished");

        // [等待] 真的等
        var (waitRunner, _, waitVars) = NewRunner();
        waitRunner.AutoAdvance = false;
        waitRunner.Play(OrphParser.Parse("### w\n[等待 0.05]\n[设置 after 是]\n[结束]", "wait2"), "w");
        bool notYet = !waitVars.Has("after");
        await Frames(20);
        Check(notYet && waitVars.Flag("after") && !waitRunner.IsPlaying, "执行器：[等待 秒数] 过了时间才继续");

        // PlayUntil：执行完第一条 [玩法] 就停
        var segment = new TestSegment { Type = "rhythm", Immediate = true, ImmediateResult = "fail" };
        VNRegistry.Register(segment);
        var (untilRunner, _, untilVars) = NewRunner();
        int untilFinished = 0;
        untilRunner.Finished += () => untilFinished++;
        untilRunner.PlayUntil(OrphParser.Parse("### retry\n[玩法 rhythm 第一幕 旋律]\n[设置 replayed 是]\n[结束]", "until"), "retry", "玩法");
        VNRegistry.Unregister(segment);
        Check(untilVars.Get("旋律").AsString() == "fail" && !untilVars.Has("replayed") && untilFinished == 1, "执行器：PlayUntil 执行完第一条 [玩法] 就停，后面的不执行");
    }

    private async Task RunnerContentChecks()
    {
        // 选项：AutoChoose 选第二项
        string choice = "### 供桌\n？？？：\n别碰！\n[选项 choice.供桌]\n- 离开 -> 离开\n- 吃下 -> 吃下\n### 离开\n[结束]\n### 吃下\n[设置 item.生命之面包 是]\n[结束]";
        var (runner, _, vars) = NewRunner();
        runner.AutoChoose = options => Array.IndexOf(options, "吃下");
        runner.Play(OrphParser.Parse(choice, "choice"), "供桌");
        Check(vars.Get("choice.供桌").VariantType == Variant.Type.String && vars.Get("choice.供桌").AsString() == "吃下" && vars.Flag("item.生命之面包"),
            "执行器：选项选中后变量 = 选项文本（string），跳到对应标签");

        // 泥板：记变量、发 TabletCollected
        var collected = new List<string>();
        void OnTablet(StringName id) => collected.Add(id);
        Events.Instance.TabletCollected += OnTablet;
        var (tabletRunner, _, tabletVars) = NewRunner();
        tabletRunner.Play(OrphParser.Parse("### t\n[泥板 nameless_day]\n[结束]", "tablet"), "t");
        Events.Instance.TabletCollected -= OnTablet;
        Check(tabletVars.Flag("tablet.nameless_day") && collected.SequenceEqual(new[] { "nameless_day" }), "执行器：[泥板 id] 写 tablet.id = true，发 TabletCollected(id) 一次");

        // 泥板数据不存在 → 停止并报错
        var (badRunner, _, _) = NewRunner();
        string badFailure = null;
        badRunner.Failed += m => badFailure = m;
        badRunner.Play(OrphParser.Parse("### t\n[泥板 不存在]\n[结束]", "bad_tablet"), "t");
        Check(badFailure != null && badFailure.Contains("第2行") && badFailure.Contains("不存在"), $"执行器：泥板数据不存在时停止并报行号（{badFailure}）");

        // 改名
        var (renameRunner, renameCtx, renameVars) = NewRunner();
        string beforeRename = renameCtx.DisplayNameFor("？？？");
        renameRunner.Play(OrphParser.Parse("### r\n[改名 ？？？ 穆尔图库尔]\n[结束]", "rename"), "r");
        Check(beforeRename == "？？？" && renameVars.Get("name.？？？").AsString() == "穆尔图库尔" && renameCtx.DisplayNameFor("？？？") == "穆尔图库尔" && renameCtx.DisplayNameFor("旁白") == "",
            "执行器：[改名 ？？？ 穆尔图库尔] 写 name.？？？，之后名牌显示穆尔图库尔；旁白不显示名牌");

        // 重开
        var (restartRunner, restartCtx, restartVars) = NewRunner();
        int restartFinished = 0;
        restartRunner.Finished += () => restartFinished++;
        restartRunner.Play(OrphParser.Parse("### b\n[重开]\n[设置 after 是]\n[结束]", "restart"), "b");
        Check(restartCtx.RestartRequested && restartFinished == 1 && !restartVars.Has("after"), "执行器：[重开] 结束这段剧情并标记重开请求（由剧情层发 RestartRunRequested）");

        // 事件
        string eventName = null, eventArg = null;
        void OnEvent(string name, string arg) { eventName = name; eventArg = arg; }
        Events.Instance.StoryEventRaised += OnEvent;
        var (eventRunner, _, _) = NewRunner();
        eventRunner.Play(OrphParser.Parse("### e\n[事件 开门 S1_05]\n[结束]", "event"), "e");
        Events.Instance.StoryEventRaised -= OnEvent;
        Check(eventName == "开门" && eventArg == "S1_05", "执行器：[事件 名字 参数] 发 StoryEventRaised");

        // 字幕模式不能用选项
        var (barkRunner, _, _) = NewRunner(RunnerMode.Subtitle);
        string barkFailure = null;
        barkRunner.Failed += m => barkFailure = m;
        barkRunner.Play(OrphParser.Parse(choice, "bark_choice"), "供桌");
        Check(barkFailure != null && barkFailure.Contains("字幕"), $"执行器：字幕（不暂停）模式里遇到 [选项] 停止并报错（{barkFailure}）");

        // BGM 报未实现
        int warnings = NarrativeLog.WarningCount;
        var (bgmRunner, _, bgmVars) = NewRunner();
        bgmRunner.Play(OrphParser.Parse("### m\n[BGM igi_hymn 2]\n[设置 after 是]\n[结束]", "bgm"), "m");
        Check(NarrativeLog.WarningCount == warnings + 1 && NarrativeLog.LastWarning.Contains("还没有实现") && bgmVars.Flag("after"), "执行器：[BGM] 报“还没有实现”警告，剧本照常继续");

        // 有解析错误的剧本不播放
        var (errRunner, _, _) = NewRunner();
        string errFailure = null;
        errRunner.Failed += m => errFailure = m;
        errRunner.Play(OrphParser.Parse("### a\n[不存在]\n[结束]", "has_errors"), "a");
        Check(errFailure != null && errFailure.Contains("解析错误") && !errRunner.IsPlaying, "执行器：有解析错误的剧本拒绝播放");
        await Frames(1);
    }
}
