using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 跨系统事件总线（唯一新增的 autoload，名字 Events）。
/// 只有跨系统的通知走这里；同一系统内部用本地信号。参数只能是 Godot 内置类型或 Core 自己的类型。
/// </summary>
public partial class Events : Node
{
    /// <summary>autoload 实例。测试场景里没有 autoload 时为 null，发送方要用 ?. 调用。</summary>
    public static Events Instance { get; private set; }

    private static int _lastRequestId;

    /// <summary>换房等“请求 + 回执”类事件用的编号，全局递增，不会重复。</summary>
    public static int NewRequestId() => System.Threading.Interlocked.Increment(ref _lastRequestId);

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            GD.PushError("Events 已存在一个实例，忽略重复的 Events 节点。");
            return;
        }
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance != this) return;
        Instance = null;
        // Events 是 autoload，只在进程退出时离开场景树，这时引擎还活着：
        // 先释放静态持有的 Godot 对象，再强制回收，让终结器在引擎销毁之前跑完（否则退出时会崩溃）。
        ShutdownHooks.RunAll();
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
    }

    // World
    [Signal] public delegate void RoomEnteredEventHandler(string roomId);
    [Signal] public delegate void RoomExitingEventHandler(string roomId);
    [Signal] public delegate void CellChangedEventHandler(Vector3I cell);
    // 换房请求与回执：requestId 由发请求的一方用 NewRequestId() 取得。
    // TravelFinished 在玩家已被放到目标出生点之后发出（淡入可能还没结束）；被拒绝或失败时 success=false。
    [Signal] public delegate void TravelRequestedEventHandler(int requestId, string roomIdOrPath, StringName spawnId);
    [Signal] public delegate void TravelFinishedEventHandler(int requestId, string roomId, bool success);
    [Signal] public delegate void MessageRequestedEventHandler(string text);

    // Save / 菜单流程
    [Signal] public delegate void SaveRequestedEventHandler(StringName savePointId);
    [Signal] public delegate void GameSavedEventHandler();
    [Signal] public delegate void GameLoadedEventHandler(string roomId);
    [Signal] public delegate void NewGameRequestedEventHandler();
    [Signal] public delegate void ContinueRequestedEventHandler();
    [Signal] public delegate void QuitToMenuRequestedEventHandler();
    // 开局结果：SaveService 在新游戏 / 读档真正到达房间之后发 success=true；
    // 存档损坏、版本不支持、目标房间不存在、换房失败时发 success=false 并带上原因（失败时不改动存档文件）。
    [Signal] public delegate void GameStartFinishedEventHandler(bool success, string error);
    // 重开一局：一律重建整个游戏实例（World 的 GameBoot 负责切回正式入口场景），不在当前实例里重置。
    // NewGameRequested 只能由 GameBoot 发；其他系统要重开一局，发这个事件。
    [Signal] public delegate void RestartRunRequestedEventHandler();
    // 整份进度被换掉之后（新游戏清空、读档恢复都算）由 SaveService 发出，这时 GameSession.Progress 已经是最终内容。
    // 这种整体替换不会为每个变量发 VariableChanged，需要按进度推导状态的系统（例如玩家的血量上限）在这里重新计算。
    [Signal] public delegate void ProgressRestoredEventHandler();

    // 进度（ProgressStore 发出）
    [Signal] public delegate void AbilityUnlockedEventHandler(StringName abilityId);
    [Signal] public delegate void VariableChangedEventHandler(StringName key, Variant value);

    // Player
    [Signal] public delegate void PlayerSpawnedEventHandler(Node2D player);
    [Signal] public delegate void PlayerHealthChangedEventHandler(int current, int max);
    [Signal] public delegate void PlayerFormChangedEventHandler(int form);
    [Signal] public delegate void PlayerDiedEventHandler();
    // 玩家附近最近的可交互对象的提示文字变了；空字符串表示附近没有可交互的东西。
    [Signal] public delegate void InteractPromptChangedEventHandler(string prompt);

    // 换房契约（规格第 4 节）
    // RoomExiting：每次换房（走边缘、过门都算）在卸载旧房间之前发出 → Player 关掉正在生效的 HitBox。
    // PlayerTeleported：只在过门和复活后发出 → Player 做完整清理（回 Idle、速度清零、清空缓冲……）。
    [Signal] public delegate void PlayerTeleportedEventHandler(StringName spawnId);
    [Signal] public delegate void CheckpointRespawnedEventHandler();

    // 淡入淡出契约：请求和完成用 requestId 配对，发请求的一方必须等到同一 id 的完成通知再继续。
    [Signal] public delegate void FadeRequestedEventHandler(int requestId, bool fadeOut);
    [Signal] public delegate void FadeCompletedEventHandler(int requestId, bool fadeOut);

    // 战斗反馈
    [Signal] public delegate void HitStopRequestedEventHandler(float seconds);
    [Signal] public delegate void CameraShakeRequestedEventHandler(float strength, float duration);

    // Enemy / Boss
    [Signal] public delegate void BossEncounterStartedEventHandler(StringName bossId, string displayName, int maxHp);
    [Signal] public delegate void BossHealthChangedEventHandler(StringName bossId, int hp, int maxHp);
    [Signal] public delegate void BossPhaseChangedEventHandler(StringName bossId, int phase);
    [Signal] public delegate void BossDefeatedEventHandler(StringName bossId, Vector2 position);
    [Signal] public delegate void EnemyKilledEventHandler(StringName enemyKind, Vector2 position);
    [Signal] public delegate void ItemDroppedEventHandler(StringName itemId, Vector2 position);

    // Narrative
    [Signal] public delegate void StoryRequestedEventHandler(string scriptId, string label);
    [Signal] public delegate void NarrativeStartedEventHandler(string scriptId, string label);
    [Signal] public delegate void NarrativeFinishedEventHandler(string scriptId, string label);
    [Signal] public delegate void StoryEventRaisedEventHandler(string name, string arg);
    [Signal] public delegate void TabletCollectedEventHandler(StringName tabletId);

    // UI
    [Signal] public delegate void ArtifactsChangedEventHandler(int mask);
    [Signal] public delegate void MapToggleRequestedEventHandler();

    // 暂停与输入锁（只由 PauseService / InputLock 发出）
    [Signal] public delegate void PauseChangedEventHandler(bool paused);
    [Signal] public delegate void InputLockChangedEventHandler(bool locked);

    // Tools
    [Signal] public delegate void DebugFlyToggledEventHandler();
}
