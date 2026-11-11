using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Player.States;

/// <summary>
/// 第 4 轮流程测试的公共部分：一律从正式入口 main_game.tscn 启动、用真实玩家和真实按键（RealKeys），不手动发 PlayerSpawned。
/// 记录 Boss / 敌人 / 掉落 / 死亡事件；结果写到 tests/output/enemy_&lt;名字&gt;_results.json。
/// </summary>
public abstract partial class EnemyFlowBase : NarrativeFlowBase
{
    protected override string ResultPrefix => "enemy";

    protected readonly List<string> Encounters = new();
    protected readonly List<string> Defeats = new();
    protected readonly List<string> Killed = new();
    protected readonly List<string> Dropped = new();
    protected int Deaths;
    protected int PlayerSpawnedCount;

    protected void ListenEnemy()
    {
        Listen();
        Events.Instance.BossEncounterStarted += OnEncounter;
        Events.Instance.BossDefeated += OnDefeated;
        Events.Instance.EnemyKilled += OnKilled;
        Events.Instance.ItemDropped += OnDropped;
        Events.Instance.PlayerDied += OnDied;
        Events.Instance.PlayerSpawned += OnSpawned;
    }

    protected void UnlistenEnemy()
    {
        RealKeys.ReleaseAll();
        Events.Instance.BossEncounterStarted -= OnEncounter;
        Events.Instance.BossDefeated -= OnDefeated;
        Events.Instance.EnemyKilled -= OnKilled;
        Events.Instance.ItemDropped -= OnDropped;
        Events.Instance.PlayerDied -= OnDied;
        Events.Instance.PlayerSpawned -= OnSpawned;
        Unlisten();
    }

    private void OnEncounter(StringName id, string name, int maxHp) => Encounters.Add($"{id}|{name}|{maxHp}");
    private void OnDefeated(StringName id, Vector2 position) => Defeats.Add(id.ToString());
    private void OnKilled(StringName kind, Vector2 position) => Killed.Add(kind.ToString());
    private void OnDropped(StringName item, Vector2 position) => Dropped.Add(item.ToString());
    private void OnDied() => Deaths++;
    private void OnSpawned(Node2D player) => PlayerSpawnedCount++;

    protected string RoomId => World?.CurrentRoomDef?.Id ?? "";

    /// <summary>按住一个真实按键直到条件成立（或超时），然后松开。</summary>
    protected async Task<bool> HoldKeyUntil(Key key, Func<bool> condition, int maxFrames)
    {
        RealKeys.Down(key);
        bool ok = await WaitUntil(condition, maxFrames);
        RealKeys.Up(key);
        await Frames(2);
        return ok;
    }

    /// <summary>放到 from 房间的出口出生点，再按住右键走过房间边缘进入 to。</summary>
    protected async Task<bool> CrossEdgeRight(string from, StringName spawn, string to)
    {
        await Teleport(from, spawn);
        return await HoldKeyUntil(RealKeys.Right, () => RoomId == to, 400);
    }

    protected List<T> InRoom<T>() where T : class
    {
        var list = new List<T>();
        void Walk(Node n)
        {
            if (n is T t) list.Add(t);
            foreach (Node c in n.GetChildren()) Walk(c);
        }
        if (World?.CurrentRoom != null) Walk(World.CurrentRoom);
        return list;
    }

    protected static ulong FindSeed(Func<RandomNumberGenerator, bool> predicate)
    {
        for (ulong seed = 1; seed < 200000; seed++)
            if (predicate(new RandomNumberGenerator { Seed = seed })) return seed;
        throw new InvalidOperationException("找不到符合条件的种子");
    }

    protected bool PlayerFree => Player.State is IdleState or RunState;

    /// <summary>用真实按键走到敌人前面、面朝它，按一下 J，等这一刀结束；没打中就再来（最多 tries 次）。返回结算结果。</summary>
    protected async Task<HitOutcome?> Strike(EnemyBody enemy, int tries = 4)
    {
        for (int t = 0; t < tries; t++)
        {
            HitOutcome? outcome = await StrikeOnce(enemy);
            if (outcome != null) return outcome;
        }
        return null;
    }

    private async Task<HitOutcome?> StrikeOnce(EnemyBody enemy, int maxFrames = 900)
    {
        HitOutcome? outcome = null;
        void OnLanded(HurtBox target, Hit hit, int value)
        {
            if (target.Entity == enemy) outcome = (HitOutcome)value;
        }
        Player.AttackHitBox.HitLanded += OnLanded;
        Key? held = null;
        void Hold(Key? key)
        {
            if (held == key) return;
            if (held is { } old) RealKeys.Up(old);
            held = key;
            if (key is { } k) RealKeys.Down(k);
        }
        bool pressed = false, attacking = false;
        for (int i = 0; i < maxFrames; i++)
        {
            if (pressed)
            {
                RealKeys.Up(RealKeys.Attack);
                attacking |= Player.State is AttackState;
                if (attacking && Player.State is not AttackState) break;
                await Frames(1);
                continue;
            }
            float dx = enemy.GlobalPosition.X - Player.GlobalPosition.X;
            bool facing = Mathf.Sign(dx) == Mathf.Sign(Player.Facing);
            if (Mathf.Abs(dx) > 75f) Hold(RealKeys.Toward(dx));
            else if (Mathf.Abs(dx) < 40f) Hold(RealKeys.Toward(-dx));
            else if (!facing) Hold(RealKeys.Toward(dx));
            else
            {
                Hold(null);
                if (PlayerFree && Player.Timers.AttackReady)
                {
                    RealKeys.Down(RealKeys.Attack);
                    pressed = true;
                }
            }
            await Frames(1);
        }
        Hold(null);
        RealKeys.Up(RealKeys.Attack);
        Player.AttackHitBox.HitLanded -= OnLanded;
        await Frames(2);
        return outcome;
    }
}
