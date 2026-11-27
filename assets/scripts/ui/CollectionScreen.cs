using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 收集页（Tab）：左边人物页（全身立绘），右边上排 3 个道具格、下面 2 × 4 共 8 个收集品格，最下面是说明栏。
/// 人物页来自 CharacterPageTable（assets/data/ui/character_pages.tres）：遇到过的人物才有一页（主角一开始就有，伊吉第一次说完话后加上），
/// 用鼠标点左右箭头翻页（到头了接着从另一头开始），只有一页时不显示箭头和页码；键盘不翻页（方向键用来选格子）。
/// 格子内容来自 CollectionTable（assets/data/ui/collection_entries.tres），每次打开时按当前进度刷新：
/// 得到过的显示图（图存在场景里每格的 Icon 上，编辑器里看得到、能换；泥肝写「现有 / 5」，有几件的类别写「已得到 / 总数」），没得到过的是空格。
/// 打开时 PauseService.Request(Collection, true)，关闭时释放；ProcessMode = Always。开关和互斥由 GameUi 决定。
/// 选格子：方向键 / WASD（按输入事件），上下换排时选同一列附近的格子；鼠标移到格子上也会选中。说明栏显示选中格子的名字和说明，
/// 没得到过的显示「？？？ / 还没有得到。」。
/// 选中泥瓶按 E（交互键）喝一瓶：回 30 血、数量减 1；满血或没有时不喝，说明栏写原因。
/// 格子节点写在场景里（Slots/Item0..2、Slots/Col0..7），每格有 Icon、Count、Highlight 三个子节点。
/// </summary>
public partial class CollectionScreen : Control
{
    public const string UnknownName = "？？？";
    public const string UnknownText = "还没有得到。";

    private static readonly StringName[] Up = { "ui_up", "move_up" };
    private static readonly StringName[] Down = { "ui_down", "move_down" };
    private static readonly StringName[] Left = { "ui_left", "move_left" };
    private static readonly StringName[] Right = { "ui_right", "move_right" };

    /// <summary>可调数值；不导出，统一用 UiSettings.Shared。测试可以替换。</summary>
    public UiSettings Settings { get; set; }
    public CollectionTable Table { get; set; }
    public CharacterPageTable People { get; set; }

    public bool IsOpen { get; private set; }
    public int Selected { get; private set; }
    /// <summary>11 个格子：0～2 道具，3～10 收集品（按场景里 Item0..2、Col0..7 的顺序）。</summary>
    public IReadOnlyList<Control> Slots => _slots;
    public IReadOnlyList<CollectionEntry> Entries => _entries;
    public Label NameLabel { get; private set; }
    public Label BodyLabel { get; private set; }

    private readonly List<Control> _slots = new();
    private readonly List<CollectionEntry> _entries = new();

    public TextureRect PortraitRect { get; private set; }
    public Label PersonName { get; private set; }
    public Label PersonPage { get; private set; }
    public BaseButton PrevPerson { get; private set; }
    public BaseButton NextPerson { get; private set; }
    /// <summary>现在显示的人物编号（CharacterPage.Id）。</summary>
    public string CurrentPerson { get; private set; } = "";

    public override void _Ready()
    {
        Settings ??= UiSettings.Shared;
        Table ??= GD.Load<CollectionTable>(CollectionTable.DefaultPath);
        People ??= GD.Load<CharacterPageTable>(CharacterPageTable.DefaultPath);
        PortraitRect = GetNodeOrNull<TextureRect>("Portrait");
        PersonName = GetNodeOrNull<Label>("People/Name");
        PersonPage = GetNodeOrNull<Label>("People/Page");
        PrevPerson = GetNodeOrNull<BaseButton>("People/Prev");
        NextPerson = GetNodeOrNull<BaseButton>("People/Next");
        if (PrevPerson != null) PrevPerson.Pressed += () => TurnPerson(-1);
        if (NextPerson != null) NextPerson.Pressed += () => TurnPerson(1);
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        NameLabel = GetNodeOrNull<Label>("Desc/Name");
        BodyLabel = GetNodeOrNull<Label>("Desc/Body");
        _slots.Clear();
        _entries.Clear();
        for (int i = 0; i < 3; i++) AddSlot($"Slots/Item{i}", Table?.Items.ElementAtOrDefault(i));
        for (int i = 0; i < 8; i++) AddSlot($"Slots/Col{i}", Table?.Collectibles.ElementAtOrDefault(i));
    }

