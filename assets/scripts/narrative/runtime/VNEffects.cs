namespace SonOfMud.Narrative.Runtime;

/// <summary>给玩法代码用的效果入口；剧本里的 [效果] 走的也是这里。</summary>
public static class VNEffects
{
    public static void Play(string id, params string[] args)
    {
        if (VNRegistry.TryGetEffect(id, out IStageEffect effect)) effect.Play(args);
        else NarrativeLog.Warn($"[剧情] 找不到舞台效果「{id}」。它所在的场景加载了吗？");
    }
}
