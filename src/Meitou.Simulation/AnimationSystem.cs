using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Gameplay;
using Meitou.Simulation.Combat;

namespace Meitou.Simulation;

/// <summary>
/// The animations a character plays: weighted clips (the movement blend by speed, an idle, a combat technique, a hit reaction) that cross-fade at
/// the CONSTANTS <c>animation blend rate</c>. Cold state of <see cref="CharacterCold"/>, changed only by <see cref="AnimationSystem"/>
/// in the character's own partition, hashed, and published as <see cref="AnimationLayer"/>s.
/// </summary>
public sealed class CharacterAnimation
{
    public const int MaxLayers = 8;

    /// <summary>One clip in play: the library index, its own time (seconds; unsynched clips only), its weight and the weight it is fading to.</summary>
    public struct Layer
    {
        public int Definition;
        public float Time, Weight, Target;
    }

    public readonly Layer[] Layers = new Layer[MaxLayers];
    public int Count;
    /// <summary>Phase of the synched movement clips in cycles (0..1): the lower and the upper body, and a walk fading into a run, stay in step.</summary>
    public float Phase;
    /// <summary>The idle chosen, and the seconds it still plays before another is chosen (-1 / 0: none yet).</summary>
    public int Idle = -1;
    public float IdleLeft;
    /// <summary>A one-shot clip of the whole body that plays to its end (a hit reaction, the fall, getting up): the library index (-1: none), its time in seconds, and whether it is a hit reaction.</summary>
    public int Action = -1;
    public float ActionTime;
    public bool ActionIsStumble;
    /// <summary>The tick of the last hit a reaction was chosen for (-1: none), and whether the character was down (knocked out or dead) the last tick.</summary>
    public int SeenHit = -1;
    public bool WasDown;
    /// <summary>The body's playback factor F = 2 - H (H the skeleton's movement scale; docs/animation.md "Movement"); 0 until the first tick sets it.</summary>
    public float Rate;
    /// <summary>The last <see cref="SpeedWindow"/> speeds, oldest overwritten first (<see cref="SmoothSpeed"/>).</summary>
    public readonly float[] Speeds = new float[SpeedWindow];
    public int SpeedCount;
    public const int SpeedWindow = 8, SpeedTrim = 3;

    /// <summary>
    /// The speed the animations see (docs/animation.md "Movement", <b>Verified</b>): of the last 8 samples, sorted, the 3 lowest and 3 highest dropped and the
    /// middle two averaged (fewer samples at the start: the trimmed middle of those there are). Kenshi samples every frame; here every tick, so the window
    /// is <paramref name="window"/> ticks (<see cref="WindowFor"/>: as many seconds as 8 frames at 60 fps) with the trim scaled alike (engine).
    /// </summary>
    public float SmoothSpeed(float raw, int window = SpeedWindow)
    {
        window = Math.Clamp(window, 1, SpeedWindow);
        Speeds[SpeedCount++ % window] = raw;
        int n = Math.Min(SpeedCount, window);
        Span<float> sorted = stackalloc float[SpeedWindow];
        Speeds.AsSpan(0, n).CopyTo(sorted);
        sorted = sorted[..n];
        sorted.Sort();
        int trim = Math.Min(SpeedTrim * window / SpeedWindow, (n - 1) / 2);
        float sum = 0;
        for (int i = trim; i < n - trim; i++) sum += sorted[i];
        return sum / (n - 2 * trim);
    }

    /// <summary>The window of <see cref="SmoothSpeed"/> for a tick of <paramref name="dt"/> seconds: the 8 samples of 60 fps frames in ticks (4 at 30 Hz).</summary>
    public static int WindowFor(float dt) => Math.Clamp((int)MathF.Round(SpeedWindow / (60 * dt)), 1, SpeedWindow);

    internal void Hash(ref StateHasher h)
    {
        h.Add(Count);
        for (int i = 0; i < Count; i++)
        {
            h.Add(Layers[i].Definition);
            h.Add(Layers[i].Time);
            h.Add(Layers[i].Weight);
            h.Add(Layers[i].Target);
        }
        h.Add(Phase);
        h.Add(Idle);
        h.Add(IdleLeft);
        h.Add(Action);
        h.Add(ActionTime);
        h.Add(ActionIsStumble);
        h.Add(SeenHit);
        h.Add(WasDown);
        h.Add(Rate);
        h.Add(SpeedCount);
        foreach (float s in Speeds) h.Add(s);
    }
}

