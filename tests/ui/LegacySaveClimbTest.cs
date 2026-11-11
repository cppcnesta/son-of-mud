using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.UI;
using SonOfMud.World;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>
/// 旧存档补齐初始能力（关卡几何计划 v3 第 2.2 节），从正式入口走：
/// 往存档槽 0 直接写一份“攀墙还不是初始能力时”的存档（能力只有 block、switch_form；有 boss_keeper、seen.opening、
/// tablet.nameless_day 等进度；存档点 S1_04；格式和 SaveService 写出来的 v2 文本一样）→ 新主菜单按“继续” →
/// 有攀墙（读档时补上、发一次 AbilityUnlocked），存档里的进度原样都在 → 用真实按键攀墙。
/// 然后用真实按键从 S1_04 攀墙上到 S1_08（真的换房间）。
/// </summary>
public partial class LegacySaveClimbTest : UiTestBase
{
    protected override string SuiteName => "legacy_save_climb";

    private readonly List<string> _unlocked = new();

    protected override async Task RunChecks()
    {
        // ① 拿一份真实的 MetSys 存档数据：正式入口开新游戏，到过 S1_01…S1_04
        bool started = await EnterMainGame(StartMode.NewGame);
        if (!started || World == null) { Check(false, "搭建：正式入口开新游戏失败"); return; }
        await WaitSettled();
        foreach (string room in new[] { "S1_02", "S1_03", "S1_04" })
        {
            World.LoadRoomImmediate(room, SpawnPoint.DefaultId);
            await Frames(5);
        }
        GDictionary metsys = MetSysApi.GetSaveData();

        // ② 直接写旧存档：和 SaveService.SaveToDisk 写出来的结构一样（version 2，progress 的 version 1），只是能力里没有 wall_climb
        var old = new GDictionary
        {
            ["version"] = 2,
            ["metsys"] = metsys,
            ["progress"] = new GDictionary
            {
                ["version"] = 1,
                ["abilities"] = new GArray { "block", "switch_form" },
                ["vars"] = new GDictionary
                {
                    ["boss_keeper"] = true,
                    ["seen.opening"] = true,
                    ["tablet.nameless_day"] = true,
                    ["talked.keeper"] = true,
                    ["flow_test_offerings"] = 3,
                },
            },
            ["world"] = new GDictionary { ["room"] = "S1_04", ["savePoint"] = "S1_04" },
            ["meta"] = new GDictionary { ["savedAtUnix"] = 1760000000L, ["engine"] = "4.7.2.stable.mono" },
        };
        DeleteSave();
        WriteSaveBytes(GD.VarToStr(old).ToUtf8Buffer());
        byte[] written = ReadSaveBytes();
        string text = written == null ? "" : System.Text.Encoding.UTF8.GetString(written);
        Check(written != null && !text.Contains("wall_climb") && text.Contains("\"block\"") && text.Contains("boss_keeper"),
            "搭建：存档槽 0 写入了一份旧存档（能力只有 block、switch_form，没有 wall_climb）");

        // ③ 新主菜单按“继续”
        Events.Instance.AbilityUnlocked += OnUnlocked;
        TitleMenu menu = await OpenTitleMenu();
        Check(menu != null && !menu.ContinueButton.Disabled, "新主菜单：有存档，“继续”可用");
        if (menu == null) return;
        started = await StartFromMenu(menu.ContinueButton);
        Events.Instance.AbilityUnlocked -= OnUnlocked;
        Check(started && Starts[^1].success && MainGame != null && Boot.StartedWith == StartMode.Continue, $"按“继续”：从正式入口读档开局成功（{Starts[^1].error}）");
        if (World == null || Player == null) return;
        await WaitSettled();

        // ④ 攀墙补上了，进度都在
        Check(GameSession.Progress.HasAbility(PlayerAbilities.WallClimb) && AbilityList() == "block,switch_form,wall_climb",
            $"读档后有攀墙：能力是 block、switch_form、wall_climb（实际 {AbilityList()}）");
        Check(_unlocked.SequenceEqual(new[] { "wall_climb" }), $"读档时只补了 wall_climb，发了一次 AbilityUnlocked（实际 {string.Join(",", _unlocked)}）");
        Variant offerings = GameSession.Progress.GetVar("flow_test_offerings");
        Check(GameSession.Progress.Flag("boss_keeper") && GameSession.Progress.Flag("seen.opening") && GameSession.Progress.Flag("tablet.nameless_day")
              && GameSession.Progress.Flag("talked.keeper") && offerings.VariantType == Variant.Type.Int && offerings.AsInt64() == 3
              && GameSession.Progress.VariableKeys.Count() == 5,
            "读档后存档里的进度原样都在（boss_keeper、seen.opening、tablet.nameless_day、talked.keeper、int 3），没有多出变量");
        Check(World.CurrentRoomDef?.Id == "S1_04" && Saves.CheckpointRoom == "S1_04" && Saves.CheckpointSavePoint == "S1_04", "读档后人在 S1_04，最近存档点 S1_04");
        Check(SameBytes(written, ReadSaveBytes()), "读档没有改动存档文件（补齐只在内存里）");

        await ClimbToS108();
    }

