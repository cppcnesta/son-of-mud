using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.UI;
using SonOfMud.World;

/// <summary>
/// 正式入口开新游戏：PendingStart = NewGame → 切到 main_game.tscn。
/// 有且只有一个真实玩家、WorldGame 和镜头都拿到它；能力正好是能力表里的初始两项；人在 S1_01 的 Default 出生点，
/// 140 血、水形态；GameStartFinished(true) 恰好一次；PendingStart 清零；新游戏不碰存档文件。
/// 另外确认测试容器 game.tscn 不会自动开局；最后发 QuitToMenuRequested：清掉暂停和输入锁、回到主菜单场景。
/// </summary>
public partial class BootNewGameTest : FlowTestBase
{
    protected override string SuiteName => "boot_new_game";

    protected override async Task RunChecks()
    {
        await GameContainerDoesNotAutoStart();

        bool finished = await EnterMainGame(StartMode.NewGame);
        Check(finished && MainGame != null, "切到正式入口 main_game.tscn 后收到开局结果");
        if (MainGame == null || World == null || Player == null) return;
        await WaitSettled();
        await Frames(30);

        var players = AllPlayers();
        Check(players.Count == 1 && players[0] == Player, $"场景里有且只有一个真实玩家（实际 {players.Count} 个）");
        Check(Player.SceneFilePath == "res://assets/scenes/player/player_v2.tscn", "玩家来自 player_v2.tscn");
        Check(World.Player == Player && Cam.Target == Player, "WorldGame 和镜头都拿到了这个玩家");
        Check(Player.GetNodeOrNull<PlayerInteractor>("Interactor") != null, "玩家身上有交互组件 Interactor");
        Check(MainGame.GetChildren().OfType<GameBoot>().Count() == 1, "main_game 里有一个 GameBoot");

        Check(AbilityList() == "block,switch_form", $"能力正好是 block 和 switch_form（实际 {AbilityList()}）");
        Check(!GameSession.Progress.VariableKeys.Any(), "新游戏没有任何进度变量");

        SpawnPoint spawn = World.CurrentRoom?.FindSpawn("Default");
        Check(World.CurrentRoomDef?.Id == "S1_01", $"人在 S1_01（实际 {World.CurrentRoomDef?.Id}）");
        Check(spawn != null && Mathf.Abs(Player.GlobalPosition.X - spawn.GlobalPosition.X) < 1f && Mathf.Abs(Player.GlobalPosition.Y - spawn.GlobalPosition.Y) < 32f,
            $"人在 S1_01 的 Default 出生点（玩家 {Player.GlobalPosition}，出生点 {spawn?.GlobalPosition}）");
        Check(Player.IsOnFloor(), "站在地面上");
        Check(Player.Stats.Health == 140 && Player.Stats.MaxHealth == 140, $"血量 140/140（实际 {Player.Stats.Health}/{Player.Stats.MaxHealth}）");
        Check(Player.Forms.Form == FormController.Water, "水形态");

        Check(Starts.Count == 1 && Starts[0].success && Starts[0].error == "", $"收到 GameStartFinished(true) 恰好一次（实际 {Starts.Count} 次）");
        Check(GameSession.PendingStart == StartMode.None, "PendingStart 已经清零");
        Check(GameSession.LastStartError == "", "开局成功后 LastStartError 为空");
        Check(Boot != null && Boot.StartedWith == StartMode.NewGame && !Boot.AwaitingStart, "GameBoot 按 NewGame 开局并已收到结果");
        Check(!FileAccess.FileExists(SavePath), "新游戏不写存档文件");
        Check(NoPauseOrLock(), "开局后没有暂停、没有输入锁");

        await QuitToMenuChecks();
    }

    private async Task QuitToMenuChecks()
    {
        ulong playerId = Player.GetInstanceId();
        PauseService.Request(PauseService.PauseMenu, true);
        InputLock.Request(InputLock.Narrative, true);
        Events.Instance.EmitSignal(Events.SignalName.QuitToMenuRequested);
        bool inMenu = await WaitUntil(() => GetTree().CurrentScene is MudMainMenu, 120);
        await Frames(5);
        Check(inMenu && GetTree().CurrentScene?.SceneFilePath == MenuScene, $"QuitToMenuRequested：回到主菜单场景（当前 {GetTree().CurrentScene?.SceneFilePath}）");
        Check(NoPauseOrLock(), "QuitToMenuRequested：暂停和输入锁都已清掉");
        Check(!GodotObject.IsInstanceIdValid(playerId) && AllPlayers().Count == 0, "QuitToMenuRequested：正式场景（含玩家）已整个释放");
        Check(!FileAccess.FileExists(SavePath) && GameSession.LastStartError == "", "QuitToMenuRequested：不写存档、不记录开局错误");
    }

    /// <summary>game.tscn 是测试容器：没有 GameBoot、没有玩家，放进树里也不会发开局请求。</summary>
    private async Task GameContainerDoesNotAutoStart()
    {
        int newGames = 0, continues = 0, travels = 0;
        void OnNew() => newGames++;
        void OnContinue() => continues++;
        void OnTravel(int id, string room, StringName spawn) => travels++;
        Events.Instance.NewGameRequested += OnNew;
        Events.Instance.ContinueRequested += OnContinue;
        Events.Instance.TravelRequested += OnTravel;

        Node game = GD.Load<PackedScene>("res://assets/scenes/world/game.tscn").Instantiate();
        AddChild(game);
        await Frames(20);
        Check(FindChild<GameBoot>(game) == null && FindChild<PlayerCharacter>(game) == null, "game.tscn 里没有 GameBoot、没有玩家");
        Check(newGames == 0 && continues == 0 && travels == 0 && Starts.Count == 0,
            $"game.tscn 放进树里 20 帧不会自动开局（新游戏 {newGames}、继续 {continues}、换房 {travels}、开局结果 {Starts.Count}）");
        game.QueueFree();
        await Frames(2);

        Events.Instance.NewGameRequested -= OnNew;
        Events.Instance.ContinueRequested -= OnContinue;
        Events.Instance.TravelRequested -= OnTravel;
    }
}
