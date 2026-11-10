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
        if (Instance == this) Instance = null;
    }

    // World
    [Signal] public delegate void RoomEnteredEventHandler(string roomId);
    [Signal] public delegate void RoomExitingEventHandler(string roomId);
    [Signal] public delegate void CellChangedEventHandler(Vector3I cell);
    [Signal] public delegate void TravelRequestedEventHandler(string roomPath, StringName spawnId);
    [Signal] public delegate void MessageRequestedEventHandler(string text);

    // Save / 菜单流程
    [Signal] public delegate void SaveRequestedEventHandler(StringName savePointId);
    [Signal] public delegate void GameSavedEventHandler();
    [Signal] public delegate void GameLoadedEventHandler(string roomId);
    [Signal] public delegate void NewGameRequestedEventHandler();
    [Signal] public delegate void ContinueRequestedEventHandler();
    [Signal] public delegate void QuitToMenuRequestedEventHandler();

    // 进度（ProgressStore 发出）
    [Signal] public delegate void AbilityUnlockedEventHandler(StringName abilityId);
    [Signal] public delegate void VariableChangedEventHandler(StringName key, Variant value);

    // Player
    [Signal] public delegate void PlayerSpawnedEventHandler(Node2D player);
    [Signal] public delegate void PlayerHealthChangedEventHandler(int current, int max);
    [Signal] public delegate void PlayerFormChangedEventHandler(int form);
    [Signal] public delegate void PlayerDiedEventHandler();

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
