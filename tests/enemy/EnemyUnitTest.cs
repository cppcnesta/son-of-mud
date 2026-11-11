using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Enemy.Bosses.Keeper;
using SonOfMud.Player;
using SonOfMud.World;

/// <summary>
/// 第 4 轮（Enemy）的单元检查：
/// ① SkillCooldown 的有阻回规则（计划第 6 节第 1 步）；② 掉落表用固定随机种子测概率；
/// ③ 正式 .tres 的数值（Q1、Q2、Q7、Q8、Q9、E2 的算术）；④ PlayerRegistry 的登记规则；
/// ⑤ 依赖方向和代码约定（Enemy 只依赖 Core、Combat；World 新增类不认识 Enemy；一个文件一个类；节点脚本 partial）；
/// ⑥ 拾取台词剧本；⑦ 各房间的敌人摆在设计的位置。
/// </summary>
public partial class EnemyUnitTest : LabSuiteBase
{
    protected override string ReportName => "enemy_unit_results";

    protected override Task RunChecks()
    {
        CooldownChecks();
        DropChecks();
        DataChecks();
        RegistryChecks();
        DependencyChecks();
        PickupScriptChecks();
        PlacementChecks();
        return Task.CompletedTask;
    }

    // ---------- ① 有阻回 ----------

    private void CooldownChecks()
    {
        var sweep = new SkillCooldown(5f, true);
        Check(sweep.IsReady && Mathf.IsEqualApprox(sweep.Charged, 5f) && sweep.Remaining == 0f, "冷却：横扫开局就绪（5/5）");
        var tail = new SkillCooldown(20f, false);
        Check(tail.IsRecharging && tail.Charged == 0f && Mathf.IsEqualApprox(tail.Remaining, 20f), "冷却：蝎尾开局 0/20，进入回充");

        sweep.BeginCast();
        sweep.Tick(3f);
        Check(sweep.IsCasting && !sweep.IsReady && sweep.Charged == 0f, "冷却：施法中不回充（Tick 3 秒仍是施法中）");
        sweep.EndCast();
        Check(sweep.IsRecharging && Mathf.IsEqualApprox(sweep.Remaining, 5f) && sweep.Charged == 0f, "冷却：EndCast 之后从 0 开始充（剩 5 秒）");
        sweep.Tick(4.9f);
        bool notYet = !sweep.IsReady;
        sweep.Tick(0.2f);
        Check(notYet && sweep.IsReady && sweep.Remaining == 0f, "冷却：充满 5 秒才就绪");

        tail.Tick(2f);
        bool reduced = tail.Reduce(5f);
        Check(reduced && Mathf.IsEqualApprox(tail.Remaining, 13f), $"冷却：回充中 Reduce(5) 生效（20 → 18 → 13，实际 {tail.Remaining}）");
        bool reducedToZero = tail.Reduce(100f);
        Check(reducedToZero && tail.Remaining == 0f && tail.IsReady, "冷却：Reduce 最多减到 0，减到 0 立即就绪");
        bool whenReady = tail.Reduce(5f);
        Check(!whenReady && tail.IsReady && tail.Remaining == 0f, "冷却：已就绪时 Reduce 不起作用");
        tail.BeginCast();
        bool whenCasting = tail.Reduce(5f);
        tail.EndCast();
        Check(!whenCasting && Mathf.IsEqualApprox(tail.Remaining, 20f), "冷却：施法中 Reduce 不起作用，也不存起来（EndCast 后仍是 0/20，Q5）");
    }

    // ---------- ② 掉落 ----------

