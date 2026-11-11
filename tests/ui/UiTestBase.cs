using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Save;
using SonOfMud.UI;

/// <summary>
/// UI 测试的公共底座：沿用 FlowTestBase（驱动节点挂在 root 下、切场景不被释放、存档槽备份恢复），
/// 结果写到 tests/output/ui_&lt;名字&gt;_results.json。
/// 按钮一律用键盘真实操作：先给焦点，再发一次 ui_accept（按下、松开），所以被禁用的按钮不会有反应。
/// </summary>
public abstract partial class UiTestBase : FlowTestBase
{
    public const string TitleMenuScene = "res://assets/scenes/ui/title_menu.tscn";

    protected override string ResultPrefix => "ui";

    // 两个按键事件整个进程只建一次、一直持有：每次 new 一个交给 Input.ParseInputEvent，
    // 偶尔会在 GC 终结器里触发引擎的 FATAL（gchandle.is_released），所以不让它们被终结。

    /// <summary>当前正式场景里的整套界面（不在 main_game 里时为 null）。</summary>
    protected GameUi Ui => MainGame?.GetNodeOrNull<GameUi>("GameUi");

    /// <summary>切到新主菜单并等它就绪。</summary>
    protected async Task<TitleMenu> OpenTitleMenu()
    {
        ReleaseInputs();
        Error error = GetTree().ChangeSceneToFile(TitleMenuScene);
        if (error != Error.Ok)
        {
            Check(false, $"切到 {TitleMenuScene} 失败：{error}");
            return null;
        }
        return await WaitForTitleMenu();
    }

    /// <summary>等新主菜单成为当前场景并就绪（GameBoot 回主菜单之后用）。</summary>
    protected async Task<TitleMenu> WaitForTitleMenu(int maxFrames = 240)
    {
        await WaitUntil(() => GetTree().CurrentScene is TitleMenu menu && menu.IsNodeReady(), maxFrames);
        await Frames(3);
        BindMainGame();
        return GetTree().CurrentScene as TitleMenu;
    }

    /// <summary>
    /// 用键盘按一个按钮：GrabFocus 后发 ui_accept 按下、松开。按钮拿不到焦点（隐藏）时不发事件，返回 false。
    /// 被禁用的按钮拿到焦点也不会触发。
    /// </summary>
    protected async Task<bool> Activate(BaseButton button)
    {
        ReleaseInputs();
        if (button == null || !button.IsVisibleInTree()) return false;
        button.GrabFocus();
        await Frames(1);
        if (!button.HasFocus()) return false;
        Input.ParseInputEvent(new InputEventAction { Action = "ui_accept", Pressed = true });
        await Frames(1);
        Input.ParseInputEvent(new InputEventAction { Action = "ui_accept", Pressed = false });
        await Frames(2);
        return true;
    }

    /// <summary>从主菜单按一个按钮进入 main_game，等到 GameStartFinished。</summary>
    protected async Task<bool> StartFromMenu(BaseButton button, int maxFrames = 600)
    {
        int before = Starts.Count;
        await Activate(button);
        bool finished = await WaitUntil(() => Starts.Count > before, maxFrames);
        BindMainGame();
        return finished;
    }

    /// <summary>Esc 打开暂停菜单，按“回主菜单”，等新主菜单就绪。</summary>
    protected async Task<TitleMenu> QuitToMenuViaPauseMenu()
    {
        GameUi ui = Ui;
        if (ui == null) return null;
        await Tap(GameUi.MenuAction);
        if (!ui.Menu.IsOpen) return null;
        await Activate(ui.Menu.QuitToMenuButton);
        return await WaitForTitleMenu();
    }

    // ---------- 坏存档 ----------

    protected static byte[] GarbageSave()
    {
        var garbage = new byte[257];
        new System.Random(20261110).NextBytes(garbage);
        garbage[0] = 0xFF;
        return garbage;
    }

    protected static byte[] UnsupportedVersionSave()
    {
        var data = new SaveData { Version = 99, Room = "S1_04", SavePoint = "S1_04" };
        return Encoding.UTF8.GetBytes(GD.VarToStr(data.ToDictionary()));
    }

    protected static string Fills(HpBeadBar bar) =>
        string.Join(" ", Enumerable.Range(0, bar.BeadCount).Select(i => bar.GetBead(i).Fill.ToString("0.##")));
}
