using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.UI;
using SonOfMud.World;

/// <summary>
/// 计划第 4 节步骤 3（从正式入口 main_game.tscn 启动）：
/// ① main_game 里只多了一个 GameUi；Esc 暂停、再按恢复；“继续”按钮；暂停时世界不动。
/// ② 地图键开关地图，打开时以当前格为中心，方向键平移；再打开重新居中。
/// ③ 剧情对话中（PauseService 的 Narrative 来源）按 Esc / 地图键无效；规格反例：剧情暂停 → 开地图 → 关地图，仍保持暂停。
/// ④ 地图和暂停菜单互斥：菜单开着按地图键 = 关菜单开地图（中间不恢复）；地图开着按 Esc 只关地图；同一帧两键只开一个；
///    反例：即使两者被同时打开，关掉一个也不会解除另一个的暂停。
/// ⑤ 小地图：开局时中心是当前格；换房、走进新格子后跟着更新。
/// </summary>
public partial class UiPauseMapTest : UiTestBase
{
    protected override string SuiteName => "pause_map";

    private GameUi _ui;
    private readonly List<bool> _pauseEvents = new();

    protected override async Task RunChecks()
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        Check(finished && Starts.Count == 1 && Starts[0].success, "正式入口开新游戏成功");
        if (World == null || Player == null) return;
        await WaitSettled();
        _ui = Ui;
        Check(_ui != null && _ui.Hud != null && _ui.Map != null && _ui.Menu != null, "main_game.tscn 下有 GameUi（HUD、地图、暂停菜单）");
        if (_ui == null) return;
        // 第 3 轮合并后 main_game.tscn 还会有 Narrative 的 NarrativeLayer；这里只检查 UI 自己的约定：
        // 原有的 Game、Player、GameBoot 都在，UI 只加了一个 GameUi。
        var names = MainGame.GetChildren().Select(c => c.Name.ToString()).ToList();
        string children = string.Join(",", names);
        bool baseIntact = names.Take(3).SequenceEqual(new[] { "Game", "Player", "GameBoot" });
        int uiNodes = MainGame.GetChildren().Count(c => c is GameUi);
        Check(baseIntact && uiNodes == 1 && names.Contains("GameUi"), $"main_game.tscn 原有节点不变，UI 只加了一个 GameUi（{children}）");
        Check(FindChild<GameUi>(MainGame.GetNode("Game")) == null && FindChild<Hud>(MainGame.GetNode("Game")) == null, "测试容器 game.tscn 里没有加 UI");

