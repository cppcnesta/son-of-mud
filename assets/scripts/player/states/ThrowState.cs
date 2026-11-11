using System;
using System.Collections.Generic;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 扔泥肝（R）。只能从自由状态（地面 Idle / Run、空中 Rise / Fall）进入：攻击、后摇、闪避、格挡、受伤、眩晕、
/// 死亡、锁定、调试飞行时按 R 都没反应；R 不进输入缓冲，这些动作结束后也不会补扔。
/// 进入的那一帧就出手：count.泥肝 减 1，朝面向方向抛出一个玩家方的投掷物（只打敌人受击框），伤害固定 15、
/// 不吃形态倍率和泥格挡增伤。动作持续 Duration（0.2 秒），之后回自由状态；两次出手至少隔 Interval（0.4 秒）。
/// 地面上站定出手；空中出手照常受重力下落、可以左右移动（不打断下落）。没有泥肝时按 R 不进这个状态。
/// 换房（RoomExiting）时，还在飞的泥肝直接消失，不会留在旧房间的坐标里。
/// </summary>
public partial class ThrowState : PlayerState
{
    [Export] public LiverThrowConfig Config { get; set; }

    /// <summary>距离下一次能扔还要多久（秒）。切形态、换房都不重置。</summary>
    public float Cooldown { get; private set; }
    public float Remaining { get; private set; }
    /// <summary>一共扔出去的个数（测试和调试用）。</summary>
    public int Thrown { get; private set; }
    /// <summary>最近一次扔出去的泥肝（可能已经释放，用前先 IsInstanceValid）。</summary>
    public Projectile LastProjectile { get; private set; }
    /// <summary>每扔出一个泥肝通知一次（测试和以后的表现用）。</summary>
    public event Action<Projectile> ProjectileThrown;

    public override string AnimName => "throw";
    public override string DisplayName => "扔泥肝";
    public override float AnimDuration => Config?.Duration ?? 0f;

    private readonly List<Projectile> _flying = new();

    public StringName ItemId => Config?.ItemId ?? new StringName("泥肝");
    public long Count => ItemCounts.Get(ItemId);
    public bool IntervalReady => Cooldown <= 0f;

    /// <summary>自由状态里调用：这一帧按了 R、间隔已过、手里有泥肝就进入投掷，返回 true。</summary>
    public static bool TryEnter(StateMachine machine) => machine?.Get<ThrowState>()?.TryBegin() ?? false;

    private bool TryBegin()
    {
        if (Config?.Hit == null || !IntervalReady || Count <= 0 || !P.JustPressed(PlayerActions.ThrowItem)) return false;
        Machine.TransitionTo<ThrowState>();
        return true;
    }

    public override void _EnterTree()
    {
        if (Events.Instance != null) Events.Instance.RoomExiting += OnRoomExitingEvent;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.RoomExiting -= OnRoomExitingEvent;
    }

    /// <summary>间隔自己计时（玩家暂停时它也跟着暂停）。</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (Cooldown > 0f) Cooldown = Mathf.Max(0f, Cooldown - (float)delta);
        if (Cooldown < 0.000001f) Cooldown = 0f;
    }

    public override void Enter(StateMsg msg)
    {
        Remaining = Config?.Duration ?? 0f;
        Cooldown = Config?.Interval ?? 0f;
        long count = Count;
        if (Config?.Hit == null || count <= 0) return;
        ItemCounts.Set(ItemId, count - 1);
        Launch();
    }

    private void Launch()
    {
        float facing = P.Facing >= 0f ? 1f : -1f;
        Vector2 from = P.GlobalPosition + new Vector2(Mathf.Abs(Config.LaunchOffset.X) * facing, Config.LaunchOffset.Y);
        float gravity = Config.Projectile?.Gravity ?? 1200f;
        float flight = Mathf.Max(Config.FlightTime, 0.05f);
        Vector2 velocity = Projectile.ArcVelocity(from, from + new Vector2(facing * Config.Range, 0f), gravity, Config.Range / flight, flight);
        // 倍率固定为 1：泥肝不吃形态倍率和泥格挡增伤，也不消耗增伤。
        Hit hit = Hit.From(Config.Hit, P.NextAttackId("liver"), from, 1f, P);
        Node parent = P.GetParent() ?? P;
        Projectile projectile = Projectile.Launch(parent, from, velocity, hit, ProjectileTeam.Player, Config.Projectile);
        Thrown++;
        LastProjectile = projectile;
        _flying.RemoveAll(p => !IsInstanceValid(p) || p.Ended);
        _flying.Add(projectile);
        ProjectileThrown?.Invoke(projectile);
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        if (P.IsOnFloor())
        {
            P.StopHorizontal();
            P.StickToFloor(dt);
        }
        else
        {
            P.Walk(P.Axis);
            P.ApplyAirGravity(dt);
        }
        Remaining -= dt;
        if (Remaining <= 0.000001f) P.ToFreeState();
    }

    public override void AfterMove(double delta)
    {
        if (P.IsOnFloor()) P.JumpProfile = null;
        else if (P.IsOnCeiling() && P.Velocity.Y < 0f) P.Velocity = new Vector2(P.Velocity.X, 0f);
    }

    private void OnRoomExitingEvent(string roomId)
    {
        foreach (Projectile p in _flying)
            if (IsInstanceValid(p) && !p.Ended) p.QueueFree();
        _flying.Clear();
    }
}