/// <summary>
/// Picks each character's animation layers from its movement, stance and fight and advances their times in game time (docs/simulation.md
/// "Animation", the original's rules in docs/animation.md). Runs after movement and combat, in the Act phase, and writes only its own characters'
/// cold animation state and, for root motion, their position.
/// <list type="bullet">
/// <item><b>Verified</b> (decompilation, docs/animation.md): every movement clip that fits plays at once, weighted by a sigmoid of its speed ramp and its
/// leg ramp, normalised per body half; a synched clip's phase advances F x v x the weighted <c>play speed</c> cycles per second, an unsynched one F x
/// <c>play speed</c> x v clip seconds per second; weights move linearly towards their targets; hit reactions are chosen by <c>stumbles</c>,
/// <c>big stumble</c> and <c>stumble from</c>; <c>relocates</c> clips, attacks and dodges move the character by their <c>Bip01</c> track; getting up
/// plays <c>standing up 3</c> to 86 %.</item>
/// <item><b>Engine choice</b>: the blend rate is the same for every clip; in combat a standing character holds an upper-body guard idle over the
/// lower movement blend; techniques cut a hit reaction short; a character going down falls with <c>dodgefall</c> and lies in <c>sleeponfloor</c>
/// (the original switches to its ragdoll); a character counts as moving from <see cref="MovingSpeed"/> units per second.</item>
/// </list>
/// </summary>
public sealed class AnimationSystem(AnimationLibrary library, AnimationLengths lengths, float blendRate = 4, CombatSystem? combat = null) : ITickSystem, IAnimationSource
{
    /// <summary>Speed below which a character stands.</summary>
    public const float MovingSpeed = 1;
    /// <summary>Getting up ends when <c>standing up 3</c> is this far through (<b>Verified</b>, <c>Task_GetUp</c>).</summary>
    public const float GetUpEnd = 0.86f;

    public AnimationLibrary Library { get; } = library;
    public AnimationLengths Lengths { get; } = lengths;
    /// <summary>The CONSTANTS <c>animation blend rate</c> (fcs.def default 4, "1 is very slow"): weight per second.</summary>
    public float BlendRate { get; } = blendRate;

    /// <summary>The clip a knocked-out or dead character lies in: <c>sleeponfloor</c>, -1 when the data has none.</summary>
    int LyingClip { get; } = library.IndexOf("sleeponfloor");
    /// <summary>The whole-body clips held while a character bandages a patient (<c>medic</c>) or rests (<c>sitting idle</c>); -1 when the data has none. A held clip like the lying one (engine choice).</summary>
    int TreatingClip { get; } = library.IndexOf("medic");
    int RestingClip { get; } = library.IndexOf("sitting idle");
    /// <summary>The carrier's lift clip (<c>shoulder lift left</c>, played backwards to put down) and a carried body's held pose (<c>carry me</c>, a category 3 record the library appends); -1 when the data has none.</summary>
    int LiftClip { get; } = library.IndexOf(Carrying.CarrySystem.LiftClip);
    int CarriedClip { get; } = library.IndexOf(Carrying.CarrySystem.CarriedClip);

    int HeldClip(CharacterTask task) => task switch { CharacterTask.Treat => TreatingClip, CharacterTask.Rest => RestingClip, _ => -1 };
    // The fall (a stand-in for the ragdoll) and getting up.
    readonly int fall = library.IndexOfAny("Dodge back fall"), getUp = library.IndexOf("standing up 3");
    // The crossbow clips (RANGED records, played by the combat system's timing).
    readonly int aimClip = library.IndexOf(AnimationLibrary.RangedAimRecord), defaultReload = library.IndexOf(AnimationLibrary.RangedReloadRecord);
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> reloadClips = new(StringComparer.Ordinal);

    /// <summary>The reload clip of the character's crossbow: its record's <c>reload anim</c>, else <c>reload 1 phase</c>.</summary>
    int ReloadClip(CharacterCold cold) => cold.Fighter?.Crossbow is { Data.ReloadAnimId: { Length: > 0 } id }
        ? reloadClips.GetOrAdd(id, static (key, lib) => lib.IndexOfRecord(key), Library) is >= 0 and var k ? k : defaultReload
        : defaultReload;

    // Scratch lists of the thread running a partition, kept between ticks (Update clears them on entry).
    [ThreadStatic] static List<(int Index, float Weight)>? lowerScratch, upperScratch;

