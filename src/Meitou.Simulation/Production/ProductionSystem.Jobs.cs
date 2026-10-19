using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation.Construction;

namespace Meitou.Simulation.Production;

// Job lists (Perma-Jobs; docs/simulation.md "Production", the original's rules in docs/game/ai-tasks.md "Perma-jobs"): a player character whose task is idle or came from its list takes the
// first job of its list that is not blocked, so a higher job takes over as soon as it can run and a blocked one passes to the next. Meitou runs two types: Operating machine (87, the
// operator below) and Hauling to (124: a machine's output carried to the job's storage, CharacterTask.Haul). Blocked uses one test for starting and for going on.
public sealed partial class ProductionSystem
{
    /// <summary>How often (ticks) a character's job list is looked at, staggered by slot (engine choice; the original re-plans when its task ends or its commitment runs out).</summary>
    public const int JobCheckTicks = 10;

    void AddJobs(World world, AddJob add)
    {
        if (world.Buildings.Find(add.Site) is null) return;
        var table = world.Characters;
        foreach (var id in Who(world, add.Characters))
            if (table.TryResolveNext(id, out int slot) && table.Cold(slot) is { IsPlayer: true } cold) PermaJobs.Add(cold, add.Type, new PermaJob(add.Type, add.Site));
    }

