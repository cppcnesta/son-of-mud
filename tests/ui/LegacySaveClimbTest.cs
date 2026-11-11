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
/// S1_04 现在还是 64 像素骨架，爬不到 S1_08（实测）：整个房间只有地面一行瓦片，和左右两列只从房顶挂到 y=308 的墙
/// （下半截是通往 S1_03、S1_05 的出口开口），没有天花板；原地跳的身体顶部只到 y≈398，够不着任何一面墙。
/// 所以这一轮改成：从 S1_04 走进 S1_03，在 S1_03 右墙（出口平台下面那面墙）用真实按键往上爬，高度远超单跳。
/// 合并新地形之后换回“从 S1_04 攀墙上到 S1_08（真的换房间）”。
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

        await ClimbInS103();
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
    /// 从 S1_04 的存档点用真实按键：按住 A 走进 S1_03（落在出口平台上）→ 继续往左走下平台落到地面 →
    /// 按住 D 走回平台下面、贴住右墙 → 按住 D+W、按一下空格，攀墙往上爬。
    /// </summary>
    private async Task ClimbInS103()
    {
        bool inS103 = await HoldUntil(new[] { RealKeys.Left }, () => RoomId == "S1_03" && !World.IsTransitioning, 600);
        Check(inS103, $"真实按键：从 S1_04 往左走进 S1_03（实际 {RoomId}）");
        if (!inS103) return;
        float roomWidth = World.CurrentRoomDef.Size.X * MetSysApi.CellSize.X;
        bool down = await HoldUntil(new[] { RealKeys.Left }, () => Player.IsOnFloor() && LocalY > 800f, 600);
        float floorY = LocalY;
        bool atWall = down && await HoldUntil(new[] { RealKeys.Right }, () => Player.IsOnWall() && LocalX > roomWidth - 128f, 600);
        Check(down && atWall, $"真实按键：走下出口平台落到 S1_03 地面（y={floorY:F0}），再走回右墙脚下（x={LocalX:F0}）");
        if (!atWall) return;

        float startY = LocalY, minY = startY;
        bool sawClimb = false;
        RealKeys.Down(RealKeys.Right);
        RealKeys.Down(RealKeys.MoveUp);
        RealKeys.Down(RealKeys.Jump);
        await Frames(2);
        RealKeys.Up(RealKeys.Jump);
        for (int i = 0; i < 300; i++)
        {
            await Frames(1);
            minY = Mathf.Min(minY, LocalY);
            sawClimb |= Player.State is WallClimbState;
        }
        RealKeys.ReleaseAll();
        await Frames(2);
        FormConfig cfg = Player.Forms.Current;
        float single = cfg.JumpSpeed * cfg.JumpSpeed / (2f * cfg.Gravity);
        float climbed = startY - minY;
        Check(sawClimb && RoomId == "S1_03" && climbed > single * 3f,
            $"真实按键攀墙：在 S1_03 右墙往上爬了 {climbed:F0}px（单跳按配置约 {single:F0}px），进入过攀墙状态");
    }
}
