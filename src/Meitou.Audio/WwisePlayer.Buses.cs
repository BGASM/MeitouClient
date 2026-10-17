using Meitou.Data.Audio;

namespace Meitou.Audio;

/// <summary>
/// Buses (docs/audio.md#buses-pause-and-reverb): the bus gain chain with action-driven offsets that fade (the pause ducking), and the auxiliary
/// buses with their reverb: sounds send a copy of their signal to the aux buses their node lists, each aux bus runs the effects in its
/// un-bypassed slots (the banks' bypass actions switch the outdoor and the indoor rooms on and off) and adds the result to the mix.
/// </summary>
public sealed partial class WwisePlayer
{
    // Offsets set by actions, keyed by (target node or bus, game object; 0 for the global scope), fading to their target value.
    readonly OffsetTable volumeOffsets = new(), pitchOffsets = new(), lowPassOffsets = new(), busVolumes = new();
    readonly Dictionary<uint, AuxBusState> auxBuses = [];

    /// <summary>
    /// A value in dB (or cents, or low-pass units) set by an action: it moves to its target over the action's fade time in a straight line and an
    /// entry that has fallen back to 0 is dropped. Entries are looked up by (target, game object).
    /// </summary>
    sealed class OffsetTable
    {
        sealed class Entry
        {
            public float Value, Target, Rate;
        }

        readonly Dictionary<(uint Id, ulong Object), Entry> entries = [];

        public bool IsEmpty => entries.Count == 0;

        public void Set(uint id, ulong gameObject, float target, float seconds)
        {
            if (!entries.TryGetValue((id, gameObject), out var e)) entries[(id, gameObject)] = e = new Entry();
            e.Target = target;
            if (seconds <= 0) e.Value = target;
            e.Rate = seconds > 0 ? Math.Abs(target - e.Value) / seconds : 0;
        }

        public void Reset(uint id, ulong gameObject, float seconds)
        {
            if (!entries.TryGetValue((id, gameObject), out var e)) return;
            e.Target = 0;
            if (seconds <= 0) e.Value = 0;
            e.Rate = seconds > 0 ? Math.Abs(e.Value) / seconds : 0;
        }

        public void Advance(double dt)
        {
            if (entries.Count == 0) return;
            List<(uint, ulong)>? gone = null;
            foreach (var (key, e) in entries)
            {
                if (e.Value != e.Target)
                {
                    float step = e.Rate * (float)dt;
                    e.Value = e.Value < e.Target ? Math.Min(e.Target, e.Value + step) : Math.Max(e.Target, e.Value - step);
                }
                if (e.Value == 0 && e.Target == 0) (gone ??= []).Add(key);
            }
            if (gone is not null) foreach (var key in gone) entries.Remove(key);
        }

        /// <summary>The global offset plus the one for the object.</summary>
        public float Get(uint id, ulong gameObject)
        {
            if (entries.Count == 0) return 0;
            float sum = entries.TryGetValue((id, 0), out var g) ? g.Value : 0;
            if (gameObject != 0 && entries.TryGetValue((id, gameObject), out var o)) sum += o.Value;
            return sum;
        }

        public void ForgetObject(ulong gameObject)
        {
            foreach (var key in entries.Keys.Where(k => k.Object == gameObject).ToList()) entries.Remove(key);
        }
    }

    /// <summary>An auxiliary bus: the stereo input its senders add to, the bypass state of its effect slots and the reverbs it runs.</summary>
    sealed class AuxBusState(WwiseBus bus)
    {
        public readonly WwiseBus Bus = bus;
        /// <summary>Bit n set: the effect in slot n is bypassed (starts as the bank's, changed by bypass actions).</summary>
        public byte Bypass = bus.FxBypass;
        public readonly Dictionary<byte, RoomReverb> Units = [];
        public float[] Input = [];
        /// <summary>Senders added to <see cref="Input"/> in the block being mixed.</summary>
        public bool Received;

        public float[] Prepare(int frames)
        {
            if (Input.Length < frames * 2) Input = new float[frames * 2];
            Received = true;
            return Input;
        }
    }

    /// <summary>The indoor auxiliary bus (two RoomVerb slots: 0 the big room, 1 the small one) and the outdoor one (slot 0), both bypassed until a bank event enables them.</summary>
    public const uint IndoorAuxBus = 0x23F4785D, OutdoorAuxBus = 0x5E1D37D4;

    /// <summary>
    /// Bypasses or enables effect slots of an auxiliary bus directly, as the banks' bypass actions do (<c>Enter_Building</c>, a biome's <c>Enter_</c> event):
    /// <paramref name="slots"/> bits 0-3 are the slots, bit 4 means all. A bypassed reverb takes no more input and its tail rings out.
    /// </summary>
    public void SetAuxBypass(uint auxBus, bool bypass, byte slots = 0x10) => commands.Enqueue(() => ApplyBypass(auxBus, bypass, slots));

