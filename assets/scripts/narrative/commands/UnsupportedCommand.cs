using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>OrpheusVN 原版有、本作不支持的命令（背景、登场、退场、存档）：解析时报“本作不支持”，不写进剧本。</summary>
public class UnsupportedCommand : VNCommand
{
    private readonly string _name;
    private readonly string _reason;

    public UnsupportedCommand(string name, string reason)
    {
        _name = name;
        _reason = reason;
    }

    public override string Name => _name;

    public override string Validate(string[] args) => $"本作不支持「{_name}」命令（{_reason}）";

    public override void Execute(string[] args, VNContext ctx, Action done) =>
        throw new InvalidOperationException(Validate(args));
}
