using System.Collections.Generic;
using SonOfMud.Core;

// 只给 CoreSuite 用的测试状态：在代码里 new 出来，不挂到场景文件上。

public partial class CoreTestStateA : SonOfMud.Core.State
{
    public List<string> Log;
    public override void Enter(StateMsg msg) => Log?.Add("A.enter");
    public override void Exit() => Log?.Add("A.exit");
}

public partial class CoreTestStateB : SonOfMud.Core.State
{
    public List<string> Log;
    public bool ChainToC;
    public object LastPayload;

    public override void Enter(StateMsg msg)
    {
        Log?.Add("B.enter");
        LastPayload = msg?.Payload;
        if (ChainToC) Machine.TransitionTo<CoreTestStateC>();
    }

    public override void Exit() => Log?.Add("B.exit");
}

public partial class CoreTestStateC : SonOfMud.Core.State
{
    public List<string> Log;
    public override void Enter(StateMsg msg) => Log?.Add("C.enter");
    public override void Exit() => Log?.Add("C.exit");
}

/// <summary>没有挂进状态机的类型，用来测试切到不存在的状态。</summary>
public partial class CoreTestStateMissing : SonOfMud.Core.State { }