    /// <summary>The state of an auxiliary bus (made on first use); null when the bus is not a known aux bus (its bank is not loaded).</summary>
    AuxBusState? AuxBus(uint id)
    {
        if (auxBuses.TryGetValue(id, out var state)) return state;
        if (!objects.TryGetValue(id, out var o) || o is not WwiseBus { IsAux: true } bus) return null;
        return auxBuses[id] = new AuxBusState(bus);
    }

    /// <summary>
    /// A bypass action on a bus: sets or clears the bypass of the slots in <paramref name="mask"/> (bits 0-3 are the slots, bit 4 means all of them).
    /// </summary>
    void ApplyBypass(uint busId, bool bypass, byte mask)
    {
        if (AuxBus(busId) is not { } aux) return;
        byte slots = (byte)((mask & 0x10) != 0 ? 0x0F : mask & 0x0F);
        aux.Bypass = (byte)(bypass ? aux.Bypass | slots : aux.Bypass & ~slots);
    }

    /// <summary>
    /// The linear gain of a bus and its parents: bus volume (dB), the offsets set by actions and the volume curves on the user volume parameters
    /// (the sliders, which store gain - 1 as the RTPC volume curves).
    /// </summary>
    float BusGain(uint busId, ulong gameObject)
    {
        float db = 0, linear = 1;
        for (int guard = 0; busId != 0 && guard < 16 && objects.TryGetValue(busId, out var o) && o is WwiseBus bus; guard++)
        {
            db += bus.Properties.Float(WwisePropertyId.BusVolume) + busVolumes.Get(bus.Id, gameObject);
            foreach (var rtpc in bus.Rtpcs)
            {
                if (rtpc.ParameterId != WwisePropertyId.Volume) continue;
                float y = WwiseCurves.Evaluate(rtpc.Points, Parameter(rtpc.GameParameterId, gameObject));
                if (rtpc.Scaling == 2) linear *= Math.Max(0, 1 + y);
                else db += y;
            }
            busId = bus.ParentBusId;
        }
        return WwiseCurves.DbToGain(db) * linear;
    }

    /// <summary>
    /// Runs the effects of every auxiliary bus that received sound (or whose tail still rings) and adds their output to <paramref name="mix"/>.
    /// Only RoomVerb is implemented; the bus's other effects (none on an aux bus in Kenshi) are skipped. Slots run side by side, not in series.
    /// </summary>
    void MixAuxBuses(float[] mix, int frames)
    {
        foreach (var aux in auxBuses.Values)
        {
            float busGain = BusGain(aux.Bus.Id, 0);
            foreach (var slot in aux.Bus.Effects)
            {
                if (!objects.TryGetValue(slot.EffectId, out var o) || o is not WwiseEffect { IsRoomVerb: true } effect) continue;
                if (!aux.Units.TryGetValue(slot.Index, out var unit)) aux.Units[slot.Index] = unit = new RoomReverb(outputRate);
                bool enabled = (aux.Bypass & (1 << slot.Index)) == 0;
                unit.Enabled = enabled;
                if (!aux.Received && unit.TailDead) continue;
                float level = ConfigureReverb(unit, effect);
                // A block with no senders still runs the tail through; the input is silence (cleared after each block).
                if (aux.Input.Length < frames * 2) aux.Input = new float[frames * 2];
                unit.Process(aux.Input, mix, frames, level * busGain);
            }
            if (aux.Received) Array.Clear(aux.Input, 0, frames * 2);
            aux.Received = false;
        }
    }

    /// <summary>
    /// Sets a reverb from its effect's decoded values and the current game parameters, and returns the wet level (linear). The level is the
    /// root-sum-square of the effect's two output levels (words 18 and 19, which one is the early reflections and which the late reverb is
    /// <b>Unknown</b>), each scaled by the RTPC curve on its parameter (60 and 62, Observed: the outdoor room fades with the camera's height); the
    /// RTPC on parameter 16 (25 to 175 for the outdoor room) is read as the pre-delay in ms.
    /// </summary>
    float ConfigureReverb(RoomReverb unit, WwiseEffect effect)
    {
        float preDelay = 0, gain18 = 1, gain19 = 1;
        foreach (var rtpc in effect.Rtpcs)
        {
            float y = WwiseCurves.Evaluate(rtpc.Points, Parameter(rtpc.GameParameterId));
            switch (rtpc.ParameterId)
            {
                case 16: preDelay = y; break;
                case 60: gain18 *= rtpc.Scaling == 2 ? Math.Max(0, 1 + y) : y; break;
                case 62: gain19 *= rtpc.Scaling == 2 ? Math.Max(0, 1 + y) : y; break;
            }
        }
        unit.Configure(effect.RoomVerbValue(0), effect.RoomVerbValue(1), effect.RoomVerbValue(2), effect.RoomVerbValue(3), preDelay);
        float a = WwiseCurves.DbToGain(effect.RoomVerbValue(18)) * gain18, b = WwiseCurves.DbToGain(effect.RoomVerbValue(19)) * gain19;
        return MathF.Sqrt(a * a + b * b);
    }
}
