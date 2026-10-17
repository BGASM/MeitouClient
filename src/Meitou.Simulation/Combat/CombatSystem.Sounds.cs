using System.Numerics;

namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    /// <summary>Queues a sound-worthy happening for the host when it plays them (<see cref="World.Sounds"/>); display only.</summary>
    static void Sound(World world, SoundKind kind, int subject, int other, Vector3 at, bool heavy = false, bool unarmed = false, bool robot = false, bool armoured = false, bool fatal = false)
    {
        if (world.Sounds is not { } queue) return;
        queue.Enqueue(new SoundEvent(kind, world.Characters.IdOf(subject), other >= 0 ? world.Characters.IdOf(other) : CharacterId.None, at, heavy, unarmed, robot, armoured, fatal));
    }
}
