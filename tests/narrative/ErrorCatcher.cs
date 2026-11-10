using System.Collections.Generic;
using Godot;

/// <summary>
/// 测试用日志钩子（OS.AddLogger）：记下引擎和脚本报出的错误（不含警告），用来验收“零报错”。
/// 回调可能来自别的线程，只做加锁记录，不调用 Godot API。
/// </summary>
public partial class ErrorCatcher : Logger
{
    private const int WarningType = 1;
    private readonly object _gate = new();
    private readonly List<string> _errors = new();

    public int Count
    {
        get { lock (_gate) return _errors.Count; }
    }

    public string[] Snapshot()
    {
        lock (_gate) return _errors.ToArray();
    }

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (errorType == WarningType) return;
        lock (_gate) _errors.Add($"{(string.IsNullOrEmpty(rationale) ? code : rationale)} @ {file}:{line}");
    }

    public override void _LogMessage(string message, bool error) { }
}
