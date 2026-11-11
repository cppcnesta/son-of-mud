using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.UI;
using SonOfMud.World;

/// <summary>
/// 扔泥肝（C 步），从正式入口 main_game.tscn 启动、真实玩家、真实按键（RealKeys，R = 扔泥肝）：
/// ① 没有泥肝时按 R 没反应（不进投掷状态、不扣、不生成投掷物），HUD 的泥肝数量隐藏；
/// ② 有 3 个时按 R：扔出一个、剩 2 个、HUD 显示 ×2；投掷物是玩家方（只打敌人受击框）、伤害 15、倍率 1、带 player_liver 标签，
///    朝面向方向飞一段短抛物线（约 300 像素）；投掷状态约 0.2 秒、动画名 throw，之后回 Idle；朝左也能扔；扔到 0 个 HUD 隐藏，再按 R 没反应；
/// ③ 投掷间隔：0.4 秒内连按只扔一个，间隔约 24 帧后才能扔下一个；
/// ④ 攻击（水攻击、泥攻击后摇）/ 闪避 / 格挡中按 R 不扔，动作结束后也不补扔；空中能扔，而且不打断下落；
/// ⑤ S1_03 真实走近祈祷泥人按 R：水形态打中掉 15 血；泥形态（带着泥格挡增伤）同样只掉 15，增伤不被消耗；
/// ⑥ 存档点存档后再扔掉一个，读档后数量回到存档时的值，HUD 同步；重开一局清零、HUD 隐藏。
/// 结果写到 tests/output/enemy_liver_throw_results.json。
/// </summary>
public partial class LiverThrowTest : EnemyFlowBase
{
    protected override string SuiteName => "liver_throw";

    private const string Liver = "泥肝";
    private const int Damage = 15;

    private ThrowState _throw;
    private LiverCounter _hud;
    private readonly List<Projectile> _thrown = new();
    private readonly List<string> _states = new();

    protected override async Task RunChecks()
    {
        ListenEnemy();
        try
        {
            bool finished = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            await WaitSettled();
            Check(finished && Starts.Count == 1 && Starts[0].success && Player != null && RoomId == "S1_01", "新游戏：从正式入口开局，玩家在 S1_01 出生");
            if (Player == null) return;
            Bind();
            await NoLiverChecks();
            await ThrowChecks();
            await IntervalChecks();
            await BlockedChecks();
            await AirChecks();
            await DamageChecks();
            await SaveChecks();
        }
        finally
        {
            Unbind();
            UnlistenEnemy();
        }
    }

    // ---------- 搭建与工具 ----------

    private void Bind()
    {
        _throw = Player.Machine.Get<ThrowState>();
        _hud = MainGame != null ? FindChild<LiverCounter>(MainGame) : null;
        _thrown.Clear();
        _states.Clear();
        if (_throw != null) _throw.ProjectileThrown += OnThrown;
        Player.Machine.StateChanged += OnStateChanged;
    }

    private void Unbind()
    {
        if (_throw != null && GodotObject.IsInstanceValid(_throw)) _throw.ProjectileThrown -= OnThrown;
        if (Player != null && GodotObject.IsInstanceValid(Player)) Player.Machine.StateChanged -= OnStateChanged;
    }

    private void OnThrown(Projectile p) => _thrown.Add(p);
    private void OnStateChanged(StringName from, StringName to) => _states.Add(to.ToString());

    private static long Count => ItemCounts.Get(Liver);
    private static void SetCount(long n) => ItemCounts.Set(Liver, n);

    private bool HudShows(long n) =>
        _hud != null && GodotObject.IsInstanceValid(_hud) && (n > 0 ? _hud.Visible && _hud.Text == $"×{n}" : !_hud.Visible);

    private string HudText => _hud == null || !GodotObject.IsInstanceValid(_hud) ? "(无 HUD)" : _hud.Visible ? _hud.Text : "(隐藏)";

    private int ThrowStates => _states.Count(s => s == "Throw");

    private async Task TapKey(Key key)
    {
        RealKeys.Down(key);
        await Frames(1);
        RealKeys.Up(key);
        await Frames(1);
    }

    /// <summary>站稳、回到自由状态、投掷间隔走完。</summary>
    private async Task Settle()
    {
        RealKeys.ReleaseAll();
        await WaitUntil(() => Player.IsOnFloor() && PlayerFree && _throw.IntervalReady && Mathf.Abs(Player.Velocity.X) < 1f, 300);
        await Frames(2);
    }

