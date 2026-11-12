using Godot;

namespace SonOfMud.Narrative.Views;

/// <summary>剧情界面从自己的场景里取节点；找不到时报错并说明是哪个场景、哪个节点（节点名不能改）。</summary>
public static class ViewNodes
{
    public static T Need<T>(Node owner, string path, string scenePath) where T : Node
    {
        T node = owner.GetNodeOrNull<T>(path);
        if (node == null) GD.PushError($"{owner.Name}：场景里找不到 {path}（{typeof(T).Name}）。请检查 {scenePath}，节点名不能改。");
        return node;
    }
}
