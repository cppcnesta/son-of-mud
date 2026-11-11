using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.UI;

/// <summary>
/// 计划第 4 节步骤 1、2：在 ui_lab.tscn 里只发假事件检查 HUD。
/// ① 泥珠（珠子数 = ⌊上限 / 20⌋、至少 1 颗，每颗容量 = 上限 / 珠子数）：70 → 3 颗满；60 → 2 颗满 + 第 3 颗 0.57；
///    140 → 7 颗满；130 → 6 颗满 + 1 颗半；0 → 全空（掉空的珠子播碎裂）；最大 150 → 7 颗；160 → 8 颗；19 → 1 颗；静态规则逐个血量检查。
/// ② 交互提示、提示条（出现约 2 秒后自动消失）、剧情开始 / 结束时 HUD 淡出 / 淡回。
/// ③ 器物环：mask 0..127 每个都显示正确；7 个槽摆成环。
/// ④ Boss 血条：开战、平滑扣血、换阶段、击败收起；别的 bossId 的事件不理会。
/// ⑤ 源码扫描：UI 脚本只引用 Core 和 UI 自己；UI 场景只挂 UI 脚本和 MetSys 的 Minimap。
/// </summary>
public partial class UiHudTest : UiTestBase
{
    protected override string SuiteName => "hud";

    private const string LabScene = "res://assets/scenes/tests/ui_lab.tscn";

    private Hud _hud;

    protected override async Task RunChecks()
    {
        PauseService.Reset();
        InputLock.Reset();
        MetSysApi.ResetState();
        MetSysApi.SetSaveData();
        Error error = GetTree().ChangeSceneToFile(LabScene);
        bool ready = error == Error.Ok && await WaitUntil(() => GetTree().CurrentScene?.SceneFilePath == LabScene && GetTree().CurrentScene.IsNodeReady(), 120);
        await Frames(3);
        _hud = GetTree().CurrentScene?.GetNodeOrNull<GameUi>("GameUi")?.Hud;
        Check(ready && _hud != null && _hud.Beads != null && _hud.Artifacts != null && _hud.Boss != null && _hud.Prompt != null && _hud.Toast != null && _hud.Minimap != null,
            "ui_lab.tscn：GameUi 下的 HUD 和各个组件都在");
        if (_hud == null) return;

        await BeadChecks();
        await PromptChecks();
        await ToastChecks();
        await NarrativeFadeChecks();
        ArtifactChecks();
        await BossChecks();
        SourceScan();
    }

    private static void Emit(StringName signal, params Variant[] args) => Events.Instance.EmitSignal(signal, args);

    // ---------- ① 泥珠 ----------

    /// <summary>泥珠基准（UiSettings.BeadHp，默认 20）。测试不写死 20，跟着设置走。</summary>
    private int BeadBase => _hud.Beads.Settings.BeadHp;

    /// <summary>独立按规则算：珠子数 = ⌊max / 基准⌋（至少 1），每颗容量 = max / 珠子数，第 i 颗装 [i×容量, (i+1)×容量)。</summary>
    private int ExpectedCount(int max) => max <= 0 ? 0 : Math.Max(1, max / BeadBase);

    private float Expected(int hp, int max, int index)
    {
        float capacity = (float)max / ExpectedCount(max);
        return Math.Clamp(hp - index * capacity, 0f, capacity) / capacity;
    }

    private bool FillsMatch(int hp, int max, bool displayed)
    {
        HpBeadBar bar = _hud.Beads;
        if (bar.BeadCount != ExpectedCount(max)) return false;
        for (int i = 0; i < bar.BeadCount; i++)
        {
            HpBead bead = bar.GetBead(i);
            float actual = displayed ? bead.DisplayedFill : bead.Fill;
            if (Mathf.Abs(actual - Expected(hp, max, i)) > 0.001f) return false;
        }
        return true;
    }

