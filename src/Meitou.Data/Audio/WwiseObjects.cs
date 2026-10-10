namespace Meitou.Data.Audio;

/// <summary>HIRC object types (docs/formats/wwise-bnk.md#hirc). The numbers are the format's; the names describe what each holds.</summary>
public enum WwiseObjectType : byte
{
    State = 1,
    Sound = 2,
    Action = 3,
    Event = 4,
    RandomSequenceContainer = 5,
    SwitchContainer = 6,
    ActorMixer = 7,
    Bus = 8,
    LayerContainer = 9,
    MusicSegment = 10,
    MusicTrack = 11,
    MusicSwitch = 12,
    MusicRandomSequence = 13,
    Attenuation = 14,
    DialogueEvent = 15,
    FeedbackBus = 16,
    FeedbackNode = 17,
    FxShareSet = 18,
    FxCustom = 19,
    AuxBus = 20,
}

/// <summary>One HIRC object. Types the reader does not decode are kept as <see cref="WwiseRawObject"/>.</summary>
public abstract record WwiseObject(WwiseObjectType Type, uint Id);

/// <summary>An object kept undecoded: its type is not parsed (music, buses, effects), or its body did not match the documented layout.</summary>
public sealed record WwiseRawObject(WwiseObjectType Type, uint Id, byte[] Body, string? Error) : WwiseObject(Type, Id);

/// <summary>An event: the actions it runs, in order. Its id is the hash of its name (<see cref="WwiseHash"/>).</summary>
public sealed record WwiseEvent(uint Id, uint[] Actions) : WwiseObject(WwiseObjectType.Event, Id);

/// <summary>
/// An action. <see cref="Kind"/> is the high byte of the action type (what it does), <see cref="Scope"/> the low byte (which objects:
/// 1 the target, 2 the target in the event's game object, 3 all, 4 all in the game object, ...). Kind-specific fields are set where they apply.
/// </summary>
public sealed record WwiseAction(uint Id, ushort ActionType, uint Target, bool TargetIsBus, WwiseProperties Properties) : WwiseObject(WwiseObjectType.Action, Id)
{
    public WwiseActionKind Kind => (WwiseActionKind)(ActionType >> 8);
    public int Scope => ActionType & 0xFF;
    public byte FadeCurve { get; init; }
    /// <summary>Play: the bank holding the target.</summary>
    public uint BankId { get; init; }
    /// <summary>Set/reset pitch, volume, bus volume, low-pass: 1 = an offset, 2 = an absolute value.</summary>
    public byte ValueMeaning { get; init; }
    public float Value { get; init; }
    public float ValueMin { get; init; }
    public float ValueMax { get; init; }
    /// <summary>Set state: the state group and the state.</summary>
    public uint GroupId { get; init; }
    public uint StateId { get; init; }
    /// <summary>Bypass effects: whether to bypass, and the effect slot mask.</summary>
    public bool Bypass { get; init; }
    public byte BypassMask { get; init; }
    /// <summary>Objects excluded from a stop or set-value action over all objects: (id, is a bus).</summary>
    public (uint Id, bool IsBus)[] Exceptions { get; init; } = [];
}

/// <summary>The action kinds seen in Kenshi's banks (high byte of the action type).</summary>
public enum WwiseActionKind : byte
{
    Stop = 0x01,
    Pause = 0x02,
    Resume = 0x03,
    Play = 0x04,
    SetPitch = 0x08,
    ResetPitch = 0x09,
    SetVolume = 0x0A,
    ResetVolume = 0x0B,
    SetBusVolume = 0x0C,
    ResetBusVolume = 0x0D,
    SetLowPass = 0x0E,
    ResetLowPass = 0x0F,
    SetState = 0x12,
    BypassFx = 0x1A,
}

/// <summary>
/// A node's property list: values by property id (raw 32 bits; most are floats, see <see cref="WwisePropertyId"/>), and the random ranges
/// added to some of them each time the node plays.
/// </summary>
public sealed record WwiseProperties(IReadOnlyDictionary<byte, uint> Values, IReadOnlyDictionary<byte, (float Min, float Max)> Ranges)
{
    public static readonly WwiseProperties Empty = new(new Dictionary<byte, uint>(), new Dictionary<byte, (float, float)>());

    public float Float(byte id, float fallback = 0) => Values.TryGetValue(id, out var v) ? BitConverter.UInt32BitsToSingle(v) : fallback;
    public int Int(byte id, int fallback = 0) => Values.TryGetValue(id, out var v) ? (int)v : fallback;
}

/// <summary>Property ids whose meaning is established (docs/formats/wwise-bnk.md#properties).</summary>
public static class WwisePropertyId
{
    /// <summary>Volume offset in dB (float).</summary>
    public const byte Volume = 0x00;
    /// <summary>Pitch offset in cents (float).</summary>
    public const byte Pitch = 0x02;
    /// <summary>Low-pass filter amount 0..100 (float).</summary>
    public const byte LowPass = 0x03;
    /// <summary>On actions: the delay before the action runs, in ms (int; 6000 on some actions, Observed).</summary>
    public const byte Delay = 0x0E;
    /// <summary>On actions: the fade time, in ms (int).</summary>
    public const byte FadeTime = 0x0F;
    /// <summary>On buses: the bus volume in dB (float).</summary>
    public const byte BusVolume = 0x05;
    /// <summary>Loop count: 0 = forever, 1 = once (int).</summary>
    public const byte Loop = 0x3A;
}