    /// <summary>朝 direction 轻点方向键转身（尽量不走动）。</summary>
    private async Task Face(float direction)
    {
        if (Mathf.Sign(Player.Facing) == Mathf.Sign(direction)) return;
        await TapKey(RealKeys.Toward(direction));
        await Settle();
    }

    private sealed class Flight
    {
        public Vector2 Launch;
        public float MinY = float.MaxValue;
        public Vector2 EndPosition;
        public ProjectileEnd End;
        public HitOutcome? Outcome;
        public HurtBox Target;
        public int Dealt;
        public bool Freed;
    }

    private async Task<Flight> Follow(Projectile p, int maxFrames = 300)
    {
        var flight = new Flight();
        if (p == null || !GodotObject.IsInstanceValid(p)) return flight;
        flight.Launch = p.GlobalPosition;
        p.Finished += (reason, position) =>
        {
            flight.End = (ProjectileEnd)reason;
            flight.EndPosition = position;
            flight.Outcome = p.Outcome;
            flight.Target = p.Target;
            flight.Dealt = p.Hit?.Dealt ?? 0;
        };
        for (int i = 0; i < maxFrames && GodotObject.IsInstanceValid(p) && !p.Ended; i++)
        {
            flight.MinY = Mathf.Min(flight.MinY, p.GlobalPosition.Y);
            await Frames(1);
        }
        await Frames(2);
        flight.Freed = !GodotObject.IsInstanceValid(p);
        return flight;
    }

    // ---------- ① 没有泥肝 ----------

    private async Task NoLiverChecks()
    {
        Check(_throw != null && _throw.Config != null && _throw.Config.Hit != null && _throw.AnimName == "throw",
            "玩家的状态机里有 Throw 状态（扔泥肝），配置来自 liver_throw.tres，动画名 throw");
        Check(_hud != null && _hud.GetParent()?.Name == "Root" && HudShows(0) && Count == 0,
            $"HUD：正式 HUD 里有泥肝数量，开局 0 个时隐藏（{HudText}）");
        if (_throw == null) return;
        await Settle();
        int states = _states.Count;
        await TapKey(RealKeys.Throw);
        await Frames(10);
        Check(Count == 0 && _throw.Thrown == 0 && _thrown.Count == 0 && _states.Count == states && PlayerFree && FindChild<Projectile>(MainGame) == null && HudShows(0),
            $"没有泥肝时按 R：没反应（不进投掷状态、不扣、不生成投掷物，状态还是 {Player.State?.Name}），HUD 仍然隐藏");
    }

    // ---------- ② 扔出、扣数量、HUD ----------

