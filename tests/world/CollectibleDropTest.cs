using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Player;
using SonOfMud.World;

/// <summary>
/// 收集品掉落（计划 v2）验收：正式入口开新游戏，真实玩家、真实按键。
/// ① 祈祷泥人：真实攻击打死 → 掉祈祷泥片，落在地面上（同时泥瓶照常按掉落表掉）；再打死一只 → 地上两份；
///    按 E 捡其中一份 → 对话框依次显示「拾取：祈祷泥片」和说明，翻完后暂停、输入锁解除，玩家能动；变量记上；
///    另一份马上隐藏、没有提示，按 E 没反应，说明只出现一次；之后再打死祈祷泥人不再掉。
/// ② 虔诚泥人：打死 → 掉虔诚泥片；没捡就离开房间 → 回来地上没有了，再打死一只 → 又掉。
/// ③ 塔庙泥匠、塔庙泥卫：打死各掉一块石头，两块分开记录。
/// ④ S1_05 小泥偶：boss_keeper 为假时不出现；为真时出现在场地地面上；没捡离开房间、回来还在；
///    捡起 → 对话框：拾取、说明，翻页后同一个对话框里「？？？：母神的土偶！打碎它！」，关掉后能动；再进来不再出现。
/// ⑤ 存档后读档：捡过的仍记着，打死祈祷泥人不再掉。
/// </summary>
public partial class CollectibleDropTest : EnemyFlowBase
{
    protected override string SuiteName => "collectible_drop";
    protected override string ResultPrefix => "world";

    private readonly List<string> _mainStarts = new();

    protected override async Task RunChecks()
    {
        ListenEnemy();
        void OnStarted(string script, string label) => _mainStarts.Add($"{script}:{label}");
        Events.Instance.NarrativeStarted += OnStarted;
        try
        {
            bool finished = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            await WaitSettled();
            Check(finished && Player != null && RoomId == "S1_01", "新游戏：从正式入口开局");
            if (Player == null) return;
            await PrayerChecks();
            await DevoutChecks();
            await StoneChecks();
            await FigurineChecks();
            await SaveLoadChecks();
        }
        finally
        {
            Events.Instance.NarrativeStarted -= OnStarted;
            RealKeys.ReleaseAll();
            UnlistenEnemy();
        }
    }

    // ---------- 工具 ----------

    private List<CollectiblePickup> Pickups(string id, bool availableOnly = true) =>
        InRoom<CollectiblePickup>().Where(p => p.Def?.Id == id && (!availableOnly || p.Available)).ToList();

    private static bool Collected(string id) => GameSession.Progress.Flag(CollectibleDef.Key(id));

    private EnemyBody FirstAlive(EnemyKind kind) => InRoom<EnemyBody>().FirstOrDefault(e => !e.IsDead && e.Data?.Kind == kind.Id);

    private sealed record EnemyKind(string Id);
    private static readonly EnemyKind PrayerKind = new("prayer");
    private static readonly EnemyKind DevoutKind = new("devout");

    private async Task<Vector2?> KillByStrikes(EnemyBody enemy)
    {
        if (enemy == null) return null;
        Vector2 at = enemy.GlobalPosition;
        for (int i = 0; i < 10 && !enemy.IsDead; i++)
        {
            await Strike(enemy);
            if (IsInstanceValid(enemy)) at = enemy.GlobalPosition;
        }
        await Frames(4);
        Player.Stats.Refill();
        return enemy.IsDead ? at : null;
    }

    /// <summary>站到 at 上（脚放在那一点），等交互提示出来。</summary>
    private async Task StandAt(Vector2 at)
    {
        RealKeys.ReleaseAll();
        // 攻击收势（泥 1.5 秒）里不能交互：等人能自由行动
        await WaitUntil(() => PlayerFree && NoPauseOrLock(), 180);
        BodyFeet.PlaceFeetAt(Player, at + new Vector2(0f, -2f));
        Player.Velocity = Vector2.Zero;
        await Frames(8);
        await WaitUntil(() => PlayerFree, 60);
    }