    public void Act(World world, Partition part, EffectBuffer effects)
    {
        var table = world.Characters;
        var prev = table.Previous;
        var next = table.Next;
        float dt = world.TickSeconds;
        var wantedLower = lowerScratch ??= [];
        var wantedUpper = upperScratch ??= [];
        for (int i = part.Start; i < part.End; i++)
        {
            if (!prev[i].Alive) continue;
            var cold = table.Cold(i)!;
            var a = cold.Animation ??= new CharacterAnimation();
            var v = next[i].Velocity;
            float speed = v.Length();
            // In combat the speed is signed along the facing: backing off is negative and picks the clips with a negative move speed.
            if (cold.InCombat) speed = v.X * MathF.Sin(next[i].Yaw) + v.Z * MathF.Cos(next[i].Yaw);
            speed = a.SmoothSpeed(speed, CharacterAnimation.WindowFor(dt));
            var id = table.IdOf(i);
            var root = Update(a, cold, speed, dt, world.Seed, Rng.Key(id), world.Tick, wantedLower, wantedUpper, ViewOf(id, world.Tick, cold, dt), (CharacterTask)next[i].Task);
            if (root != Vector2.Zero) Relocate(world, i, ref next[i], root);
        }
    }

    /// <summary>What the combat system says about a character for its animation: the technique playing and its progress, and the last hit taken (the last finished tick's state).</summary>
    CombatView ViewOf(CharacterId id, long tick, CharacterCold cold, float tickSeconds)
    {
        if (combat is null) return CombatView.None;
        var slot = combat.StateOf(id);
        int technique = -1;
        float progress = 0;
        if (combat.Playing(id, tick) is { } playing)
        {
            technique = Library.IndexOfTechnique(playing.Technique.StringId);
            progress = playing.Progress;
        }
        else if (combat.RangedPoseOf(id, tick, tickSeconds) is { } pose)
        {
            // The crossbow (engine choice of clips and timing): aiming raises "aimH" (clip aimshoot) through its first half over the steady time, the shot plays the second
            // half, the reload plays the crossbow's own "reload anim" record (else "reload 1 phase") stretched over the reload time.
            (technique, progress) = pose.Pose switch
            {
                RangedPose.Aim => (aimClip, pose.Progress * 0.5f),
                RangedPose.Shot => (aimClip, 0.5f + pose.Progress * 0.5f),
                _ => (ReloadClip(cold), pose.Progress),
            };
        }
        string? part = slot.HitPart >= 0 && cold.Medical is { } body && slot.HitPart < body.Parts.Count ? body.Parts[slot.HitPart].Template.StringId : null;
        return new CombatView(technique, progress, slot.HitTick, slot.HitHeavy, slot.HitBehind, part);
    }

    /// <summary>
    /// Root motion: moves the character by the ground part of its relocating clips' <c>Bip01</c> track (model X and Z turned by its heading), onto
    /// the ground, unless that point is not walkable (Kenshi moves its physics controller, so collisions apply; docs/animation.md "Root motion").
    /// The bodies collide too (engine stand-in for the controllers): the part of the move that would bring it nearer than the two footprint radii to a living
    /// character (last tick's positions) is dropped, so an attack that gains ground stops at its target instead of walking into it.
    /// </summary>
    internal static void Relocate(World world, int self, ref CharacterHot c, Vector2 root)
    {
        float sin = MathF.Sin(c.Yaw), cos = MathF.Cos(c.Yaw);
        var step = new Vector2(root.X * cos + root.Y * sin, -root.X * sin + root.Y * cos);
        var table = world.Characters;
        var block = new BodyBlock { Table = table, Self = self, From = new Vector2(c.Position.X, c.Position.Z), Step = step, Radius = CombatTuning.Footprint(table.Cold(self)) };
        world.Grid.Query(block.From.X, block.From.Y, block.Radius + MaxFootprint + step.Length(), ref block, static (int other, ref BodyBlock b) =>
        {
            var all = b.Table.Previous;
            if (other == b.Self || !all[other].Alive || b.Table.Cold(other) is { CarriedBy.IsNone: false }) return;   // a carried body is on its carrier, not in the way
            var away = b.From - new Vector2(all[other].Position.X, all[other].Position.Z);
            float reach = b.Radius + CombatTuning.Footprint(b.Table.Cold(other));
            if ((away + b.Step).LengthSquared() >= reach * reach) return;
            // It would end up inside: keep only the part of the step that does not close the distance.
            float length = away.Length();
            if (length < 1e-4f) return;
            var normal = away / length;
            float closing = Vector2.Dot(b.Step, normal);
            if (closing < 0) b.Step -= normal * closing;
        });
        float x = c.Position.X + block.Step.X, z = c.Position.Z + block.Step.Y;
        var walk = world.Walkability;
        if (!walk.IsWalkable(x, z)) return;
        c.Position = new Vector3(x, walk.GroundHeight(x, z), z);
    }

