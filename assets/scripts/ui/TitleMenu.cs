using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 主菜单（项目的 run/main_scene）。画面在 title_menu.tscn：PSD 各层、5 个图片按钮（Buttons/Wake…Quit，按钮上的字是子节点 Label）、
/// 动画播放器 Idle（AnimationPlayer）里的开场 intro 和待机 idle（标题光晕、双手、线稿人物）、飘尘 Dust（粒子），以及收集 / 制作人员名单两个面板。
/// - 开场：每次进主页面先播 intro（黑底 + 边框 → 背景亮起 → 人物淡入 → 双手从两边伸进来 → 标题落下 → 5 个按钮依次出现），
///   播完接 idle 循环。开场中按钮不能点；按任意键 / 点鼠标直接跳到开场结尾。时间在场景的 intro 动画里改。
/// - 醒来：PendingStart = NewGame，切到 main_game.tscn。
/// - 再续征程：存档槽里没有文件时变暗、点不了（SavePaths.Exists）；有就 PendingStart = Continue 再切场景。
/// - 收集：泥板和 CG（TitleCollectionPanel，内容按跨存档记录 CollectionRecord）。进主页面时先把存档里捡过的泥板导入记录并写盘，
///   在开场期间、任何按钮能点之前完成，所以开新档覆盖旧档之前一定已经导入过。制作人员名单：文字在场景里改。
/// - 退出。
/// - 选中（鼠标移上去或方向键）的按钮放大、变亮、轻轻浮动，其余略暗（数值在检查器里调）。
/// - 面板打开时：遮罩挡住鼠标，5 个按钮暂时不能点、不能获得焦点，开局 / 退出直接拒绝；焦点进到面板里，
///   关闭（返回 / Esc）后回到打开它的按钮，“再续征程”重新按存档在不在决定能不能点。两个面板同时只开一个。
/// - 开局失败：进入菜单时 GameSession.LastStartError 不为空就弹出原因（右上角，不挡按钮），点“知道了”后清空。
/// </summary>
public partial class TitleMenu : Control
{
    /// <summary>可调数值；不导出，统一用 UiSettings.Shared（原因见那里）。测试可以替换。</summary>
    public UiSettings Settings { get; set; }
    [Export] public BaseButton NewGameButton { get; set; }
    [Export] public BaseButton ContinueButton { get; set; }
    [Export] public BaseButton QuitButton { get; set; }
    [Export] public BaseButton CollectionButton { get; set; }
    [Export] public BaseButton CreditsButton { get; set; }
    [Export] public Control Overlay { get; set; }
    [Export] public TitleCollectionPanel CollectionPanel { get; set; }
    [Export] public Control CreditsPanel { get; set; }
    [Export] public BaseButton CollectionBack { get; set; }
    [Export] public BaseButton CreditsBack { get; set; }
    [Export] public Control ErrorPopup { get; set; }
    [Export] public Label ErrorTitle { get; set; }
    [Export] public Label ErrorText { get; set; }
    [Export] public Button ErrorOkButton { get; set; }
    /// <summary>放开场 intro 和待机 idle 两段动画的播放器（场景里的 Idle 节点）。</summary>
    [Export] public AnimationPlayer TitleAnimation { get; set; }

    public const string IntroAnimation = "intro";
    public const string IdleAnimation = "idle";

    /// <summary>选中的按钮放大到多少倍、变多亮；没选中的按钮变多暗；选中时上下浮动的幅度（像素）和速度（每秒圈数）。</summary>
    [Export] public float FocusScale { get; set; } = 1.08f;
    [Export] public float FocusBrightness { get; set; } = 1.12f;
    [Export] public float OtherBrightness { get; set; } = 0.82f;
    [Export] public float FloatPixels { get; set; } = 2f;
    [Export] public float FloatSpeed { get; set; } = 0.8f;
    /// <summary>“再续征程”点不了时的颜色。</summary>
    [Export] public Color DisabledTint { get; set; } = new(0.45f, 0.45f, 0.45f, 0.85f);

    /// <summary>已经在切场景，之后的按键一律忽略。</summary>
    public bool Leaving { get; private set; }
    /// <summary>开场动画还在播（按钮不能点）。</summary>
    public bool IntroPlaying { get; private set; }
    public bool ErrorShowing => ErrorPopup != null && ErrorPopup.Visible;
    /// <summary>弹出提示时显示的原因（关掉后保留，便于检查）。</summary>
    public string ShownError { get; private set; } = "";
    /// <summary>现在打开的面板（收集 / 名单），没有是 null。</summary>
    public Control OpenPanel { get; private set; }
    public bool PanelOpen => OpenPanel != null;
    /// <summary>现在选中（放大）的按钮。</summary>
    public BaseButton Selected { get; private set; }
    /// <summary>这次进主页面时从存档导入了几块泥板（测试用）。</summary>
    public int ImportedTablets { get; private set; }

