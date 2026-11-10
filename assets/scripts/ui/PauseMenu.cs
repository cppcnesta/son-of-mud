using System;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 暂停菜单：继续 / 地图 / 回主菜单 / 退出游戏。打开时 PauseService.Request(PauseMenu, true)，关闭时释放；
/// ProcessMode = Always。“地图”只发 MapRequested，由 GameUi 做“关菜单、开地图”。
/// “回主菜单”只发 QuitToMenuRequested，清暂停、清输入锁和切场景都由 World 的 GameBoot 负责。
/// </summary>
public partial class PauseMenu : Control
{
    [Export] public Button ResumeButton { get; set; }
    [Export] public Button MapButton { get; set; }
    [Export] public Button QuitToMenuButton { get; set; }
    [Export] public Button QuitGameButton { get; set; }

    public bool IsOpen { get; private set; }
    /// <summary>点了“地图”。</summary>
    public event Action MapRequested;
    /// <summary>发过几次 QuitToMenuRequested。</summary>
    public int QuitToMenuCount { get; private set; }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        if (ResumeButton != null) ResumeButton.Pressed += Close;
        if (MapButton != null) MapButton.Pressed += () => MapRequested?.Invoke();
        if (QuitToMenuButton != null) QuitToMenuButton.Pressed += QuitToMenu;
        if (QuitGameButton != null) QuitGameButton.Pressed += () => GetTree().Quit();
    }

    public override void _ExitTree()
    {
        if (IsOpen) PauseService.Request(PauseService.PauseMenu, false);
        IsOpen = false;
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        PauseService.Request(PauseService.PauseMenu, true);
        Visible = true;
        ResumeButton?.GrabFocus();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        PauseService.Request(PauseService.PauseMenu, false);
    }

    private void QuitToMenu()
    {
        Close();
        QuitToMenuCount++;
        Events.Instance?.EmitSignal(Events.SignalName.QuitToMenuRequested);
    }
}