    /// <summary>The largest footprint radius a body is tested against (a human's is 4; the biggest animals' are larger).</summary>
    const float MaxFootprint = 30;

    struct BodyBlock
    {
        public CharacterTable Table;
        public int Self;
        public Vector2 From, Step;
        public float Radius;
    }

    /// <summary>
    /// One tick of one character's animation (public for tests). <paramref name="speed"/> is in units per second (signed along the facing in
    /// combat). Returns how far its relocating clips moved it this tick, in model space (X, Z; Z forward), weighted by their weights.
    /// </summary>
    public Vector2 Update(CharacterAnimation a, CharacterCold cold, float speed, float dt, ulong seed, ulong key, long tick,
        List<(int Index, float Weight)>? lowerScratch = null, List<(int Index, float Weight)>? upperScratch = null, CombatView? combatView = null, CharacterTask task = CharacterTask.Idle)
    {
        var fight = combatView ?? CombatView.None;
        var lower = lowerScratch ?? [];
        var upper = upperScratch ?? [];
        lower.Clear();
        upper.Clear();
        if (Library.Animal(cold.RecordId) is { } animal) return UpdateAnimal(a, cold, PlanOf(animal), speed, dt, seed, key, tick, lower, fight);
        if (a.Rate == 0) a.Rate = cold.Appearance is { } look ? 2 - CharacterShape.MovementScaleOf(look) : 1;
        // Carrying picks the "carrying left" clips and a carried body the "being carried" ones (the ANIMATION booleans, docs/animation.md).
        bool carried = !cold.CarriedBy.IsNone;
        var stance = new AnimationStance { Right = new HandHold(cold.DrawnWeapon), Combat = cold.InCombat, CarryingLeft = !cold.Carrying.IsNone, BeingCarried = carried };
        bool down = false;
        if (cold.Medical is { } body)
        {
            // Leg health in per cent weighs the limp clips; a knocked-out or dead character falls and lies on the floor.
            float left = 100, right = 100;
            foreach (var part in body.Parts)
            {
                if (part.Template.Type != Meitou.Data.Gameplay.Bodies.BodyPartType.Leg) continue;
                if (part.Template.Side == Meitou.Data.Gameplay.Bodies.BodySide.Left) left = MathF.Min(left, part.Fraction * 100);
                else right = MathF.Min(right, part.Fraction * 100);
            }
            stance = stance with { LeftLeg = left, RightLeg = right };
            down = body.Incapacitated;
        }

        if (down && !a.WasDown && !carried) StartAction(a, fall, false);
        if (carried) a.Action = -1;   // held on the shoulder: no falling or getting up
        else if (!down && a.WasDown) StartAction(a, getUp, false);
        a.WasDown = down;
        if (!down && fight.HitTick > a.SeenHit)
        {
            a.SeenHit = fight.HitTick;
            Stumble(a, fight, stance, seed, key, tick, upper);
            upper.Clear();
        }
        int technique = down ? -1 : fight.Technique;
        bool fullTechnique = technique >= 0 && Library.Definitions[technique].Area == AnimationArea.All;
        // A technique cuts a reaction or getting up short, and so does walking off (except a hit reaction, which moves the character itself).
        if (a.Action >= 0 && !down && (technique >= 0 || MathF.Abs(speed) >= MovingSpeed && !a.ActionIsStumble)) a.Action = -1;
        if (a.Action >= 0)
        {
            var d = Library.Definitions[a.Action];
            a.ActionTime += dt * (d.PlaySpeed > 0 ? d.PlaySpeed : 1);
            if (a.ActionTime >= Lengths.Of(d.Clip) * (a.Action == getUp ? GetUpEnd : 1) && !(down && a.Action == fall && LyingClip < 0)) a.Action = -1;
        }

        var wanted = lower;
        bool moving = false;
        int legs = 0;   // the lower movement clips at the head of the list (the phase follows them)
        if (a.Action >= 0) wanted.Add((a.Action, 1));
        else if (down)
        {
            if (carried && CarriedClip >= 0) wanted.Add((CarriedClip, 1));
            else if (LyingClip >= 0) wanted.Add((LyingClip, 1));
            a.Idle = -1;
        }
        else if (!fullTechnique)
        {
            moving = MathF.Abs(speed) >= MovingSpeed;
            if (moving || stance.Combat) Library.Movement(AnimationArea.Lower, speed, stance, lower);
            if (moving) Library.Movement(AnimationArea.Upper, speed, stance, upper);
            moving = moving && (lower.Count > 0 || upper.Count > 0);
            if (!moving && !(stance.Combat && lower.Count > 0)) lower.Clear();
            legs = lower.Count;
            if (moving) wanted.AddRange(upper);
            else if (technique < 0 && HeldClip(task) is >= 0 and var held) { wanted.Add((held, 1)); a.Idle = -1; }
            else if (technique < 0)
            {
                // A carrier standing: a whole-body idle as if it carried nothing, with the carrying arm pose (stand carrying left) over it (engine choice: the data has no whole-body carrying idle).
                bool carrying = stance.CarryingLeft || stance.CarryingRight;
                StandingIdle(a, carrying ? stance with { CarryingLeft = false, CarryingRight = false } : stance, lower.Count > 0, dt, seed, key, tick, wanted);
                if (carrying && Library.CarryIdle(stance) is >= 0 and var arms) wanted.Add((arms, 1));
            }
        }
        if (technique >= 0)
        {
            // An attack or a dodge is the whole body; a block the upper body over the legs' clips.
            if (fullTechnique) wanted.Clear();
            wanted.Add((technique, 1));
        }
        // Lifting or putting down (the carry system's timing): the whole-body lift clip, forwards for a pick-up, backwards from its end for a put-down.
        int lift = -1;
        float liftTime = 0;
        if (!down && LiftClip >= 0 && cold.LiftTime >= 0 && task is CharacterTask.PickUp or CharacterTask.PutDown)
        {
            float length = Lengths.Of(Library.Definitions[LiftClip].Clip);
            lift = LiftClip;
            liftTime = task == CharacterTask.PickUp ? MathF.Min(cold.LiftTime, length) : MathF.Max(length - cold.LiftTime, 0);
            wanted.Clear();
            wanted.Add((lift, 1));
            a.Idle = -1;
        }

        return Settle(a, wanted, legs, lift, liftTime, technique, fight, speed, dt, Lengths, 1);
    }

