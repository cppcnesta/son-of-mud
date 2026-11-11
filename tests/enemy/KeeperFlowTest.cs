using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Enemy.Bosses.Keeper;
using SonOfMud.Enemy.Bosses.Keeper.States;
using SonOfMud.Narrative.World;
using SonOfMud.Player;
using SonOfMud.UI;
using SonOfMud.World;

/// <summary>
/// 计划第 6 节第 6 步：竞技场门、持久化、Boss 血条、变身时序。从正式入口 main_game.tscn 开新游戏，真实玩家、真实按键：
/// 在 S1_04 存档 → 走进 S1_05 开战（入口门关、血条出现）→ 站着不动被守坑人打死 → 在 S1_04 复活、入口门打开 →
/// 再进来守坑人满血 → 用真实按键打赢 → 闪白 1 秒内老人看不见、按 E 没反应、出口门还关着；闪白结束老人出现、能对话、
/// 出口门打开、BossDefeated 恰好一次 → 离开再回来：守坑人不在、老人还在 → 存档读档之后一样。
/// </summary>
public partial class KeeperFlowTest : EnemyFlowBase
{
    protected override string SuiteName => "keeper_flow";

    private KeeperBoss _keeper;
    private ArenaLock _lock;
    private NpcTalk _npc;
    private GateBarrier _gate;
    private BossBar _bar;

    protected override async Task RunChecks()
    {
        ListenEnemy();
        try
        {
            await Run();
        }
        finally
        {
            UnlistenEnemy();
        }
    }

    private void BindRoom()
    {
        _keeper = InRoom<KeeperBoss>().FirstOrDefault();
        _lock = InRoom<ArenaLock>().FirstOrDefault();
        _npc = InRoom<NpcTalk>().FirstOrDefault();
        _gate = InRoom<GateBarrier>().FirstOrDefault();
    }

    private bool InNpcArea => _npc != null && Interactor.GetOverlappingAreas().Contains(_npc);

    private async Task<bool> WalkIntoArena()
    {
        bool ok = await HoldKeyUntil(RealKeys.Right, () => LocalX >= 1460f, 600);
        await Frames(5);
        return ok;
    }

    private async Task Run()
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        BindNarrative();
        await WaitSettled();
        _bar = FindChild<BossBar>(MainGame);
        Check(finished && Player != null && _bar != null && !_bar.Showing, "新游戏：从正式入口开局，Boss 血条没显示");

        // 在 S1_04 存档（复活点）
        await Teleport("S1_04", SpawnPoint.SaveId);
        await KeyTap(RealKeys.Interact);
        await Frames(3);
        Check(Saves.CheckpointRoom == "S1_04" && FileAccess.FileExists(SavePath), "搭建：在 S1_04 存档点真实按 E 存档");

        // 走进 S1_05
        bool in105 = await HoldKeyUntil(RealKeys.Right, () => RoomId == "S1_05", 400);
        BindRoom();
        Check(in105 && _keeper != null && _keeper.State is DormantState && _keeper.Stats.Health == 150 && _keeper.TargetAtEnterTree == Player
              && _lock != null && !_lock.IsClosed && _npc != null && !_npc.Present && _gate != null && !_gate.IsOpen,
            "S1_05：走进来时守坑人在等待（满血，已拿到玩家），入口门开着，老人不在，出口门关着");
        if (_keeper == null || _lock == null || _npc == null || _gate == null) return;

        bool walked = await WalkIntoArena();
        Check(walked && Encounters.SequenceEqual(new[] { "keeper|守坑人：蝎|150" }) && _lock.IsClosed && _lock.CollisionLayer == Layers.World,
            "开战：玩家进入竞技场 → BossEncounterStarted 一次，入口门关上（挡路）");
        Check(_bar.Showing && _bar.Visible && _bar.BossId == "keeper" && _bar.MaxHp == 150 && _bar.DisplayName == "守坑人：蝎", "开战：Boss 血条出现（守坑人：蝎，150）");

        // 战斗中死亡：站着不动
        ulong firstKeeper = _keeper.GetInstanceId();
        ArenaLock firstLock = _lock;
        bool died = await WaitUntil(() => Deaths > 0, 3600);
        bool lockOpened = await WaitUntil(() => GodotObject.IsInstanceValid(firstLock) && !firstLock.IsClosed, 5);
        bool stillThere = RoomId == "S1_05";
        Check(died && lockOpened && stillThere, $"战斗中死亡：守坑人真实攻击打死玩家，PlayerDied 后入口门打开（还在 S1_05 时就打开）");
        bool respawned = await WaitUntil(() => RoomId == "S1_04" && !World.IsTransitioning && Player.Stats.Health == 140, 600);
        await Frames(10);
        Check(respawned && Deaths == 1 && Defeats.Count == 0 && !GameSession.Progress.Flag("boss_keeper"), "战斗中死亡：在 S1_04 复活、满血，守坑人没被记为击败");
        Check(!_bar.Showing && !_bar.Visible, "战斗中死亡：复活后 Boss 血条已收起，不再挂着（审查 Minor）");

