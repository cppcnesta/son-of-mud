using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 一件收集品（assets/data/world/collectibles/*.tres）：泥片、石头、小泥偶。
/// 捡起后进度变量 collected.&lt;编号&gt; 为真（跟存档保存），说明文字和拾取台词在 pickups.orph 里同名标签下（只用对话框）。
/// DroppedBy 里的敌人种类被打死时，只要这件还没捡过就掉一份（CollectibleDropper）；DroppedBy 为空的是摆在场景里的（小泥偶）。
/// </summary>
[GlobalClass]
public partial class CollectibleDef : KeptResource
{
    public const string StoryScript = "pickups";

    [Export] public StringName Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>地上显示的图（高 CollectiblePickup.ArtHeight，宽度按比例）；为空时画色块。</summary>
    [Export] public Texture2D Art { get; set; }
    /// <summary>哪些敌人种类（EnemyData.Kind）被打死时掉这件。</summary>
    [Export] public Godot.Collections.Array<StringName> DroppedBy { get; set; } = new();
    /// <summary>拾取音效（collect）；为空就不响。</summary>
    [Export] public Godot.Collections.Array<AudioStream> PickupSounds { get; set; } = new();

    public string CollectedKey => Key(Id);
    public static string Key(StringName id) => "collected." + id;
    public bool Collected => GameSession.Progress.Flag(CollectedKey);
}