    private void AddSlot(string path, CollectionEntry entry)
    {
        var slot = GetNodeOrNull<Control>(path);
        if (slot == null) return;
        int index = _slots.Count;
        _slots.Add(slot);
        _entries.Add(entry);
        slot.MouseFilter = MouseFilterEnum.Stop;
        slot.MouseEntered += () => { if (IsOpen) Select(index); };
    }

    public override void _ExitTree()
    {
        if (IsOpen) PauseService.Request(PauseService.Collection, false);
        IsOpen = false;
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        UseMessage = "";
        PauseService.Request(PauseService.Collection, true);
        Visible = true;
        Refresh();
        Select(0);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        PauseService.Request(PauseService.Collection, false);
    }

    /// <summary>遇到过的人物（按表里的顺序）。</summary>
    public List<CharacterPage> MetPeople()
    {
        var list = new List<CharacterPage>();
        if (People == null) return list;
        foreach (CharacterPage page in People.Pages)
            if (page != null && page.Met(GameSession.Progress)) list.Add(page);
        return list;
    }

    /// <summary>翻页：step = -1 上一个、+1 下一个，到头了从另一头接着翻。</summary>
    public void TurnPerson(int step)
    {
        List<CharacterPage> met = MetPeople();
        if (met.Count == 0) return;
        int at = met.FindIndex(p => p.Id == CurrentPerson);
        if (at < 0) at = 0;
        CurrentPerson = met[((at + step) % met.Count + met.Count) % met.Count].Id;
        ShowPerson();
    }

    /// <summary>显示 CurrentPerson 的立绘和名字（不在遇到过的人物里时显示第一个）；一页时藏起箭头和页码。</summary>
    private void ShowPerson()
    {
        List<CharacterPage> met = MetPeople();
        int at = met.FindIndex(p => p.Id == CurrentPerson);
        if (at < 0 && met.Count > 0) at = 0;
        CharacterPage page = at >= 0 ? met[at] : null;
        CurrentPerson = page?.Id ?? "";
        if (PortraitRect != null && page?.Portrait != null) PortraitRect.Texture = page.Portrait;
        if (PersonName != null) PersonName.Text = page?.DisplayName ?? "";
        bool many = met.Count > 1;
        if (PersonPage != null)
        {
            PersonPage.Text = many ? $"{at + 1} / {met.Count}" : "";
            PersonPage.Visible = many;
        }
        if (PrevPerson != null) PrevPerson.Visible = many;
        if (NextPerson != null) NextPerson.Visible = many;
    }

    /// <summary>按当前进度刷新人物页和 11 个格子。</summary>
    public void Refresh()
    {
        ShowPerson();
        ProgressStore progress = GameSession.Progress;
        for (int i = 0; i < _slots.Count; i++)
        {
            CollectionEntry entry = _entries[i];
            bool obtained = entry != null && entry.Obtained(progress);
            // 道具图存在场景里（Slots/xxx/Icon 的 Texture），这里只管得到过才显示
            if (_slots[i].GetNodeOrNull<TextureRect>("Icon") is { } icon) icon.Visible = obtained && icon.Texture != null;
            if (_slots[i].GetNodeOrNull<Label>("Count") is { } count)
            {
                count.Text = obtained ? entry.CountText(progress) : "";
                count.Visible = count.Text != "";
            }
        }
        ShowDescription();
    }

    public bool IsObtained(int index) => index >= 0 && index < _entries.Count && _entries[index] != null && _entries[index].Obtained(GameSession.Progress);
    public string IconPath(int index) => _slots[index].GetNodeOrNull<TextureRect>("Icon") is { Visible: true, Texture: { } t } ? t.ResourcePath : "";
    public string CountOf(int index) => _slots[index].GetNodeOrNull<Label>("Count") is { Visible: true } l ? l.Text : "";