    /// <summary>
    /// The layers' targets from <paramref name="wanted"/>, the synched phase (from the first <paramref name="legs"/> entries), the weights faded and the times advanced, with
    /// the clip lengths of <paramref name="lengths"/>; returns the root motion of the relocating clips times <paramref name="rootScale"/>.
    /// </summary>
    Vector2 Settle(CharacterAnimation a, List<(int Index, float Weight)> wanted, int legs, int lift, float liftTime, int technique, in CombatView fight, float speed, float dt,
        AnimationLengths lengths, float rootScale)
    {
        // Targets: what is wanted fades in, everything else out. The first clips a character ever plays start at full weight (no fade from the binding pose).
        bool first = a.Count == 0;
        for (int k = 0; k < a.Count; k++) a.Layers[k].Target = 0;
        foreach (var (index, weight) in wanted)
        {
            int k = Find(a, index);
            if (k < 0)
            {
                k = a.Count < CharacterAnimation.MaxLayers ? a.Count++ : Evict(a);
                if (k < 0) continue;
                a.Layers[k] = new CharacterAnimation.Layer { Definition = index, Weight = first ? weight : 0 };
            }
            a.Layers[k].Target = weight;
        }

        // The phase of the synched clips advances F x v x the weighted play speed of the lower movement clips, in cycles per second.
        float cycles = 0;
        for (int k = 0; k < legs; k++)
        {
            var (index, weight) = wanted[k];
            var d = Library.Definitions[index];
            if (d.Synchs && d.MoveSpeed != 0) cycles += weight * d.PlaySpeed;
        }
        a.Phase += a.Rate * speed * cycles * dt;
        a.Phase -= MathF.Floor(a.Phase);

        float step = BlendRate * dt;
        int kept = 0;
        var root = Vector2.Zero;
        for (int k = 0; k < a.Count; k++)
        {
            ref var layer = ref a.Layers[k];
            var d = Library.Definitions[layer.Definition];
            layer.Weight = layer.Weight < layer.Target ? MathF.Min(layer.Weight + step, layer.Target) : MathF.Max(layer.Weight - step, layer.Target);
            float before = layer.Time;
            float length = lengths.Of(d.Clip);
            bool synched = d.Synchs && d.MoveSpeed != 0;
            if (layer.Definition == lift) layer.Time = liftTime;
            else if (layer.Definition == technique) layer.Time = fight.Progress * length;   // the combat system's timing: the blow lands at its frame
            else if (layer.Definition == a.Action) layer.Time = a.ActionTime;
            else if (synched) layer.Time = (a.Phase + d.SynchOffset - MathF.Floor(a.Phase + d.SynchOffset)) * length;
            else if (d.MoveSpeed != 0) layer.Time += dt * a.Rate * d.PlaySpeed * speed;
            else layer.Time += dt * (d.PlaySpeed > 0 ? d.PlaySpeed : d.Technique ? 1 : 0);
            if (!d.Loop && !synched) layer.Time = Math.Clamp(layer.Time, 0, length * 0.999f);   // a one-shot clip holds its last frame
            if (d.Relocates && layer.Time > before) root += (lengths.Root(d.Clip, layer.Time) - lengths.Root(d.Clip, before)) * layer.Weight;
            if (layer.Weight <= 0 && layer.Target <= 0) continue;   // faded out: dropped
            a.Layers[kept++] = layer;
        }
        a.Count = kept;
        return root * rootScale;
    }