    private async Task Health(int hp, int max, int settleFrames = 30)
    {
        Emit(Events.SignalName.PlayerHealthChanged, hp, max);
        await Frames(settleFrames);
    }

    private async Task BeadChecks()
    {
        HpBeadBar bar = _hud.Beads;
        Check(BeadBase == 20, $"泥珠基准来自 UiSettings.BeadHp（ui_settings.tres 里是 {BeadBase}）");

        // 起始上限 70：3 颗，每颗约 23.3
        await Health(70, 70, 2);
        Check(bar.BeadCount == 3 && FillsMatch(70, 70, false) && FillsMatch(70, 70, true), $"70/70：3 颗满珠（{Fills(bar)}）");
        Check(Enumerable.Range(0, 3).All(i => bar.GetBead(i).Fill == 1f && Mathf.IsEqualApprox(bar.GetBead(i).Capacity, 70f / 3f)),
            $"70/70：每颗容量 70/3 ≈ 23.3，满血时每颗都正好是满的（{Fills(bar)}）");

        Emit(Events.SignalName.PlayerHealthChanged, 60, 70);
        await Frames(2);
        float mid = bar.GetBead(2).DisplayedFill;
        Check(FillsMatch(60, 70, false) && Fills(bar) == "1 1 0.57", $"60/70：从最后一颗开始空，前两颗满、第 3 颗 13.3/23.3（{Fills(bar)}）");
        Check(mid > 0.57f && mid < 1f, $"扣血时液面渐变，不是瞬间跳过去（2 帧后第 3 颗显示 {mid:0.00}）");
        await Frames(30);
        Check(FillsMatch(60, 70, true), "渐变结束后画面上的填充和目标一致");

        int[] breaks3 = Enumerable.Range(0, 3).Select(i => bar.GetBead(i).BreakCount).ToArray();
        await Health(46, 70);
        Check(FillsMatch(46, 70, true) && Fills(bar) == "1 0.97 0", $"46/70（一次 24 伤害之后）：按比例填充（{Fills(bar)}）");
        Check(bar.GetBead(2).BreakCount == breaks3[2] + 1 && bar.GetBead(1).BreakCount == breaks3[1] && bar.GetBead(0).BreakCount == breaks3[0],
            "46/70：第 3 颗从有血变成空，碎裂一次；其余不碎");
        await Health(47, 70);
        Check(FillsMatch(47, 70, true) && Fills(bar) == "1 1 0.01", $"47/70：刚过第 2 颗的边界（46.7），前两颗满、第 3 颗有一点点（{Fills(bar)}）");

        // 真实上限 140：7 颗，每颗 20
        await Health(140, 140, 3);
        Check(bar.BeadCount == 7 && FillsMatch(140, 140, false) && FillsMatch(140, 140, true) && bar.GetBead(0).Capacity == 20f, $"140/140：7 颗满珠，每颗 20（{Fills(bar)}）");

        Emit(Events.SignalName.PlayerHealthChanged, 130, 140);
        await Frames(32);
        Check(FillsMatch(130, 140, true) && Fills(bar) == "1 1 1 1 1 1 0.5", $"130/140：6 颗满 + 1 颗半（{Fills(bar)}）");

        await Health(116, 140);
        Check(FillsMatch(116, 140, true) && Fills(bar) == "1 1 1 1 1 0.8 0", $"116/140（一次 24 伤害之后）：按比例填充（{Fills(bar)}）");

        int[] breaksBefore = Enumerable.Range(0, 7).Select(i => bar.GetBead(i).BreakCount).ToArray();
        Emit(Events.SignalName.PlayerHealthChanged, 0, 140);
        await Frames(3);
        bool breaking = Enumerable.Range(0, 6).All(i => bar.GetBead(i).BreakPlaying);
        int[] breaksAfter = Enumerable.Range(0, 7).Select(i => bar.GetBead(i).BreakCount).ToArray();
        await Frames(30);
        Check(bar.BeadCount == 7 && FillsMatch(0, 140, false) && FillsMatch(0, 140, true), $"0/140：7 颗全空（{Fills(bar)}）");
        Check(Enumerable.Range(0, 6).All(i => breaksAfter[i] == breaksBefore[i] + 1) && breaksAfter[6] == breaksBefore[6] && breaking,
            $"掉空的 6 颗各播一次碎裂，原本就空的第 7 颗不播（碎裂次数 {string.Join(",", breaksAfter)}）");
        Check(Enumerable.Range(0, 7).All(i => !bar.GetBead(i).BreakPlaying), "碎裂效果播完后自动收起");

        // 不是基准倍数的上限：向下取整，容量平分
        await Health(150, 150, 3);
        Check(bar.BeadCount == 7 && Mathf.IsEqualApprox(bar.GetBead(6).Capacity, 150f / 7f) && FillsMatch(150, 150, true) && Fills(bar) == "1 1 1 1 1 1 1",
            $"最大血量 150：⌊150/20⌋ = 7 颗，每颗 150/7，满血时全满（{Fills(bar)}）");
        await Health(160, 160, 3);
        Check(bar.BeadCount == 8 && FillsMatch(160, 160, true), $"最大血量 160：8 颗满珠（{Fills(bar)}）");
        await Health(10, 19, 3);
        Check(bar.BeadCount == 1 && bar.GetBead(0).Capacity == 19f && FillsMatch(10, 19, true), $"最大血量 19（不到一个基准）：至少 1 颗（{Fills(bar)}）");
        await Health(70, 70, 3);
        Check(bar.BeadCount == 3 && FillsMatch(70, 70, true) && bar.Health == 70 && bar.MaxHealth == 70 && Fills(bar) == "1 1 1", "回到 70/70：3 颗满珠");
        await Health(140, 140, 3);
        Check(bar.BeadCount == 7 && FillsMatch(140, 140, true) && bar.Health == 140 && bar.MaxHealth == 140, "回到 140/140：7 颗满珠");

        Check(HpBeadBar.BeadCountFor(70, 20) == 3 && HpBeadBar.BeadCountFor(140, 20) == 7 && HpBeadBar.BeadCountFor(159, 20) == 7
              && HpBeadBar.BeadCountFor(160, 20) == 8 && HpBeadBar.BeadCountFor(19, 20) == 1 && HpBeadBar.BeadCountFor(0, 20) == 0,
            "珠子数 = ⌊max / 20⌋（至少 1 颗；max 为 0 时没有珠子）");
        Check(BeadUnitChecks(), "泥珠规则单元检查：70 → 3 颗、140 → 7 颗；各种血量下每颗的填充都按规则（满血全满、从最后一颗往前空、总量守恒）");
    }

