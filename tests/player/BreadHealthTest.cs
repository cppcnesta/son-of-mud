using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.UI;
using SonOfMud.World;

/// <summary>
/// 生命之面包：血量上限 70 → 140，泥珠 3 → 7 颗（从正式入口 main_game.tscn 启动，真实玩家，真实按键）。
/// ① 新游戏：上限 70、满血，HUD 3 颗满珠。
/// ② 供桌选“离开”：什么也不变。选“吃下”：上限 140，当前血量 +70（先挨一刀，确认是加 70 而不是回满），HUD 7 颗。
/// ③ 吃完之后真实过门、死亡复活，上限都保持 140；复活回满 140。
/// ④ 存档 → 从正式入口“继续”读档：上限 140、满血、HUD 7 颗；读档过程中 HUD 从没收到 / 显示过 70 上限（不闪 3 颗）。
/// ⑤ 重开一局：回到 70 / 3 颗，过程中 HUD 也没有先显示 140。
/// </summary>
public partial class BreadHealthTest : NarrativeFlowBase
{
    protected override string SuiteName => "bread_health";

    private const string BreadFlag = "item.生命之面包";

    /// <summary>收到的全部 PlayerHealthChanged(current, max)。</summary>
    private readonly List<(int hp, int max)> _health = new();
    /// <summary>每帧开始时 HUD 上的泥珠数（有 HUD 且有珠子时才记）。</summary>
    private readonly List<int> _beadSamples = new();
    private int _respawns;
    /// <summary>不采样的场景（重开时旧场景还没释放的那几帧）。</summary>
    private Node _skipScene;

    private HpBeadBar Beads => MainGame?.GetNodeOrNull<GameUi>("GameUi")?.Hud?.Beads;

    protected override async Task RunChecks()
    {
        Listen();
        Events.Instance.PlayerHealthChanged += OnHealth;
        Events.Instance.CheckpointRespawned += OnRespawned;
        GetTree().ProcessFrame += SampleBeads;
        try
        {
            await NewGameChecks();
            if (Player == null || Narr == null) return;
            await LeaveChecks();
            await EatChecks();
            await DoorAndRespawnChecks();
            await SaveLoadChecks();
            await RestartChecks();
            Check(Errors.Count == 0, "全程没有报错" + (Errors.Count > 0 ? "：" + string.Join(" | ", Errors.Snapshot().Take(5)) : ""));
        }
        finally
        {
            GetTree().ProcessFrame -= SampleBeads;
            Events.Instance.PlayerHealthChanged -= OnHealth;
            Events.Instance.CheckpointRespawned -= OnRespawned;
            Unlisten();
        }
    }

    private void OnHealth(int hp, int max) => _health.Add((hp, max));
    private void OnRespawned() => _respawns++;

    private void SampleBeads()
    {
        // 切场景的过程中 CurrentScene 可能已经释放，先确认还有效
        Node scene = GetTree().CurrentScene;
        if (!IsInstanceValid(scene) || scene == _skipScene) return;
        HpBeadBar bar = scene.GetNodeOrNull<GameUi>("GameUi")?.Hud?.Beads;
        if (bar != null && bar.BeadCount > 0) _beadSamples.Add(bar.BeadCount);
    }

    private string Fills()
    {
        HpBeadBar bar = Beads;
        return bar == null ? "（没有 HUD）" : string.Join(" ", Enumerable.Range(0, bar.BeadCount).Select(i => bar.GetBead(i).Fill.ToString("0.##")));
    }

    private string HealthLog(int from) => string.Join(",", _health.Skip(from).Select(h => $"{h.hp}/{h.max}"));

    private bool HudShows(int hp, int max, int beads) =>
        Beads is { } bar && bar.Health == hp && bar.MaxHealth == max && bar.BeadCount == beads;

    private async Task<HitOutcome> Hurt(string id, float damage)
    {
        HitOutcome outcome = Player.HurtBox.Receive(new Hit
        {
            AttackId = id, Damage = damage, SourcePosition = Player.GlobalPosition + new Vector2(40, 0), Blockable = false,
        });
        await Frames(30);
        await WaitUntil(() => Player.State is not HurtState, 120);
        return outcome;
    }

    // ---------- ① 新游戏 ----------

