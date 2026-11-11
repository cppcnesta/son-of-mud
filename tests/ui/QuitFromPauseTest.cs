using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.UI;

/// <summary>
/// 正式入口的退出（修复退出崩溃时加）：从 main_game 开新游戏 → Esc 打开暂停菜单 → 用真实按键按“退出游戏”。
/// 结果文件只记录前置检查；真正的判定是进程退出码，由 Run_Tests 逐场景检查，必须是 0（崩溃会是 132 / 139）。
/// </summary>
public partial class QuitFromPauseTest : UiTestBase
{
    protected override string SuiteName => "quit_from_pause";

    protected override async Task RunChecks()
    {
        bool started = await EnterMainGame(StartMode.NewGame);
        Check(started && Starts.Count == 1 && Starts[0].success, "正式入口开新游戏成功");
        GameUi ui = Ui;
        Check(ui != null && Player != null, "main_game 里有界面和真实玩家");
        if (ui == null) return;
        await Frames(30);

        await Tap(GameUi.MenuAction);
        Check(ui.Menu.IsOpen && PauseService.IsPaused, "按 Esc 打开暂停菜单，游戏暂停");
        Check(ui.Menu.QuitGameButton != null && ui.Menu.QuitGameButton.IsVisibleInTree(), "暂停菜单里有“退出游戏”按钮");

        // 写完结果后，用真实按键按“退出游戏”，由游戏自己结束进程
        AfterReport = async () => await Activate(ui.Menu.QuitGameButton);
    }
}
