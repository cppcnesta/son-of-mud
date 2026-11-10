using System.Collections.Generic;

namespace SonOfMud.Narrative.Runtime;

/// <summary>
/// 舞台效果和玩法段的登记处。它们放在房间或战斗场景里，执行器在剧情层里，两边不能直接拿引用，
/// 所以由节点在 _EnterTree 登记、_ExitTree 注销（必须成对）。
/// </summary>
public static class VNRegistry
{
    private static readonly Dictionary<string, IStageEffect> Effects = new();
    private static readonly Dictionary<string, IGameplaySegment> Segments = new();

    public static void Register(IStageEffect effect) => Effects[effect.Id] = effect;

    public static void Unregister(IStageEffect effect)
    {
        if (Effects.TryGetValue(effect.Id, out IStageEffect current) && current == effect) Effects.Remove(effect.Id);
    }

    public static void Register(IGameplaySegment segment) => Segments[segment.Type] = segment;

    public static void Unregister(IGameplaySegment segment)
    {
        if (Segments.TryGetValue(segment.Type, out IGameplaySegment current) && current == segment) Segments.Remove(segment.Type);
    }

    public static bool TryGetEffect(string id, out IStageEffect effect) => Effects.TryGetValue(id, out effect);
    public static bool TryGetSegment(string type, out IGameplaySegment segment) => Segments.TryGetValue(type, out segment);
}
