using Meitou.Data.Gameplay.Dialogue;

namespace Meitou.Simulation.Dialogue;

public sealed partial class DialogueSystem
{
    /// <summary>A speech-bubble conversation under way (docs/game/dialogue.md "Ambient conversations"): the line said next and when.</summary>
    sealed record Bubble(Conversation C, DialogueNode Line, long DueTick);

    readonly List<Bubble> bubbles = [];

    /// <summary>
    /// A campaign's <c>dialog announcement</c> (docs/game/factions-squads-towns.md "Campaigns", announcement): <paramref name="dialogueId"/> fired as event 2 by
    /// <paramref name="npc"/> with no target. Its lines are said one after another as bubbles; speech bubbles are not built, so each goes to the message log as
    /// "name: text". Their effects run as said (an <c>interrupt</c> makes the next talk with the speaker that dialogue). Returns whether it started.
    /// </summary>
    public bool Announce(World world, CharacterId npc, string dialogueId)
    {
        if (dialogue.Node(dialogueId) is not { IsRoot: true } root || !world.Characters.TryResolveNext(npc, out int n) || !Awake(world.Characters, n)) return false;
        var c = new Conversation { Npc = npc, Player = CharacterId.None, Event = DialogueEvents.Announcement, Root = root };
        if (NextBubble(world, c, root) is not { } first) return false;
        bubbles.Add(new Bubble(c, first, world.Tick));
        return true;
    }

    /// <summary>Whether talking to <paramref name="npc"/> opens a pending <c>interrupt</c> dialogue (a campaign's negotiation).</summary>
    public bool HasInterrupt(CharacterId npc) => interrupts.ContainsKey(npc);

    /// <summary>The first child of <paramref name="line"/> the speaker says (its own lines; the bubble chain does not pass to interjectors), or null.</summary>
    DialogueNode? NextBubble(World world, Conversation c, DialogueNode line)
    {
        foreach (var child in line.Children)
            if (child.Speaker is Talker.Me or Talker.WholeSquad && Check(world, c, child, c.Npc, c.Player)) return child;
        return null;
    }

    /// <summary>Says the bubbles that are due (serial): the text, its effects, then the next line after 2 + 0.12 × the text's length seconds, or DA 12's own wait when longer.</summary>
    void StepBubbles(World world)
    {
        for (int k = 0; k < bubbles.Count; k++)
        {
            var (c, line, due) = bubbles[k];
            if (world.Tick < due) continue;
            if (!world.Characters.TryResolveNext(c.Npc, out int n) || !Awake(world.Characters, n))
            {
                bubbles.RemoveAt(k--);
                continue;
            }
            MarkUsed(world, c, line);
            string text = Expand(world, c, line, PickText(world, c.Npc, line), c.Npc, c.Player);
            if (text.Length > 0) world.Post($"{NameOf(world, c.Npc)}: {text}");
            RunEffects(world, c, line, c.Npc);
            float seconds = 2 + 0.12f * text.Length;
            foreach (var e in line.Effects) if (e.Action == DialogueActions.BubbleWait) seconds = MathF.Max(seconds, e.Value);
            if (c.Ended || NextBubble(world, c, line) is not { } next) bubbles.RemoveAt(k--);
            else bubbles[k] = new Bubble(c, next, world.Tick + (long)MathF.Ceiling(seconds / world.TickSeconds));
        }
    }
}
