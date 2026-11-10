using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 新主菜单（项目的 run/main_scene）。
/// - 新的旅程：PendingStart = NewGame，切到正式游戏场景 main_game.tscn。
/// - 继续：存档槽里没有文件时置灰（SavePaths.Exists）；有就 PendingStart = Continue 再切场景。
/// - 退出。调试版本里多一个“旧白模”入口，打开旧的 main_menu.tscn。
/// - 开局失败：进入菜单时 GameSession.LastStartError 不为空就弹出原因，点“知道了”后清空它。
///   提示不挡菜单，按钮始终可以操作；菜单从不删除、也不覆盖存档，“继续”只看文件在不在。
/// </summary>
public partial class TitleMenu : Control
{
    /// <summary>可调数值；不导出，统一用 UiSettings.Shared（原因见那里）。测试可以替换。</summary>
    public UiSettings Settings { get; set; }
    [Export] public Button NewGameButton { get; set; }
    [Export] public Button ContinueButton { get; set; }
    [Export] public Button LegacyButton { get; set; }
    [Export] public Button QuitButton { get; set; }
    [Export] public Control ErrorPopup { get; set; }
    [Export] public Label ErrorTitle { get; set; }
    [Export] public Label ErrorText { get; set; }
    [Export] public Button ErrorOkButton { get; set; }

    /// <summary>已经在切场景，之后的按键一律忽略。</summary>
    public bool Leaving { get; private set; }
    public bool ErrorShowing => ErrorPopup != null && ErrorPopup.Visible;
    /// <summary>弹出提示时显示的原因（关掉后保留，便于检查）。</summary>
    public string ShownError { get; private set; } = "";

    public override void _Ready()
    {
        Settings ??= UiSettings.Shared;
        if (NewGameButton != null) NewGameButton.Pressed += () => Start(StartMode.NewGame);
        if (ContinueButton != null) ContinueButton.Pressed += OnContinue;
        if (QuitButton != null) QuitButton.Pressed += () => GetTree().Quit();
        if (LegacyButton != null)
        {
            LegacyButton.Visible = OS.IsDebugBuild();
            LegacyButton.Pressed += OpenLegacy;
        }
        if (ErrorOkButton != null) ErrorOkButton.Pressed += DismissError;
        RefreshContinue();
        ShowStartError();
        if (!ErrorShowing) FocusFirstButton();
    }

    public bool SaveExists => SavePaths.Exists(Settings.SaveSlot);

    /// <summary>按存档文件在不在决定“继续”亮不亮。</summary>
    public void RefreshContinue()
    {
        if (ContinueButton != null) ContinueButton.Disabled = !SaveExists;
    }

    private void ShowStartError()
    {
        string error = GameSession.LastStartError;
        if (string.IsNullOrEmpty(error) || ErrorPopup == null) return;
        ShownError = error;
        if (ErrorTitle != null) ErrorTitle.Text = Settings.StartErrorTitle;
        if (ErrorText != null) ErrorText.Text = error;
        ErrorPopup.Visible = true;
        ErrorOkButton?.GrabFocus();
    }

    public void DismissError()
    {
        if (ErrorPopup != null) ErrorPopup.Visible = false;
        GameSession.LastStartError = "";
        FocusFirstButton();
    }

    private void FocusFirstButton()
    {
        foreach (Button button in new[] { ContinueButton, NewGameButton, QuitButton })
        {
            if (button == null || button.Disabled || !button.IsVisibleInTree()) continue;
            button.GrabFocus();
            return;
        }
    }

    private void OnContinue()
    {
        RefreshContinue();
        if (!SaveExists) return;
        Start(StartMode.Continue);
    }

    private void Start(StartMode mode)
    {
        if (Leaving) return;
        if (ErrorPopup != null) ErrorPopup.Visible = false;
        GameSession.LastStartError = "";
        GameSession.PendingStart = mode;
        Leave(Settings.GameScene);
    }

    private void OpenLegacy()
    {
        if (!OS.IsDebugBuild()) return;
        Leave(Settings.LegacyMenuScene);
    }

    private void Leave(string scenePath)
    {
        if (Leaving) return;
        Leaving = true;
        Error result = GetTree().ChangeSceneToFile(scenePath);
        if (result == Error.Ok) return;
        Leaving = false;
        GameSession.PendingStart = StartMode.None;
        GD.PushError($"TitleMenu：切换到 {scenePath} 失败：{result}");
    }
}