    /// <summary>An animal's clips with those its skeleton lacks taken out, and the fall and lying clip chosen (<see cref="PlanOf"/>).</summary>
    sealed record AnimalPlan(int[] Movement, IReadOnlyList<(int Index, float Weight)> Idles, int Fall, int Lying, AnimationLengths Lengths);

    readonly System.Collections.Concurrent.ConcurrentDictionary<string, AnimalPlan> animalPlans = new(StringComparer.Ordinal);

    /// <summary>
    /// The plan of an animal's clip set: the race's skeleton lengths (<see cref="AnimationLengths.For"/>), and when that skeleton was read, only the clips it has (a record whose
    /// clip the skeleton lacks would pose nothing: the skimmer's <c>idle1</c>); the first fall and lying candidates it has.
    /// </summary>
    AnimalPlan PlanOf(AnimalClips clips) => animalPlans.GetOrAdd(clips.RecordId, static (_, s) =>
    {
        var (self, c) = s;
        var lengths = self.Lengths.For(c.RaceId);
        bool known = c.RaceId is { } race && self.Lengths.HasSpecies(race);
        bool Has(int i) => !known || lengths.Has(self.Library.Definitions[i].Clip);
        int First(int[] list) { foreach (int i in list) if (Has(i)) return i; return -1; }
        return new AnimalPlan([.. c.Movement.Where(Has)], [.. c.Idles.Where(x => Has(x.Index))], First(c.Fall), First(c.Lying), lengths);
    }, (this, clips));

