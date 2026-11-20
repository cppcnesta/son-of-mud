using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 收集页用的「得到过」记录：进度变量 obtained.&lt;物品编号&gt;（泥瓶、泥肝），只会写上、不会拿掉，所以泥肝用完以后仍算得到过。
/// - 拾取物（Pickup）捡到时写上；
/// - 旧存档兼容：开局 / 读档 / 试玩补完变量之后、以及游戏中 count.泥肝 变化时，count.泥肝 &gt; 0 就补上 obtained.泥肝。
///   旧存档里已经喝掉的泥瓶、已经用完的泥肝没有记录，无法还原，等更新后再捡到一次才算。
/// </summary>
public static class ItemObtainedRule
{
    public static readonly StringName Liver = "泥肝";

    public static string Key(StringName itemId) => "obtained." + itemId;

    public static void Mark(ProgressStore progress, StringName itemId)
    {
        if (progress != null && !progress.Flag(Key(itemId))) progress.SetVar(Key(itemId), true);
    }

    /// <summary>身上有泥肝（count.泥肝 &gt; 0）就补上 obtained.泥肝。返回有没有补。</summary>
    public static bool Reconcile(ProgressStore progress)
    {
        if (progress == null || progress.Flag(Key(Liver)) || progress.GetVar(ItemCounts.Key(Liver), 0).AsInt64() <= 0) return false;
        progress.SetVar(Key(Liver), true);
        return true;
    }

    /// <summary>变量刚变化：是 count.泥肝 且大于 0 就补上。</summary>
    public static void OnVariableChanged(ProgressStore progress, StringName key, Variant value)
    {
        if (key.ToString() != ItemCounts.Key(Liver)) return;
        if (value.VariantType is Variant.Type.Int && value.AsInt64() > 0) Mark(progress, Liver);
    }
}