    /// <summary>直接检查 HpBeadBar 的静态规则：对 70 和 140 的每一个血量值，逐颗比较装的血量。</summary>
    private static bool BeadUnitChecks()
    {
        foreach (int max in new[] { 70, 140 })
        {
            int count = HpBeadBar.BeadCountFor(max, 20);
            if (count != (max == 70 ? 3 : 7)) return false;
            float capacity = HpBeadBar.BeadCapacityFor(max, count);
            for (int hp = 0; hp <= max; hp++)
            {
                float sum = 0f;
                for (int i = 0; i < count; i++)
                {
                    float amount = HpBeadBar.BeadAmountFor(hp, max, count, i);
                    float expected = Math.Clamp(hp - i * capacity, 0f, capacity);
                    if (Mathf.Abs(amount - expected) > 0.001f) return false;
                    // 前面的珠子不满时后面的必须是空的（从最后一颗往前空）
                    if (i > 0 && HpBeadBar.BeadAmountFor(hp, max, count, i - 1) < capacity && amount > 0f) return false;
                    sum += amount;
                }
                if (Mathf.Abs(sum - hp) > 0.001f) return false;
            }
            for (int i = 0; i < count; i++)
                if (HpBeadBar.BeadAmountFor(max, max, count, i) != capacity) return false;
        }
        return true;
    }

