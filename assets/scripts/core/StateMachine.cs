using System;
using System.Collections.Generic;
using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 平铺的有限状态机（不做层级状态）。子节点里的每个 State 按类型登记，
/// 用 TransitionTo&lt;T&gt;() 切换，类型写错在编译期就会报错。
/// 在 Enter / Exit 里再次请求切换时，会排队到本次切换结束后执行，保证 Exit→Enter 的顺序不乱。
/// </summary>
public partial class StateMachine : Node
{
    [Signal] public delegate void StateChangedEventHandler(StringName from, StringName to);

    [Export] public State Initial { get; set; }

    public State Current { get; private set; }
    public bool Started => Current != null;

    private readonly Dictionary<Type, State> _states = new();
    private bool _transitioning;
    private (State target, StateMsg msg)? _pending;

    public override void _Ready()
    {
        foreach (Node child in GetChildren())
        {
            if (child is not State state) continue;
            if (!_states.TryAdd(state.GetType(), state))
            {
                GD.PushError($"{GetPath()} 下有两个同类型状态 {state.GetType().Name}，第二个被忽略。");
                continue;
            }
            state.Machine = this;
        }

        // 等拥有者（玩家 / 敌人）准备好再进初始状态，否则状态的 Enter 里拿不到拥有者的子节点。
        if (Owner != null && !Owner.IsNodeReady())
            Owner.Ready += Start;
        else
            Start();
    }

    private void Start()
    {
        if (Owner != null && IsInstanceValid(Owner)) Owner.Ready -= Start;
        // 拥有者就绪之前整个场景可能已经被移出树（例如开局失败立刻切场景），这时不再进初始状态。
        if (Started || !IsInsideTree()) return;
        State first = Initial ?? FirstState();
        if (first == null)
        {
            GD.PushError($"{GetPath()} 没有任何 State 子节点。");
            return;
        }
        Change(first, null);
    }

    private State FirstState()
    {
        foreach (Node child in GetChildren())
            if (child is State state) return state;
        return null;
    }

    public bool IsIn<T>() where T : State => Current is T;

    public T Get<T>() where T : State => _states.TryGetValue(typeof(T), out var s) ? (T)s : null;

    public void TransitionTo<T>(StateMsg msg = null) where T : State
    {
        if (!_states.TryGetValue(typeof(T), out var target))
        {
            GD.PushError($"{GetPath()} 没有状态 {typeof(T).Name}。");
            return;
        }
        Change(target, msg);
    }

    private void Change(State target, StateMsg msg)
    {
        if (_transitioning)
        {
            _pending = (target, msg);
            return;
        }

        _transitioning = true;
        StringName from = Current?.Name ?? new StringName("");
        Current?.Exit();
        Current = target;
        Current.Enter(msg);
        _transitioning = false;
        EmitSignal(SignalName.StateChanged, from, Current.Name);

        if (_pending is { } next)
        {
            _pending = null;
            Change(next.target, next.msg);
        }
    }

    public override void _Process(double delta) => Current?.Update(delta);
    public override void _PhysicsProcess(double delta) => Current?.PhysicsUpdate(delta);
    public override void _UnhandledInput(InputEvent @event) => Current?.HandleInput(@event);
}