    /// <summary>
    /// One tick of an animal's animation (docs/simulation.md "Animation", animals): its own clips only. Moving, the movement clips of its list by speed (unsynched: F x
    /// <c>play speed</c> x v clip seconds per second, F = 1 / its size: the humanoid F = (2 - H) / g with H = 1 and g the size, an <b>engine reading</b>); standing (in a
    /// fight too), one of its idles; a creature technique the combat system plays, over everything; down, its <c>gotosleep</c> once, then <c>sleep</c> (or the fall's last
    /// frame held); no hit reactions (no animal record has <c>stumbles</c>) and no getting up (none exists: it fades back to standing). Root motion of an attack is scaled by
    /// the size.
    /// </summary>
    Vector2 UpdateAnimal(CharacterAnimation a, CharacterCold cold, AnimalPlan plan, float speed, float dt, ulong seed, ulong key, long tick, List<(int Index, float Weight)> wanted, in CombatView fight)
    {
        float size = cold.Medical?.ScaleK ?? 1;
        if (!(size > 0.01f)) size = 1;
        if (a.Rate == 0) a.Rate = 1 / size;
        bool carried = !cold.CarriedBy.IsNone;
        bool down = cold.Medical?.Incapacitated ?? false;
        if (down && !a.WasDown && !carried) StartAction(a, plan.Fall, false);
        if (carried || !down) a.Action = -1;   // the fall is an animal's only one-shot clip
        a.WasDown = down;
        int technique = down ? -1 : fight.Technique;
        if (a.Action >= 0)
        {
            var d = Library.Definitions[a.Action];
            a.ActionTime += dt * (d.PlaySpeed > 0 ? d.PlaySpeed : 1);
            if (a.ActionTime >= plan.Lengths.Of(d.Clip) && plan.Lying >= 0) a.Action = -1;   // without a lying clip the fall's last frame is held
        }
        wanted.Clear();
        float v = MathF.Abs(speed);
        if (a.Action >= 0) wanted.Add((a.Action, 1));
        else if (down && plan.Lying >= 0)
        {
            wanted.Add((plan.Lying, 1));
            a.Idle = -1;
        }
        else if (technique >= 0) wanted.Add((technique, 1));
        else
        {
            // Down without any lying clip (the crab, the robot spiders, gorillos: their skeletons have none) it stays in its idle rather than the binding pose.
            if (v >= MovingSpeed && !down) Library.Movement(plan.Movement, v, wanted);
            if (wanted.Count == 0)
            {
                bool fresh = a.Idle < 0 || a.IdleLeft <= 0;
                StandingIdle(a, plan.Idles, dt, seed, key, tick, wanted);
                // A one-shot idle (the bonedog's "idle noloop", 2.5 s) is held only for its own length, not the record's 10 to 40 s idle time (engine choice).
                if (fresh && a.Idle >= 0 && Library.Definitions[a.Idle] is { Loop: false } once)
                    a.IdleLeft = MathF.Min(a.IdleLeft, plan.Lengths.Of(once.Clip) / MathF.Max(once.PlaySpeed, 0.01f));
            }
            else a.Idle = -1;
        }
        return Settle(a, wanted, 0, -1, 0, technique, fight, v, dt, plan.Lengths, size);
    }

    /// <summary>
    /// A standing character's idle: out of combat a whole-body idle, in combat an upper-body guard over the lower movement blend (<paramref name="legs"/>:
    /// the lower clips are already wanted), kept until its time is up and then drawn again by <c>idle chance</c>.
    /// </summary>
    void StandingIdle(CharacterAnimation a, in AnimationStance stance, bool legs, float dt, ulong seed, ulong key, long tick, List<(int Index, float Weight)> wanted) =>
        StandingIdle(a, legs ? Library.CombatIdles(stance) : Library.Idles(stance), dt, seed, key, tick, wanted);

    /// <summary>The idle among <paramref name="idles"/> (index, <c>idle chance</c>): kept until its time is up, then drawn again by weight.</summary>
    void StandingIdle(CharacterAnimation a, IReadOnlyList<(int Index, float Weight)> idles, float dt, ulong seed, ulong key, long tick, List<(int Index, float Weight)> wanted)
    {
        if (a.Idle < 0 || a.IdleLeft <= 0 || !idles.Any(x => x.Index == a.Idle))
        {
            float total = 0;
            foreach (var (_, w) in idles) total += w;
            if (idles.Count > 0)
            {
                ulong roll = Rng.Hash(seed, key, RngPurpose.Animation, (ulong)tick);
                float pick = Rng.Float(roll) * total;
                int chosen = idles[^1].Index;
                foreach (var (index, w) in idles)
                {
                    if ((pick -= w) < 0) { chosen = index; break; }
                }
                var d = Library.Definitions[chosen];
                a.Idle = chosen;
                a.IdleLeft = d.IdleTimeMin + Rng.Float(Rng.Mix(roll)) * Math.Max(d.IdleTimeMax - d.IdleTimeMin, 0);
                // A new idle starts from its first frame.
                for (int k = 0; k < a.Count; k++) if (a.Layers[k].Definition == chosen) a.Layers[k].Time = 0;
            }
            else a.Idle = -1;
        }
        if (a.Idle >= 0)
        {
            wanted.Add((a.Idle, 1));
            a.IdleLeft -= dt;
        }
    }

    /// <summary>
    /// The hit reaction for a hit (docs/animation.md "Hit reactions", <b>Verified</b>): none unless the hit is heavy or no reaction is playing; else one of
    /// the hit part's stumbles with <c>big stumble</c> = heavy and free and <c>stumble from</c> = the side, drawn by weight.
    /// </summary>
    void Stumble(CharacterAnimation a, in CombatView fight, in AnimationStance stance, ulong seed, ulong key, long tick, List<(int Index, float Weight)> scratch)
    {
        bool free = !(a.Action >= 0 && a.ActionIsStumble);
        if (fight.HitPart is not { } part || !fight.HitHeavy && !free) return;
        scratch.Clear();
        Library.Stumbles(part, fight.HitHeavy && free, fight.HitBehind ? 1 : 0, stance, a.ActionIsStumble ? a.Action : -1, scratch);
        if (scratch.Count == 0) return;
        float total = 0;
        foreach (var (_, w) in scratch) total += w;
        float pick = Rng.Float(Rng.Hash(seed, key, RngPurpose.Animation, (ulong)tick ^ 0x5354554D424CUL)) * total;
        int chosen = scratch[^1].Index;
        foreach (var (index, w) in scratch)
        {
            if ((pick -= w) < 0) { chosen = index; break; }
        }
        StartAction(a, chosen, true);
    }