        // 再进来：守坑人重新生成、满血
        in105 = await HoldKeyUntil(RealKeys.Right, () => RoomId == "S1_05", 400);
        BindRoom();
        Check(in105 && _keeper != null && _keeper.GetInstanceId() != firstKeeper && !GodotObject.IsInstanceIdValid(firstKeeper) && _keeper.Stats.Health == 150
              && _keeper.State is DormantState && !_lock.IsClosed,
            "再进来：守坑人重新生成、满血 150、等待；入口门开着");
        walked = await WalkIntoArena();
        Check(walked && Encounters.Count == 2 && _lock.IsClosed && _bar.Showing && _bar.TargetHp == 150, "再开战：入口门再次关上，血条重新显示满血");

        // 打赢
        var bot = new KeeperBot(Player, _keeper, n => Frames(n)) { AllowMove = false };
        bool won = await bot.FightUntil(() => _keeper.State is TransformState || Player.Stats.IsDepleted, 3600);
        Check(won && _keeper.State is TransformState && !Player.Stats.IsDepleted,
            $"打赢：真实按键把守坑人打到 0 血（出刀 {bot.Attacks}、格挡 {bot.BlockPresses}，玩家剩 {Player.Stats.Health}）");
        if (_keeper.State is not TransformState) return;

        // 闪白 1 秒内
        if (!InNpcArea) await HoldKeyUntil(RealKeys.Toward(_npc.GlobalPosition.X - Player.GlobalPosition.X), () => InNpcArea, 30);
        bool inArea = InNpcArea;
        int requests = Requests.Count;
        bool npcSeen = false, prompt = false, gateOpen = false, flag = false, defeated = false, talked = false, lockClosed = true;
        int frames = 0;
        while (_keeper.State is TransformState && frames < 120)
        {
            if (frames == 8 || frames == 30) RealKeys.Down(RealKeys.Interact);
            if (frames == 9 || frames == 31) RealKeys.Up(RealKeys.Interact);
            npcSeen |= _npc.Visible || _npc.Present;
            prompt |= Interactor.CurrentPrompt == "交谈";
            gateOpen |= _gate.IsOpen;
            lockClosed &= _lock.IsClosed;
            flag |= GameSession.Progress.Flag("boss_keeper");
            defeated |= Defeats.Count > 0;
            talked |= Narr.MainActive || Requests.Count > requests;
            await Frames(1);
            frames++;
        }
        Check(inArea && !npcSeen && !prompt && !talked, $"闪白 1 秒内（{frames} 帧）：站在老人的位置上，老人看不见、没有交互提示，按 E 没反应");
        Check(!gateOpen && !flag && !defeated && lockClosed, "闪白 1 秒内：出口门还关着（入口门也还关着），boss_keeper 还没写，BossDefeated 还没发");

        // 闪白结束
        await Frames(2);
        Check(Defeats.SequenceEqual(new[] { "keeper" }) && GameSession.Progress.Flag("boss_keeper") && _npc.Present && _npc.Visible && _gate.IsOpen && !_lock.IsClosed
              && _keeper.State is GoneState && !_keeper.Visible,
            "闪白结束：boss_keeper = true，老人出现，出口门和入口门都打开，BossDefeated(keeper) 发出，守坑人（蝎）隐藏");
        Check(!_bar.Showing, "闪白结束：Boss 血条收起");
        bool atNpc = Interactor.CurrentPrompt == "交谈" || await WalkToPrompt("交谈", PlayerActions.Right);
        await KeyTap(RealKeys.Interact);
        await WaitUntil(() => Narr.MainActive, 10);
        bool dialog = Narr.MainActive && Narr.MainLabel == "守坑人" && Narr.Dialog.CurrentText == "祂……醒了……";
        bool done = await AdvanceUntil(() => !Narr.MainActive, 30);
        await WaitUntil(() => !PauseService.IsPaused, 60);
        Check(atNpc && dialog && done && GameSession.Progress.Flag("talked.keeper"), "闪白结束：按 E 和老人对话（“祂……醒了……”），对话播完");
        await Frames(120);
        Check(Defeats.Count == 1, "BossDefeated 恰好一次");

        // 离开再回来
        bool left = await HoldKeyUntil(RealKeys.Left, () => RoomId == "S1_04", 900);
        bool back = left && await HoldKeyUntil(RealKeys.Right, () => RoomId == "S1_05", 400);
        BindRoom();
        Check(back && _keeper != null && _keeper.State is GoneState && !_keeper.Visible && _npc.Present && _gate.IsOpen && !_lock.IsClosed && Encounters.Count == 2 && Defeats.Count == 1,
            "离开再回来：守坑人不在（直接是已击败），老人还在，出口门开着，不再开战");

        // 存档读档
        await Teleport("S1_04", SpawnPoint.SaveId);
        await KeyTap(RealKeys.Interact);
        await Frames(3);
        finished = await EnterMainGame(StartMode.Continue);
        BindNarrative();
        await WaitSettled();
        back = finished && await HoldKeyUntil(RealKeys.Right, () => RoomId == "S1_05", 400);
        BindRoom();
        Check(back && PlayerRegistry.Current == Player && _keeper != null && _keeper.State is GoneState && !_keeper.Visible && _npc.Present && _gate.IsOpen
              && GameSession.Progress.Flag("boss_keeper") && Encounters.Count == 2,
            "存档读档之后：守坑人不在，老人还在，出口门开着");
    }
}