    /// <summary>按 E 捡起，翻完对话框；返回看到的每一句（名牌 + 台词）。</summary>
    private async Task<List<string>> PickUpAndRead()
    {
        var seen = new List<string>();
        await KeyTap(RealKeys.Interact);
        await WaitUntil(() => Narr.Dialog.IsOpen && !Narr.Dialog.IsTyping, 120);
        await AdvanceUntil(() => !Narr.MainActive, 12, () =>
        {
            string line = (Narr.Dialog.NameVisible ? Narr.Dialog.CurrentName + "：" : "") + Narr.Dialog.CurrentText;
            if (seen.Count == 0 || seen[^1] != line) seen.Add(line);
        });
        await WaitUntil(() => NoPauseOrLock() && PlayerFree, 120);
        // 同一个说话人的几句放得下时排在同一页上（下一句接在上一句下面），只留每一页最后的样子
        for (int i = seen.Count - 2; i >= 0; i--)
            if (seen[i + 1].StartsWith(seen[i])) seen.RemoveAt(i);
        return seen;
    }

    // ---------- ① 祈祷泥人 ----------

    private async Task PrayerChecks()
    {
        await Teleport("S1_03", "From_S1_02");
        int droppedBefore = Dropped.Count;
        int prayersBefore = Killed.Count(k => k == "prayer");
        EnemyBody first = FirstAlive(PrayerKind);
        first?.SetSeeds(0, FindSeed(r => r.Randf() < 0.3f));   // 固定掉落种子：这一只同时掉泥瓶
        Vector2? at = await KillByStrikes(first);
        await Frames(3);
        var drops = Pickups("prayer_shard");
        // 泥的横砍可能一刀打死挨在一起的两只：每死一只掉一块
        int killed = Killed.Count(k => k == "prayer") - prayersBefore;
        bool onGround = at is Vector2 p && drops.Any(d => Mathf.Abs(d.GlobalPosition.Y - p.Y) < 40f && Mathf.Abs(d.GlobalPosition.X - p.X) < 1f);
        Check(at != null && killed >= 1 && drops.Count == killed && onGround,
            $"真实攻击打死祈祷泥人：死几只掉几块祈祷泥片（死 {killed} 只，{drops.Count} 块），落在倒下的地面上");
        Check(Dropped.Skip(droppedBefore).Contains("泥瓶") && InRoom<Pickup>().Any(x => x.ItemId == "泥瓶"), "泥瓶照常按掉落表掉（固定种子），和收集品互不影响");

        Vector2? at2 = await KillByStrikes(FirstAlive(PrayerKind));
        await Frames(3);
        drops = Pickups("prayer_shard");
        killed = Killed.Count(k => k == "prayer") - prayersBefore;
        Check(at2 != null && killed >= 2 && drops.Count == killed, $"没捡之前再打死一只：每只都掉，地上有 {drops.Count} 块（死了 {killed} 只）");
        if (drops.Count < 2) return;

        CollectiblePickup take = drops[0], other = drops[1];
        await StandAt(take.GlobalPosition);
        Check(Interactor.CurrentPrompt == "拾取祈祷泥片", $"站在泥片上出现提示「拾取祈祷泥片」（实际「{Interactor.CurrentPrompt}」）");
        int starts = _mainStarts.Count(s => s == "pickups:prayer_shard");
        List<string> seen = await PickUpAndRead();
        Check(seen.SequenceEqual(new[] { "拾取：祈祷泥片\n这块土，来自塔姆图的祈祷之子。" }),
            $"按 E：对话框显示「拾取：祈祷泥片」，接着「这块土，来自塔姆图的祈祷之子。」（同一页）（{string.Join(" / ", seen)}）");
        Check(NoPauseOrLock() && PlayerFree, "对话框关掉后：暂停、输入锁都解除，玩家能动");
        Check(Collected("prayer_shard") && !IsInstanceValid(take), "变量 collected.prayer_shard 记上，捡起的那块消失");
        var others = drops.Skip(1).ToList();
        Check(others.All(o => IsInstanceValid(o) && !o.Visible && !o.Available), $"其余 {others.Count} 块马上隐藏、不能交互");

        await StandAt(other.GlobalPosition);
        Check(Interactor.CurrentPrompt != "拾取祈祷泥片", $"站在另一块的位置：没有拾取提示（「{Interactor.CurrentPrompt}」）");
        await KeyTap(RealKeys.Interact);
        await Frames(10);
        Check(_mainStarts.Count(s => s == "pickups:prayer_shard") == starts + 1 && !Narr.MainActive,
            "按 E 没反应：说明只出现过一次");

        await KillByStrikes(FirstAlive(PrayerKind));
        await Frames(3);
        Check(Pickups("prayer_shard").Count == 0, "捡过以后再打死祈祷泥人：不再掉");
    }

