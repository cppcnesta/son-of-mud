using System;

namespace SonOfMud.Narrative.Runtime;

/// <summary>要等播完才继续剧本的效果：播完调用 done（原版是协程，这里改成回调）。</summary>
public interface IBlockingStageEffect
{
    void Play(string[] args, Action done);
}
