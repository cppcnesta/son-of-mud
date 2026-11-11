namespace SonOfMud.Core;

/// <summary>
/// 物理层（位掩码值）。前三层和旧白盒一致：1 地形、2 玩家身体、3 敌人身体。
/// 名字同时写在 project.godot 的 layer_names 里，编辑器里看到的是同一套。
/// </summary>
public static class Layers
{
    public const uint World = 1u << 0;
    public const uint PlayerBody = 1u << 1;
    public const uint EnemyBody = 1u << 2;
    public const uint PlayerHurt = 1u << 3;
    /// <summary>敌人和可破坏物的受击框。</summary>
    public const uint EnemyHurt = 1u << 4;
    public const uint PlayerHit = 1u << 5;
    public const uint EnemyHit = 1u << 6;
    /// <summary>门、存档点、NPC 等互动区域。</summary>
    public const uint Interact = 1u << 7;
}