    // ---------- ② 虔诚泥人 ----------

    /// <summary>直接打死（只看死亡掉落；虔诚泥人远程、会跑，真实攻击不一定追得上）。返回倒下的位置。</summary>
    private async Task<Vector2?> KillDirect(EnemyBody enemy)
    {
        if (enemy == null) return null;
        Vector2 at = enemy.GlobalPosition;
        enemy.HurtBox.Receive(new Hit { AttackId = "collectible_kill_" + enemy.GetInstanceId(), Damage = 999f, SourcePosition = at, Source = Player, Blockable = false });
        await Frames(4);
        return enemy.IsDead ? at : null;
    }

    private async Task DevoutChecks()
    {
        EnemyBody devout = FirstAlive(DevoutKind);
        Vector2? at = await KillDirect(devout);
        await Frames(3);
        Check(at != null && Pickups("devout_shard").Count == 1, "打死虔诚泥人：掉一块虔诚泥片");
        // 没捡就离开房间，再回来
        await Teleport("S1_02", "From_S1_03");
        await Teleport("S1_03", "From_S1_02");
        Check(Pickups("devout_shard", availableOnly: false).Count == 0 && !Collected("devout_shard"), "没捡就离开房间：回来地上没有了，也没有记成捡过");
        Vector2? at2 = await KillDirect(FirstAlive(DevoutKind));
        await Frames(3);
        var drops = Pickups("devout_shard");
        Check(at2 != null && drops.Count == 1, "再打死一只虔诚泥人：又掉了");
        if (drops.Count == 0) return;
        await StandAt(drops[0].GlobalPosition);
        List<string> seen = await PickUpAndRead();
        Check(Collected("devout_shard") && seen.FirstOrDefault() == "拾取：虔诚泥片\n这块土，来自塔姆图的虔诚之子。",$"捡起虔诚泥片（{string.Join(" / ", seen)}）");
    }

    // ---------- ③ 石头 ----------

    private async Task StoneChecks()
    {
        foreach ((string room, StringName spawn, string kind, string id) in new[]
                 { ("S2_01", (StringName)"From_S1_07", "mason", "stone_mason"), ("S3_01", (StringName)"From_S2_06", "temple_guard", "stone_guard") })
        {
            await Teleport(room, spawn);
            EnemyBody enemy = InRoom<EnemyBody>().FirstOrDefault(e => e.Data?.Kind == kind);
            foreach (EnemyBody e in InRoom<EnemyBody>()) e.ProcessMode = ProcessModeEnum.Disabled;   // 不让它来打人；只看死亡掉落
            if (enemy != null) enemy.ProcessMode = ProcessModeEnum.Inherit;
            enemy?.HurtBox.Receive(new Hit { AttackId = "collectible_kill_" + kind, Damage = 999f, SourcePosition = enemy.GlobalPosition, Source = Player, Blockable = false });
            await Frames(4);
            var drops = Pickups(id);
            Check(enemy != null && enemy.IsDead && drops.Count == 1 && drops[0].Def.DisplayName == "石头", $"打死{(kind == "mason" ? "塔庙泥匠" : "塔庙泥卫")}：掉一块石头（{id}）");
            if (drops.Count == 0) continue;
            await StandAt(drops[0].GlobalPosition);
            List<string> seen = await PickUpAndRead();
            Check(Collected(id) && seen.Count == 1 && seen[0].StartsWith("拾取：石头\n工人们挖地基时挖出来的石头"),$"捡起石头，记成 {id}（{string.Join(" / ", seen)}）");
        }
        Check(Collected("stone_mason") && Collected("stone_guard"), "两块石头分开记录");
    }