    private async Task ThrowChecks()
    {
        if (_throw == null) return;
        SetCount(3);
        await Frames(1);
        Check(HudShows(3), $"有 3 个泥肝时 HUD 显示 ×3（{HudText}）");
        await Settle();
        await Face(1f);
        Vector2 at = Player.GlobalPosition;
        int throwStates = ThrowStates;
        RealKeys.Down(RealKeys.Throw);
        bool entered = await WaitUntil(() => Player.State is ThrowState, 5);
        RealKeys.Up(RealKeys.Throw);
        string anim = Player.Animator?.CurrentAnimation ?? "";
        Projectile p = _thrown.LastOrDefault();
        await Frames(1);
        Check(entered && _thrown.Count == 1 && _throw.Thrown == 1 && Count == 2 && HudShows(2),
            $"有 3 个时按 R：进入投掷状态，扔出一个，剩 {Count} 个，HUD 立即显示 ×2（{HudText}）");
        Check(anim == "water_throw", $"投掷动画：水形态播 water_throw（实际 {anim}）");
        Check(p != null && GodotObject.IsInstanceValid(p) && p.Team == ProjectileTeam.Player && p.CollisionLayer == Layers.PlayerHit && p.CollisionMask == Layers.EnemyHurt
              && p.Hit?.Damage == Damage && Mathf.IsEqualApprox(p.Hit.Multiplier, 1f) && p.Hit.Tags.Contains(LiverSwitch.DefaultTag) && p.Hit.Source == Player
              && p.Velocity.X > 0f && p.GlobalPosition.X > at.X,
            "泥肝：玩家方投掷物（PlayerHit 层，只打敌人受击框），伤害 15、倍率 1、带 player_liver 标签，攻击方是玩家，朝面向（右）飞");

        // 一边跟踪泥肝的飞行，一边数投掷状态持续的帧数
        Task<Flight> following = Follow(p);
        int frames = 0;
        while (Player.State is ThrowState && frames < 60)
        {
            await Frames(1);
            frames++;
        }
        Check(Mathf.Abs(frames - 11) <= 1 && Player.State is IdleState && ThrowStates == throwStates + 1 && Mathf.Abs(Player.GlobalPosition.X - at.X) < 1f,
            $"投掷动作约 0.2 秒（出手后又停了 {frames} 帧），站定不动，结束后回到 Idle");

        Flight flight = await following;
        float travel = flight.EndPosition.X - flight.Launch.X;
        Check(flight.MinY < flight.Launch.Y - 15f && flight.MinY > flight.Launch.Y - 50f && flight.End == ProjectileEnd.World && travel > 280f && travel < 400f && flight.Freed,
            $"泥肝：短抛物线（最高比出手点高 {flight.Launch.Y - flight.MinY:F0} 像素），飞出 {travel:F0} 像素落地碎掉、消失");

        // 朝左也能扔
        await Settle();
        await Face(-1f);
        at = Player.GlobalPosition;
        await TapKey(RealKeys.Throw);
        Projectile left = _thrown.LastOrDefault();
        Check(_thrown.Count == 2 && Count == 1 && HudShows(1) && left != null && GodotObject.IsInstanceValid(left) && left.Velocity.X < 0f && left.GlobalPosition.X < at.X,
            $"朝左按 R：泥肝朝左飞，剩 {Count} 个，HUD ×1（{HudText}）");

        // 扔到 0 个
        await Settle();
        await TapKey(RealKeys.Throw);
        await Frames(2);
        Check(_thrown.Count == 3 && Count == 0 && HudShows(0), $"扔掉最后一个：剩 0 个，HUD 隐藏（{HudText}）");
        await Settle();
        int before = _states.Count;
        await TapKey(RealKeys.Throw);
        await Frames(5);
        Check(_thrown.Count == 3 && Count == 0 && _states.Count == before && HudShows(0), "扔完之后再按 R：没反应，数量不会变成负数");
    }

    // ---------- ③ 投掷间隔 ----------

    private async Task IntervalChecks()
    {
        if (_throw == null) return;
        SetCount(3);
        await Settle();
        int thrown = _thrown.Count;
        await TapKey(RealKeys.Throw);
        // 0.4 秒内又连按了好几下
        for (int i = 0; i < 4; i++)
        {
            await Frames(3);
            await TapKey(RealKeys.Throw);
        }
        Check(_thrown.Count == thrown + 1 && Count == 2, $"投掷间隔内连按 5 下 R：只扔出一个（扔了 {_thrown.Count - thrown} 个，剩 {Count} 个）");

        // 每帧都按 R，直到扔出第二个：间隔约 0.4 秒（24 帧）
        await Settle();
        thrown = _thrown.Count;
        await TapKey(RealKeys.Throw);
        int frames = 2;
        while (_thrown.Count == thrown + 1 && frames < 120)
        {
            await TapKey(RealKeys.Throw);
            frames += 2;
        }
        Check(_thrown.Count == thrown + 2 && frames >= 23 && frames <= 27 && Count == 0,
            $"一直连按 R：第二个在第一个之后约 0.4 秒（{frames} 帧）才扔出");
    }

    // ---------- ④ 攻击 / 闪避 / 格挡中不能扔；空中能扔 ----------

    private async Task<bool> PressDuring<T>(Key action, string name) where T : PlayerState
    {
        await Settle();
        int thrown = _thrown.Count;
        long count = Count;
        // 动作键一直按住到按完 R（格挡要按住才持续）
        RealKeys.Down(action);
        bool inState = await WaitUntil(() => Player.State is T, 30);
        await Frames(1);
        bool still = Player.State is T;
        await TapKey(RealKeys.Throw);
        await Frames(1);
        bool stillAfter = Player.State is T;
        RealKeys.Up(action);
        await WaitUntil(() => PlayerFree, 300);
        await Frames(20);
        bool ok = inState && still && stillAfter && _thrown.Count == thrown && Count == count;
        Check(ok, $"{name}中按 R：不扔（扔出 {_thrown.Count - thrown} 个，数量 {count} → {Count}），动作结束后也不补扔");
        return ok;
    }