    private void DropChecks()
    {
        var pilgrim = GD.Load<EnemyData>("res://assets/data/enemy/pilgrim.tres");
        var kneeler = GD.Load<EnemyData>("res://assets/data/enemy/kneeler.tres");
        var rng = new RandomNumberGenerator { Seed = 20261111 };
        int pilgrimDrops = 0, kneelerDrops = 0;
        const int rolls = 2000;
        for (int i = 0; i < rolls; i++) if (pilgrim.Drops.Roll(rng)) pilgrimDrops++;
        for (int i = 0; i < rolls; i++) if (kneeler.Drops.Roll(rng)) kneelerDrops++;
        float ratio = pilgrimDrops / (float)rolls;
        Check(ratio > 0.27f && ratio < 0.33f, $"掉落：固定种子 20261111 掷 {rolls} 次，朝圣者掉小泥偶的比例 {ratio:P1}（配置 30%）");
        Check(kneelerDrops == rolls, $"掉落：跪拜泥人 100% 掉小泥偶（Q9，{kneelerDrops}/{rolls}）");

        var a = new RandomNumberGenerator { Seed = 77 };
        var b = new RandomNumberGenerator { Seed = 77 };
        bool same = Enumerable.Range(0, 50).All(_ => pilgrim.Drops.Roll(a) == pilgrim.Drops.Roll(b));
        Check(same, "掉落：同一个种子掷出的结果完全一样（测试可以用固定种子保证掉落）");
    }

    // ---------- ③ 数值 ----------

    private void DataChecks()
    {
        var pilgrim = GD.Load<EnemyData>("res://assets/data/enemy/pilgrim.tres");
        var kneeler = GD.Load<EnemyData>("res://assets/data/enemy/kneeler.tres");
        var hammer = GD.Load<EnemyData>("res://assets/data/enemy/worker_hammer.tres");
        var shovel = GD.Load<EnemyData>("res://assets/data/enemy/worker_shovel.tres");
        var guard = GD.Load<EnemyData>("res://assets/data/enemy/guard.tres");
        var keeper = GD.Load<EnemyData>("res://assets/data/enemy/keeper/KeeperData.tres") as KeeperData;

        Check(pilgrim.MaxHealth == 30 && pilgrim.Behaviour == EnemyBehaviour.Passive && pilgrim.ReactsToHits && pilgrim.DisplayName == "塔姆图之子"
              && pilgrim.Drops?.ItemId == "小泥偶" && Mathf.IsEqualApprox(pilgrim.Drops.Chance, 0.3f),
            "数值：朝圣者「塔姆图之子」血 30、被动、受击有反应、30% 掉小泥偶");
        Check(kneeler.MaxHealth == 10 && kneeler.Behaviour == EnemyBehaviour.Passive && !kneeler.ReactsToHits && kneeler.Drops?.Chance == 1f,
            "数值：跪拜泥人血 10、被动、被打也没反应、100% 掉小泥偶");
        Check(hammer.MaxHealth == 40 && hammer.AttackSpec?.Damage == 20f && hammer.Behaviour == EnemyBehaviour.Melee && hammer.AttackSpec.Blockable
              && hammer.DisplayName == "持锤的塔姆图之子" && shovel.DisplayName == "持铲的塔姆图之子" && shovel.MaxHealth == 40 && hammer.Drops?.ItemId == "石头",
            "数值：工人（持锤 / 持铲）血 40、伤害 20、近战、可格挡、掉石头");
        Check(guard.MaxHealth == 60 && guard.AttackSpec?.Damage == 20f && guard.Behaviour == EnemyBehaviour.Charge && guard.DisplayName == "塔姆图之子·守卫" && guard.Drops == null,
            "数值：守卫血 60、伤害 20、冲锋、不掉落");

        Check(keeper != null && keeper.MaxHealth == 150 && keeper.DisplayName == "守坑人：蝎" && Mathf.IsEqualApprox(keeper.MoveSpeed, 120f),
            "数值：守坑人血 150，中速 120px/s（D1、Q7）");
        Check(keeper.SweepSpec?.Damage == 40f && keeper.TailSpec?.Damage == 60f && keeper.TailSpec.StunSeconds == 3f && keeper.TailSpec.CounterOnBlock && keeper.TailSpec.Blockable,
            "数值：横扫 40、蝎尾 60 + 眩晕 3 秒 + CounterOnBlock（Q1，用户已批准，待试玩）");
        Check(keeper.SweepCooldown == 5f && keeper.SweepWindup == 1f && keeper.SweepRecover == 0.5f && keeper.SweepRange == 128f && keeper.SweepRecoverMultiplier == 2f && keeper.SweepReducesTail == 5f,
            "数值：横扫 CD 5、前摇 1、收招 0.5（2 倍易伤）、距离 128、命中让蝎尾减 5");
        Check(keeper.TailCooldown == 20f && keeper.TailAim == 3f && keeper.TailDelay == 0.1f && keeper.TailWidth == 96f && keeper.TailTriggerRange == 384f && keeper.TailRecover == 0.5f,
            "数值：蝎尾 CD 20、瞄准 3、过渡 0.1、竖带宽 96、触发 384（Q2）、收招 0.5（Q6）");
        Check(keeper.Stagger != null && keeper.Stagger.Duration == 5f && keeper.Stagger.DamageMultiplier == 2f && keeper.Stagger.ReducePerHit == 1f && keeper.TransformFlash == 1f,
            "数值：反制僵直 5 秒 / 2 倍 / 每次受伤减 1 秒；变身闪白 1 秒");

        // E2：基础攻击（水 10 / 泥 24）打朝圣者
        var water = GD.Load<FormConfig>("res://assets/data/player_v2/water.tres");
        var mud = GD.Load<FormConfig>("res://assets/data/player_v2/mud.tres");
        int w = DamageMath.Final(water.AttackSpec.Damage), m = DamageMath.Final(mud.AttackSpec.Damage), mBonus = DamageMath.Final(mud.AttackSpec.Damage, mud.BlockBonusMultiplier);
        Check(w == 10 && m == 24 && 30 - w == 20 && 30 - m == 6 && (30 + w - 1) / w == 3 && (30 + m - 1) / m == 2 && mBonus == 38 && mBonus >= 30,
            $"数值（E2）：基础攻击第一刀打不死朝圣者（水剩 {30 - w}、泥剩 {30 - m}），水 3 刀、泥 2 刀；泥增伤一刀 {mBonus} 可以打死");

        foreach (string scene in new[] { "pilgrim", "kneeler" })
        {
            Node node = GD.Load<PackedScene>($"res://assets/scenes/enemy/{scene}.tscn").Instantiate();
            bool noHit = node.GetChildren().OfType<HitBox>().Count() == 0;
            Check(noHit && node is CharacterBody2D body && body.CollisionLayer == Layers.EnemyBody && (body.CollisionMask & Layers.PlayerBody) == 0,
                $"没有接触伤害（Q8）：{scene} 没有攻击判定，身体只在敌人层、不和玩家身体碰撞");
            node.Free();
        }
        Node worker = GD.Load<PackedScene>("res://assets/scenes/enemy/worker.tscn").Instantiate();
        HitBox workerHit = worker.GetNode<HitBox>("HitBox");
        Check(workerHit.CollisionLayer == Layers.EnemyHit && workerHit.CollisionMask == Layers.PlayerHurt && worker is CharacterBody2D wb && (wb.CollisionMask & Layers.PlayerBody) == 0,
            "没有接触伤害（Q8）：工人只有出手时打开的攻击判定，身体不和玩家碰撞");
        worker.Free();
    }