    // ---------- ④ 小泥偶 ----------

    private async Task FigurineChecks()
    {
        await Teleport("S1_05", SpawnPoint.DefaultId);
        CollectiblePickup fig = Pickups("clay_figurine", availableOnly: false).FirstOrDefault();
        Check(fig != null && !fig.Visible && !fig.Available, "S1_05：还没打赢守坑人时小泥偶不出现");
        GameSession.Progress.SetVar("boss_keeper", true);
        await Frames(10);
        fig = Pickups("clay_figurine").FirstOrDefault();
        Vector2 local = fig == null ? Vector2.Zero : fig.GlobalPosition - World.CurrentRoom.GlobalPosition;
        Check(fig != null && fig.Visible && local == new Vector2(1904f, 952f), $"boss_keeper 为真：小泥偶出现在场地中间的地面上（{local}）");
        await Teleport("S1_04", "From_S1_05");
        await Teleport("S1_05", SpawnPoint.DefaultId);
        fig = Pickups("clay_figurine").FirstOrDefault();
        Check(fig != null && fig.Visible, "没捡就离开房间、再回来：小泥偶还在");
        if (fig == null) return;
        await StandAt(fig.GlobalPosition);
        Check(Interactor.CurrentPrompt == "拾取小泥偶", $"提示「拾取小泥偶」（「{Interactor.CurrentPrompt}」）");
        List<string> seen = await PickUpAndRead();
        string[] expected =
        {
            "拾取：小泥偶",
            "一个小小的泥偶。头上带着角冠，耳垂有两枚金耳环，手里握着弯刃。被许多手指摸得很光滑。泥坑边有成百上千个，全都朝着坑底。",
            "？？？：母神的土偶！打碎它！",
        };
        Check(seen.SequenceEqual(expected), $"捡起：对话框依次是拾取、说明，翻页后同一个对话框里「？？？：母神的土偶！打碎它！」（{string.Join(" / ", seen)}）");
        Check(NoPauseOrLock() && PlayerFree && Collected("clay_figurine"), "关掉后暂停、输入锁解除，玩家能动；collected.clay_figurine 记上");
        await Teleport("S1_04", "From_S1_05");
        await Teleport("S1_05", SpawnPoint.DefaultId);
        Check(Pickups("clay_figurine").Count == 0, "捡过以后再进 S1_05：不再出现");
    }

    // ---------- ⑤ 存档、读档 ----------

    private async Task SaveLoadChecks()
    {
        await Teleport("S1_04", SpawnPoint.SaveId);
        await KeyTap(RealKeys.Interact);
        await Frames(3);
        bool saved = FileAccess.FileExists(SavePath);
        bool finished = await EnterMainGame(StartMode.Continue);
        BindNarrative();
        await WaitSettled();
        Check(saved && finished && Player != null, "在 S1_04 存档点存档，再从正式入口读档");
        if (Player == null) return;
        Check(new[] { "prayer_shard", "devout_shard", "stone_mason", "stone_guard", "clay_figurine" }.All(Collected), "读档：五件收集品都还记着");
        await Teleport("S1_03", "From_S1_02");
        Vector2? at = await KillByStrikes(FirstAlive(PrayerKind));
        await Frames(3);
        Check(at != null && Pickups("prayer_shard").Count == 0, "读档后打死祈祷泥人：不再掉");
    }
}