    private async Task BlockedChecks()
    {
        if (_throw == null) return;
        SetCount(3);
        await PressDuring<AttackState>(RealKeys.Attack, "水形态攻击");
        await PressDuring<DodgeState>(RealKeys.Dodge, "闪避");
        await PressDuring<BlockState>(RealKeys.Block, "格挡");

        // 泥形态：攻击后摇（1.5 秒可移动后摇）也算攻击中
        await Settle();
        await TapKey(RealKeys.SwitchForm);
        await Frames(2);
        bool mud = Player.Forms.Form == 1;
        await Settle();
        int thrown = _thrown.Count;
        RealKeys.Down(RealKeys.Attack);
        bool recovering = await WaitUntil(() => Player.State is AttackRecoveryState, 60);
        RealKeys.Up(RealKeys.Attack);
        await Frames(10);
        await TapKey(RealKeys.Throw);
        bool stillRecovering = Player.State is AttackRecoveryState;
        await WaitUntil(() => PlayerFree, 300);
        await Frames(10);
        Check(mud && recovering && stillRecovering && _thrown.Count == thrown && Count == 3,
            "泥形态攻击后摇中按 R：不扔，后摇结束后也不补扔");
        await TapKey(RealKeys.SwitchForm);
        await Frames(2);
        await Settle();
        Check(Player.Forms.Form == 0 && Count == 3, "对照：切回水形态，泥肝还是 3 个");
    }

    private async Task AirChecks()
    {
        if (_throw == null) return;
        await Settle();
        int thrown = _thrown.Count;
        RealKeys.Down(RealKeys.Jump);
        bool falling = await WaitUntil(() => Player.State is FallState && Player.Velocity.Y > 0f, 120);
        RealKeys.Up(RealKeys.Jump);
        float y0 = Player.GlobalPosition.Y;
        await TapKey(RealKeys.Throw);
        bool airThrow = Player.State is ThrowState && !Player.IsOnFloor();
        float vy = Player.Velocity.Y;
        int frames = 0;
        bool fellDuring = true;
        float lastY = Player.GlobalPosition.Y;
        while (Player.State is ThrowState && frames < 30)
        {
            await Frames(1);
            frames++;
            if (!Player.IsOnFloor() && Player.GlobalPosition.Y <= lastY) fellDuring = false;
            lastY = Player.GlobalPosition.Y;
        }
        Check(falling && airThrow && _thrown.Count == thrown + 1 && Count == 2 && vy > 0f,
            $"空中（下落中）按 R：能扔，在空中进入投掷状态，剩 {Count} 个");
        Check(fellDuring && lastY > y0 + 5f && (Player.State is FallState || Player.IsOnFloor()),
            $"空中扔不打断下落：投掷期间一直往下掉（{y0:F0} → {lastY:F0}），之后接着下落或落地");
        await Settle();
    }

    // ---------- ⑤ 打中敌人掉 15 血（水、泥都一样） ----------

    private EnemyBody FirstPrayer() =>
        InRoom<EnemyBody>().Where(e => e.Data?.Kind == "prayer").OrderBy(e => e.GlobalPosition.X).FirstOrDefault();

    /// <summary>用真实方向键走到祈祷泥人左边约 250 像素处，面朝它站定。</summary>
    private async Task<bool> ApproachPrayer(EnemyBody prayer)
    {
        if (prayer == null) return false;
        await HoldKeyUntil(RealKeys.Right, () => prayer.GlobalPosition.X - Player.GlobalPosition.X <= 250f, 600);
        await Settle();
        await Face(1f);
        float dx = prayer.GlobalPosition.X - Player.GlobalPosition.X;
        return dx > 180f && dx < 280f && Player.Facing > 0f;
    }

    private async Task<(Flight flight, int before, int after)> ThrowAt(EnemyBody prayer)
    {
        int before = prayer.Stats.Health;
        int thrown = _thrown.Count;
        await TapKey(RealKeys.Throw);
        Projectile p = _thrown.Count > thrown ? _thrown[^1] : null;
        Flight flight = await Follow(p);
        return (flight, before, GodotObject.IsInstanceValid(prayer) ? prayer.Stats.Health : -1);
    }

