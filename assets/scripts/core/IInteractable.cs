using Godot;

namespace SonOfMud.Core;

/// <summary>可按互动键交互的对象：存档点、门、NPC、泥板。actor 一般是玩家，但这里只当 Node2D 用。</summary>
public interface IInteractable
{
    string Prompt { get; }
    bool CanInteract(Node2D actor);
    void Interact(Node2D actor);
}