    private async Task NewGameChecks()
    {
        int events = _health.Count;
        _beadSamples.Clear();
        bool finished = await EnterMainGame(StartMode.NewGame);
        BindNarrative();
        Check(finished && Player != null && Narr != null && Interactor != null, "正式入口开新游戏成功");
        if (Player == null || Narr == null) return;
        await WaitSettled();
        Check(Player.Stats.MaxHealth == 70 && Player.Stats.Health == 70 && !GameSession.Progress.Flag(BreadFlag),
            $"新游戏：血量上限 70、满血（{Player.Stats.Health}/{Player.Stats.MaxHealth}）");
        Check(HudShows(70, 70, 3) && Fills() == "1 1 1", $"新游戏：HUD 3 颗满珠（{Fills()}）");
        Check(_health.Skip(events).All(h => h.max == 70) && _beadSamples.All(c => c == 3),
            $"新游戏：开局过程中 HUD 只收到过上限 70（{HealthLog(events)}；珠子数 {string.Join(",", _beadSamples.Distinct())}）");
    }

    // ---------- ② 供桌 ----------

    private async Task<bool> OpenOffering()
    {
        await Teleport("S1_04", "From_S1_03");
        bool atTable = await WalkToPrompt("供桌", PlayerActions.Right);
        if (!atTable) return false;
        await KeyTap(Key.E);
        return await WaitUntil(() => Narr.Choice.IsOpen, 240);
    }

    private async Task Choose(int option)
    {
        // 选项默认在第一项（离开），往下一格是“吃下”
        for (int i = 0; i < option; i++) await Tap(PlayerActions.Down);
        await KeyTap(Key.E);
        await WaitUntil(() => !Narr.MainActive, 60);
        await WaitUntil(() => !PauseService.IsPaused, 30);
        await Frames(5);
    }

    private async Task LeaveChecks()
    {
        await Teleport("S1_04", "From_S1_03");
        HitOutcome hurt = await Hurt("bread_pre_hit", 30f);
        Check(hurt == HitOutcome.Damaged && Player.Stats.Health == 40 && HudShows(40, 70, 3),
            $"搭建：先挨一刀 30，剩 40/70（{Player.Stats.Health}/{Player.Stats.MaxHealth}，HUD {Fills()}）");

        bool opened = await OpenOffering();
        Check(opened && Narr.Choice.Options.SequenceEqual(new[] { "离开", "吃下" }), "供桌：真实玩家走到供桌按 E，出现 离开 / 吃下");
        if (!opened) return;
        int events = _health.Count;
        await Choose(0);
        Check(GameSession.Progress.GetVar("choice.供桌").AsString() == "离开" && !GameSession.Progress.HasVar(BreadFlag),
            "供桌选“离开”：没有写生命之面包");
        Check(Player.Stats.MaxHealth == 70 && Player.Stats.Health == 40 && HudShows(40, 70, 3) && _health.Count == events,
            $"供桌选“离开”：血量和上限都不变（{Player.Stats.Health}/{Player.Stats.MaxHealth}，HUD {Fills()}，血量事件 {HealthLog(events)}）");
    }

    private async Task EatChecks()
    {
        bool opened = await OpenOffering();
        Check(opened, "供桌：离开之后还能再打开");
        if (!opened) return;
        int events = _health.Count;
        await Choose(1);
        Check(GameSession.Progress.Flag(BreadFlag), "供桌选“吃下”：item.生命之面包 = true");
        Check(Player.Stats.MaxHealth == 140 && Player.Stats.Health == 110,
            $"吃下生命之面包：上限 70 → 140，当前血量 40 → 110（+70，不是回满）（实际 {Player.Stats.Health}/{Player.Stats.MaxHealth}）");
        Check(HudShows(110, 140, 7) && Fills() == "1 1 1 1 1 0.5 0", $"吃下之后 HUD 7 颗：5 颗满、第 6 颗半、第 7 颗空（{Fills()}）");
        Check(_health.Count == events + 1 && _health[^1] == (110, 140), $"吃下：只发一次 PlayerHealthChanged(110, 140)（{HealthLog(events)}）");
    }

    // ---------- ③ 过门、复活 ----------

