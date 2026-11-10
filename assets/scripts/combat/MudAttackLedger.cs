using System.Collections.Generic;

namespace SonOfMud.Combat;

/// <summary>Deduplicate each incoming attack for its entire lifetime.</summary>
public sealed class MudAttackLedger
{
    private readonly HashSet<string> _seen = new();
    public bool Accept(string attackId) => !string.IsNullOrEmpty(attackId) && _seen.Add(attackId);
}