    // ---------- ② 提示、提示条、剧情淡出 ----------

    private async Task PromptChecks()
    {
        InteractPrompt prompt = _hud.Prompt;
        Check(!prompt.Visible, "交互提示：一开始隐藏");
        Emit(Events.SignalName.InteractPromptChanged, "存档");
        await Frames(1);
        Check(prompt.Visible && prompt.Text == "E 存档", $"交互提示：收到“存档”显示“E 存档”（实际“{prompt.Text}”）");
        Emit(Events.SignalName.InteractPromptChanged, "进入");
        await Frames(1);
        Check(prompt.Visible && prompt.Text == "E 进入", $"交互提示：换成“E 进入”（实际“{prompt.Text}”）");
        Emit(Events.SignalName.InteractPromptChanged, "");
        await Frames(1);
        Check(!prompt.Visible && prompt.Prompt == "", "交互提示：空字符串时隐藏");
    }

    private async Task<int> FramesUntilHidden(Toast toast, int max)
    {
        int frames = 0;
        while (toast.IsShowing && frames < max)
        {
            await Frames(1);
            frames++;
        }
        return frames;
    }

    private async Task ToastChecks()
    {
        Toast toast = _hud.Toast;
        Check(!toast.IsShowing, "提示条：一开始隐藏");

        Emit(Events.SignalName.MessageRequested, "石门紧闭，需要攀墙之力");
        await Frames(1);
        Check(toast.IsShowing && toast.Text == "石门紧闭，需要攀墙之力", $"提示条：MessageRequested 显示原文（“{toast.Text}”）");
        int frames = await FramesUntilHidden(toast, 400) + 1;
        float seconds = frames / 60f;
        Check(!toast.IsShowing && seconds >= 2.0f && seconds <= 2.6f, $"提示条：约 2 秒后自动消失（{seconds:0.00} 秒，含 0.3 秒淡出）");

        Emit(Events.SignalName.GameSaved);
        await Frames(1);
        Check(toast.IsShowing && toast.Text == "已存档", $"提示条：GameSaved 显示“已存档”（“{toast.Text}”）");

        Emit(Events.SignalName.TabletCollected, new StringName("tablet_07"));
        await Frames(1);
        Check(toast.Text == "获得泥板「tablet_07」", $"提示条：TabletCollected 没有登记名字时显示编号（“{toast.Text}”）");
        toast.Settings.TabletNames["tablet_07"] = "祂睡在泥里";
        Emit(Events.SignalName.TabletCollected, new StringName("tablet_07"));
        await Frames(1);
        toast.Settings.TabletNames.Remove("tablet_07");
        Check(toast.Text == "获得泥板「祂睡在泥里」", $"提示条：登记了名字时显示名字（“{toast.Text}”）");

        // 新消息替换旧的并重新计时
        Emit(Events.SignalName.MessageRequested, "第一条");
        await Frames(60);
        Emit(Events.SignalName.MessageRequested, "第二条");
        await Frames(90);
        Check(toast.IsShowing && toast.Text == "第二条", "提示条：新消息替换旧的，并重新计时（第一条出现 2.5 秒后第二条仍在）");
        await FramesUntilHidden(toast, 200);
        Check(!toast.IsShowing, "提示条：第二条随后自动消失");
    }

