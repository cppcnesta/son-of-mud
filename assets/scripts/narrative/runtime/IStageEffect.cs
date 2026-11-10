namespace SonOfMud.Narrative.Runtime;

/// <summary>舞台效果（[效果 名字 参数]）。剧本和玩法代码都可以触发，走同一个入口 VNEffects.Play。</summary>
public interface IStageEffect
{
    string Id { get; }
    void Play(string[] args);
}