    private readonly Dictionary<BaseButton, Vector2> _basePositions = new();
    private BaseButton _opener;
    private double _time;

    public BaseButton[] MainButtons => new[] { NewGameButton, ContinueButton, CollectionButton, CreditsButton, QuitButton }.Where(b => b != null).ToArray();

    public override void _Ready()
    {
        Settings ??= UiSettings.Shared;
        if (NewGameButton != null) NewGameButton.Pressed += () => Start(StartMode.NewGame);
        if (ContinueButton != null) ContinueButton.Pressed += OnContinue;
        if (QuitButton != null) QuitButton.Pressed += OnQuit;
        if (CollectionButton != null) CollectionButton.Pressed += OpenCollection;
        if (CreditsButton != null) CreditsButton.Pressed += OpenCredits;
        if (CollectionBack != null) CollectionBack.Pressed += ClosePanel;
        if (CreditsBack != null) CreditsBack.Pressed += ClosePanel;
        if (ErrorOkButton != null) ErrorOkButton.Pressed += DismissError;
        foreach (BaseButton button in MainButtons.Concat(new[] { CollectionBack, CreditsBack }).Where(b => b != null))
        {
            BaseButton b = button;
            _basePositions[b] = b.Position;
            b.FocusEntered += () => Select(b);
            b.MouseEntered += () => { if (!b.Disabled && b.FocusMode != FocusModeEnum.None) b.GrabFocus(); };
        }
        if (Overlay != null) Overlay.Visible = false;
        if (CollectionPanel != null) CollectionPanel.Visible = false;
        if (CreditsPanel != null) CreditsPanel.Visible = false;
        // 旧档里捡过的泥板并进跨存档记录（写盘）；按钮在开场结束前都点不了，所以一定早于开新档
        ImportedTablets = CollectionRecord.ImportFromSave(SavePaths.SlotPath(Settings.SaveSlot));
        StartIntro();
        ShowStartError();
        if (!ErrorShowing) FocusFirstButton();
    }

    // ---------- 开场 ----------

    private void StartIntro()
    {
        if (TitleAnimation == null || !TitleAnimation.HasAnimation(IntroAnimation))
        {
            IntroPlaying = false;
            if (TitleAnimation != null && TitleAnimation.HasAnimation(IdleAnimation)) TitleAnimation.Play(IdleAnimation);
            RefreshButtons();
            return;
        }
        IntroPlaying = true;
        RefreshButtons();
        TitleAnimation.AnimationFinished += OnAnimationFinished;
        TitleAnimation.Play(IntroAnimation);
        TitleAnimation.Advance(0);   // 马上套用开场第一帧，不让完整画面闪一下
    }

    private void OnAnimationFinished(StringName name)
    {
        if (name == IntroAnimation) FinishIntro();
    }

    /// <summary>跳过开场：直接跳到开场最后一帧，接待机。</summary>
    public void SkipIntro() => FinishIntro();

    private void FinishIntro()
    {
        if (!IntroPlaying) return;
        IntroPlaying = false;
        if (TitleAnimation != null)
        {
            if (TitleAnimation.CurrentAnimation == IntroAnimation)
                TitleAnimation.Seek(TitleAnimation.GetAnimation(IntroAnimation).Length, true);
            if (TitleAnimation.HasAnimation(IdleAnimation)) TitleAnimation.Play(IdleAnimation);
        }
        RefreshButtons();
        if (!ErrorShowing && !PanelOpen) FocusFirstButton();
    }

    private static bool IsSkipPress(InputEvent e) => !e.IsEcho() && e switch
    {
        InputEventKey key => key.Pressed,
        InputEventMouseButton mouse => mouse.Pressed,
        InputEventJoypadButton pad => pad.Pressed,
        InputEventAction action => action.Pressed,
        _ => false,
    };

    public bool SaveExists => SavePaths.Exists(Settings.SaveSlot);

    /// <summary>主按钮现在能不能点：开场中、面板开着时都不能。</summary>
    private bool ButtonsLocked => PanelOpen || IntroPlaying;