    private async Task NarrativeFadeChecks()
    {
        Check(Mathf.IsEqualApprox(_hud.RootAlpha, 1f), "HUD：平时完全不透明");
        PauseService.Request(PauseService.Narrative, true);
        Emit(Events.SignalName.NarrativeStarted, "test_script", "intro");
        await Frames(40);
        Check(GetTree().Paused && _hud.RootAlpha < 0.01f, $"HUD：NarrativeStarted 后淡出（剧情暂停中照样淡出，alpha {_hud.RootAlpha:0.00}）");
        Emit(Events.SignalName.NarrativeFinished, "test_script", "intro");
        PauseService.Request(PauseService.Narrative, false);
        await Frames(40);
        Check(!GetTree().Paused && _hud.RootAlpha > 0.99f, $"HUD：NarrativeFinished 后淡回（alpha {_hud.RootAlpha:0.00}）");

        // 嵌套：两段开始、一段结束时仍保持淡出
        Emit(Events.SignalName.NarrativeStarted, "a", "1");
        Emit(Events.SignalName.NarrativeStarted, "b", "1");
        Emit(Events.SignalName.NarrativeFinished, "b", "1");
        await Frames(40);
        Check(_hud.RootAlpha < 0.01f && _hud.NarrativeActive, "HUD：还有剧情没结束时保持淡出");
        Emit(Events.SignalName.NarrativeFinished, "a", "1");
        await Frames(40);
        Check(_hud.RootAlpha > 0.99f && !_hud.NarrativeActive, "HUD：全部结束后淡回");
    }

    // ---------- ③ 器物环 ----------

    private void ArtifactChecks()
    {
        ArtifactRing ring = _hud.Artifacts;
        Check(ring.Slots.Count == 7 && ring.Slots.Select(s => s.Index).SequenceEqual(Enumerable.Range(0, 7)), "器物环：7 个槽，编号 0..6");
        Check(ring.Mask == 127 && ring.Slots.All(s => s.IsWorn), "器物环：开局（没收到事件时）7 件都戴着");

        Vector2 center = ring.Size / 2f;
        float[] radii = ring.Slots.Select(s => (s.Position + s.Size / 2f).DistanceTo(center)).ToArray();
        float[] angles = ring.Slots.Select(s => (s.Position + s.Size / 2f - center).Angle()).OrderBy(a => a).ToArray();
        bool evenlySpaced = Enumerable.Range(0, 7).All(i =>
        {
            float next = i == 6 ? angles[0] + Mathf.Tau : angles[i + 1];
            return Mathf.Abs(next - angles[i] - Mathf.Tau / 7f) < 0.05f;
        });
        Check(radii.Max() - radii.Min() < 1.5f && radii.Min() > 40f && evenlySpaced, $"器物环：7 个槽等距摆成环（半径 {radii.Min():0.0}..{radii.Max():0.0}）");

        var wrong = new List<int>();
        for (int mask = 0; mask <= 127; mask++)
        {
            Emit(Events.SignalName.ArtifactsChanged, mask);
            bool ok = ring.Mask == mask;
            foreach (ArtifactSlot slot in ring.Slots)
            {
                bool worn = (mask & (1 << slot.Index)) != 0;
                ok &= slot.IsWorn == worn && ring.IsWorn(slot.Index) == worn && (worn ? slot.ShowsWorn : slot.ShowsRemoved);
            }
            if (!ok) wrong.Add(mask);
        }
        Check(wrong.Count == 0, $"器物环：mask 0..127 共 128 种都显示正确（错误 {wrong.Count} 种{(wrong.Count > 0 ? "：" + string.Join(",", wrong.Take(10)) : "")}）");
        Check(ring.Slots.All(s => s.GetNode<TextureRect>("Worn").Texture is { } w && s.GetNode<TextureRect>("Removed").Texture is { } r && w != r),
            "器物环：每个槽“戴着”和“已放下”是两张不同的图");

        Emit(Events.SignalName.ArtifactsChanged, 0b1111_1010);
        Check(ring.Mask == 0b111_1010 && !ring.IsWorn(0) && ring.IsWorn(1), "器物环：超出 7 位的部分被忽略");
        Emit(Events.SignalName.ArtifactsChanged, 127);
    }

    // ---------- ④ Boss 血条 ----------