    private async Task DamageChecks()
    {
        if (_throw == null) return;
        // 反应随机数：逃跑（不反击）；掉落随机数：不掉
        ulong flee = FindSeed(r => r.Randf() < 0.5f);
        ulong noDrop = FindSeed(r => r.Randf() >= 0.3f);

        SetCount(2);
        await Teleport("S1_03", "From_S1_02");
        await Settle();
        EnemyBody prayer = FirstPrayer();
        prayer?.SetSeeds(flee, noDrop);
        bool near = await ApproachPrayer(prayer);
        var (flight, before, after) = await ThrowAt(prayer);
        Check(near && flight.End == ProjectileEnd.Target && flight.Target == prayer?.HurtBox && flight.Outcome == HitOutcome.Damaged
              && flight.Dealt == Damage && before == 30 && after == before - Damage && Count == 1,
            $"水形态：真实走到祈祷泥人前按 R，泥肝打中它，掉 15 血（{before} → {after}，结果 {flight.Outcome}），只结算一次，剩 {Count} 个");

        // 泥形态 + 泥格挡增伤（测试直接给增伤，代替一次成功的泥格挡）：泥肝照样只掉 15，也不消耗增伤
        await Teleport("S1_03", "From_S1_02");
        await Settle();
        await TapKey(RealKeys.SwitchForm);
        await Frames(2);
        await Settle();
        bool mud = Player.Forms.Form == 1;
        prayer = FirstPrayer();
        prayer?.SetSeeds(flee, noDrop);
        near = await ApproachPrayer(prayer);
        Player.Timers.Bonus.Grant(1.6f);
        (flight, before, after) = await ThrowAt(prayer);
        Check(mud && near && flight.End == ProjectileEnd.Target && flight.Target == prayer?.HurtBox && flight.Outcome == HitOutcome.Damaged
              && flight.Dealt == Damage && after == before - Damage && Count == 0,
            $"泥形态（带着 1.6 倍泥格挡增伤）：泥肝同样只掉 15 血（{before} → {after}），不吃形态倍率和增伤");
        Check(Player.Timers.Bonus.Ready, "泥肝打中敌人不消耗泥格挡增伤（增伤留给下一次近战）");
        Player.Timers.Bonus.Clear();
        await TapKey(RealKeys.SwitchForm);
        await Frames(2);
        await Settle();
    }

    // ---------- ⑥ 存档 / 读档 / 重开 ----------

    private async Task SaveChecks()
    {
        if (_throw == null) return;
        SetCount(3);
        await Teleport("S1_04", SpawnPoint.SaveId);
        await Settle();
        await TapKey(RealKeys.Interact);
        await Frames(3);
        bool saved = FileAccess.FileExists(SavePath) && Saves.CheckpointRoom == "S1_04";
        // 存档之后再扔掉一个：读档应该回到存档时的 3 个
        await Settle();
        await TapKey(RealKeys.Throw);
        await Frames(2);
        Check(saved && Count == 2 && HudShows(2), $"在 S1_04 存档点按 E 存档（3 个泥肝），之后又扔掉一个（剩 {Count} 个）");

        Unbind();
        bool finished = await EnterMainGame(StartMode.Continue);
        BindNarrative();
        await WaitSettled();
        Bind();
        Check(finished && Starts[^1].success && RoomId == "S1_04" && Count == 3 && HudShows(3),
            $"读档：泥肝数量回到存档时的 3 个，新的 HUD 显示 ×3（{HudText}）");
        await Settle();
        await TapKey(RealKeys.Throw);
        await Frames(2);
        Check(_thrown.Count == 1 && Count == 2 && HudShows(2), "读档后按 R 照常能扔，剩 2 个，HUD ×2");

        Unbind();
        Node oldScene = MainGame;
        int before = Starts.Count;
        Events.Instance.EmitSignal(Events.SignalName.RestartRunRequested);
        bool restarted = await WaitUntil(() => Starts.Count > before && GetTree().CurrentScene != oldScene, 600);
        BindMainGame();
        BindNarrative();
        await WaitSettled();
        Bind();
        Check(restarted && Starts[^1].success && Count == 0 && !GameSession.Progress.HasVar(ItemCounts.Key(Liver)) && HudShows(0),
            $"重开一局：泥肝清零，HUD 隐藏（{HudText}）");
    }
}