        Events.Instance.PauseChanged += OnPauseChanged;
        try
        {
            await PauseMenuChecks();
            await MapChecks();
            await NarrativeChecks();
            await ExclusionChecks();
            await MinimapChecks();
        }
        finally
        {
            Events.Instance.PauseChanged -= OnPauseChanged;
        }
        Check(!_ui.Map.IsOpen && !_ui.Menu.IsOpen && NoPauseOrLock(), "结束时地图和菜单都关着，没有暂停和输入锁");
    }

    private void OnPauseChanged(bool paused) => _pauseEvents.Add(paused);

    private Vector2I CurrentCell => new(MetSysApi.CurrentCoords.X, MetSysApi.CurrentCoords.Y);

    private bool OnlyMenu => _ui.Menu.IsOpen && _ui.Menu.Visible && !_ui.Map.IsOpen && !_ui.Map.Visible
                             && PauseService.IsRequestedBy(PauseService.PauseMenu) && !PauseService.IsRequestedBy(PauseService.Map) && GetTree().Paused;

    private bool OnlyMap => _ui.Map.IsOpen && _ui.Map.Visible && !_ui.Menu.IsOpen && !_ui.Menu.Visible
                            && PauseService.IsRequestedBy(PauseService.Map) && !PauseService.IsRequestedBy(PauseService.PauseMenu) && GetTree().Paused;

    private bool NothingOpen => !_ui.Map.IsOpen && !_ui.Menu.IsOpen && !_ui.Map.Visible && !_ui.Menu.Visible
                                && !PauseService.IsRequestedBy(PauseService.Map) && !PauseService.IsRequestedBy(PauseService.PauseMenu);

    // ---------- ① 暂停菜单 ----------

    private async Task PauseMenuChecks()
    {
        Check(NothingOpen && !GetTree().Paused, "开局时没有界面打开，没有暂停");
        await Tap(GameUi.MenuAction);
        Check(OnlyMenu, "按 Esc：打开暂停菜单，PauseService 的 PauseMenu 来源暂停游戏");
        Check(_ui.Menu.ResumeButton.HasFocus(), "暂停菜单打开时“继续”按钮拿到焦点");

        Vector2 before = Player.GlobalPosition;
        Input.ActionPress(PlayerActions.Right);
        await Frames(20);
        Input.ActionRelease(PlayerActions.Right);
        Check(Player.GlobalPosition == before, "暂停中按住右键，玩家不动");

        await Tap(GameUi.MenuAction);
        Check(NothingOpen && !GetTree().Paused && !PauseService.IsPaused, "再按 Esc：关闭菜单，恢复");

        await Tap(GameUi.MenuAction);
        bool sent = await Activate(_ui.Menu.ResumeButton);
        Check(sent && NothingOpen && !GetTree().Paused, "暂停菜单“继续”按钮：关闭菜单，恢复");
    }

    // ---------- ② 地图 ----------

    private async Task MapChecks()
    {
        Vector2I cell = CurrentCell;
        await Tap(GameUi.MapAction);
        Check(OnlyMap, "按地图键：打开地图，PauseService 的 Map 来源暂停游戏");
        Check(_ui.Map.Center == cell && _ui.Map.Layer == MetSysApi.CurrentCoords.Z, $"地图打开时以当前格为中心（地图 {_ui.Map.Center}，当前 {cell}）");
        Check(MetSysApi.IsCellDiscovered(MetSysApi.CurrentCoords, false), "当前格已探索（地图上会显示）");

        await Tap("ui_right");
        Check(_ui.Map.Center == cell + Vector2I.Right, $"方向键右：地图平移一格（{_ui.Map.Center}）");
        await Tap("ui_left");
        await Tap("ui_left");
        Check(_ui.Map.Center == cell + Vector2I.Left, $"方向键左两下：向左平移两格（{_ui.Map.Center}）");
        Input.ActionPress("ui_down");
        await Frames(30);
        Input.ActionRelease("ui_down");
        await Frames(1);
        int moved = _ui.Map.Center.Y - cell.Y;
        Check(moved >= 4 && moved <= 7 && _ui.Map.Center.X == cell.X - 1, $"按住方向键下半秒：连续平移（{moved} 格，设定每秒 10 格）");

        await Tap(GameUi.MapAction);
        Check(NothingOpen && !GetTree().Paused, "再按地图键：关闭地图，恢复");
        await Tap(GameUi.MapAction);
        Check(OnlyMap && _ui.Map.Center == cell, "重新打开地图：回到以当前格为中心");
        await Tap(GameUi.MapAction);
        Check(NothingOpen && !GetTree().Paused, "关闭地图");
    }

    // ---------- ③ 剧情中 ----------

    private async Task NarrativeChecks()
    {
        PauseService.Request(PauseService.Narrative, true);
        await Frames(2);
        await Tap(GameUi.MenuAction);
        await Tap(GameUi.MapAction);
        Events.Instance.EmitSignal(Events.SignalName.MapToggleRequested);
        await Frames(2);
        Check(NothingOpen && GetTree().Paused && PauseService.IsRequestedBy(PauseService.Narrative),
            "剧情对话中按 Esc、地图键、发 MapToggleRequested：都没有反应，仍是剧情的暂停");

        // 规格反例：剧情（计数 1）→ 地图（计数 2）→ 关地图（计数 1，仍暂停）→ 剧情结束（计数 0，恢复）
        _ui.Map.Open();
        bool both = PauseService.IsRequestedBy(PauseService.Narrative) && PauseService.IsRequestedBy(PauseService.Map) && GetTree().Paused;
        _ui.Map.Close();
        await Frames(2);
        Check(both && GetTree().Paused && PauseService.IsPaused && PauseService.IsRequestedBy(PauseService.Narrative) && !PauseService.IsRequestedBy(PauseService.Map),
            "规格反例：剧情暂停 → 开地图 → 关地图，仍保持暂停");
        PauseService.Request(PauseService.Narrative, false);
        await Frames(2);
        Check(!GetTree().Paused && !PauseService.IsPaused, "剧情结束后恢复");

        await Tap(GameUi.MenuAction);
        Check(OnlyMenu, "剧情结束后 Esc 又能打开暂停菜单");
        await Tap(GameUi.MenuAction);

        InputLock.Request(InputLock.RoomTransition, true);
        await Tap(GameUi.MenuAction);
        await Tap(GameUi.MapAction);
        bool blocked = NothingOpen && !GetTree().Paused;
        InputLock.Request(InputLock.RoomTransition, false);
        await Frames(2);
        Check(blocked, "换房淡入淡出期间（RoomTransition 输入锁）按 Esc / 地图键也不打开界面");
    }

    // ---------- ④ 互斥 ----------

    private async Task ExclusionChecks()
    {
        await Tap(GameUi.MenuAction);
        Check(OnlyMenu, "互斥搭建：暂停菜单开着");
        _pauseEvents.Clear();
        await Tap(GameUi.MapAction);
        Check(OnlyMap, "菜单开着时按地图键：菜单关闭、地图打开，同一时间只有一个");
        Check(!_pauseEvents.Contains(false) && GetTree().Paused, $"从菜单切到地图的过程中没有恢复过一帧（PauseChanged：{string.Join(",", _pauseEvents)}）");
        await Tap(GameUi.MenuAction);
        await Frames(2);
        Check(NothingOpen && !GetTree().Paused, "地图开着时按 Esc：只关地图，不会接着打开暂停菜单");

        await Tap(GameUi.MenuAction);
        bool sent = await Activate(_ui.Menu.MapButton);
        Check(sent && OnlyMap, "暂停菜单里点“地图”：菜单关闭、地图打开");
        await Tap(GameUi.MapAction);
        Check(NothingOpen && !GetTree().Paused, "关闭地图后恢复");

        Input.ActionPress(GameUi.MenuAction);
        Input.ActionPress(GameUi.MapAction);
        await Frames(1);
        ReleaseInputs();
        await Frames(2);
        Check(OnlyMenu, "同一帧按下 Esc 和地图键：只打开暂停菜单");
        await Tap(GameUi.MenuAction);

        Events.Instance.EmitSignal(Events.SignalName.MapToggleRequested);
        await Frames(1);
        Check(OnlyMap, "MapToggleRequested 事件：打开地图");
        Events.Instance.EmitSignal(Events.SignalName.MapToggleRequested);
        await Frames(1);
        Check(NothingOpen && !GetTree().Paused, "再发一次 MapToggleRequested：关闭地图");

        // 反例：就算两个界面被直接同时打开（漏判），计数也保证关掉一个不会解除另一个的暂停
        _ui.Menu.Open();
        _ui.Map.Open();
        _ui.Map.Close();
        await Frames(1);
        bool stillPaused = GetTree().Paused && PauseService.IsRequestedBy(PauseService.PauseMenu);
        _ui.Menu.Close();
        await Frames(1);
        Check(stillPaused && !GetTree().Paused, "反例：菜单和地图被同时打开时，关地图仍保持菜单的暂停，关菜单后才恢复");
    }

    // ---------- ⑤ 小地图 ----------

    private async Task MinimapChecks()
    {
        Control minimap = _ui.Hud.Minimap;
        Check(minimap != null && minimap.IsVisibleInTree() && minimap.GetParent() == _ui.Hud.Root, "HUD 右上角有小地图");
        if (minimap == null) return;
        Check(MetSysApi.GetMapViewCenter(minimap) == CurrentCell, $"小地图中心是当前格（{MetSysApi.GetMapViewCenter(minimap)} / {CurrentCell}）");

        Vector2I first = CurrentCell;
        Input.ActionPress(PlayerActions.Left);
        bool crossed = await WaitUntil(() => CurrentCell != first, 400);
        Input.ActionRelease(PlayerActions.Left);
        await Frames(2);
        Check(crossed && MetSysApi.GetMapViewCenter(minimap) == CurrentCell,
            $"走进新格子后小地图跟着更新（{first} → {CurrentCell}，小地图 {MetSysApi.GetMapViewCenter(minimap)}）");

        Vector2I before = CurrentCell;
        World.LoadRoomImmediate("S1_04", SpawnPoint.SaveId);
        await Frames(15);
        Check(CurrentCell != before && MetSysApi.GetMapViewCenter(minimap) == CurrentCell,
            $"换到 S1_04 后小地图跟着更新（{before} → {CurrentCell}）");

        await Tap(GameUi.MapAction);
        Check(OnlyMap && _ui.Map.Center == CurrentCell, "换房后打开大地图，中心也是新的当前格");
        await Tap(GameUi.MapAction);
    }
}
