using System.Buffers.Binary;

namespace Meitou.Data.Audio;

/// <summary>
/// Decodes a version 112 <c>HIRC</c> section (docs/formats/wwise-bnk.md#hirc). Each object is u8 type, u32 size, then <c>size</c> bytes
/// starting with its u32 id. A decoded object must use exactly its size; one that does not, or a type that is not decoded, is kept as
/// <see cref="WwiseRawObject"/> (with the error when decoding failed).
/// </summary>
static class WwiseHierarchyReader
{
    public static List<WwiseObject> Read(ReadOnlySpan<byte> hirc, bool decode)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(hirc);
        var objects = new List<WwiseObject>(count);
        int p = 4;
        for (int i = 0; i < count; i++)
        {
            if (p + 9 > hirc.Length) throw new InvalidDataException($"HIRC object {i} header runs past the section");
            var type = (WwiseObjectType)hirc[p];
            int size = BinaryPrimitives.ReadInt32LittleEndian(hirc[(p + 1)..]);
            if (size < 4 || p + 5 + (long)size > hirc.Length) throw new InvalidDataException($"HIRC object {i} runs past the section");
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(hirc[(p + 5)..]);
            var body = hirc.Slice(p + 9, size - 4);
            objects.Add(decode ? Decode(type, id, body) : new WwiseRawObject(type, id, body.ToArray(), null));
            p += 5 + size;
        }
        if (p != hirc.Length) throw new InvalidDataException("HIRC has trailing bytes");
        return objects;
    }

    static WwiseObject Decode(WwiseObjectType type, uint id, ReadOnlySpan<byte> body)
    {
        if (type == WwiseObjectType.Bus) return Bus(id, body);
        var r = new Reader(body);
        try
        {
            WwiseObject? o = type switch
            {
                WwiseObjectType.Event => new WwiseEvent(id, r.Ids(r.U32())),
                WwiseObjectType.Action => Action(ref r, id),
                WwiseObjectType.Sound => new WwiseSound(id, Source(ref r), NodeBase(ref r)),
                WwiseObjectType.ActorMixer => new WwiseActorMixer(id, NodeBase(ref r), r.Ids(r.U32())),
                WwiseObjectType.RandomSequenceContainer => RandomSequence(ref r, id),
                WwiseObjectType.SwitchContainer => Switch(ref r, id),
                WwiseObjectType.LayerContainer => Layer(ref r, id),
                WwiseObjectType.MusicSegment => MusicSegment(ref r, id),
                WwiseObjectType.MusicTrack => MusicTrack(ref r, id),
                WwiseObjectType.MusicSwitch => MusicSwitch(ref r, id),
                WwiseObjectType.MusicRandomSequence => MusicPlaylist(ref r, id),
                _ => null,
            };
            if (o is null) return new WwiseRawObject(type, id, body.ToArray(), null);
            if (r.Remaining != 0) return new WwiseRawObject(type, id, body.ToArray(), $"{r.Remaining} bytes left over");
            return o;
        }
        catch (InvalidDataException e)
        {
            return new WwiseRawObject(type, id, body.ToArray(), e.Message);
        }
    }

    static WwiseAction Action(ref Reader r, uint id)
    {
        ushort actionType = r.U16();
        uint target = r.U32();
        bool isBus = r.U8() != 0;
        var props = Properties(ref r);
        var action = new WwiseAction(id, actionType, target, isBus, props);
        switch ((WwiseActionKind)(actionType >> 8))
        {
            case WwiseActionKind.Play:
                return action with { FadeCurve = r.U8(), BankId = r.U32() };
            case WwiseActionKind.Stop or WwiseActionKind.Pause or WwiseActionKind.Resume:
                return action with { FadeCurve = r.U8(), Exceptions = Exceptions(ref r) };
            case WwiseActionKind.SetPitch or WwiseActionKind.ResetPitch or WwiseActionKind.SetVolume or WwiseActionKind.ResetVolume
                or WwiseActionKind.SetBusVolume or WwiseActionKind.ResetBusVolume or WwiseActionKind.SetLowPass or WwiseActionKind.ResetLowPass:
                return action with
                {
                    FadeCurve = r.U8(), ValueMeaning = r.U8(), Value = r.F32(), ValueMin = r.F32(), ValueMax = r.F32(), Exceptions = Exceptions(ref r),
                };
            case WwiseActionKind.SetState:
                return action with { GroupId = r.U32(), StateId = r.U32() };
            case WwiseActionKind.BypassFx:
                return action with { Bypass = r.U8() != 0, BypassMask = r.U8(), Exceptions = Exceptions(ref r) };
            default:
                throw new InvalidDataException($"Action type {actionType:X4} not decoded");
        }
    }

    static (uint, bool)[] Exceptions(ref Reader r)
    {
        var list = new (uint, bool)[r.Count(r.U32(), 5)];
        for (int i = 0; i < list.Length; i++) list[i] = (r.U32(), r.U8() != 0);
        return list;
    }

    /// <summary>u8 count, the ids, the u32 values; then u8 count, the ids, the (min, max) float pairs.</summary>
    static WwiseProperties Properties(ref Reader r)
    {
        int n = r.U8();
        var ids = r.Bytes(n);
        var values = new Dictionary<byte, uint>(n);
        for (int i = 0; i < n; i++) values[ids[i]] = r.U32();
        int m = r.U8();
        var rangeIds = r.Bytes(m);
        var ranges = new Dictionary<byte, (float, float)>(m);
        for (int i = 0; i < m; i++) ranges[rangeIds[i]] = (r.F32(), r.F32());
        return n == 0 && m == 0 ? WwiseProperties.Empty : new WwiseProperties(values, ranges);
    }

    static WwiseSource Source(ref Reader r)
    {
        uint plugin = r.U32();
        byte stream = r.U8();
        uint source = r.U32(), file = r.U32();
        uint offset = 0, size = 0;
        if (stream == 0)
        {
            offset = r.U32();
            size = r.U32();
        }
        else throw new InvalidDataException($"Stream type {stream} not decoded");
        byte bits = r.U8();
        byte[] parameters = [];
        if ((plugin & 0xF) == 2) parameters = r.Bytes(r.Count(r.U32(), 1));
        return new WwiseSource(plugin, stream, source, file, offset, size, bits, parameters);
    }

    static WwiseNodeBase NodeBase(ref Reader r)
    {
        bool overrideFx = r.U8() != 0;
        int fxCount = r.U8();
        byte bypass = 0;
        var fx = new (byte, uint, bool, bool)[fxCount];
        if (fxCount > 0)
        {
            bypass = r.U8();
            for (int i = 0; i < fxCount; i++) fx[i] = (r.U8(), r.U32(), r.U8() != 0, r.U8() != 0);
        }
        byte unknown = r.U8();
        uint bus = r.U32(), parent = r.U32();
        byte priority = r.U8();
        var props = Properties(ref r);
        var positioning = Positioning(ref r);
        byte auxBits = r.U8();
        uint[] aux = (auxBits & 8) != 0 ? r.Ids(4) : [];
        byte adv = r.U8(), virtualQueue = r.U8();
        ushort maxInstances = r.U16();
        byte below = r.U8(), adv2 = r.U8();
        var states = new WwiseStateGroup[r.Count(r.U32(), 7)];
        for (int i = 0; i < states.Length; i++)
        {
            uint group = r.U32();
            byte sync = r.U8();
            var list = new (uint, uint)[r.Count(r.U16(), 8)];
            for (int j = 0; j < list.Length; j++) list[j] = (r.U32(), r.U32());
            states[i] = new WwiseStateGroup(group, sync, list);
        }
        return new WwiseNodeBase(overrideFx, fx, bypass, unknown, bus, parent, priority, props, positioning, auxBits, aux, adv, virtualQueue,
            maxInstances, below, adv2, states, Rtpcs(ref r));
    }

    static WwisePositioning Positioning(ref Reader r)
    {
        byte bits = r.U8();
        byte bits3D = 0, pathMode = 0;
        uint attenuation = 0;
        int transition = 0;
        WwisePathVertex[] vertices = [];
        (uint, uint)[] paths = [];
        (float, float, float)[] ranges = [];
        if ((bits & 1) != 0 && (bits & 8) != 0)
        {
            bits3D = r.U8();
            attenuation = r.U32();
            if (((bits >> 5) & 3) == 3)
            {
                pathMode = r.U8();
                transition = r.I32();
                vertices = new WwisePathVertex[r.Count(r.U32(), 16)];
                for (int i = 0; i < vertices.Length; i++) vertices[i] = new WwisePathVertex(r.F32(), r.F32(), r.F32(), r.I32());
                paths = new (uint, uint)[r.Count(r.U32(), 20)];
                for (int i = 0; i < paths.Length; i++) paths[i] = (r.U32(), r.U32());
                ranges = new (float, float, float)[paths.Length];
                for (int i = 0; i < ranges.Length; i++) ranges[i] = (r.F32(), r.F32(), r.F32());
            }
        }
        return new WwisePositioning(bits, bits3D, attenuation, pathMode, transition, vertices, paths, ranges);
    }

    static WwiseRtpc[] Rtpcs(ref Reader r)
    {
        var list = new WwiseRtpc[r.Count(r.U16(), 14)];
        for (int i = 0; i < list.Length; i++)
        {
            uint parameter = r.U32();
            byte type = r.U8(), accumulation = r.U8(), param = r.U8();
            uint curve = r.U32();
            byte scaling = r.U8();
            list[i] = new WwiseRtpc(parameter, type, accumulation, param, curve, scaling, Points(ref r, r.U16()));
        }
        return list;
    }

    static WwiseCurvePoint[] Points(ref Reader r, uint count)
    {
        var points = new WwiseCurvePoint[r.Count(count, 12)];
        for (int i = 0; i < points.Length; i++) points[i] = new WwiseCurvePoint(r.F32(), r.F32(), r.U32());
        return points;
    }

    static WwiseRandomSequence RandomSequence(ref Reader r, uint id)
    {
        var node = NodeBase(ref r);
        ushort loop = r.U16(), loopMin = r.U16(), loopMax = r.U16();
        float transition = r.F32(), transitionMin = r.F32(), transitionMax = r.F32();
        ushort avoidRepeat = r.U16();
        byte transitionMode = r.U8(), randomMode = r.U8(), mode = r.U8(), flags = r.U8();
        var children = r.Ids(r.U32());
        var playlist = new (uint, int)[r.Count(r.U16(), 8)];
        for (int i = 0; i < playlist.Length; i++) playlist[i] = (r.U32(), r.I32());
        return new WwiseRandomSequence(id, node, loop, loopMin, loopMax, transition, transitionMin, transitionMax, avoidRepeat, transitionMode,
            randomMode, mode, flags, children, playlist);
    }

    static WwiseSwitchContainer Switch(ref Reader r, uint id)
    {
        var node = NodeBase(ref r);
        byte groupType = r.U8();
        uint group = r.U32(), defaultSwitch = r.U32();
        bool continuous = r.U8() != 0;
        var children = r.Ids(r.U32());
        var switches = new (uint, uint[])[r.Count(r.U32(), 8)];
        for (int i = 0; i < switches.Length; i++) switches[i] = (r.U32(), r.Ids(r.U32()));
        var parameters = new (uint, byte, byte, int, int)[r.Count(r.U32(), 14)];
        for (int i = 0; i < parameters.Length; i++) parameters[i] = (r.U32(), r.U8(), r.U8(), r.I32(), r.I32());
        return new WwiseSwitchContainer(id, node, groupType, group, defaultSwitch, continuous, children, switches, parameters);
    }

    /// <summary>
    /// A bus, partly: u32 parent bus, then the property list without ranges (bus volume is <c>0x05</c>). The rest of the layout is not decoded;
    /// the RTPC list is found by its shape (docs/formats/wwise-bnk.md#buses): the last place where a u16 count of 1..8 is followed by that many
    /// well-formed RTPC entries that end exactly at a known position (the end, or before u16 0 / u32 0 tails seen in the banks).
    /// </summary>
    static WwiseObject Bus(uint id, ReadOnlySpan<byte> body)
    {
        try
        {
            var r = new Reader(body);
            uint parent = r.U32();
            int n = r.U8();
            var ids = r.Bytes(n);
            var values = new Dictionary<byte, uint>(n);
            for (int i = 0; i < n; i++) values[ids[i]] = r.U32();
            var rtpcs = FindRtpcs(body, body.Length - r.Remaining);
            return new WwiseBus(id, parent, new WwiseProperties(values, new Dictionary<byte, (float, float)>()), rtpcs, body.ToArray());
        }
        catch (InvalidDataException e)
        {
            return new WwiseRawObject(WwiseObjectType.Bus, id, body.ToArray(), e.Message);
        }
    }

    static WwiseRtpc[] FindRtpcs(ReadOnlySpan<byte> body, int from)
    {
        for (int start = body.Length - 2; start >= from; start--)
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(body[start..]);
            if (count is < 1 or > 8) continue;
            var r = new Reader(body[start..]);
            try
            {
                var list = Rtpcs(ref r);
                if (list.All(x => x.Points.Length is > 0 and < 64 && x.ParameterId < 64 && x.RtpcType == 0 && x.Points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y))))
                    return list;
            }
            catch (InvalidDataException) { }
        }
        return [];
    }

    static WwiseLayerContainer Layer(ref Reader r, uint id)
    {
        var node = NodeBase(ref r);
        var children = r.Ids(r.U32());
        var layers = new WwiseLayer[r.Count(r.U32(), 15)];
        for (int i = 0; i < layers.Length; i++)
        {
            uint layerId = r.U32();
            var rtpcs = Rtpcs(ref r);
            uint crossfade = r.U32();
            byte crossfadeType = r.U8();
            var assoc = new (uint, WwiseCurvePoint[])[r.Count(r.U32(), 8)];
            for (int j = 0; j < assoc.Length; j++) assoc[j] = (r.U32(), Points(ref r, r.U32()));
            layers[i] = new WwiseLayer(layerId, rtpcs, crossfade, crossfadeType, assoc);
        }
        return new WwiseLayerContainer(id, node, children, layers);
    }

    /// <summary>
    /// What all four music types share after their own leading fields: u8 flags, node base, child ids, grid (f64 period, f64 offset,
    /// f32 tempo, u8 beats, u8 beat unit, u8 flags) and stingers (none in Kenshi; not decoded).
    /// </summary>
    static WwiseMusicNode MusicNode(ref Reader r)
    {
        byte flags = r.U8();
        var node = NodeBase(ref r);
        var children = r.Ids(r.U32());
        var meter = new WwiseMeter(r.F64(), r.F64(), r.F32(), r.U8(), r.U8(), r.U8());
        uint stingers = r.U32();
        if (stingers != 0) throw new InvalidDataException($"{stingers} stingers, not decoded");
        return new WwiseMusicNode(flags, node, children, meter);
    }

    static WwiseFade Fade(ref Reader r) => new(r.I32(), r.U32(), r.I32());

    /// <summary>Transition rules of a music switch or playlist. A rule with a transition segment is not decoded (none in Kenshi).</summary>
    static WwiseMusicTransition[] Transitions(ref Reader r)
    {
        var rules = new WwiseMusicTransition[r.Count(r.U32(), 64)];
        for (int i = 0; i < rules.Length; i++)
        {
            var sources = r.Ids(r.U32());
            var destinations = r.Ids(r.U32());
            var source = new WwiseTransitionSource(Fade(ref r), r.U32(), r.U32(), r.U8() != 0);
            var destination = new WwiseTransitionDestination(Fade(ref r), r.U32(), r.U32(), r.U16(), r.U8() != 0, r.U8() != 0);
            if (r.U8() != 0) throw new InvalidDataException("Transition segment not decoded");
            rules[i] = new WwiseMusicTransition(sources, destinations, source, destination);
        }
        return rules;
    }

    static WwiseMusicSegment MusicSegment(ref Reader r, uint id)
    {
        var music = MusicNode(ref r);
        double duration = r.F64();
        var markers = new WwiseMarker[r.Count(r.U32(), 16)];
        for (int i = 0; i < markers.Length; i++)
        {
            uint markerId = r.U32();
            double position = r.F64();
            markers[i] = new WwiseMarker(markerId, position, System.Text.Encoding.ASCII.GetString(r.Bytes(r.Count(r.U32(), 1))));
        }
        return new WwiseMusicSegment(id, music, duration, markers);
    }

    /// <summary>A music track. Track types other than 0 (random, sequence, switch tracks) are not decoded (none in Kenshi).</summary>
    static WwiseMusicTrack MusicTrack(ref Reader r, uint id)
    {
        byte flags = r.U8();
        var sources = new WwiseSource[r.Count(r.U32(), 15)];
        for (int i = 0; i < sources.Length; i++) sources[i] = Source(ref r);
        var clips = new WwiseMusicClip[r.Count(r.U32(), 40)];
        for (int i = 0; i < clips.Length; i++) clips[i] = new WwiseMusicClip(r.U32(), r.U32(), r.F64(), r.F64(), r.F64(), r.F64());
        uint subTracks = r.U32();
        var automation = new WwiseClipAutomation[r.Count(r.U32(), 12)];
        for (int i = 0; i < automation.Length; i++) automation[i] = new WwiseClipAutomation(r.U32(), r.U32(), Points(ref r, r.U32()));
        var node = NodeBase(ref r);
        byte trackType = r.U8();
        if (trackType != 0) throw new InvalidDataException($"Track type {trackType} not decoded");
        return new WwiseMusicTrack(id, flags, sources, clips, subTracks, automation, node, trackType, r.I32());
    }

    static WwiseMusicSwitch MusicSwitch(ref Reader r, uint id)
    {
        var music = MusicNode(ref r);
        var transitions = Transitions(ref r);
        bool continuePlayback = r.U8() != 0;
        int depth = r.Count(r.U32(), 5);
        var groups = r.Ids((uint)depth);
        var arguments = new (uint, byte)[depth];
        for (int i = 0; i < depth; i++) arguments[i] = (groups[i], r.U8());
        int treeSize = r.Count(r.U32(), 1);
        byte mode = r.U8();
        if (treeSize % 12 != 0 || treeSize == 0) throw new InvalidDataException($"Decision tree of {treeSize} bytes");
        var flat = new (uint Key, uint Value, ushort Weight, ushort Probability)[treeSize / 12];
        for (int i = 0; i < flat.Length; i++) flat[i] = (r.U32(), r.U32(), r.U16(), r.U16());
        return new WwiseMusicSwitch(id, music, transitions, continuePlayback, arguments, mode, DecisionNode(flat, 0, 0, depth));
    }

    /// <summary>
    /// The decision tree is stored flat, 12 bytes a node: u32 key, then at the last depth u32 audio node id, above it u16 first child index and
    /// u16 child count; then u16 weight, u16 probability. The root (index 0) is above the first depth.
    /// </summary>
    static WwiseDecisionNode DecisionNode((uint Key, uint Value, ushort Weight, ushort Probability)[] flat, int index, int level, int depth)
    {
        var (key, value, weight, probability) = flat[index];
        if (level == depth) return new WwiseDecisionNode(key, value, weight, probability, []);
        int first = (int)(value & 0xFFFF), count = (int)(value >> 16);
        if (first + count > flat.Length || first <= index) throw new InvalidDataException($"Decision node {index} children {first}+{count} out of range");
        var children = new WwiseDecisionNode[count];
        for (int i = 0; i < count; i++) children[i] = DecisionNode(flat, first + i, level + 1, depth);
        return new WwiseDecisionNode(key, 0, weight, probability, children);
    }

    static WwiseMusicPlaylist MusicPlaylist(ref Reader r, uint id)
    {
        var music = MusicNode(ref r);
        var transitions = Transitions(ref r);
        int count = r.Count(r.U32(), 30);
        int read = 0;
        var root = PlaylistItem(ref r, ref read, count);
        if (read != count) throw new InvalidDataException($"Playlist tree holds {read} of {count} items");
        return new WwiseMusicPlaylist(id, music, transitions, root);
    }

    /// <summary>
    /// The playlist is stored depth-first: per item u32 segment id (0 for a group), u32 item id, u32 child count, u32 playlist type,
    /// s16 loop count, u16, u16, u32 weight, u16 avoid-repeat count, u8 using weight, u8 shuffle; then its children.
    /// </summary>
    static WwisePlaylistItem PlaylistItem(ref Reader r, ref int read, int count)
    {
        if (++read > count) throw new InvalidDataException("Playlist tree holds more items than its count");
        uint segment = r.U32(), itemId = r.U32();
        uint childCount = r.U32(), type = r.U32();
        short loop = (short)r.U16();
        ushort loopMin = r.U16(), loopMax = r.U16();
        uint weight = r.U32();
        ushort avoid = r.U16();
        bool usingWeight = r.U8() != 0, shuffle = r.U8() != 0;
        if (childCount > count - read) throw new InvalidDataException($"Playlist item {itemId:X8} has {childCount} children, more than the count");
        var children = new WwisePlaylistItem[childCount];
        for (int i = 0; i < children.Length; i++) children[i] = PlaylistItem(ref r, ref read, count);
        return new WwisePlaylistItem(segment, itemId, type, loop, loopMin, loopMax, weight, avoid, usingWeight, shuffle, children);
    }

    ref struct Reader(ReadOnlySpan<byte> span)
    {
        readonly ReadOnlySpan<byte> span = span;
        int p;

        public readonly int Remaining => span.Length - p;

        ReadOnlySpan<byte> Take(int n)
        {
            if (n > span.Length - p) throw new InvalidDataException($"Read of {n} bytes at {p} runs past the object ({span.Length} bytes)");
            var s = span.Slice(p, n);
            p += n;
            return s;
        }

        public byte U8() => Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
        public double F64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
        public byte[] Bytes(int n) => Take(n).ToArray();

        /// <summary>A count, checked against what is left (each element at least <paramref name="minSize"/> bytes).</summary>
        public readonly int Count(uint n, int minSize)
        {
            if (n > (uint)(Remaining / minSize)) throw new InvalidDataException($"Count {n} at {p} is more than the object holds");
            return (int)n;
        }

        public uint[] Ids(uint n)
        {
            var ids = new uint[Count(n, 4)];
            for (int i = 0; i < ids.Length; i++) ids[i] = U32();
            return ids;
        }
    }
}
