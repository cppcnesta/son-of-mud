using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 状态基类：一个状态一个类，作为 StateMachine 的子节点。
/// 状态之间不互相引用，只调用 Machine.TransitionTo&lt;T&gt;()。
/// </summary>
public partial class State : Node
{
    public StateMachine Machine { get; internal set; }

    /// <summary>动画名（不含形态前缀），由各系统的动画组件读取。</summary>
    public virtual string AnimName => "";

    /// <summary>调试 HUD 上显示的名字。</summary>
    public virtual string DisplayName => Name;

    public virtual void Enter(StateMsg msg) { }
    public virtual void Exit() { }
    public virtual void Update(double delta) { }
    public virtual void PhysicsUpdate(double delta) { }
    public virtual void HandleInput(InputEvent @event) { }
}