    // ---------- ④ PlayerRegistry ----------

    private void RegistryChecks()
    {
        Node2D before = PlayerRegistry.Current;
        var a = new Node2D { Name = "A" };
        var b = new Node2D { Name = "B" };
        var seen = new List<string>();
        void OnChanged(Node2D p) => seen.Add(p?.Name ?? "null");
        PlayerRegistry.Changed += OnChanged;
        PlayerRegistry.Register(a);
        Check(PlayerRegistry.Current == a && seen.SequenceEqual(new[] { "A" }), "PlayerRegistry：登记后 Current 是它，并通知 Changed");
        PlayerRegistry.Register(a);
        Check(seen.Count == 1, "PlayerRegistry：重复登记同一个玩家不重复通知");
        GameSession.Reset();
        Check(PlayerRegistry.Current == a, "PlayerRegistry：GameSession.Reset() 不碰它（开新游戏 / 读档时玩家早已登记）");
        PlayerRegistry.Register(b);
        PlayerRegistry.Unregister(a);
        Check(PlayerRegistry.Current == b && seen.SequenceEqual(new[] { "A", "B" }), "PlayerRegistry：旧玩家注销时只注销自己，新玩家不受影响");
        PlayerRegistry.Unregister(b);
        Check(PlayerRegistry.Current == null && seen.Last() == "null", "PlayerRegistry：当前玩家注销后 Current 为 null，并通知 Changed(null)");
        PlayerRegistry.Changed -= OnChanged;
        if (before != null) PlayerRegistry.Register(before);
        a.Free();
        b.Free();

        string playerSource = FileAccess.GetFileAsString("res://assets/scripts/player/PlayerCharacter.cs");
        Check(playerSource.Contains("_EnterTree() => PlayerRegistry.Register(this)") && playerSource.Contains("PlayerRegistry.Unregister(this);"),
            "PlayerRegistry：玩家在 _EnterTree 登记、_ExitTree 注销（跟着节点生命周期走）");
        string session = FileAccess.GetFileAsString("res://assets/scripts/core/GameSession.cs");
        Check(!session.Contains("PlayerRegistry"), "PlayerRegistry：GameSession 里没有任何清空它的代码");
    }