    private async Task BossChecks()
    {
        BossBar bar = _hud.Boss;
        Check(!bar.Visible && !bar.Showing, "Boss 血条：平时隐藏");

        Emit(Events.SignalName.BossEncounterStarted, new StringName("keeper"), "守坑人", 150);
        await Frames(30);
        Check(bar.Showing && bar.Visible && bar.Modulate.A > 0.99f && bar.NameLabel.Text == "守坑人" && bar.Fill.MaxValue == 150 && bar.Fill.Value == 150 && bar.Delay.Value == 150,
            $"Boss 开战：显示名字“{bar.NameLabel.Text}”和满血条 {bar.Fill.Value}/{bar.Fill.MaxValue}");

        Emit(Events.SignalName.BossHealthChanged, new StringName("other"), 10, 150);
        await Frames(2);
        Check(bar.TargetHp == 150 && bar.Fill.Value == 150, "Boss 血条：别的 bossId 的扣血事件不理会");

        Emit(Events.SignalName.BossHealthChanged, new StringName("keeper"), 126, 150);
        await Frames(3);
        double during = bar.Fill.Value;
        Check(bar.TargetHp == 126 && during < 150 && during > 126, $"Boss 扣血：主血条平滑下降（3 帧后 {during:0.0}，目标 126）");
        await Frames(12);
        double delayMid = bar.Delay.Value;
        Check(Mathf.IsEqualApprox((float)bar.Fill.Value, 126f) && delayMid > 149, $"Boss 扣血：主血条到位 126，延迟条稍后才追（此时 {delayMid:0.0}）");
        await Frames(60);
        Check(Mathf.IsEqualApprox((float)bar.Delay.Value, 126f), $"Boss 扣血：延迟条追上 126（{bar.Delay.Value:0.0}）");

        bar.Settings.BossPhaseNames["keeper:2"] = "守坑的老人";
        Emit(Events.SignalName.BossPhaseChanged, new StringName("keeper"), 2);
        await Frames(1);
        bar.Settings.BossPhaseNames.Remove("keeper:2");
        Check(bar.Phase == 2 && bar.DisplayName == "守坑的老人" && bar.NameLabel.Text == "守坑的老人", $"Boss 换阶段：换成登记的名字（“{bar.NameLabel.Text}”）");
        Emit(Events.SignalName.BossPhaseChanged, new StringName("keeper"), 3);
        await Frames(1);
        Check(bar.NameLabel.Text == "守坑人（第3阶段）", $"Boss 换阶段：没登记名字时用“原名（第 N 阶段）”（“{bar.NameLabel.Text}”）");

        Emit(Events.SignalName.BossDefeated, new StringName("other"), Vector2.Zero);
        await Frames(2);
        Check(bar.Showing, "Boss 血条：别的 bossId 的击败事件不理会");
        Emit(Events.SignalName.BossDefeated, new StringName("keeper"), Vector2.Zero);
        await Frames(2);
        Check(!bar.Showing && bar.Fill.Value == 0, "Boss 击败：血条清空并开始收起");
        await Frames(30);
        Check(!bar.Visible, "Boss 击败：收起后隐藏");

        Emit(Events.SignalName.BossEncounterStarted, new StringName("dragon"), "龙蛇", 300);
        await Frames(30);
        Check(bar.Visible && bar.NameLabel.Text == "龙蛇" && bar.Fill.Value == 300 && bar.Delay.Value == 300 && bar.Phase == 1, "Boss 血条：下一场战斗重新开始（名字、满血、第 1 阶段）");

        // 战斗中断：玩家死亡
        Emit(Events.SignalName.PlayerDied);
        await Frames(30);
        Check(!bar.Showing && !bar.Visible, "Boss 血条：战斗中玩家死亡（PlayerDied）后收起");
        Emit(Events.SignalName.BossHealthChanged, new StringName("dragon"), 100, 300);
        await Frames(5);
        Check(!bar.Visible, "Boss 血条：中断后同一个 Boss 的扣血事件不再让它出现");

        // 战斗中断：离开房间
        Emit(Events.SignalName.BossEncounterStarted, new StringName("keeper"), "守坑人", 150);
        await Frames(30);
        Emit(Events.SignalName.RoomExiting, "S1_05");
        await Frames(30);
        Check(!bar.Showing && !bar.Visible, "Boss 血条：战斗中离开房间（RoomExiting）后收起");

        // 中断之后重新开战，原来接着发的击败事件用它来收尾
        Emit(Events.SignalName.BossEncounterStarted, new StringName("dragon"), "龙蛇", 300);
        await Frames(30);
        Check(bar.Visible && bar.Showing && bar.Fill.Value == 300 && bar.Delay.Value == 300, "Boss 血条：中断后重新开战，满血重新出现");
        Emit(Events.SignalName.BossDefeated, new StringName("dragon"), Vector2.Zero);
        await Frames(30);
    }

