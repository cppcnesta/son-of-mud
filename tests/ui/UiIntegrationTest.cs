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
/// 计划第 4 节步骤 5：真实玩家 + 真实 World，从正式入口 main_game.tscn 启动。
/// ① 开局 70 血、3 颗满珠；玩家真实受伤（HurtBox.Receive，24 / 10 / 30），泥珠按 Stats.Health 比例变化，掉空的珠子碎裂。
/// ② 站到 S1_04 存档点上 HUD 出现“E 存档”，按 E 后提示条“已存档”并自动消失，走开后提示隐藏。
/// ③ 真实过门（S3_08 → S4_01）：黑幕仍按第 2 轮的配对约定——每个 FadeRequested 都收到同 id、同方向的 FadeCompleted，
///    淡出完成时全黑、淡入完成时全透明，没有超时。
/// </summary>
public partial class UiIntegrationTest : UiTestBase
{
    protected override string SuiteName => "integration";

    private Hud _hud;

    protected override async Task RunChecks()
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        Check(finished && Starts.Count == 1 && Starts[0].success, "正式入口开新游戏成功");
        if (World == null || Player == null || Ui == null) return;
        await WaitSettled();
        _hud = Ui.Hud;

        await DamageChecks();
        await SavePointChecks();
        await FadeChecks();
    }

    // ---------- ① 真实受伤 ----------

    private bool BeadsMatchPlayer(bool displayed)
    {
        HpBeadBar bar = _hud.Beads;
        int hp = Player.Stats.Health, max = Player.Stats.MaxHealth;
        // 独立按规则算：珠子数 = ⌊max / 基准⌋（至少 1），每颗容量 = max / 珠子数
        int count = Mathf.Max(1, max / bar.Settings.BeadHp);
        if (bar.Health != hp || bar.MaxHealth != max || bar.BeadCount != count) return false;
        for (int i = 0; i < bar.BeadCount; i++)
        {
            float capacity = (float)max / count;
            float expected = Mathf.Clamp(hp - i * capacity, 0f, capacity) / capacity;
            float actual = displayed ? bar.GetBead(i).DisplayedFill : bar.GetBead(i).Fill;
            if (Mathf.Abs(actual - expected) > 0.001f) return false;
        }
        return true;
    }

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

    private async Task DamageChecks()
    {
        HpBeadBar bar = _hud.Beads;
        Check(Player.Stats.Health == 70 && Player.Stats.MaxHealth == 70 && bar.BeadCount == 3 && BeadsMatchPlayer(true) && Fills(bar) == "1 1 1",
            $"开局：玩家 70/70 血，HUD 3 颗满珠（{Fills(bar)}）");

        HitOutcome outcome = await Hurt("ui_hit_water", 24f);
        Check(outcome == HitOutcome.Damaged && Player.Stats.Health == 46 && BeadsMatchPlayer(true) && Fills(bar) == "1 0.97 0",
            $"真实受伤 24：玩家 {Player.Stats.Health} 血，泥珠按比例变化（{Fills(bar)}）");

        await Hurt("ui_hit_small", 10f);
        Check(Player.Stats.Health == 36 && BeadsMatchPlayer(true) && Fills(bar) == "1 0.54 0", $"真实受伤 10：36 血（{Fills(bar)}）");

        int[] before = Enumerable.Range(0, 3).Select(i => bar.GetBead(i).BreakCount).ToArray();
        await Hurt("ui_hit_big", 30f);
        int[] after = Enumerable.Range(0, 3).Select(i => bar.GetBead(i).BreakCount).ToArray();
        Check(Player.Stats.Health == 6 && BeadsMatchPlayer(true) && Fills(bar) == "0.26 0 0", $"真实受伤 30：6 血（{Fills(bar)}）");
        Check(after[1] == before[1] + 1 && after[0] == before[0] && after[2] == before[2],
            $"掉空的第 2 颗碎裂一次，其余不碎（{string.Join(",", after)}）");

        Player.Stats.Refill();
        await Frames(30);
        Check(Player.Stats.Health == 70 && BeadsMatchPlayer(true) && Fills(bar) == "1 1 1", $"回满血：泥珠全部回满（{Fills(bar)}）");
    }

    // ---------- ② 存档点 ----------

    private async Task SavePointChecks()
    {
        InteractPrompt prompt = _hud.Prompt;
        Toast toast = _hud.Toast;
        Check(!prompt.Visible, "S1_01 出生点附近没有可交互的东西，HUD 不显示交互提示");
        World.LoadRoomImmediate("S1_04", SpawnPoint.SaveId);
        await Frames(15);
        Check(prompt.Visible && prompt.Text == "E 存档", $"站到 S1_04 存档点上：HUD 出现“E 存档”（“{prompt.Text}”）");

        int shown = toast.ShownCount;
        await Tap(PlayerActions.Interact);
        await Frames(3);
        Check(FileAccess.FileExists(SavePath), "按 E：生成存档文件");
        Check(toast.IsShowing && toast.Text == "已存档" && toast.ShownCount == shown + 1, $"按 E：提示条出现“已存档”（“{toast.Text}”）");
        bool gone = await WaitUntil(() => !toast.IsShowing, 240);
        Check(gone, "“已存档”约 2 秒后自动消失");

        Input.ActionPress(PlayerActions.Left);
        bool hidden = await WaitUntil(() => !prompt.Visible, 240);
        Input.ActionRelease(PlayerActions.Left);
        await Frames(5);
        Check(hidden && prompt.Text == "", "走开后交互提示隐藏");
    }

    // ---------- ③ 黑幕配对约定 ----------

    private async Task FadeChecks()
    {
        var fadeOverlay = MainGame.GetNodeOrNull<FadeOverlay>("Game/FadeOverlay");
        Check(fadeOverlay != null, "黑幕仍是 game.tscn 里的 FadeOverlay（这一轮没有换位置）");
        if (fadeOverlay == null) return;

        World.LoadRoomImmediate("S3_08", "From_S4_01");
        await Frames(15);
        Input.ActionPress(PlayerActions.Left);
        bool atDoor = await WaitUntil(() => _hud.Prompt.Visible && _hud.Prompt.Text == "E 进入", 240);
        Input.ActionRelease(PlayerActions.Left);
        await WaitUntil(() => Player.State is IdleState, 60);
        await Frames(3);
        Check(atDoor, $"走到 S3_08 的门前：HUD 提示“E 进入”（“{_hud.Prompt.Text}”）");

        var requests = new List<(int id, bool fadeOut)>();
        var completions = new List<(int id, bool fadeOut, float alpha)>();
        void OnRequest(int id, bool fadeOut) => requests.Add((id, fadeOut));
        void OnComplete(int id, bool fadeOut) => completions.Add((id, fadeOut, fadeOverlay.Alpha));
        Events.Instance.FadeRequested += OnRequest;
        Events.Instance.FadeCompleted += OnComplete;
        int timeouts = World.FadeTimeouts;
        await Tap(PlayerActions.Interact);
        await WaitUntil(() => World.IsTransitioning, 30);
        await WaitUntil(() => !World.IsTransitioning, 400);
        await Frames(10);
        Events.Instance.FadeRequested -= OnRequest;
        Events.Instance.FadeCompleted -= OnComplete;

        string log = string.Join(" ", requests.Select(r => $"req{r.id}:{(r.fadeOut ? "out" : "in")}"))
                     + " | " + string.Join(" ", completions.Select(c => $"done{c.id}:{(c.fadeOut ? "out" : "in")}@{c.alpha:0.##}"));
        Check(World.CurrentRoomDef?.Id == "S4_01", $"真实过门到 S4_01（实际 {World.CurrentRoomDef?.Id}）");
        Check(requests.Count == 2 && requests[0].fadeOut && !requests[1].fadeOut, $"过门：先请求淡出再请求淡入（{log}）");
        Check(completions.Count == 2 && completions[0].id == requests[0].id && completions[0].fadeOut
              && completions[1].id == requests[1].id && !completions[1].fadeOut,
            "过门：每个请求都收到同 id、同方向的完成通知（配对约定不变）");
        Check(completions.Count == 2 && completions[0].alpha > 0.99f && completions[1].alpha < 0.01f,
            "过门：淡出完成时全黑，淡入完成时全透明");
        Check(World.FadeTimeouts == timeouts && !InputLock.IsLocked && Mathf.IsZeroApprox(fadeOverlay.Alpha), "过门：没有等待超时，结束后输入锁解除、黑幕透明");
        Check(Mathf.IsEqualApprox(_hud.RootAlpha, 1f) && _hud.Beads.BeadCount == 3, "过门后 HUD 照常显示");
    }
}
