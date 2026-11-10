using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.UI;

/// <summary>
/// 计划第 4 节步骤 4（失败流程）：存档损坏、版本不支持两种情况，都从新主菜单的“继续”进入：
/// 回到主菜单，弹出对应原因；存档文件字节不变；“继续”仍然亮着；菜单按钮还能用——
/// 关掉提示后 LastStartError 清空；再按“继续”同样失败、同样回来；提示开着时也能直接点“新的旅程”开新游戏（不碰存档）。
/// </summary>
public partial class TitleMenuFailureTest : UiTestBase
{
    protected override string SuiteName => "title_menu_failure";

    protected override async Task RunChecks()
    {
        await RunCase("存档损坏", GarbageSave(), "无法解析");
        await RunCase("版本不支持", UnsupportedVersionSave(), "版本 99 不受支持");
    }

    private async Task<(bool failed, string error, TitleMenu menu)> ContinueAndFail(TitleMenu menu)
    {
        int before = Starts.Count;
        await Activate(menu.ContinueButton);
        bool finished = await WaitUntil(() => Starts.Count > before, 600);
        TitleMenu back = await WaitForTitleMenu();
        bool failed = finished && Starts.Count == before + 1 && !Starts[^1].success;
        return (failed, finished ? Starts[^1].error : "(没收到 GameStartFinished)", back);
    }

    private static bool Usable(Button button) =>
        button != null && button.IsVisibleInTree() && !button.Disabled && button.MouseFilter == Control.MouseFilterEnum.Stop;

    private async Task RunCase(string name, byte[] bytes, string expectedReason)
    {
        DeleteSave();
        WriteSaveBytes(bytes);
        byte[] before = ReadSaveBytes();
        GameSession.LastStartError = "";
        TitleMenu menu = await OpenTitleMenu();
        Check(menu != null && !menu.ContinueButton.Disabled && SameBytes(before, bytes), $"[{name}] 搭建：存档槽里有文件（{bytes.Length} 字节），主菜单“继续”亮着");
        if (menu == null) return;
        TitleMenu first = menu;

        var (failed, error, back) = await ContinueAndFail(menu);
        menu = back;
        Check(failed, $"[{name}] 点“继续”：收到 GameStartFinished(false)（{error}）");
        Check(menu != null && menu != first && GetTree().CurrentScene.SceneFilePath == TitleMenuScene, $"[{name}] 回到新主菜单（当前 {GetTree().CurrentScene?.SceneFilePath}）");
        if (menu == null) return;
        Check(menu.ErrorShowing && menu.ErrorText.Text == error && menu.ShownError == error && error.Contains(expectedReason),
            $"[{name}] 弹出对应原因：“{menu.ErrorText.Text}”");
        Check(menu.ErrorTitle.Text == "无法开始旅程", $"[{name}] 提示标题“{menu.ErrorTitle.Text}”");
        Check(GameSession.LastStartError == error, $"[{name}] 关掉提示之前 LastStartError 保留原因");
        Check(SameBytes(before, ReadSaveBytes()) && !FileAccess.FileExists(SavePath + ".tmp"), $"[{name}] 存档文件的字节没有变化");
        Check(!menu.ContinueButton.Disabled, $"[{name}] “继续”按钮仍然亮着");
        Check(NoPauseOrLock() && GameSession.PendingStart == StartMode.None && AllPlayers().Count == 0, $"[{name}] 没有暂停、输入锁残留，PendingStart 为 None，正式场景已释放");

        bool overlap = menu.ErrorPopup.GetGlobalRect().Intersects(menu.NewGameButton.GetGlobalRect())
                       || menu.ErrorPopup.GetGlobalRect().Intersects(menu.ContinueButton.GetGlobalRect())
                       || menu.ErrorPopup.GetGlobalRect().Intersects(menu.QuitButton.GetGlobalRect());
        Check(Usable(menu.NewGameButton) && Usable(menu.ContinueButton) && Usable(menu.QuitButton) && !overlap,
            $"[{name}] 提示开着时菜单按钮都可用，提示框不挡住按钮");

        // 关掉提示：LastStartError 清空
        bool sent = await Activate(menu.ErrorOkButton);
        Check(sent && !menu.ErrorShowing && GameSession.LastStartError == "", $"[{name}] 点“知道了”：提示关闭，LastStartError 清空");
        Check(menu.ContinueButton.HasFocus(), $"[{name}] 提示关闭后焦点回到“继续”");

        // 按钮还能用：再按一次“继续”，同样失败、同样回来
        (failed, error, back) = await ContinueAndFail(menu);
        menu = back;
        Check(failed && menu != null && menu.ErrorShowing && menu.ErrorText.Text == error && error.Contains(expectedReason),
            $"[{name}] 再按“继续”：同样失败、回到主菜单并再次弹出原因");
        Check(SameBytes(before, ReadSaveBytes()) && menu != null && !menu.ContinueButton.Disabled, $"[{name}] 第二次失败后存档字节仍不变，“继续”仍亮着");
        if (menu == null) return;

        // 提示开着时直接点“新的旅程”：能进游戏，且不碰存档
        bool started = await StartFromMenu(menu.NewGameButton);
        Check(started && Starts[^1].success && MainGame != null && GameSession.LastStartError == "", $"[{name}] 提示开着时点“新的旅程”：开局成功");
        if (World == null) return;
        await WaitSettled();
        Check(World.CurrentRoomDef?.Id == "S1_01" && SameBytes(before, ReadSaveBytes()), $"[{name}] 新的旅程在 S1_01，存档文件没有被覆盖");

        menu = await QuitToMenuViaPauseMenu();
        Check(menu != null && !menu.ErrorShowing && !menu.ContinueButton.Disabled && SameBytes(before, ReadSaveBytes()) && NoPauseOrLock(),
            $"[{name}] 经暂停菜单回主菜单：没有提示，“继续”亮着，存档不变，没有残留");
    }
}
