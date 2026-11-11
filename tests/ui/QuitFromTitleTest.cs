using System.Threading.Tasks;
using SonOfMud.Core;
using SonOfMud.UI;

/// <summary>
/// 正式入口的退出（修复退出崩溃时加）：先进一次 main_game（让 UI 设置等资源都被载入过），从暂停菜单回主菜单，
/// 再用真实按键按主菜单的“退出”。真正的判定是进程退出码，由 Run_Tests 逐场景检查，必须是 0。
/// </summary>
public partial class QuitFromTitleTest : UiTestBase
{
    protected override string SuiteName => "quit_from_title";

    protected override async Task RunChecks()
    {
        bool started = await EnterMainGame(StartMode.NewGame);
        Check(started && Starts.Count == 1 && Starts[0].success, "正式入口开新游戏成功");
        await Frames(30);

        TitleMenu menu = await QuitToMenuViaPauseMenu();
        Check(menu != null && NoPauseOrLock(), "经暂停菜单回到主菜单，没有残留的暂停或输入锁");
        if (menu == null) return;
        Check(menu.QuitButton != null && menu.QuitButton.IsVisibleInTree() && !menu.QuitButton.Disabled, "主菜单里“退出”按钮可用");

        // 写完结果后，用真实按键按主菜单的“退出”，由游戏自己结束进程
        AfterReport = async () => await Activate(menu.QuitButton);
    }
}