    /// <summary>按存档文件在不在决定“再续征程”能不能点（开场中、面板开着时 5 个按钮都不能点）。</summary>
    public void RefreshContinue()
    {
        if (ContinueButton == null) return;
        ContinueButton.Disabled = ButtonsLocked || !SaveExists;
        ContinueButton.FocusMode = ContinueButton.Disabled ? FocusModeEnum.None : FocusModeEnum.All;
        ContinueButton.SelfModulate = !SaveExists ? DisabledTint : Colors.White;
        if (ContinueButton.GetNodeOrNull<CanvasItem>("Label") is { } label) label.Modulate = !SaveExists ? DisabledTint : Colors.White;
    }

    // ---------- 选中效果 ----------

    private void Select(BaseButton button)
    {
        Selected = button;
        foreach (BaseButton b in _basePositions.Keys)
        {
            bool on = b == button;
            b.Scale = Vector2.One * (on ? FocusScale : 1f);
            float bright = on ? FocusBrightness : MainButtons.Contains(b) ? OtherBrightness : 1f;
            b.Modulate = new Color(bright, bright, bright);
        }
    }

    public override void _Process(double delta)
    {
        _time += delta;
        foreach (var (b, basePos) in _basePositions)
            b.Position = b == Selected ? basePos + new Vector2(0f, -FloatPixels * Mathf.Sin((float)(_time * FloatSpeed * Mathf.Tau))) : basePos;
    }

    // ---------- 面板 ----------

    public void OpenCollection()
    {
        if (CollectionPanel == null || Leaving || IntroPlaying) return;
        CollectionPanel.Refresh();
        OpenPanelFrom(CollectionPanel, CollectionButton);
    }
    public void OpenCredits() => OpenPanelFrom(CreditsPanel, CreditsButton);

    private void OpenPanelFrom(Control panel, BaseButton opener)
    {
        if (panel == null || Leaving || IntroPlaying) return;
        if (OpenPanel != null) OpenPanel.Visible = false;   // 两个面板互斥
        else _opener = opener;
        OpenPanel = panel;
        if (Overlay != null) Overlay.Visible = true;
        panel.Visible = true;
        RefreshButtons();
        Control first = panel == CollectionPanel ? (Control)CollectionPanel.List ?? CollectionBack : CreditsBack;
        first?.GrabFocus();
    }

    /// <summary>关掉现在的面板（返回 / Esc），焦点回到打开它的按钮。</summary>
    public void ClosePanel()
    {
        if (OpenPanel == null) return;
        OpenPanel.Visible = false;
        OpenPanel = null;
        if (Overlay != null) Overlay.Visible = false;
        RefreshButtons();
        BaseButton back = _opener;
        _opener = null;
        if (back != null && !back.Disabled) back.GrabFocus();
        else FocusFirstButton();
    }

    /// <summary>开场中 / 面板开着时 5 个按钮都不能点、不能获得焦点，否则恢复（“再续征程”还要看存档）。</summary>
    private void RefreshButtons()
    {
        bool enabled = !ButtonsLocked;
        foreach (BaseButton b in MainButtons)
        {
            if (b == ContinueButton) continue;
            b.Disabled = !enabled;
            b.FocusMode = enabled ? FocusModeEnum.All : FocusModeEnum.None;
        }
        // “再续征程”不能一律恢复：重新按存档在不在决定
        RefreshContinue();
    }

    public override void _Input(InputEvent @event)
    {
        if (IntroPlaying)
        {
            if (!IsSkipPress(@event)) return;
            SkipIntro();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (OpenPanel == null || @event.IsEcho()) return;
        if (@event.IsActionPressed("ui_cancel") || (InputMap.HasAction("menu") && @event.IsActionPressed("menu")))
        {
            ClosePanel();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------- 开局失败提示 ----------

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
        foreach (BaseButton button in new[] { ContinueButton, NewGameButton, CollectionButton, CreditsButton, QuitButton })
        {
            if (button == null || button.Disabled || !button.IsVisibleInTree()) continue;
            button.GrabFocus();
            return;
        }
    }

    // ---------- 开局 / 退出 ----------

    private void OnContinue()
    {
        RefreshContinue();
        if (!SaveExists || ButtonsLocked) return;
        Start(StartMode.Continue);
    }

    private void OnQuit()
    {
        if (ButtonsLocked || Leaving) return;
        GetTree().Quit();
    }

    private void Start(StartMode mode)
    {
        if (Leaving || ButtonsLocked) return;
        if (ErrorPopup != null) ErrorPopup.Visible = false;
        GameSession.LastStartError = "";
        GameSession.PendingStart = mode;
        Leave(Settings.GameScene);
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
