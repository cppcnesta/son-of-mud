using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 玩家状态基类。PhysicsUpdate 在 MoveAndSlide 之前调用（设速度、切状态），AfterMove 在之后调用（落地、离地判断）。
/// 「能否切形态 / 能否被打 / 能否被输入锁打断」做成虚属性，各状态自己声明。
/// </summary>
public partial class PlayerState : State
{
    private PlayerCharacter _player;
    protected PlayerCharacter P => _player ??= Machine.GetParent<PlayerCharacter>();

    /// <summary>这个状态里能不能切形态。</summary>
    public virtual bool CanSwitchForm => false;
    /// <summary>为 false 时 HurtBox 一律返回 Ignored（死亡、锁定、调试飞行）。</summary>
    public virtual bool AcceptsHits => true;
    /// <summary>
    /// 这个状态里按交互键能不能触发交互（PlayerInteractor 读取）。默认不能：攻击、后摇、闪避、格挡、
    /// 受伤、眩晕、死亡、锁定、调试飞行都不触发；只有自由状态（地面 Idle / Run、空中 Rise / Fall）可以。
    /// </summary>
    public virtual bool AllowsInteract => false;
    /// <summary>输入锁生效时会不会被打断进 Locked。</summary>
    public virtual bool Lockable => true;
    /// <summary>动画的目标时长（秒），用来算 SpeedScale；0 表示按动画原速播放。</summary>
    public virtual float AnimDuration => 0f;

    public virtual void AfterMove(double delta) { }

    /// <summary>换房前：攻击类状态在这里把这一刀标记为「判定已结束」。</summary>
    public virtual void OnRoomExiting() { }
}