/// <summary>
/// Where a sound's audio is: an embedded WEM (<see cref="StreamType"/> 0), <see cref="FileId"/> the bank holding it, <see cref="Offset"/> its offset in the
/// bank file (not in DATA) and <see cref="Size"/> its length, both equal to the bank's DIDX entry for <see cref="SourceId"/>.
/// </summary>
public sealed record WwiseSource(uint PluginId, byte StreamType, uint SourceId, uint FileId, uint Offset, uint Size, byte SourceBits, byte[] PluginParameters)
{
    /// <summary>The codec or source plugin: 0x00040001 Wwise Vorbis, 0x00650002 the silence source.</summary>
    public bool IsVorbis => PluginId == 0x00040001;
}

/// <summary>A point of an RTPC curve or a layer's crossfade curve: x, y and the interpolation to the next point.</summary>
public readonly record struct WwiseCurvePoint(float X, float Y, uint Interpolation);

/// <summary>A game parameter driving one of a node's properties through a curve.</summary>
public sealed record WwiseRtpc(uint GameParameterId, byte RtpcType, byte Accumulation, byte ParameterId, uint CurveId, byte Scaling, WwiseCurvePoint[] Points);

/// <summary>A state group whose states change some of the node's properties.</summary>
public sealed record WwiseStateGroup(uint GroupId, byte SyncType, (uint StateId, uint StateObjectId)[] States);

/// <summary>A point on a 3D path, relative to the listener, with the time to reach it (ms).</summary>
public readonly record struct WwisePathVertex(float X, float Y, float Z, int Duration);

/// <summary>
/// A node's positioning when it overrides its parent's: <see cref="Bits"/> as stored; bit 3 set means 3D with an attenuation, and a 3D type of 3
/// (bits 5-6) adds a set of paths the sound moves along around the listener.
/// </summary>
public sealed record WwisePositioning(byte Bits, byte Bits3D, uint AttenuationId, byte PathMode, int TransitionTime, WwisePathVertex[] Vertices,
    (uint FirstVertex, uint VertexCount)[] Paths, (float X, float Y, float Z)[] PathRanges)
{
    public bool OverridesParent => (Bits & 1) != 0;
    public bool Is3D => (Bits & 8) != 0;
    public int PositionType3D => (Bits >> 5) & 3;
}

/// <summary>The parameters every sound and container carries (docs/formats/wwise-bnk.md#node-base).</summary>
public sealed record WwiseNodeBase(
    bool OverrideParentFx, (byte Index, uint FxId, bool IsShareSet, bool IsRendered)[] Effects, byte FxBypass, byte Unknown,
    uint OverrideBusId, uint ParentId, byte PriorityBits, WwiseProperties Properties, WwisePositioning Positioning,
    byte AuxBits, uint[] AuxBuses, byte AdvancedBits, byte VirtualQueueBehaviour, ushort MaxInstances, byte BelowThresholdBehaviour, byte AdvancedBits2,
    WwiseStateGroup[] StateGroups, WwiseRtpc[] Rtpcs);

/// <summary>A sound: one source (a WEM) and its node parameters.</summary>
public sealed record WwiseSound(uint Id, WwiseSource Source, WwiseNodeBase Node) : WwiseObject(WwiseObjectType.Sound, Id);

/// <summary>An actor-mixer: a grouping node whose parameters its children inherit.</summary>
public sealed record WwiseActorMixer(uint Id, WwiseNodeBase Node, uint[] Children) : WwiseObject(WwiseObjectType.ActorMixer, Id);

/// <summary>
/// A random or sequence container: plays one child at a time from <see cref="Playlist"/> (weights out of 50000 = 1.0), with a transition
/// between them. <see cref="Mode"/> 0 = random, 1 = sequence; <see cref="LoopCount"/> 0 = forever.
/// </summary>
public sealed record WwiseRandomSequence(uint Id, WwiseNodeBase Node, ushort LoopCount, ushort LoopModMin, ushort LoopModMax,
    float TransitionTime, float TransitionTimeModMin, float TransitionTimeModMax, ushort AvoidRepeatCount, byte TransitionMode, byte RandomMode,
    byte Mode, byte Flags, uint[] Children, (uint Id, int Weight)[] Playlist) : WwiseObject(WwiseObjectType.RandomSequenceContainer, Id);

/// <summary>A switch container: plays the children assigned to the current state or switch of <see cref="GroupId"/>.</summary>
public sealed record WwiseSwitchContainer(uint Id, WwiseNodeBase Node, byte GroupType, uint GroupId, uint DefaultSwitch, bool ContinuousValidation,
    uint[] Children, (uint SwitchId, uint[] Nodes)[] Switches, (uint NodeId, byte Bits, byte Bits2, int FadeOut, int FadeIn)[] NodeParameters)
    : WwiseObject(WwiseObjectType.SwitchContainer, Id);

/// <summary>A layer of a layer container: its children's volume follows <see cref="CrossfadeParameterId"/> through each child's curve.</summary>
public sealed record WwiseLayer(uint Id, WwiseRtpc[] Rtpcs, uint CrossfadeParameterId, byte CrossfadeParameterType, (uint ChildId, WwiseCurvePoint[] Curve)[] Associations);

/// <summary>A layer container (blend container): plays all its children at once.</summary>
public sealed record WwiseLayerContainer(uint Id, WwiseNodeBase Node, uint[] Children, WwiseLayer[] Layers) : WwiseObject(WwiseObjectType.LayerContainer, Id);

/// <summary>
/// A bus, partly decoded: its parent bus, its properties (bus volume <c>0x05</c> in dB) and its RTPCs (the user volume sliders), found by
/// shape in the undecoded rest (<see cref="Body"/>).
/// </summary>
public sealed record WwiseBus(uint Id, uint ParentBusId, WwiseProperties Properties, WwiseRtpc[] Rtpcs, byte[] Body) : WwiseObject(WwiseObjectType.Bus, Id);