    public void Select(int index)
    {
        if (_slots.Count == 0) return;
        if (index != Selected) UseMessage = "";
        Selected = Mathf.Clamp(index, 0, _slots.Count - 1);
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].GetNodeOrNull<CanvasItem>("Highlight") is { } h) h.Visible = i == Selected;
        ShowDescription();
    }

    private void ShowDescription()
    {
        if (NameLabel == null || BodyLabel == null || _entries.Count == 0) return;
        CollectionEntry entry = _entries[Mathf.Clamp(Selected, 0, _entries.Count - 1)];
        if (entry == null || !entry.Obtained(GameSession.Progress))
        {
            NameLabel.Text = UnknownName;
            BodyLabel.Text = UnknownText;
            return;
        }
        NameLabel.Text = entry.DisplayName;
        BodyLabel.Text = Describe(entry) + (entry.Id == BottleEntry && UseMessage != "" ? "\n" + UseMessage : "");
    }

    /// <summary>说明文字：泥板类列出已得到的泥板名字和数量。</summary>
    public string Describe(CollectionEntry entry)
    {
        if (entry.FlagNotes.Count > 0)
            return string.Join("\n", entry.Flags.Select((f, i) => (f, i)).Where(p => GameSession.Progress.Flag(p.f) && p.i < entry.FlagNotes.Count).Select(p => entry.FlagNotes[p.i]));
        if (entry.TabletIds.Count == 0) return entry.Description;
        var names = entry.TabletIds.Where(id => GameSession.Progress.Flag("tablet." + id)).Select(id => $"「{TabletTitle(id)}」");
        string list = string.Join("", names);
        string head = string.IsNullOrEmpty(entry.Description) ? "" : entry.Description + "\n";
        return $"{head}已得到 {entry.Earned(GameSession.Progress)}/{entry.FlagCount}：{list}";
    }

    public const string TabletDirectory = "res://assets/data/narrative/tablets";

    /// <summary>泥板标题：读剧情数据里的泥板（tablet_&lt;编号&gt;.tres 的 Title），没有就用 UiSettings.TabletNames / 编号。</summary>
    public string TabletTitle(string id)
    {
        string path = $"{TabletDirectory}/tablet_{id}.tres";
        if (ResourceLoader.Exists(path) && GD.Load<Resource>(path)?.Get("Title").AsString() is { Length: > 0 } title) return title;
        return Settings.TabletName(id);
    }

    /// <summary>可以在收集页里用掉的道具：格子编号 → 物品编号（泥瓶）。</summary>
    public const string BottleEntry = "mud_bottle";
    public static readonly StringName BottleItem = "泥瓶";
    private static readonly StringName[] Use = { "interact", "ui_accept" };

    /// <summary>最近一次在收集页里用道具的结果（空表示还没用过或用成功了）。</summary>
    public string UseMessage { get; private set; } = "";

    /// <summary>选中泥瓶时喝一瓶：发 UseItemRequested，World 那边能喝就回血、计数减 1；数量没变就说明喝不了，说明栏写原因。</summary>
    public bool UseSelected()
    {
        CollectionEntry entry = _entries.ElementAtOrDefault(Selected);
        if (entry == null || entry.Id != BottleEntry) return false;
        long before = ItemCounts.Get(BottleItem);
        Events.Instance?.EmitSignal(Events.SignalName.UseItemRequested, BottleItem);
        long after = ItemCounts.Get(BottleItem);
        UseMessage = after < before ? "" : before <= 0 ? "没有泥瓶了。" : "血是满的，现在不用喝。";
        Refresh();
        return after < before;
    }

    /// <summary>方向键 / WASD 按下时移动选中格；E 用掉选中的道具（按输入事件处理，一次按键一定只算一次，暂停中也能收到）。</summary>
    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || @event.IsEcho()) return;
        if (Is(@event, Use))
        {
            if (_entries.ElementAtOrDefault(Selected)?.Id == BottleEntry)
            {
                UseSelected();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        Vector2 dir = Is(@event, Left) ? Vector2.Left : Is(@event, Right) ? Vector2.Right : Is(@event, Up) ? Vector2.Up : Is(@event, Down) ? Vector2.Down : Vector2.Zero;
        if (dir == Vector2.Zero) return;
        Move(dir);
        GetViewport().SetInputAsHandled();
    }

    private static bool Is(InputEvent e, StringName[] actions)
    {
        foreach (StringName action in actions)
            if (InputMap.HasAction(action) && e.IsActionPressed(action)) return true;
        return false;
    }


    /// <summary>左右在同一排里移；上下换到相邻一排，选中心横坐标最近的格子。</summary>
    public void Move(Vector2 direction)
    {
        if (_slots.Count == 0) return;
        Vector2 from = Center(Selected);
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (i == Selected) continue;
            Vector2 d = Center(i) - from;
            bool sameRow = Mathf.Abs(d.Y) < 8f;
            float score;
            if (direction.X != 0f)
            {
                if (!sameRow || Mathf.Sign(d.X) != Mathf.Sign(direction.X)) continue;
                score = Mathf.Abs(d.X);
            }
            else
            {
                if (sameRow || Mathf.Sign(d.Y) != Mathf.Sign(direction.Y)) continue;
                score = Mathf.Abs(d.Y) * 1000f + Mathf.Abs(d.X);
            }
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0) Select(best);
    }

    private Vector2 Center(int index) => _slots[index].Position + _slots[index].Size * 0.5f;
}