    /// <summary>Starts a one-shot clip from its first frame (nothing when the data has no such clip).</summary>
    static void StartAction(CharacterAnimation a, int clip, bool stumble)
    {
        if (clip < 0) return;
        a.Action = clip;
        a.ActionTime = 0;
        a.ActionIsStumble = stumble;
        for (int k = 0; k < a.Count; k++) if (a.Layers[k].Definition == clip) a.Layers[k].Time = 0;
    }

    static int Find(CharacterAnimation a, int definition)
    {
        for (int k = 0; k < a.Count; k++) if (a.Layers[k].Definition == definition) return k;
        return -1;
    }

    /// <summary>Frees the faintest fading-out layer when all are in use; -1 when every layer is wanted.</summary>
    static int Evict(CharacterAnimation a)
    {
        int worst = -1;
        for (int k = 0; k < a.Count; k++)
            if (a.Layers[k].Target <= 0 && (worst < 0 || a.Layers[k].Weight < a.Layers[worst].Weight)) worst = k;
        return worst;
    }

    /// <summary>
    /// The layers to publish for a snapshot: the record name (the renderer finds its track masks by it), the clip time in seconds and the weight. The
    /// weights are normalised as Kenshi's layers are (docs/animation.md "The three layers each frame"): the upper body (upper and whole-body clips) to a
    /// total of at most 1, the lower body to at most 1 minus the whole-body clips' weight (actions and <c>all</c> clips; a block is played without its lower-body
    /// tracks and keeps the legs' clips); overlays are left alone.
    /// </summary>
    public IReadOnlyList<AnimationLayer> Publish(CharacterAnimation a)
    {
        int visible = 0;
        float upperTotal = 0, full = 0, lowerTotal = 0;
        for (int k = 0; k < a.Count; k++)
        {
            var layer = a.Layers[k];
            if (layer.Weight <= 0) continue;
            visible++;
            var d = Library.Definitions[layer.Definition];
            switch (d.Area)
            {
                case AnimationArea.Lower: lowerTotal += layer.Weight; break;
                case AnimationArea.Overlay: break;
                default:
                    upperTotal += layer.Weight;
                    if (d.Area == AnimationArea.All || d.IsAction && !d.Technique) full += layer.Weight;   // a block keeps the legs' clips
                    break;
            }
        }
        if (visible == 0) return [];
        float upperScale = upperTotal > 1 ? 1 / upperTotal : 1;
        float lowerRoom = MathF.Max(0, 1 - full * upperScale);
        float lowerScale = lowerTotal > lowerRoom ? lowerRoom / lowerTotal : 1;
        var list = new AnimationLayer[visible];   // the snapshot keeps its own copy, sized exactly
        int n = 0;
        for (int k = 0; k < a.Count; k++)
        {
            var layer = a.Layers[k];
            if (layer.Weight <= 0) continue;
            var d = Library.Definitions[layer.Definition];
            float scale = d.Area switch { AnimationArea.Lower => lowerScale, AnimationArea.Overlay => 1, _ => upperScale };
            list[n++] = new AnimationLayer(d.Name, layer.Time, layer.Weight * scale);
        }
        return list;
    }
}

/// <summary>What the world asks to turn a character's animation state into the layers a snapshot carries.</summary>
public interface IAnimationSource
{
    IReadOnlyList<AnimationLayer> Publish(CharacterAnimation animation);
}

/// <summary>
/// The combat state the animation follows: the technique playing (library index, -1 none) and its progress (0 to 1), and the last hit taken (tick, -1
/// none; heavy, from behind, the body part's LOCATIONAL_DAMAGE string id).
/// </summary>
public readonly record struct CombatView(int Technique, float Progress, int HitTick, bool HitHeavy, bool HitBehind, string? HitPart)
{
    public static readonly CombatView None = new(-1, 0, -1, false, false, null);
}