    // ---------- ⑤ 依赖与约定 ----------

    private static readonly Regex UsingSonOfMud = new(@"^using\s+SonOfMud\.([A-Za-z.]+)\s*;", RegexOptions.Multiline);
    private static readonly Regex TypeDecl = new(@"^(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+)?(?:partial\s+)?(class|enum|struct|interface)\s+(\w+)", RegexOptions.Multiline);

    private void DependencyChecks()
    {
        var files = new List<string>();
        Collect("res://assets/scripts/enemy", files);
        var bad = new List<string>();
        var multi = new List<string>();
        var notPartial = new List<string>();
        foreach (string file in files)
        {
            string text = FileAccess.GetFileAsString(file);
            foreach (Match m in UsingSonOfMud.Matches(text))
            {
                string ns = m.Groups[1].Value;
                if (!(ns == "Core" || ns == "Combat" || ns == "Enemy" || ns.StartsWith("Enemy."))) bad.Add($"{file.GetFile()}: SonOfMud.{ns}");
            }
            if (text.Contains("PlayerCharacter") || text.Contains("SonOfMud.Player") || text.Contains("SonOfMud.World") || text.Contains("SonOfMud.Narrative") || text.Contains("SonOfMud.UI"))
                bad.Add(file.GetFile() + " 引用了 Player / World / Narrative / UI");
            var decls = TypeDecl.Matches(text);
            if (decls.Count != 1) multi.Add($"{file.GetFile()}（{decls.Count}）");
            foreach (Match d in decls)
                if (d.Groups[1].Value == "class" && !d.Value.Contains("partial") && !d.Value.Contains("static") && d.Groups[2].Value != "SkillCooldown")
                    notPartial.Add(file.GetFile());
            string folder = file.GetBaseDir().Replace("res://assets/scripts/", "");
            string expectedNs = "SonOfMud." + string.Join(".", folder.Split('/').Select(p => char.ToUpper(p[0]) + p[1..]));
            if (!text.Contains($"namespace {expectedNs};")) bad.Add($"{file.GetFile()} 命名空间不是 {expectedNs}");
        }
        Check(files.Count >= 30 && bad.Count == 0, $"依赖：Enemy 的 {files.Count} 个文件只用 Core 和 Combat，命名空间和文件夹一致" + (bad.Count > 0 ? "：" + string.Join("; ", bad.Take(6)) : ""));
        Check(multi.Count == 0, "约定：Enemy 一个文件一个类型" + (multi.Count > 0 ? "：" + string.Join(", ", multi) : ""));
        Check(notPartial.Count == 0, "约定：Enemy 的节点 / 资源脚本都是 partial（只有纯 C# 的 SkillCooldown 不是）" + (notPartial.Count > 0 ? "：" + string.Join(", ", notPartial) : ""));

        var worldNew = new[] { "ArenaLock", "DropSpawner", "Pickup" }.Select(n => FileAccess.GetFileAsString($"res://assets/scripts/world/{n}.cs")).ToArray();
        Check(worldNew.All(t => !t.Contains("SonOfMud.Enemy") && !t.Contains("EnemyBody") && !t.Contains("SonOfMud.Narrative")),
            "依赖：World 新增的 ArenaLock / DropSpawner / Pickup 不认识 Enemy 和 Narrative，只听 Events");
        string registry = FileAccess.GetFileAsString("res://assets/scripts/core/PlayerRegistry.cs");
        Check(UsingSonOfMud.Matches(registry).Count == 0 && !registry.Contains("PlayerCharacter"), "依赖：Core 的 PlayerRegistry 不引用其他系统（玩家只当 Node2D）");
    }

