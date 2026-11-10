using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.UI;
using SonOfMud.World;

/// <summary>
/// 计划第 4 节步骤 4（正常流程，全部用键盘真实操作主菜单和暂停菜单）：
/// ① run/main_scene 是新主菜单；没有存档时“继续”置灰，按了没有反应；“旧白模”只在调试版本出现。
/// ② “新的旅程”进入 main_game.tscn、到 S1_01；新游戏不生成也不改动存档。
/// ③ 在 S1_04 存档点按 E 存档；Esc → “回主菜单”：回到新主菜单，没有残留的暂停或输入锁，上一局已释放。
/// ④ 有存档后“继续”亮起，按“继续”回到存档点房间。
/// ⑤ “旧白模”入口打开旧的 main_menu.tscn。
/// </summary>
public partial class TitleMenuTest : UiTestBase
{
    protected override string SuiteName => "title_menu";

    protected override async Task RunChecks()
    {
        Check(ProjectSettings.GetSetting("application/run/main_scene").AsString() == TitleMenuScene, "project.godot 的 run/main_scene 是新主菜单 title_menu.tscn");
        Check(MenuScene == TitleMenuScene, $"GameBoot 回主菜单的路径指向新主菜单（{MenuScene}）");

        // ① 没有存档
        DeleteSave();
        GameSession.LastStartError = "";
        TitleMenu menu = await OpenTitleMenu();
        Check(menu != null, "新主菜单能打开");
        if (menu == null) return;
        Check(menu.ContinueButton.Disabled && !menu.NewGameButton.Disabled && !menu.QuitButton.Disabled, "没有存档时“继续”置灰，“新的旅程”“退出”可用");
        Check(menu.LegacyButton.Visible == OS.IsDebugBuild() && OS.IsDebugBuild(), "调试版本里有“旧白模”入口");
        Check(!menu.ErrorShowing, "没有开局失败时不弹提示");
        Check(menu.NewGameButton.HasFocus(), "“继续”置灰时，焦点默认在“新的旅程”");

        int starts = Starts.Count;
        bool sent = await Activate(menu.ContinueButton);
        await Frames(20);
        Check(GetTree().CurrentScene == menu && Starts.Count == starts && GameSession.PendingStart == StartMode.None,
            $"置灰的“继续”按下没有反应（发出按键 {sent}，仍在主菜单）");

        // ② 新的旅程
        bool started = await StartFromMenu(menu.NewGameButton);
        Check(started && Starts[^1].success && MainGame != null, "点“新的旅程”：进入 main_game.tscn，开局成功");
        if (World == null || Player == null || Ui == null) return;
        await WaitSettled();
        Check(World.CurrentRoomDef?.Id == "S1_01" && Boot.StartedWith == StartMode.NewGame, $"新的旅程：人在 S1_01（实际 {World.CurrentRoomDef?.Id}）");
        Check(!FileAccess.FileExists(SavePath), "新的旅程不会生成存档文件");

        // ③ 存档后经暂停菜单回主菜单
        World.LoadRoomImmediate("S1_04", SpawnPoint.SaveId);
        await Frames(15);
        await Tap(PlayerActions.Interact);
        await Frames(2);
        byte[] saved = ReadSaveBytes();
        Check(saved != null, "在 S1_04 存档点按 E 生成了存档");

        await Tap(GameUi.MenuAction);
        Check(Ui.Menu.IsOpen && GetTree().Paused, "Esc 打开暂停菜单");
        InputLock.Request(InputLock.Narrative, true); // 故意留一个输入锁来源，回主菜单时应当被清掉
        ulong playerId = Player.GetInstanceId();
        sent = await Activate(Ui.Menu.QuitToMenuButton);
        menu = await WaitForTitleMenu();
        Check(sent && menu != null && GetTree().CurrentScene.SceneFilePath == TitleMenuScene, "暂停菜单“回主菜单”：回到新主菜单");
        if (menu == null) return;
        Check(NoPauseOrLock() && !PauseService.IsRequestedBy(PauseService.PauseMenu) && !PauseService.IsRequestedBy(PauseService.Map)
              && !InputLock.IsRequestedBy(InputLock.Narrative), "回主菜单后没有残留的暂停或输入锁");
        Check(!GodotObject.IsInstanceIdValid(playerId) && AllPlayers().Count == 0, "上一局（含玩家）已整个释放");
        Check(SameBytes(saved, ReadSaveBytes()), "回主菜单不改动存档");
        Check(!menu.ContinueButton.Disabled && menu.ContinueButton.HasFocus(), "有存档后“继续”亮起，焦点默认在“继续”");
        Check(!menu.ErrorShowing, "正常回主菜单不弹失败提示");

        // ④ 继续
        started = await StartFromMenu(menu.ContinueButton);
        Check(started && Starts[^1].success && MainGame != null, "点“继续”：开局成功");
        if (World == null || Player == null) return;
        await WaitSettled();
        await Frames(10);
        SpawnPoint save = World.CurrentRoom?.FindSpawn(SpawnPoint.SaveId);
        Check(World.CurrentRoomDef?.Id == "S1_04" && Boot.StartedWith == StartMode.Continue && save != null
              && Mathf.Abs(Player.GlobalPosition.X - save.GlobalPosition.X) < 1f,
            $"继续：回到存档点房间 S1_04 的存档点（实际 {World.CurrentRoomDef?.Id}）");
        Check(Ui.Hud.Prompt.Visible && Ui.Hud.Prompt.Text == "E 存档", $"继续：站在存档点上，HUD 提示“E 存档”（“{Ui.Hud.Prompt.Text}”）");
        Check(SameBytes(saved, ReadSaveBytes()), "继续不改动存档");

        // ⑤ 旧白模入口
        menu = await QuitToMenuViaPauseMenu();
        Check(menu != null && NoPauseOrLock(), "再次经暂停菜单回主菜单");
        if (menu == null) return;
        sent = await Activate(menu.LegacyButton);
        bool legacy = await WaitUntil(() => GetTree().CurrentScene?.SceneFilePath == LegacyMenuScene, 120);
        Check(sent && legacy, "“旧白模”入口打开旧的 main_menu.tscn");
        await Frames(5);
        Check(SameBytes(saved, ReadSaveBytes()), "整个流程里存档文件没有被主菜单改动");
    }
}
