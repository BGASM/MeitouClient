using Meitou.Data.Gameplay.Bodies;

namespace Meitou.Simulation;

public sealed partial class World
{
    internal Partition PartitionAt(int i) => partitions[i];
    internal EffectBuffer BufferAt(int i) => buffers[i];

    /// <summary>Builds the <see cref="WorldSnapshot"/> of the tick that just ended from the state it left in <see cref="CharacterTable.Previous"/>.</summary>
    void Publish()
    {
        var table = Characters;
        var list = new List<CharacterSnapshot>(table.Count);
        var state = table.Previous;
        for (int i = 0; i < state.Length; i++)
        {
            if (!state[i].Alive) continue;
            var id = new CharacterId(i, state[i].Generation);
            var cold = table.Cold(i);
            // The details only the selection shows: asked once per character, the selection is a list.
            bool selected = cold is { IsPlayer: true } && Player.Selection.Contains(id);
            list.Add(new CharacterSnapshot(id, cold?.Appearance, state[i].Position, state[i].Yaw, cold?.Animation is { } anim && animations is not null ? animations.Publish(anim) : AnimationLayers.For(state[i].Animation, state[i].AnimationTime))
            {
                Faction = cold?.Faction ?? -1,
                WeaponDrawn = cold is not null && cold.DrawnWeapon != Meitou.Data.Gameplay.WeaponKinds.None,
                Name = cold?.Name ?? "",
                SquadId = cold?.SquadId ?? -1,
                IsPlayer = cold?.IsPlayer ?? false,
                IsTrader = cold is { IsPlayer: false, SquadId: >= 0 } && Squads.Find(cold.SquadId)?.Shop is not null,
                Selected = selected,
                Skills = selected && cold!.Stats is { } sk ? new SkillSummary(sk[StatsEnumerated.MeleeAttack], sk[StatsEnumerated.MeleeDefence], sk[StatsEnumerated.Dodge], sk.Toughness, sk.Strength, sk.Athletics) : null,
                Inventory = selected && cold!.Inventory is { } carried ? Items.InventoryText.Lines(carried) : [],
                AllSkills = selected && cold!.Stats is { } allStats ? Array.AsReadOnly(allStats.ToArray()) : [],
                HealthParts = selected && cold!.Medical is { } health && cold.Race is { } anatomy ? Array.AsReadOnly(health.Parts.Select(p => new HealthPartView(p.Template.StringId, p.Template.Name, p.Flesh, p.Stun, p.Bandage, p.Rig, p.MaxHp, p.IsRobotic(anatomy))).ToArray()) : [],
                ItemGrid = selected && cold!.Inventory is { } grid ? new InventoryView(grid.Width, grid.Height, grid.Backpack?.Contents?.Width ?? 0, grid.Backpack?.Contents?.Height ?? 0,
                    Array.AsReadOnly(grid.All().Select((item, index) => new InventoryItemView(index, item.Record, item.Name, item.Section, item.X, item.Y, item.Width, item.Height, item.Quantity, item.Weight, item.Quality)).ToArray())) : null,
                Bounties = selected && cold!.Bounties is not null ? Crime.CrimeSystem.Lines(cold, Tick, TickSeconds) : [],
                Body = cold?.Medical is { } med && cold.Race is { } race ? BodyOf(cold, med, race, state[i].Task) : null,
                Path = selected && (state[i].Flags & (ushort)MoveFlags.HasPath) != 0 ? cold!.Path.Skip(state[i].PathCursor).ToArray() : [],
                CarriedBy = cold?.CarriedBy ?? CharacterId.None,
                AttachBone = cold is { CarriedBy.IsNone: false } ? Carrying.CarrySystem.AttachBone : null,
                Carrying = cold?.Carrying ?? CharacterId.None,
                Scale = cold is { Fighter.IsAnimal: true, Medical: { } size } ? size.ScaleK : 1,
                Activity = cold is { Ai.Goal: { } goal } && Ai.AiSystem.Holds(state[i], cold) ? Ai.TaskBehaviours.Text(goal.Task.Type) : null,
            });
        }
        PreviousSnapshot = Snapshot;
        Snapshot = new WorldSnapshot(Tick, list) { Projectiles = projectiles?.PublishProjectiles() ?? [], Trade = trades?.PublishTrade() };
    }

    static BodyStatus BodyOf(CharacterCold cold, Bodies.MedicalState med, RaceData race, byte task) => new(
        med.Blood / MathF.Max(Bodies.MedicalState.BloodCapacity(race, cold.Stats?.Strength ?? 50, med.HasStatsObject, med.ScaleK, med.ScaleZ), 1), WorstPart(med), med.Hunger, med.Unconscious, med.Dead,
        cold.IsPlayer && (med.NeedsAid(race, false) || med.NeedsAid(race, true)),
        task == (byte)CharacterTask.Treat ? CareKind.Treating : task == (byte)CharacterTask.Rest ? CareKind.Resting : CareKind.None, cold.MedicJob, cold.RescueJob);

    /// <summary>The lowest part fraction (1 for a body without parts), the way <c>Enumerable.Min</c> reads floats: a NaN wins.</summary>
    static float WorstPart(Bodies.MedicalState med)
    {
        var parts = med.Parts;
        if (parts.Count == 0) return 1;
        float worst = parts[0].Fraction;
        if (float.IsNaN(worst)) return worst;
        for (int k = 1; k < parts.Count; k++)
        {
            float f = parts[k].Fraction;
            if (f < worst) worst = f;
            else if (float.IsNaN(f)) return f;
        }
        return worst;
    }
}