    private static void Collect(string dir, List<string> files)
    {
        foreach (string f in DirAccess.GetFilesAt(dir))
            if (f.EndsWith(".cs")) files.Add(dir + "/" + f);
        foreach (string d in DirAccess.GetDirectoriesAt(dir)) Collect(dir + "/" + d, files);
    }

    // ---------- ⑥ 拾取台词 ----------

    private void PickupScriptChecks()
    {
        string text = FileAccess.GetFileAsString("res://assets/story/pickups.orph");
        var script = SonOfMud.Narrative.Parsing.OrphParser.Parse(text, "pickups");
        int doll = script.FindLabel("小泥偶"), stone = script.FindLabel("石头");
        bool subtitles = doll >= 0 && stone >= 0 && script.Entries[doll].Command == "字幕" && script.Entries[stone].Command == "字幕";
        bool line = script.Entries.Any(e => e.Command == "说" && e.Args[0] == "？？？" && e.Args[2] == "母神的土偶！打碎它！");
        Check(script.Errors.Count == 0 && script.Warnings.Count == 0 && subtitles && line,
            "拾取台词：pickups.orph 有「小泥偶」「石头」两个标签，都走字幕条；小泥偶照抄策划案“？？？：母神的土偶！打碎它！”");
    }

    // ---------- ⑦ 摆放 ----------

    /// <summary>
    /// 设计摆放：房间 → (节点名, 场景)。位置以房间文字图的锚点为准（关卡几何计划 v3 第 6 节：测试里的固定坐标改成读锚点），
    /// 见 <see cref="AnchorCell"/>：节点要站在锚点那一格里（x 在格子左右边界内，脚在格子底边或上方 1 像素以内）。
    /// </summary>
    public static readonly (string room, string node, string scene)[] Layout =
    {
        ("S1_03", "Pilgrim1", "pilgrim"), ("S1_03", "Pilgrim2", "pilgrim"), ("S1_03", "Pilgrim3", "pilgrim"),
        ("S1_03", "Pilgrim4", "pilgrim"), ("S1_03", "Pilgrim5", "pilgrim"),
        ("S1_05", "Keeper", "keeper"),
        ("S2_01", "WorkerHammer", "worker"), ("S2_01", "WorkerShovel", "worker"),
        ("S2_02", "WorkerShovel", "worker"),
        ("S2_03", "WorkerHammer", "worker"),
        ("S3_01", "Guard", "guard"),
        ("S3_02", "Kneeler1", "kneeler"), ("S3_02", "Kneeler2", "kneeler"),
        ("S3_02", "Kneeler3", "kneeler"), ("S3_02", "Kneeler4", "kneeler"),
    };

    /// <summary>房间文字图里，名为 node 的锚点所在那一格（房间局部像素）；找不到返回 null。</summary>
    public static Rect2? AnchorCell(string room, string node)
    {
        RoomDef def = GD.Load<WorldLayout>(WorldLayout.DefaultPath).Rooms.FirstOrDefault(r => r.Id == room);
        if (def == null) return null;
        Vector2 cell = MetSysApi.CellSize;
        RoomGrid grid = RoomGrid.Load(def, new Vector2I((int)cell.X, (int)cell.Y), out _);
        if (grid == null) return null;
        foreach (var (letter, name) in grid.Anchors)
            if (name == node && grid.Find(letter) is { } at) return grid.TileRect(at.X, at.Y);
        return null;
    }

    /// <summary>位置是否站在锚点那一格里（和生成工具的“就近不动”规则相同）。</summary>
    public static bool InAnchorCell(Vector2 position, Rect2 cell) =>
        position.X >= cell.Position.X && position.X < cell.End.X && position.Y <= cell.End.Y && position.Y >= cell.End.Y - 1f;

