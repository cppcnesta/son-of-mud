using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.UI;
using SonOfMud.World;

/// <summary>
/// 计划第 4 节步骤 4（正常流程，全部用键盘真实操作主菜单和暂停菜单）：
/// ① run/main_scene 是新主菜单；没有存档时“继续”置灰，按了没有反应；旧白模已删除，主菜单里没有“旧白模”入口。
/// ② “新的旅程”进入 main_game.tscn、到 S1_01；新游戏不生成也不改动存档。
/// ③ 在 S1_04 存档点按 E 存档；Esc → “回主菜单”：回到新主菜单，没有残留的暂停或输入锁，上一局已释放。
/// ④ 有存档后“继续”亮起，按“继续”回到存档点房间。
/// ⑤ 再经暂停菜单回一次主菜单，整个流程里存档文件不被主菜单改动。
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
        Check(menu.GetNodeOrNull("Buttons/Legacy") == null, "旧白模已删除，主菜单里没有“旧白模”入口");
        Check(!menu.ErrorShowing, "没有开局失败时不弹提示");
        Check(menu.NewGameButton.HasFocus(), "“继续”置灰时，焦点默认在“新的旅程”");

        int starts = Starts.Count;
        bool sent = await Activate(menu.ContinueButton);
        await Frames(20);
        Check(GetTree().CurrentScene == menu && Starts.Count == starts && GameSession.PendingStart == StartMode.None,
            $"置灰的“继续”按下没有反应（发出按键 {sent}，仍在主菜单）");

        await ArtAndPanelChecks(menu);

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

        // ⑤ 再回一次主菜单（旧白模入口已随旧白模一起删除）
        menu = await QuitToMenuViaPauseMenu();
        Check(menu != null && NoPauseOrLock(), "再次经暂停菜单回主菜单");
        if (menu == null) return;
        await Frames(5);
        Check(SameBytes(saved, ReadSaveBytes()), "整个流程里存档文件没有被主菜单改动");
    }

    // ---------- 美术、5 个按钮、设置 / 名单面板（没有存档时） ----------

    private async Task ArtAndPanelChecks(TitleMenu menu)
    {
        string Tex(string path) => menu.GetNodeOrNull<TextureRect>(path)?.Texture?.ResourcePath ?? "";
        Check(Tex("Background").EndsWith("title/tex_ui_title_bg.png") && Tex("Title").EndsWith("title/tex_ui_title_title.png")
              && Tex("HandLeft").EndsWith("tex_ui_title_hand_left.png") && Tex("Figure").EndsWith("tex_ui_title_figure.png")
              && menu.GetNodeOrNull("Logo") == null && !FileAccess.FileExists("res://assets/art/ui/tex_ui_menu_bg.png"),
            "主页面换成 PSD 的图（场景里的背景、标题、双手、人物线稿节点），旧的占位背景和 logo 已删除");
        var expected = new (BaseButton button, string art, string text)[]
        {
            (menu.NewGameButton, "btn_wake", "醒来"), (menu.ContinueButton, "btn_continue", "再续征程"), (menu.SettingsButton, "btn_settings", "设置"),
            (menu.CreditsButton, "btn_credits", "制作人员名单"), (menu.QuitButton, "btn_quit", "退出"),
        };
        Check(expected.All(e => e.button is TextureButton tb && tb.TextureNormal.ResourcePath.EndsWith($"tex_ui_title_{e.art}.png")
                                && tb.GetNode<Label>("Label").Text == e.text && tb.IsVisibleInTree()),
            "5 个按钮：醒来 / 再续征程 / 设置 / 制作人员名单 / 退出，图和字都在场景里");
        var idle = menu.GetNodeOrNull<AnimationPlayer>("Idle");
        var dust = menu.GetNodeOrNull<CpuParticles2D>("Dust");
        Check(idle != null && idle.IsPlaying() && idle.CurrentAnimation == "idle" && dust != null && dust.Emitting,
            "待机动画（标题光晕、双手、人物）在循环播放，飘尘在飘");
        await Frames(2);
        Check(menu.Selected == menu.NewGameButton && menu.NewGameButton.Scale.X > 1.01f && menu.QuitButton.Scale.X < 1.01f,
            "选中的按钮放大，其余不放大");

        // 设置面板：打开后底层按钮被隔离
        int starts = Starts.Count;
        bool opened = await Activate(menu.SettingsButton);
        await Frames(2);
        bool isolated = menu.MainButtons.All(b => b.Disabled && b.FocusMode == Control.FocusModeEnum.None);
        Check(opened && menu.OpenPanel == menu.SettingsPanel && menu.Overlay.Visible && menu.MusicSlider.HasFocus() && isolated,
            "设置：面板打开、压暗遮罩显示，焦点在音乐滑块上，5 个按钮暂时不能点、不能获得焦点");
        foreach (BaseButton target in new[] { menu.NewGameButton, menu.QuitButton })
            await ClickAt(target.GetGlobalRect().GetCenter());
        await Frames(10);
        Check(GetTree().CurrentScene == menu && Starts.Count == starts && GameSession.PendingStart == StartMode.None && menu.PanelOpen,
            "面板开着时，用鼠标点底层“醒来”和“退出”的位置：没有开新游戏，也没有退出");
        foreach (string action in new[] { "ui_down", "ui_down", "ui_up", "ui_accept" })
        {
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
            await Frames(1);
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
            await Frames(1);
        }
        Control focus = GetViewport().GuiGetFocusOwner();
        Check(Starts.Count == starts && GameSession.PendingStart == StartMode.None && focus != null && menu.SettingsPanel.IsAncestorOf(focus),
            $"面板开着时按方向键、确认键：焦点只在面板里移动（现在在 {focus?.Name}），不会开局或读档");

        // 音量滑块改音轨音量
        menu.MusicSlider.Value = 0;
        bool musicMuted = AudioSettings.BusState(AudioSettings.MusicBus).muted;
        menu.MusicSlider.Value = 100;
        Check(musicMuted && !AudioSettings.BusState(AudioSettings.MusicBus).muted, "音乐滑块拖到 0：音乐音轨静音；拖回来恢复");

        // 两个面板互斥
        menu.OpenCredits();
        await Frames(1);
        Check(menu.OpenPanel == menu.CreditsPanel && menu.CreditsPanel.Visible && !menu.SettingsPanel.Visible, "设置开着时再打开名单：只剩名单一个面板");

        // Esc 只关闭面板，焦点回到原按钮；“再续征程”仍按存档决定（没有存档 → 仍不能点）
        await PressKey(Key.Escape);   // 真实的 Esc 按键事件（ui_cancel / menu）
        await Frames(2);
        Check(!menu.PanelOpen && !menu.Overlay.Visible && menu.SettingsButton.HasFocus() && GetTree().CurrentScene == menu,
            "Esc：关闭面板，焦点回到打开它的“设置”，还在主菜单");
        Check(menu.ContinueButton.Disabled && !menu.NewGameButton.Disabled && !menu.QuitButton.Disabled,
            "无存档 → 打开设置 → 关闭：“再续征程”仍然不能点，其余按钮恢复");

        // 名单：打开 → 返回
        opened = await Activate(menu.CreditsButton);
        await Frames(2);
        Check(opened && menu.OpenPanel == menu.CreditsPanel && menu.CreditsBack.HasFocus(), "制作人员名单：面板打开，焦点在“返回”");
        await Activate(menu.CreditsBack);
        await Frames(2);
        Check(!menu.PanelOpen && menu.CreditsButton.HasFocus(), "名单按“返回”关闭，焦点回到“制作人员名单”");
        menu.NewGameButton.GrabFocus();
        await Frames(1);
    }

    private async Task PressKey(Key key)
    {
        foreach (bool pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
            await Frames(1);
        }
    }

    private async Task ClickAt(Vector2 position)
    {
        foreach (bool pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = position, GlobalPosition = position });
            await Frames(1);
        }
    }
}
