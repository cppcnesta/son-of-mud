using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 泥珠下面的道具格：收集页（Tab）上排的 3 个道具（泥瓶、泥肝、生命之面包，来自 CollectionTable.Items），
/// 每格是场景里的 Slot0..2，子节点 Frame（道具图标框）、Icon（道具图，存在场景里）、Count（数量，和收集页一样写「现有 / 上限」）。
/// 得到过的显示道具图和数量；没得到过的只有框（变暗）。
/// 只读进度变量，不引用玩家：VariableChanged 时立即更新；新游戏 / 读档 / 重开时进度是整份替换的，不逐个发 VariableChanged，
/// 所以在 RoomEntered、GameStartFinished、PlayerSpawned 时也重新读一次。
/// </summary>
public partial class ItemBar : HBoxContainer
{
    public CollectionTable Table { get; set; }
    /// <summary>没得到过的道具格，框变成这个颜色（变暗变淡）。</summary>
    [Export] public Color MissingTint { get; set; } = new(0.55f, 0.55f, 0.55f, 0.7f);

    private readonly List<Control> _slots = new();
    private readonly List<CollectionEntry> _entries = new();

    public IReadOnlyList<CollectionEntry> Entries => _entries;

    public override void _EnterTree()
    {
        if (Events.Instance is not { } events) return;
        events.VariableChanged += OnVariableChanged;
        events.RoomEntered += OnRoomEntered;
        events.GameStartFinished += OnGameStartFinished;
        events.PlayerSpawned += OnPlayerSpawned;
    }

    public override void _ExitTree()
    {
        if (Events.Instance is not { } events) return;
        events.VariableChanged -= OnVariableChanged;
        events.RoomEntered -= OnRoomEntered;
        events.GameStartFinished -= OnGameStartFinished;
        events.PlayerSpawned -= OnPlayerSpawned;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Table ??= GD.Load<CollectionTable>(CollectionTable.DefaultPath);
        _slots.Clear();
        _entries.Clear();
        for (int i = 0; i < 3; i++)
        {
            if (GetNodeOrNull<Control>($"Slot{i}") is not { } slot) continue;
            _slots.Add(slot);
            _entries.Add(Table?.Items.ElementAtOrDefault(i));
        }
        Refresh();
    }

    private void OnVariableChanged(StringName key, Variant value) => Refresh();
    private void OnRoomEntered(string roomId) => Refresh();
    private void OnPlayerSpawned(Node2D player) => Refresh();
    private void OnGameStartFinished(bool success, string error) => Refresh();

    public void Refresh()
    {
        ProgressStore progress = GameSession.Progress;
        for (int i = 0; i < _slots.Count; i++)
        {
            CollectionEntry entry = _entries[i];
            bool obtained = entry != null && entry.Obtained(progress);
            if (_slots[i].GetNodeOrNull<CanvasItem>("Frame") is { } frame) frame.SelfModulate = obtained ? Colors.White : MissingTint;
            if (_slots[i].GetNodeOrNull<TextureRect>("Icon") is { } icon) icon.Visible = obtained && icon.Texture != null;
            if (_slots[i].GetNodeOrNull<Label>("Count") is { } count)
            {
                count.Text = obtained ? entry.CountText(progress) : "";
                count.Visible = count.Text != "";
            }
        }
    }

    /// <summary>某个道具（收集页的编号，如 mud_liver）格子里显示的数量文字；没显示时是空字符串。</summary>
    public string CountOf(string entryId)
    {
        int i = _entries.FindIndex(e => e?.Id == entryId);
        return i >= 0 && _slots[i].GetNodeOrNull<Label>("Count") is { Visible: true } l ? l.Text : "";
    }

    /// <summary>某个道具格子里的道具图（场景里设好的，不管现在显不显示）。</summary>
    public Texture2D IconOf(string entryId)
    {
        int i = _entries.FindIndex(e => e?.Id == entryId);
        return i >= 0 ? _slots[i].GetNodeOrNull<TextureRect>("Icon")?.Texture : null;
    }
}
