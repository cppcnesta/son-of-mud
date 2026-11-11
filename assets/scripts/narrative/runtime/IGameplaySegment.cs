using System;

namespace SonOfMud.Narrative.Runtime;

/// <summary>
/// 玩法段：剧本暂时把控制权交给一段玩法，等它结束后传回一个结果（[玩法 类型 参数 变量]）。
/// 实现类在 _EnterTree / _ExitTree 里成对调用 VNRegistry.Register / Unregister。
/// </summary>
public interface IGameplaySegment
{
    string Type { get; }
    void Begin(string arg, Action<string> onFinished);
    /// <summary>中途取消：停止玩法，不要再调用 onFinished（即使调用了，执行器也会忽略）。</summary>
    void Cancel();
}
