namespace SonOfMud.Narrative.Commands;

/// <summary>接着这一段往下说（解析器自动生成）。</summary>
public class ContinueSayCommand : SayCommand
{
    public override string Name => SameBlock;
    protected override bool AppendToBlock => true;
}