    private void OnUnlocked(StringName id) => _unlocked.Add(id.ToString());

    private string RoomId => World?.CurrentRoomDef?.Id ?? "";
    private float LocalX => Player.GlobalPosition.X - World.CurrentRoom.GlobalPosition.X;
    private float LocalY => Player.GlobalPosition.Y - World.CurrentRoom.GlobalPosition.Y;

    private async Task<bool> HoldUntil(Key[] keys, Func<bool> condition, int maxFrames, Action perFrame = null)
    {
        foreach (Key k in keys) RealKeys.Down(k);
        bool ok = false;
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition()) { ok = true; break; }
            await Frames(1);
            perFrame?.Invoke();
        }
        ok |= condition();
        foreach (Key k in keys) RealKeys.Up(k);
        await Frames(2);
        return ok;
    }

    /// <summary>
    /// 从 S1_04 的存档点用真实按键攀墙上到 S1_08（关卡几何地图 1：天花板挂下来一面墙，左面可攀，顶上有洞通 S1_08）：
    /// 按住 A 走到挂墙左边 → 按住 W、按一下空格原地起跳 → 身体高过挂墙底边后按住 D 贴上墙 → 一直按住 D+W 往上爬，
    /// 穿过 S1_04 顶上的洞进入 S1_08，爬到顶翻上去站在 S1_08 的地面上。
    /// </summary>
    private async Task ClimbToS108()
    {
        const float wallLeft = 576f;      // 挂墙左面（S1_04 文字图第 18 列）
        const float wallBottom = 532f;    // 挂墙底边（第 16 行的底边，房间局部 y）
        bool atStart = await HoldUntil(new[] { RealKeys.Left }, () => LocalX < wallLeft - 56f, 300);
        Check(atStart && RoomId == "S1_04" && Player.IsOnFloor(), $"真实按键：从 S1_04 存档点往左走到挂墙左边（x={LocalX:F0}）");
        if (!atStart) return;

        float startY = LocalY;
        bool sawClimb = false, roseAbove = false;
        RealKeys.Down(RealKeys.MoveUp);
        RealKeys.Down(RealKeys.Jump);
        await Frames(2);
        RealKeys.Up(RealKeys.Jump);
        bool arrived = false;
        for (int i = 0; i < 900 && !arrived; i++)
        {
            await Frames(1);
            if (!roseAbove && RoomId == "S1_04" && LocalY - 24f < wallBottom - 16f)
            {
                roseAbove = true;
                RealKeys.Down(RealKeys.Right);
            }
            sawClimb |= Player.State is WallClimbState;
            arrived = RoomId == "S1_08" && !World.IsTransitioning && Player.IsOnFloor();
        }
        RealKeys.ReleaseAll();
        await Frames(2);
        Check(roseAbove && sawClimb && arrived,
            $"真实按键攀墙：从 S1_04 贴着挂墙爬上去，穿过顶上的洞到 S1_08 并站上地面（起跳 y={startY:F0}，进入过攀墙 {sawClimb}，现在 {RoomId}）");
    }
}
