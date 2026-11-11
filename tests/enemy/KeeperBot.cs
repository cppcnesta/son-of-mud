using System;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Enemy.Bosses.Keeper;
using SonOfMud.Enemy.Bosses.Keeper.States;
using SonOfMud.Player;
using SonOfMud.Player.States;

/// <summary>
/// 测试里打守坑人的“玩家”：只用真实按键（RealKeys），水形态。
/// 规则：横扫前摇快结束时按住 K 格挡（水格挡 0.5 秒、挡一次）；蝎尾瞄准快结束时也按 K；
/// 横扫前摇的最后 0.45 秒不出刀（留出格挡的空当）；瞄准期间（无敌）不出刀；其余时间离得远就走过去、够得着就按 J。
/// </summary>
public sealed class KeeperBot
{
    private readonly PlayerCharacter _player;
    private readonly KeeperBoss _keeper;
    private readonly Func<int, Task> _frames;

    public bool AllowMove { get; set; } = true;
    public int Attacks { get; private set; }
    public int BlockPresses { get; private set; }

    private Key? _moving;
    private bool _attackDown;
    private int _blockHold;

    public KeeperBot(PlayerCharacter player, KeeperBoss keeper, Func<int, Task> frames)
    {
        _player = player;
        _keeper = keeper;
        _frames = frames;
    }

    private bool PlayerFree => _player.State is IdleState or RunState;

    private void Move(Key? key)
    {
        if (_moving == key) return;
        if (_moving is { } old) RealKeys.Up(old);
        _moving = key;
        if (key is { } k) RealKeys.Down(k);
    }

    public void Release()
    {
        Move(null);
        if (_attackDown) RealKeys.Up(RealKeys.Attack);
        if (_blockHold > 0) RealKeys.Up(RealKeys.Block);
        _attackDown = false;
        _blockHold = 0;
    }

    /// <summary>一直打到 done 成立（或超时）。返回 done 的结果。</summary>
    public async Task<bool> FightUntil(Func<bool> done, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (done()) break;
            Step();
            await _frames(1);
        }
        Release();
        await _frames(1);
        return done();
    }

    private void Step()
    {
        if (_attackDown)
        {
            RealKeys.Up(RealKeys.Attack);
            _attackDown = false;
        }
        if (_blockHold > 0)
        {
            if (--_blockHold == 0) RealKeys.Up(RealKeys.Block);
            Move(null);
            return;
        }
        if (!IsInstanceValid(_keeper) || !IsInstanceValid(_player)) return;

        var state = _keeper.State;
        float dx = _keeper.GlobalPosition.X - _player.GlobalPosition.X;
        bool blockNow = state switch
        {
            SweepWindupState w => w.Remaining <= 0.10f,
            TailAimState a => a.Remaining <= 0.04f,
            TailStrikeState t => !t.Striking,
            _ => false,
        };
        if (blockNow && PlayerFree && _player.Timers.BlockReady)
        {
            Move(null);
            RealKeys.Down(RealKeys.Block);
            BlockPresses++;
            _blockHold = 24;
            return;
        }
        bool hold = state is TailAimState or TailStrikeState or TransformState or GoneState
                    || (state is SweepWindupState sw && sw.Remaining <= 0.45f);
        if (hold)
        {
            Move(null);
            return;
        }
        if (AllowMove && Mathf.Abs(dx) > 120f)
        {
            Move(RealKeys.Toward(dx));
            return;
        }
        Move(null);
        if (!PlayerFree) return;
        if (Mathf.Sign(dx) != Mathf.Sign(_player.Facing) && Mathf.Abs(dx) > 1f)
        {
            // 转身：按一帧方向键
            Move(RealKeys.Toward(dx));
            return;
        }
        if (!_player.Timers.AttackReady) return;
        RealKeys.Down(RealKeys.Attack);
        _attackDown = true;
        Attacks++;
    }

    private static bool IsInstanceValid(GodotObject o) => GodotObject.IsInstanceValid(o);
}
