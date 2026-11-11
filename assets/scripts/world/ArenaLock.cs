using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// Boss 房的入口门（只听 Events，不认识 Enemy）：开局是开着的；
/// 收到 BossEncounterStarted(BossId) 关上；收到 BossDefeated(BossId)、PlayerDied 或离开房间（RoomExiting）时打开。
/// 关着时是 World 层的阻挡并显示占位色块；开着时没有碰撞、隐藏。出口那道门是 GateBarrier（读 boss_&lt;id&gt;），不归这里管。
/// 门体在 Surfaces.NoClimbGroup 分组里：不能攀爬、不能蹬墙跳。
/// </summary>
[GlobalClass]
public partial class ArenaLock : StaticBody2D
{
    [Export] public StringName BossId { get; set; } = "";

    public bool IsClosed { get; private set; }

    public override void _EnterTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.BossEncounterStarted += OnEncounterStarted;
        Events.Instance.BossDefeated += OnBossDefeated;
        Events.Instance.PlayerDied += Open;
        Events.Instance.RoomExiting += OnRoomExiting;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.BossEncounterStarted -= OnEncounterStarted;
        Events.Instance.BossDefeated -= OnBossDefeated;
        Events.Instance.PlayerDied -= Open;
        Events.Instance.RoomExiting -= OnRoomExiting;
    }

    public override void _Ready()
    {
        // 门体不能攀爬，也不能蹬墙跳（关卡几何计划 v3 第 2.1 节）：战斗中不能贴着门爬出竞技场。
        AddToGroup(Surfaces.NoClimbGroup);
        Apply(false);
    }

    private void OnEncounterStarted(StringName bossId, string displayName, int maxHp)
    {
        if (bossId == BossId) Apply(true);
    }

    private void OnBossDefeated(StringName bossId, Vector2 position)
    {
        if (bossId == BossId) Open();
    }

    private void OnRoomExiting(string roomId) => Open();

    public void Open() => Apply(false);

    private void Apply(bool closed)
    {
        IsClosed = closed;
        CollisionLayer = closed ? Layers.World : 0u;
        CollisionMask = 0;
        Visible = closed;
    }
}
