using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 组件：让父节点成为 MetSys 的可储存对象（以后可破坏墙用）。ObjectId 必须手填，全局唯一，否则报错。
/// 父节点之前已被储存时，进入场景就会被移除。
/// </summary>
[GlobalClass]
public partial class PersistentObject : Node
{
    [Export] public string ObjectId { get; set; } = "";

    public bool WasStored { get; private set; }

    public override void _Ready()
    {
        if (string.IsNullOrEmpty(ObjectId))
        {
            GD.PushError($"PersistentObject（{GetPath()}）没有填 ObjectId。");
            return;
        }
        Node target = GetParent();
        target.SetMeta("object_id", ObjectId);
        if (!MetSysApi.Available || !MetSysApi.HasSaveData) return;
        WasStored = MetSysApi.RegisterStorable(target, () => target.QueueFree());
    }

    /// <summary>父节点被永久改变（墙被打碎等）时调用，存档后再进房间它就不会出现。</summary>
    public void MarkStored()
    {
        if (string.IsNullOrEmpty(ObjectId) || !MetSysApi.Available) return;
        MetSysApi.Store(GetParent());
    }
}
