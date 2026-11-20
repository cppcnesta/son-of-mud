using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>收集页的全部格子（assets/data/ui/collection_entries.tres）：Items 是上排 3 个道具，Collectibles 是下面 8 类收集品。</summary>
[GlobalClass]
public partial class CollectionTable : KeptResource
{
    public const string DefaultPath = "res://assets/data/ui/collection_entries.tres";

    [Export] public Godot.Collections.Array<CollectionEntry> Items { get; set; } = new();
    [Export] public Godot.Collections.Array<CollectionEntry> Collectibles { get; set; } = new();
}