    /// <summary>房间场景文件里这个节点的位置（运行中的检查用它做基准）。</summary>
    public static Vector2 ScenePosition(string room, string node)
    {
        Node scene = GD.Load<PackedScene>(RoomPath(room)).Instantiate();
        Vector2 at = scene.GetNodeOrNull<Node2D>("Objects/" + node)?.Position ?? new Vector2(float.NaN, float.NaN);
        scene.Free();
        return at;
    }

    public static string RoomPath(string room) => $"res://assets/scenes/world/s{room[1]}/{room}.tscn";

    private void PlacementChecks()
    {
        var problems = new List<string>();
        var found = new List<string>();
        var rooms = Layout.Select(l => l.room).Distinct().ToList();
        foreach (string room in rooms)
        {
            Node scene = GD.Load<PackedScene>(RoomPath(room)).Instantiate();
            Node objects = scene.GetNode("Objects");
            var enemies = objects.GetChildren().OfType<EnemyBody>().ToList();
            var expected = Layout.Where(l => l.room == room).ToList();
            if (enemies.Count != expected.Count) problems.Add($"{room} 有 {enemies.Count} 个敌人，设计是 {expected.Count} 个");
            foreach (var e in expected)
            {
                var enemy = objects.GetNodeOrNull<EnemyBody>(e.node);
                if (enemy == null) { problems.Add($"{room} 缺 {e.node}"); continue; }
                if (enemy.SceneFilePath != $"res://assets/scenes/enemy/{e.scene}.tscn") problems.Add($"{room}/{e.node} 场景是 {enemy.SceneFilePath}");
                Rect2? cell = AnchorCell(room, e.node);
                if (cell == null) problems.Add($"{room} 的文字图里没有 {e.node} 的锚点");
                else if (!InAnchorCell(enemy.Position, cell.Value)) problems.Add($"{room}/{e.node} 在 {enemy.Position}，不在锚点格 {cell.Value} 里");
                found.Add($"{room}:{e.node}");
            }
            scene.Free();
        }
        string[] others = { "S1_01", "S1_02", "S1_04", "S1_06", "S1_07", "S1_08", "S2_04", "S2_05", "S2_06", "S3_03" };
        foreach (string room in others)
        {
            Node scene = GD.Load<PackedScene>(RoomPath(room)).Instantiate();
            if (FindAll<EnemyBody>(scene).Count > 0) problems.Add($"{room} 不该有敌人");
            scene.Free();
        }
        Check(problems.Count == 0 && found.Count == Layout.Length,
            $"摆放：S1_03 朝圣者 5、S1_05 守坑人、S2_01–S2_03 工人 4、S3_01 守卫、S3_02 跪拜泥人 4，都站在文字图的锚点格里；其他房间没有敌人" + (problems.Count > 0 ? "：" + string.Join("; ", problems.Take(6)) : ""));

        Node s105 = GD.Load<PackedScene>(RoomPath("S1_05")).Instantiate();
        var lockNode = s105.GetNodeOrNull<SonOfMud.World.ArenaLock>("Objects/ArenaLock");
        var npcs = FindAll<SonOfMud.Narrative.World.NpcTalk>(s105);
        var keeper = s105.GetNodeOrNull<KeeperBoss>("Objects/Keeper");
        Check(lockNode != null && lockNode.BossId == "keeper" && AnchorCell("S1_05", "ArenaLock") is { } lockCell && InAnchorCell(lockNode.Position, lockCell) && npcs.Count == 1 && npcs[0].NpcId == "keeper"
              && keeper != null && keeper.BossId == "keeper" && keeper.Arena == new Rect2(320, 0, 1984, 972),
            "摆放：S1_05 有竞技场入口门（左侧入口，站在锚点格里）、守坑人（竞技场 x 320–2304），老人 NPC 沿用 Narrative 已摆的那一个，没有重复");
        s105.Free();
    }

    private static List<T> FindAll<T>(Node root) where T : class
    {
        var list = new List<T>();
        void Walk(Node n)
        {
            if (n is T t) list.Add(t);
            foreach (Node c in n.GetChildren()) Walk(c);
        }
        Walk(root);
        return list;
    }
}
