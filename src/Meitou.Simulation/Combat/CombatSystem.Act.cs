using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    // ------------------------------------------------------------------ act

    /// <summary>A stat as the combat formulas read it: times the injury and hunger multiplier and the stats' read-time scale (an animal's age, docs/game/animals.md).</summary>
    /// <summary>The guarding state: the fighter's own, or the Block order (its tooltip's +20 melee defence; docs/game/combat.md).</summary>
    static bool Guarding(CharacterCold c) => c.Fighter!.Guarding || Orders.Has(c, CharacterOrders.Block);

    static float Eff(CharacterCold c, StatsEnumerated stat) => c.Stats![stat] * c.Stats.Scale * c.Medical!.StatMultiplier(stat);

    int PauseTicks(World world, int slot, int counter)
    {
        float u = CombatRolls.Float(world.Seed, Rng.Key(world.Characters.IdOf(slot)), CombatRoll.Pause, (ulong)(uint)counter ^ ((ulong)(uint)world.Tick << 20));
        return Math.Max(1, (int)MathF.Ceiling((CombatTuning.PauseMin + u * (CombatTuning.PauseMax - CombatTuning.PauseMin)) / world.TickSeconds));
    }

    /// <summary>
    /// The ticks a technique's clip takes at <c>anim speed mult</c> x <paramref name="rate"/> clip seconds per second; the clip's length is from the fighter's skeleton (an
    /// animal's own, <see cref="AnimationLengths.For"/>: <c>attack1</c> is 0.77 s on a bonedog and 5 s on a beak thing).
    /// </summary>
    int Ticks(CombatTechnique t, float rate, float dt, CharacterCold cold)
    {
        float clip = lengths.For(cold.Fighter is { IsAnimal: true } ? cold.Race?.StringId : null).Of(t.AnimName);
        if (clip <= 0) clip = CombatTuning.DefaultClipSeconds;
        return Math.Max(2, (int)MathF.Ceiling(clip / (MathF.Max(t.AnimSpeedMult, 0.1f) * MathF.Max(rate, 0.05f)) / dt));
    }

    /// <summary>
    /// The playback rate of a technique (docs/game/combat.md "Animation speed", <b>Verified</b>): attacks lerp(t, 0.8, 1.2) x G capped at 1.2, blocks lerp(t, 0.6, 4) x G
    /// capped at 4, with t = 0.01 (0.6 dexterity + 0.4 skill) (attack, or defence with the bonuses); dodges 1. G = (0.5 + 0.5 min(1, e + 0.5) (1 - f)) x the gear's
    /// <c>combat speed mult</c>, e the encumbrance factor and f the weapon-weight-over-strength factor of the strength XP.
    /// </summary>
    float PlayRate(CharacterCold cold, CombatTechnique tech, bool attack)
    {
        var fighter = cold.Fighter!;
        if (!attack && (tech.IsDodge || !tech.IsBlock)) return 1;
        float weight = fighter.Unarmed ? 0 : fighter.Weapon!.Stats.Weight;
        float f = Math.Clamp(xp.WeaponWeightStrengthFactor(weight, cold.Stats![StatsEnumerated.Strength], cold.Medical!.StatMultiplier(StatsEnumerated.Strength)), 0, 1);
        float g = (0.5f + 0.5f * MathF.Min(1, fighter.EncumbranceFactor + 0.5f) * (1 - f)) * fighter.CombatSpeed;
        float dex = Eff(cold, StatsEnumerated.Dexterity) * fighter.DexterityMult;
        float skill = attack
            ? fighter.Unarmed ? Eff(cold, StatsEnumerated.MartialArts) : Eff(cold, StatsEnumerated.MeleeAttack) + fighter.Weapon!.Stats.AttackMod
            : Eff(cold, StatsEnumerated.MeleeDefence) + fighter.DefenceBonus + (fighter.Weapon?.Stats.DefenceMod ?? 0) + (Guarding(cold) ? 20 : 0);
        float t = 0.01f * (0.6f * dex + 0.4f * skill);
        return attack ? MathF.Min(1.2f, WeaponStats.Lerp(t, 0.8f, 1.2f) * g) : MathF.Min(4f, WeaponStats.Lerp(t, 0.6f, 4f) * g);
    }

    public void Act(World world, Partition part, EffectBuffer effects)
    {
        var table = world.Characters;
        var prev = table.Previous;
        var next = table.Next;
        int now = (int)world.Tick;
        float dt = world.TickSeconds;
        List<MedicalEvent>? events = null;
        for (int i = part.Start; i < part.End; i++)
        {
            if (!prev[i].Alive) continue;
            var cold = table.Cold(i)!;
            if (cold.Fighter is null || cold.Stats is null || cold.Medical is null || cold.Race is null) continue;
            ref var s = ref write[i];
            ulong key = Rng.Key(table.IdOf(i));

            // 1. The body: bleeding, healing, waking.
            if (Settings.TickMedical)
            {
                events ??= [];
                events.Clear();
                cold.Medical.Tick(dt * HoursPerSecond, MakeContext(world, i, cold), events);
            }
            s.Down = cold.Medical.Incapacitated;
            if (s.Down)
            {
                s.AttackTech = -1;
                s.NextBlow = 0;
                s.ReactTech = -1;
                s.TargetSlot = -1;
                if (cold.InCombat) EndFight(table, i);
                continue;
            }

            // 2. Helpless in a hit reaction until its clip ends; every BreakOutSeconds of it the character may break out into a block against a blow coming at it
            // (the original's +0x124; its 10 % lower bound on the swing's progress is not modelled).
            if (now < s.StunUntil)
            {
                s.AttackTech = -1;
                s.NextBlow = 0;
                int every = Math.Max(1, (int)MathF.Round(CombatTuning.BreakOutSeconds / dt));
                bool chance = s.StumbleClip >= 0 && now > s.StumbleStart && (now - s.StumbleStart) % every == 0;
                if (s.ReactTech >= 0 && now >= s.ReactStart + s.ReactTicks) s.ReactTech = -1;
                if (chance && s.ReactTech < 0)
                {
                    React(world, i, ref s, cold, key, now, dt);
                    if (s.ReactTech >= 0 && !techniques[s.ReactTech].IsDodge) { s.StunUntil = now; s.StumbleClip = -1; }
                    else s.ReactTech = -1;
                }
                if (now < s.StunUntil) continue;
            }

            // 3. The target.
            bool fighting = s.TargetSlot >= 0;
            if (fighting && !(s.TargetSlot < prev.Length && prev[s.TargetSlot].Alive && prev[s.TargetSlot].Generation == s.TargetGeneration && !read[s.TargetSlot].Down))
            {
                fighting = false;
                s.TargetSlot = -1;
                s.AttackTech = -1;
                s.NextBlow = 0;
                EndFight(table, i);
            }

            // 3b. A crossbowman fights with the crossbow or the melee weapon; with the crossbow the ranged controller runs instead of the melee attack (a blow aimed at it is still met).
            ChooseMode(world, i, ref s, cold, fighting);
            if (s.Ranged)
            {
                if (s.ReactTech >= 0 && now >= s.ReactStart + s.ReactTicks) s.ReactTech = -1;
                if (s.ReactTech < 0 && s.AttackTech < 0) React(world, i, ref s, cold, key, now, dt);
                TickRanged(world, i, ref s, cold, effects, now, dt);
                continue;
            }

            // 4. The reaction and the attack in progress.
            if (s.ReactTech >= 0 && now >= s.ReactStart + s.ReactTicks) s.ReactTech = -1;
            if (s.AttackTech >= 0)
            {
                var tech = techniques[s.AttackTech];
                if (s.NextBlow > 0 && fighting && now >= s.AttackStart + (int)MathF.Ceiling(tech.StrikeProgress(s.NextBlow) * s.AttackTicks))
                {
                    effects.Emit(i, s.TargetSlot, BlowEffect, s.AttackSeq, default, s.NextBlow);
                    s.Thrown++;
                    s.NextBlow = s.NextBlow < tech.Blows ? s.NextBlow + 1 : 0;
                }
                if (now >= s.AttackEnd)
                {
                    s.AttackTech = -1;
                    s.NextBlow = 0;
                    float pause = s.LastOutcome switch
                    {
                        BlowOutcome.Blocked => CombatTuning.BlockedPause,
                        BlowOutcome.Dodged => CombatTuning.DodgedPause,
                        BlowOutcome.Missed => CombatTuning.MissedPause,
                        _ => CombatTuning.HitPause,
                    };
                    s.ReadyTick = now + Math.Max(1, (int)MathF.Ceiling(pause / dt));
                }
            }

            // 5. Reacting comes before attacking: a free character looks for blows coming at it.
            if (s.ReactTech < 0 && s.AttackTech < 0) React(world, i, ref s, cold, key, now, dt);

            // 6. A free attacker in reach starts a technique.
            if (fighting && s.AttackTech < 0 && s.ReactTech < 0 && now >= s.ReadyTick) StartAttack(world, i, ref s, cold, key, now, dt);
        }
    }

    void StartAttack(World world, int i, ref CombatSlot s, CharacterCold cold, ulong key, int now, float dt)
    {
        var table = world.Characters;
        var prev = table.Previous;
        int t = s.TargetSlot;
        var fighter = cold.Fighter!;
        if (!fighter.CanAttack || Orders.Has(cold, CharacterOrders.Block)) return;   // the Block order: defends, starts no attack
        var tc = table.Cold(t)!;
        if (!HasSlot(world, i, t, tc) || !HasInitiative(world, i, t, cold, tc, now)) return;
        float gap =Vector2.Distance(new(prev[i].Position.X, prev[i].Position.Z), new(prev[t].Position.X, prev[t].Position.Z)) - CombatTuning.Footprint(cold) - CombatTuning.Footprint(tc);
        bool moving = new Vector2(prev[t].Velocity.X, prev[t].Velocity.Z).Length() >= 1;
        float weaponReach = fighter.Unarmed ? 99 : fighter.Weapon!.Data.Length * 0.5f;
        float skill = Eff(cold, fighter.WeaponSkill);
        float roll = CombatRolls.Float(world.Seed, key, CombatRoll.Technique, (ulong)(s.AttackSeq + 1));
        int pick = TechniqueChooser.ChooseAttack(techniques, fighter.Kind, fighter.TechniqueCategory, false, skill, gap, moving, weaponReach, roll);
        if (pick < 0) return;
        var tech = techniques[pick];
        s.AttackSeq++;
        s.AttackTech = pick;
        s.AttackStart = now;
        s.AttackTicks = Ticks(tech, PlayRate(cold, tech, attack: true), dt, cold);
        s.AttackEnd = now + Math.Max(1, (int)MathF.Ceiling(tech.EndProgress * s.AttackTicks));
        s.NextBlow = 1;
        s.LastOutcome = BlowOutcome.Missed;
        s.AttackReach = TechniqueChooser.ReachOf(tech, moving, weaponReach);
        s.AttackSkill = Eff(cold, fighter.Unarmed ? StatsEnumerated.MartialArts : StatsEnumerated.MeleeAttack) + (fighter.Unarmed ? fighter.UnarmedBonus : fighter.AttackBonus + (fighter.Weapon?.Stats.AttackMod ?? 0));
    }

    /// <summary>
    /// Whether <paramref name="i"/> may swing at <paramref name="t"/> now, asking the target first (<b>Verified</b>, FUN_14060c670 / FUN_140666a30, docs/game/combat.md
    /// "Pacing"): not while the target is swinging at it (it blocks instead); yes while the target is staggered or reacting; when the target is free and ready to swing back
    /// on the same tick, an initiative roll on both attack skills (the skill roll with <c>attack chance factor</c>, 50/50 at equal skill) picks who swings, drawn from the pair
    /// so both sides agree, and the loser waits for the winner's swing to block it. A target in its pause (the original then forces it to block) is simply attacked.
    /// </summary>
    bool HasInitiative(World world, int i, int t, CharacterCold cold, CharacterCold tc, int now)
    {
        ref readonly var target = ref read[t];
        var prev = world.Characters.Previous;
        bool atMe = target.TargetSlot == i && target.TargetGeneration == prev[i].Generation;
        if (target.AttackTech >= 0) return !atMe;
        if (now < target.StunUntil || target.ReactTech >= 0 || !atMe || target.Down || now < target.ReadyTick || tc.Fighter is not { CanAttack: true }) return true;
        // Both ready at each other: one draw for the pair, the lower slot's view.
        int lo = Math.Min(i, t), hi = Math.Max(i, t);
        float skillI = Eff(cold, cold.Fighter!.Unarmed ? StatsEnumerated.MartialArts : StatsEnumerated.MeleeAttack);
        float skillT = Eff(tc, tc.Fighter.Unarmed ? StatsEnumerated.MartialArts : StatsEnumerated.MeleeAttack);
        float lowSkill = lo == i ? skillI : skillT, highSkill = lo == i ? skillT : skillI;
        float a = 1 + combat.AttackChanceFactor * MathF.Max(0, highSkill - lowSkill), b = 1 + combat.AttackChanceFactor * MathF.Max(0, lowSkill - highSkill);
        float k = b / a, lowWins = k >= 1 ? 1 - 1 / (2 * k) : k / 2;
        ulong pair = ((ulong)(uint)lo << 32) | (uint)hi;
        float roll = CombatRolls.Float(world.Seed, pair, CombatRoll.Initiative, (ulong)(uint)now);
        return (roll < lowWins) == (i == lo);
    }

    struct SlotScan
    {
        public CombatSlot[] Read;
        public CharacterTable Table;
        public int Self, Target, TargetGeneration, Ahead;
        public float X, Z, MyDistance;
    }

    /// <summary>
    /// Whether <paramref name="i"/> holds one of its target's attack slots (docs/game/combat.md "Pacing": <see cref="AttackSlots"/> plus the target race's
    /// <c>extra attack slots</c>, FUN_140666310): the fighters at the target, nearest first (slot order on a tie), take them; the rest wait (the original circles, not modelled).
    /// </summary>
    bool HasSlot(World world, int i, int t, CharacterCold tc)
    {
        int slots = Math.Max(1, AttackSlots + (tc.Race?.ExtraAttackSlots ?? 0));
        var prev = world.Characters.Previous;
        float x = prev[t].Position.X, z = prev[t].Position.Z;
        var scan = new SlotScan { Read = read, Table = world.Characters, Self = i, Target = t, TargetGeneration = prev[t].Generation, X = x, Z = z };
        scan.MyDistance = Vector2.DistanceSquared(new(prev[i].Position.X, prev[i].Position.Z), new(x, z));
        world.Grid.Query(x, z, CombatTuning.ReactionScanRadius, ref scan, static (int other, ref SlotScan st) =>
        {
            if (other == st.Self) return;
            ref readonly var o = ref st.Read[other];
            if (o.TargetSlot != st.Target || o.TargetGeneration != st.TargetGeneration || o.Down || o.Ranged) return;
            ref readonly var p = ref st.Table.Previous[other];
            float d = Vector2.DistanceSquared(new(p.Position.X, p.Position.Z), new(st.X, st.Z));
            if (d < st.MyDistance || d == st.MyDistance && other < st.Self) st.Ahead++;
        });
        return scan.Ahead < slots;
    }

    struct Scan
    {
        public CombatSlot[] Read;
        public CombatTechnique[] Techniques;
        public int Self, SelfGeneration, Now;
        public CombatSlot Me;
        public int Attacker, Blow, Strike;
    }

    void React(World world, int i, ref CombatSlot s, CharacterCold cold, ulong key, int now, float dt)
    {
        var table = world.Characters;
        var prev = table.Previous;
        var scan = new Scan { Read = read, Techniques = techniques, Self = i, SelfGeneration = prev[i].Generation, Now = now, Me = s, Attacker = -1, Strike = int.MaxValue };
        world.Grid.Query(prev[i].Position.X, prev[i].Position.Z, CombatTuning.ReactionScanRadius, ref scan, static (int other, ref Scan st) =>
        {
            if (other == st.Self) return;
            ref readonly var a = ref st.Read[other];
            if (a.AttackTech < 0 || a.NextBlow == 0 || a.TargetSlot != st.Self || a.TargetGeneration != st.SelfGeneration) return;
            var tech = st.Techniques[a.AttackTech];
            int strike = a.AttackStart + (int)MathF.Ceiling(tech.StrikeProgress(a.NextBlow) * a.AttackTicks);
            if (strike <= st.Now) return;
            if (st.Me.ReactedSlot == other && st.Me.ReactedSeq == a.AttackSeq && st.Me.ReactedBlow == a.NextBlow) return;
            if (strike < st.Strike) { st.Strike = strike; st.Attacker = other; st.Blow = a.NextBlow; }
        });
        if (scan.Attacker < 0) return;

        var attack = read[scan.Attacker];
        var atkTech = techniques[attack.AttackTech];
        int direction = atkTech.DirectionOf(scan.Blow);
        s.ReactedSlot = scan.Attacker;
        s.ReactedSeq = attack.AttackSeq;
        s.ReactedBlow = scan.Blow;

        var fighter = cold.Fighter!;
        float meleeDefence = Eff(cold, StatsEnumerated.MeleeDefence);
        ulong counter = CombatRolls.BlowCounter(scan.Attacker, attack.AttackSeq, scan.Blow, 0);
        float chanceRoll = CombatRolls.Float(world.Seed, key, CombatRoll.Block, counter);
        float pickRoll = CombatRolls.Float(world.Seed, key, CombatRoll.Reaction, counter);
        float block, dodge = 0;
        if (fighter.CanBlock)
        {
            float d = DefenceFormulas.Defence(meleeDefence, fighter.Weapon!.Stats.DefenceMod, true, fighter.DefenceBonus, Guarding(cold));
            block = DefenceFormulas.BlockChance(d, attack.AttackSkill, combat);
        }
        else
        {
            block = 0;
            float effective = DefenceFormulas.EffectiveDefence(Eff(cold, StatsEnumerated.MartialArts), fighter.EncumbranceFactor, Speed.LegFactor(cold.Medical!), fighter.DefenceBonus, Guarding(cold));
            dodge = DefenceFormulas.BlockChance(effective, attack.AttackSkill, combat, 0, 95);
        }
        int pick = TechniqueChooser.ChooseReaction(techniques, fighter.Kind, fighter.TechniqueCategory, false, fighter.CanBlock, direction, block, dodge, chanceRoll, pickRoll);
        if (pick < 0) return;
        var reaction = techniques[pick];
        int ticks = Ticks(reaction, PlayRate(cold, reaction, attack: false), dt, cold);
        // The reaction is timed so that it is at its "blocked frame" when the blow arrives; with less lead time it starts at once and is less far along.
        int strike = scan.Strike;
        int start = Math.Max(now, strike - (int)MathF.Round(reaction.StrikeProgress(1) * ticks));
        s.ReactTech = pick;
        s.ReactStart = start;
        s.ReactTicks = ticks;
    }
}