    // ---------- ⑤ 源码扫描 ----------

    private static readonly Regex StringsAndComments = new(@"@""(?:[^""]|"""")*""|""(?:[^""\\\n]|\\.)*""|//[^\n]*|/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex NamespaceUse = new(@"\bSonOfMud\.(\w+)");
    private static readonly Regex ScenePath = new(@"path=""(res://[^""]+\.(?:cs|gd|tscn))""");
    private static readonly string[] AllowedNamespaces = { "Core", "UI" };


    private static List<string> ForeignNamespaces(string source) =>
        NamespaceUse.Matches(StringsAndComments.Replace(source, ""))
            .Select(m => m.Groups[1].Value)
            .Where(ns => !AllowedNamespaces.Contains(ns))
            .Distinct()
            .ToList();

    private void SourceScan()
    {
        Check(ForeignNamespaces("using SonOfMud.Player;\nvar x = SonOfMud.World.WorldGame.FindFrom(n);").SequenceEqual(new[] { "Player", "World" })
              && ForeignNamespaces("using SonOfMud.Core;\n// SonOfMud.Player\nvar s = \"SonOfMud.Save\";").Count == 0,
            "扫描器自检：能发现 Player / World 引用，注释和字符串里的不算");

        const string dir = "res://assets/scripts/ui";
        var offenders = new List<string>();
        int scanned = 0;
        foreach (string file in DirAccess.GetFilesAt(dir))
        {
            if (!file.EndsWith(".cs")) continue;
            scanned++;
            string source = FileAccess.GetFileAsString($"{dir}/{file}");
            List<string> foreign = ForeignNamespaces(source);
            if (!source.Contains("namespace SonOfMud.UI;")) foreign.Add("命名空间不是 SonOfMud.UI");
            if (source.Contains("/root/MetSys")) foreign.Add("直接访问 MetSys");
            if (foreign.Count > 0) offenders.Add($"{file}({string.Join("/", foreign)})");
        }
        Check(scanned >= 12 && offenders.Count == 0,
            $"源码扫描：{scanned} 个 UI 脚本只引用 Core 和 UI，不直接访问 MetSys" + (offenders.Count > 0 ? "，违规：" + string.Join(", ", offenders) : ""));

        const string sceneDir = "res://assets/scenes/ui";
        var badScenes = new List<string>();
        int scenes = 0;
        foreach (string file in DirAccess.GetFilesAt(sceneDir))
        {
            if (!file.EndsWith(".tscn")) continue;
            scenes++;
            foreach (Match m in ScenePath.Matches(FileAccess.GetFileAsString($"{sceneDir}/{file}")))
            {
                string path = m.Groups[1].Value;
                bool ok = path.StartsWith("res://assets/scripts/ui/") || path.StartsWith("res://assets/scenes/ui/")
                          || path == "res://addons/MetroidvaniaSystem/Template/Nodes/Minimap.tscn";
                if (!ok) badScenes.Add($"{file}→{path}");
            }
        }
        Check(scenes >= 5 && badScenes.Count == 0,
            $"场景扫描：{scenes} 个 UI 场景只挂 UI 脚本和 MetSys 的 Minimap" + (badScenes.Count > 0 ? "，违规：" + string.Join(", ", badScenes) : ""));
    }
}