    void RemoveJobAt(World world, RemoveJob remove)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(remove.Character, out int slot) || table.Cold(slot) is not { } cold || (uint)remove.Index >= (uint)cold.Jobs.Count) return;
        cold.Jobs.RemoveAt(remove.Index);
        if (cold.JobIndex == remove.Index) End(table, slot);
        else if (cold.JobIndex > remove.Index) cold.JobIndex--;
    }

    /// <summary>Once a tick (serial): characters due a look at their lists take the first job that can run, unless it is the one they are on.</summary>
    void PickJobs(World world)
    {
        var table = world.Characters;
        for (int i = 0; i < table.HighWater; i++)
        {
            ref var n = ref table.Next[i];
            if (!n.Alive) continue;
            var cold = table.Cold(i)!;
            var task = (CharacterTask)n.Task;
            // An order replaced the job's task.
            if (cold.JobIndex >= 0 && task is not (CharacterTask.Operate or CharacterTask.Haul)) cold.JobIndex = -1;
            if (cold.Jobs.Count == 0 || !cold.IsPlayer || (world.Tick + i) % JobCheckTicks != 0) continue;
            // Idle as the medic job counts it: no task, or following or wandering.
            if (cold.JobIndex < 0 && task is not (CharacterTask.Idle or CharacterTask.Wander or CharacterTask.Follow)) continue;
            if (!Able(cold)) continue;
            int pick = -1;
            for (int k = 0; k < cold.Jobs.Count && pick < 0; k++)
                if (Runnable(world, cold, k)) pick = k;
            if (pick == cold.JobIndex) continue;
            if (pick < 0) { if (cold.JobIndex >= 0) End(table, i); continue; }
            StartJob(world, i, pick);
        }
    }

    /// <summary>A job of the list that may run now: Meitou runs its type, the jobs switch is on (or the type ignores it), and it is not blocked.</summary>
    bool Runnable(World world, CharacterCold cold, int k)
    {
        var job = cold.Jobs[k];
        return PermaJobs.Runs(job.Type) && ((cold.Orders & CharacterOrders.Jobs) != 0 || PermaJobs.IgnoresSwitch(job.Type)) && !Blocked(world, cold, job);
    }

    /// <summary>
    /// Whether a job cannot run (the original skips such a job: an unmet start condition the planner cannot meet, or the target finder found nothing; docs/game/ai-tasks.md "Perma-jobs").
    /// Operating: the building is gone or not an operable machine, its operator places are taken by others, or it has no work (<see cref="HasWork"/>). Hauling to: the storage is gone, and
    /// the character carries nothing it takes and no machine within <see cref="ProductionSettings.HaulRange"/> has an output it takes.
    /// </summary>
    bool Blocked(World world, CharacterCold cold, PermaJob job)
    {
        if (job.Site is not { } id || world.Buildings.Find(id) is not { Complete: true } site || Ensure(world, site) is not { } m) return true;
        switch (job.Type)
        {
            case TaskType.OPERATE_MACHINERY:
                if (!m.Data.Operable) return true;
                if (OperatorsOf(world, id) - (cold.OperateSite == id ? 1 : 0) >= m.Data.MaxOperators) return true;
                return !HasWork(world, site, m, cold);
            case TaskType.OPERATE_STORAGE:
                if (!m.Data.IsStorage || m.Storage is null) return true;
                if (cold.Inventory is { } inv && inv.All().Any(i => Takes(world, m, i))) return false;
                return HaulSource(world, site, m) is null;
            default:
                return true;
        }
    }

    /// <summary>
    /// A machine an operator can work (the original's start conditions of task 87: output not full, inputs not empty, power; docs/game/ai-tasks.md): it has power, and a bench a queued
    /// research or craft; a farm a dead crop to clear or a ripe one to harvest; inputs that hold something or that the operator can fill (it carries the item or, with
    /// <see cref="ProductionSettings.AutoHaul"/>, a storage holds it); and room in the output or, with AutoHaul, a storage that takes it.
    /// </summary>
    bool HasWork(World world, BuildSite site, Machine m, CharacterCold cold)
    {
        if (WorkFactor(world, m) <= 0) return false;
        if (m.Data.IsResearch) return world.Research.Queue.Count > 0;
        if (m.Farm is { } f)
        {
            if (f.Death >= m.Data.Farm!.DeathThreshold) return true;
            return f.Growth >= 1 && f.Harvested < f.Plants.Length && Room(world, site, m);
        }
        if (m.Craft is { Queue.Count: 0 }) return false;
        foreach (var input in m.Inputs)
        {
            if (input.Rate <= 0 || input.Level + input.Count > 0) continue;
            if (cold.Inventory is { } inv && Carried(inv, input.Item).Any()) continue;
            if (Settings.AutoHaul && NearestStorage(world, site, s => s.Storage!.Items.Any(i => i.Record == input.Item && i.Quantity > 0 && i.Section == "main")) is not null) continue;
            return false;
        }
        return m.Craft is not null || Room(world, site, m);
    }

    /// <summary>The output has room, or with AutoHaul a storage takes it.</summary>
    bool Room(World world, BuildSite site, Machine m) =>
        m.Output is { } o && (!o.Full || Settings.AutoHaul && items.Create(o.Recipe.Item, 1) is { } one && NearestStorage(world, site, s => s.Data.Accepts(one.Record, one.Type) && Fits(s.Storage!, one)) is not null);

    /// <summary>A carried item a "Hauling to" job takes to its storage: a loose stack of something a player machine makes (not the character's own kit), that the storage takes and has room for.</summary>
    static bool Takes(World world, Machine store, Items.ItemInstance item) =>
        item.Section is "main" or "backpack_content" && !item.LockedOn && item.Contents is null && store.Data.Accepts(item.Record, item.Type) && Fits(store.Storage!, item)
        && world.Machines.All.Any(s => !s.Data.IsStorage && s.Output?.Recipe.Item == item.Record);

    /// <summary>The player's complete machine (not a storage) nearest a storage within <see cref="ProductionSettings.HaulRange"/> whose output holds items the storage takes (ties: the lower site id).</summary>
    string? HaulSource(World world, BuildSite storage, Machine store)
    {
        string? best = null;
        float bestGap = float.MaxValue;
        foreach (var s in world.Machines.All)
        {
            if (s.Data.IsStorage || s.Output is not { Count: > 0 } output || world.Buildings.Find(s.Site) is not { Complete: true } site || site.Owner != world.Player.Faction) continue;
            float gap = Gap(site.Position, storage.Position);
            if (gap > Settings.HaulRange || gap > bestGap || (gap == bestGap && string.CompareOrdinal(s.Site, best) > 0)) continue;
            if (items.Create(output.Recipe.Item, 1) is not { } one || !store.Data.Accepts(one.Record, one.Type) || !Fits(store.Storage!, one)) continue;
            best = s.Site;
            bestGap = gap;
        }
        return best;
    }

    void StartJob(World world, int slot, int k)
    {
        var table = world.Characters;
        var cold = table.Cold(slot)!;
        var job = cold.Jobs[k];
        var site = world.Buildings.Find(job.Site!)!;
        var m = Ensure(world, site)!;
        if (cold.JobIndex < 0) cold.ResumeTask = (CharacterTask)table.Next[slot].Task;
        if (job.Type == TaskType.OPERATE_MACHINERY)
        {
            if (cold.OperateSite != site.Id) Begin(world, slot, site, m);
        }
        else
        {
            End(table, slot);
            ref var n = ref table.Next[slot];
            MovementSystem.Stop(table, slot);
            n.Task = (byte)CharacterTask.Haul;
            n.Mode = SpeedMode.Free;
            n.TaskTime = 0;
            // What it already carries for the storage goes there first; else it fetches.
            bool carries = cold.Inventory is { } inv && inv.All().Any(i => Takes(world, m, i));
            cold.HaulSite = carries ? site.Id : HaulSource(world, site, m);
            sweep = true;
        }
        cold.JobIndex = k;
    }

    /// <summary>A hauler at the end of a leg (serial): at the source it takes the output, then makes for the storage; at the storage it puts in what the storage takes and the job's trip ends.</summary>
    void HaulStep(World world, int slot)
    {
        var table = world.Characters;
        var cold = table.Cold(slot)!;
        ref var n = ref table.Next[slot];
        if ((uint)cold.JobIndex >= (uint)cold.Jobs.Count || cold.Jobs[cold.JobIndex].Site is not { } storeId || cold.HaulSite is not { } at
            || world.Buildings.Find(storeId) is not { Complete: true } || world.Machines.Find(storeId) is not { Storage: not null } store
            || world.Buildings.Find(at) is not { Complete: true } atSite || world.Machines.Find(at) is not { } there || !Able(cold) || cold.Inventory is not { } inv)
        {
            End(table, slot);
            return;
        }
        if (!AtSite(n, atSite, there)) return;
        if (at != storeId)
        {
            if (there.Output is { Count: > 0 } output && items.Create(output.Recipe.Item, output.Count) is { } made && store.Data.Accepts(made.Record, made.Type))
            {
                int moved = Placed(inv, made);
                output.Count -= moved;
                world.Machines.Touch();
            }
            if (inv.All().Any(i => Takes(world, store, i))) cold.HaulSite = storeId;
            else End(table, slot);
            return;
        }
        foreach (var item in inv.All().Where(i => Takes(world, store, i)).ToList())
        {
            inv.Remove(item);
            if (store.Storage!.Add(item)) continue;
            if (!inv.Add(item)) world.GroundItems.Drop(item, store.Position, 0);
            break;
        }
        world.Machines.Touch();
        End(table, slot);
    }
}