    private async Task DoorAndRespawnChecks()
    {
        World.LoadRoomImmediate("S3_08", "From_S4_01");
        await Frames(15);
        Input.ActionPress(PlayerActions.Left);
        bool atDoor = await WaitUntil(() => Interactor.CurrentPrompt == "进入", 240);
        Input.ActionRelease(PlayerActions.Left);
        await WaitUntil(() => Player.State is IdleState, 60);
        await Frames(3);
        int hp = Player.Stats.Health;
        await Tap(PlayerActions.Interact);
        await WaitUntil(() => World.IsTransitioning, 30);
        await WaitUntil(() => !World.IsTransitioning, 400);
        await Frames(10);
        Check(atDoor && World.CurrentRoomDef?.Id == "S4_01" && Player.Stats.MaxHealth == 140 && Player.Stats.Health == hp && HudShows(hp, 140, 7),
            $"吃下之后真实过门 S3_08 → S4_01：上限仍是 140、血量不变（{Player.Stats.Health}/{Player.Stats.MaxHealth}，HUD {Fills()}）");

        int respawns = _respawns;
        Player.HurtBox.Receive(new Hit { AttackId = "bread_kill", Damage = 999f, SourcePosition = Player.GlobalPosition, Blockable = false });
        bool back = await WaitUntil(() => _respawns > respawns && !World.IsTransitioning, 400);
        await Frames(10);
        Check(back && Player.Stats.MaxHealth == 140 && Player.Stats.Health == 140 && HudShows(140, 140, 7) && Fills() == "1 1 1 1 1 1 1",
            $"吃下之后死亡复活：上限仍是 140，回满 140，HUD 7 颗满珠（{Player.Stats.Health}/{Player.Stats.MaxHealth}，{Fills()}）");
    }

    // ---------- ④ 存档、读档 ----------

    private async Task SaveLoadChecks()
    {
        await Teleport("S1_04", SpawnPoint.SaveId);
        Check(Interactor.CurrentPrompt == "存档", "存档：站在 S1_04 存档点上");
        await KeyTap(Key.E);
        await Frames(3);
        bool saved = FileAccess.FileExists(SavePath);
        await Hurt("bread_after_save", 50f);

        // 模拟刚启动游戏就在主菜单点“继续”：内存里的进度是空的（没吃面包），全靠读档恢复。
        // 这样新玩家创建时按进度只能算出 70，读档如果晚于第一次报血量，HUD 就会先闪 3 颗。
        GameSession.Reset();
        int events = _health.Count;
        _beadSamples.Clear();
        bool loaded = await EnterMainGame(StartMode.Continue);
        BindNarrative();
        Check(saved && loaded && Starts[^1].success && Player != null, "存档 → 从正式入口“继续”读档成功");
        if (Player == null) return;
        await WaitSettled();
        Check(GameSession.Progress.Flag(BreadFlag) && Player.Stats.MaxHealth == 140 && Player.Stats.Health == 140,
            $"读档：生命之面包从存档恢复，新玩家上限 140、满血（{Player.Stats.Health}/{Player.Stats.MaxHealth}）");
        Check(HudShows(140, 140, 7) && Fills() == "1 1 1 1 1 1 1", $"读档：HUD 7 颗满珠（{Fills()}）");
        var loadEvents = _health.Skip(events).ToList();
        Check(loadEvents.Count > 0 && loadEvents.All(h => h.max == 140),
            $"读档：开局过程中 HUD 收到的血量全是上限 140，没有先报 70 再跳 140（{HealthLog(events)}）");
        Check(_beadSamples.Count > 0 && _beadSamples.All(c => c == 7),
            $"读档：每一帧 HUD 上都是 7 颗，没有闪出 3 颗（采样到的珠子数 {string.Join(",", _beadSamples.Distinct())}）");
    }

    // ---------- ⑤ 重开一局 ----------

    private async Task RestartChecks()
    {
        Node oldScene = MainGame;
        int starts = Starts.Count;
        int events = _health.Count;
        _beadSamples.Clear();
        _skipScene = oldScene;
        Events.Instance.EmitSignal(Events.SignalName.RestartRunRequested);
        bool finished = await WaitUntil(() => Starts.Count > starts && GetTree().CurrentScene != oldScene, 600);
        BindMainGame();
        BindNarrative();
        Check(finished && Player != null && Starts[^1].success, "重开一局：重新载入正式入口并开新游戏");
        if (Player == null) return;
        await WaitSettled();
        Check(!GameSession.Progress.HasVar(BreadFlag) && Player.Stats.MaxHealth == 70 && Player.Stats.Health == 70,
            $"重开一局：生命之面包清掉，上限回到 70、满血（{Player.Stats.Health}/{Player.Stats.MaxHealth}）");
        Check(HudShows(70, 70, 3) && Fills() == "1 1 1", $"重开一局：HUD 回到 3 颗满珠（{Fills()}）");
        Check(_health.Skip(events).Any() && _health.Skip(events).All(h => h.max == 70) && _beadSamples.Count > 0 && _beadSamples.All(c => c == 3),
            $"重开一局：新实例的 HUD 只收到过上限 70（{HealthLog(events)}；珠子数 {string.Join(",", _beadSamples.Distinct())}）");
        Check(NoPauseOrLock(), "最后没有暂停和输入锁残留");
    }
}
