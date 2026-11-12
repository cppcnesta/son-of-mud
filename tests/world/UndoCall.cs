using Godot;

/// <summary>
/// 测试用：把“对某个对象调用某个方法（带参数）”包成一个 Callable，交给普通 UndoRedo。
/// C# 的 Callable 没有 Bind，lambda 又没有所属对象（UndoRedo 会跳过），所以用一个 RefCounted 承载；UndoRedo 会持有它的引用。
/// </summary>
public partial class UndoCall : RefCounted
{
    private GodotObject _target;
    private StringName _method;
    private Variant[] _args;

    public static Callable Make(GodotObject target, StringName method, Variant[] args) =>
        new(new UndoCall { _target = target, _method = method, _args = args }, MethodName.Run);

    public void Run() => _target.Call(_method, _args);
}
