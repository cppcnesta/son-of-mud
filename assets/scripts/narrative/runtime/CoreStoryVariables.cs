using Godot;
using SonOfMud.Core;

namespace SonOfMud.Narrative.Runtime;

/// <summary>
/// 剧本变量直接读写 GameSession.Progress（每次现取：新游戏 / 读档时 Progress 会整个换掉）。
/// 由 SaveService 统一存档，剧本自己不存档。
/// </summary>
public sealed class CoreStoryVariables : IStoryVariables
{
    public bool Has(string key) => GameSession.Progress.HasVar(key);
    public Variant Get(string key) => GameSession.Progress.GetVar(key);
    public void Set(string key, Variant value) => GameSession.Progress.SetVar(key, value);
    public bool Flag(string key) => GameSession.Progress.Flag(key);
}
