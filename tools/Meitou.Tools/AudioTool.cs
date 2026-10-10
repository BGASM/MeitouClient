using System.Text;
using Meitou.Content;
using Meitou.Data.Audio;

/// <summary>Surveys and dumps of the install's Wwise banks (docs/formats/wwise-bnk.md).</summary>
static class AudioTool
{
    static string AudioDirectory(GameInstall install) => Path.Combine(install.DataDirectory, "audio");

    public static int Run(GameInstall install, string[] args)
    {
        if (args.Length == 0) return Survey(install);
        var path = File.Exists(args[0]) ? args[0] : Path.Combine(AudioDirectory(install), args[0].EndsWith(".bnk", StringComparison.OrdinalIgnoreCase) ? args[0] : args[0] + ".bnk");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No bank {args[0]}");
            return 1;
        }
        var bank = WwiseBank.ReadFile(path);
        int wav = Array.IndexOf(args, "--wav");
        if (wav >= 0 && wav + 1 < args.Length) return ExportWav(install, bank, args[wav + 1], args.Length > wav + 2 ? args[wav + 2] : null);
        if (args.Contains("--decode")) return DecodeAll(install, bank);
        Dump(bank, args.Contains("--media"));
        return 0;
    }

    static WwiseCodebookLibrary? Library(GameInstall install)
    {
        var exe = Path.Combine(install.Root, "kenshi_x64.exe");
        var library = File.Exists(exe) ? WwiseCodebookLibrary.FromExecutable(exe) : null;
        if (library is null) Console.Error.WriteLine($"No Wwise codebook library found in {exe}");
        return library;
    }

    /// <summary>Converts every Vorbis WEM of the bank and reports failures and sample counts.</summary>
    static int DecodeAll(GameInstall install, WwiseBank bank)
    {
        if (Library(install) is not { } library) return 1;
        Console.WriteLine($"codebook library: {library.Count} codebooks");
        int failed = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        double seconds = 0;
        foreach (var m in bank.Media)
        {
            try
            {
                var w = WwiseVorbis.Read(bank.MediaBytes(m));
                var pcm = w.Decode(library);
                seconds += (double)w.SampleCount / w.SampleRate;
                int frames = pcm.Length / w.Channels;
                float peak = pcm.Length == 0 ? 0 : pcm.Max(MathF.Abs);
                if (frames != w.SampleCount || peak is 0 or > 4) Console.WriteLine($"  {m.Id:X8} frames {frames} of {w.SampleCount}, peak {peak:0.###}");
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException)
            {
                failed++;
                Console.WriteLine($"  {m.Id:X8} FAIL {e.Message}");
            }
        }
        Console.WriteLine($"{bank.Media.Count - failed} of {bank.Media.Count} decoded, {seconds:0.0} s of audio in {watch.Elapsed.TotalSeconds:0.00} s");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Writes WEMs of the bank (one id, or all) as 16-bit WAV files into a folder.</summary>
    static int ExportWav(GameInstall install, WwiseBank bank, string folder, string? id)
    {
        if (Library(install) is not { } library) return 1;
        Directory.CreateDirectory(folder);
        foreach (var m in bank.Media.Where(m => id is null || m.Id.ToString("X8").Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            var w = WwiseVorbis.Read(bank.MediaBytes(m));
            var pcm = w.Decode(library);
            var file = Path.Combine(folder, $"{m.Id:X8}.wav");
            WriteWav(file, pcm, w.Channels, w.SampleRate);
            Console.WriteLine($"{file}: {pcm.Length / w.Channels} frames, {w.Channels} ch, {w.SampleRate} Hz");
        }
        return 0;
    }

    static void WriteWav(string file, float[] pcm, int channels, int rate)
    {
        using var s = new BinaryWriter(File.Create(file));
        s.Write("RIFF"u8);
        s.Write(36 + pcm.Length * 2);
        s.Write("WAVEfmt "u8);
        s.Write(16);
        s.Write((short)1);
        s.Write((short)channels);
        s.Write(rate);
        s.Write(rate * channels * 2);
        s.Write((short)(channels * 2));
        s.Write((short)16);
        s.Write("data"u8);
        s.Write(pcm.Length * 2);
        foreach (var x in pcm) s.Write((short)Math.Clamp(MathF.Round(x * 32767), -32768, 32767));
    }

    /// <summary>Every bank: version, sections, object types, decode failures, media and references that do not resolve.</summary>
    static int Survey(GameInstall install)
    {
        int failures = 0;
        var allMedia = new Dictionary<uint, string>();
        var banks = Directory.GetFiles(AudioDirectory(install), "*.bnk").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => (Name: Path.GetFileName(f), Bank: WwiseBank.ReadFile(f))).ToList();
        foreach (var (name, bank) in banks)
            foreach (var m in bank.Media) allMedia.TryAdd(m.Id, name);
        foreach (var (name, bank) in banks)
        {
            var types = bank.Objects.GroupBy(o => o.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}");
            Console.WriteLine($"{name,-28} v{bank.Version} {string.Join(' ', bank.Sections.Select(s => $"{s.Tag}={s.Length}"))}");
            Console.WriteLine($"    {string.Join(", ", types)}");
            if (bank.Version != WwiseBank.DecodedVersion) continue;
            foreach (var bad in bank.Objects.OfType<WwiseRawObject>().Where(o => o.Error is not null))
            {
                failures++;
                Console.WriteLine($"    FAIL {bad.Type} {bad.Id:X8}: {bad.Error}");
            }
            var sounds = bank.Objects.OfType<WwiseSound>().ToList();
            var external = sounds.Where(s => s.Source.IsVorbis && bank.FindMedia(s.Source.SourceId) is null)
                .Select(s => allMedia.TryGetValue(s.Source.SourceId, out var other) ? other : "nowhere").GroupBy(x => x).Select(g => $"{g.Key} x{g.Count()}").ToList();
            var unused = bank.Media.Count(m => !sounds.Any(s => s.Source.SourceId == m.Id));
            var misplaced = sounds.Count(s => bank.FindMedia(s.Source.SourceId) is { } m && (m.Offset != s.Source.Offset || m.Length != s.Source.Size));
            var dangling = Dangling(bank).ToList();
            if (external.Count > 0) Console.WriteLine($"    sounds with media in another bank: {string.Join(", ", external)}");
            if (unused > 0) Console.WriteLine($"    media no sound uses: {unused}");
            if (misplaced > 0) Console.WriteLine($"    sounds whose offset/size differ from DIDX: {misplaced}");
            if (dangling.Count > 0) Console.WriteLine($"    references outside the bank: {string.Join(", ", dangling.GroupBy(d => d.Kind).Select(g => $"{g.Key} x{g.Count()}"))}");
        }
        Console.WriteLine(failures == 0 ? "All version 112 objects decoded." : $"{failures} objects failed to decode.");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Child, parent, playlist and action-target references that do not resolve inside the bank.</summary>
    public static IEnumerable<(string Kind, uint Id)> Dangling(WwiseBank bank)
    {
        foreach (var o in bank.Objects)
        {
            switch (o)
            {
                case WwiseEvent e:
                    foreach (var a in e.Actions) if (bank.Find(a) is null) yield return ("event action", a);
                    break;
                case WwiseAction a when a.Target != 0 && !a.TargetIsBus && bank.Find(a.Target) is null:
                    yield return ($"{a.Kind} target", a.Target);
                    break;
            }
            if (Node(o) is { } node && node.ParentId != 0 && bank.Find(node.ParentId) is null) yield return ("parent", node.ParentId);
            foreach (var c in Children(o)) if (bank.Find(c) is null) yield return ("child", c);
        }
    }

    public static WwiseNodeBase? Node(WwiseObject o) => o switch
    {
        WwiseSound s => s.Node,
        WwiseActorMixer m => m.Node,
        WwiseRandomSequence r => r.Node,
        WwiseSwitchContainer s => s.Node,
        WwiseLayerContainer l => l.Node,
        _ => null,
    };

    public static uint[] Children(WwiseObject o) => o switch
    {
        WwiseActorMixer m => m.Children,
        WwiseRandomSequence r => r.Children,
        WwiseSwitchContainer s => s.Children,
        WwiseLayerContainer l => l.Children,
        _ => [],
    };

    static void Dump(WwiseBank bank, bool media)
    {
        Console.WriteLine($"bank {bank.Id:X8} v{bank.Version} language {bank.LanguageId} names {string.Join(", ", bank.BankNames.Select(kv => $"{kv.Key:X8}={kv.Value}"))}");
        Console.WriteLine($"sections {string.Join(' ', bank.Sections.Select(s => $"{s.Tag}={s.Length}"))}");
        if (media)
            foreach (var m in bank.Media) Console.WriteLine($"  wem {m.Id:X8} {m.Length,9} bytes  {WemSummary(bank.MediaBytes(m).Span)}");
        var printed = new HashSet<uint>();
        foreach (var e in bank.Objects.OfType<WwiseEvent>())
        {
            Console.WriteLine($"event {e.Id:X8}");
            foreach (var a in e.Actions)
            {
                if (bank.Find<WwiseAction>(a) is not { } action)
                {
                    Console.WriteLine($"  action {a:X8} (missing)");
                    continue;
                }
                Console.WriteLine($"  {action.Kind}/{action.Scope} -> {action.Target:X8}{(action.TargetIsBus ? " (bus)" : "")}{Props(action.Properties)}{ActionDetail(action)}");
                if (action.Kind == WwiseActionKind.Play) Tree(bank, action.Target, 2, printed);
            }
        }
        var rest = bank.Objects.Where(o => Node(o) is not null && !printed.Contains(o.Id) && (Node(o)!.ParentId == 0 || bank.Find(Node(o)!.ParentId) is null)).ToList();
        foreach (var o in rest) Tree(bank, o.Id, 0, printed);
        foreach (var raw in bank.Objects.OfType<WwiseRawObject>()) Console.WriteLine($"{raw.Type} {raw.Id:X8} {raw.Body.Length} bytes{(raw.Error is null ? "" : " FAIL " + raw.Error)}");
    }

    static string ActionDetail(WwiseAction a) => a.Kind switch
    {
        WwiseActionKind.Play => $" bank {a.BankId:X8}",
        WwiseActionKind.SetState => $" group {a.GroupId:X8} state {a.StateId:X8}",
        WwiseActionKind.SetVolume or WwiseActionKind.SetPitch or WwiseActionKind.SetLowPass or WwiseActionKind.SetBusVolume
            => $" value {a.Value} ({a.ValueMin}..{a.ValueMax}) meaning {a.ValueMeaning}",
        _ => "",
    };

    static void Tree(WwiseBank bank, uint id, int depth, HashSet<uint> printed)
    {
        var pad = new string(' ', depth * 2);
        var o = bank.Find(id);
        if (o is null)
        {
            Console.WriteLine($"{pad}{id:X8} (not in this bank)");
            return;
        }
        if (!printed.Add(id))
        {
            Console.WriteLine($"{pad}{o.Type} {id:X8} (above)");
            return;
        }
        var node = Node(o);
        var sb = new StringBuilder($"{pad}{o.Type} {id:X8}");
        if (node is not null)
        {
            sb.Append(Props(node.Properties));
            if (node.Positioning.OverridesParent) sb.Append($" pos {node.Positioning.Bits:X2}{(node.Positioning.Is3D ? $" att {node.Positioning.AttenuationId:X8}" : "")}{(node.Positioning.Vertices.Length > 0 ? $" paths {node.Positioning.Paths.Length}" : "")}");
            foreach (var rtpc in node.Rtpcs) sb.Append($" rtpc {rtpc.GameParameterId:X8}->p{rtpc.ParameterId}[{rtpc.Points.Length}]");
            foreach (var s in node.StateGroups) sb.Append($" states {s.GroupId:X8}[{s.States.Length}]");
        }
        switch (o)
        {
            case WwiseSound s:
                sb.Append($" wem {s.Source.SourceId:X8} {s.Source.Size} bytes{(s.Source.IsVorbis ? "" : $" plugin {s.Source.PluginId:X8}")}");
                break;
            case WwiseRandomSequence r:
                sb.Append($" {(r.Mode == 0 ? "random" : "sequence")} loop {r.LoopCount} transition {r.TransitionMode}:{r.TransitionTime}ms avoid {r.AvoidRepeatCount} flags {r.Flags:X2}");
                break;
            case WwiseSwitchContainer w:
                sb.Append($" group {w.GroupType}:{w.GroupId:X8} default {w.DefaultSwitch:X8}");
                break;
            case WwiseLayerContainer l:
                foreach (var layer in l.Layers) sb.Append($" layer {layer.Id:X8} x-fade {layer.CrossfadeParameterId:X8}[{layer.Associations.Length}]");
                break;
        }
        Console.WriteLine(sb);
        if (o is WwiseSwitchContainer sw)
            foreach (var (switchId, nodes) in sw.Switches)
            {
                Console.WriteLine($"{pad}  when {switchId:X8}:");
                foreach (var n in nodes) Tree(bank, n, depth + 2, printed);
            }
        foreach (var c in Children(o)) if (o is not WwiseSwitchContainer || !printed.Contains(c)) Tree(bank, c, depth + 1, printed);
    }

    static string Props(WwiseProperties p)
    {
        if (p.Values.Count == 0 && p.Ranges.Count == 0) return "";
        var parts = p.Values.Select(kv => $"{PropName(kv.Key)}={PropValue(kv.Key, kv.Value)}")
            .Concat(p.Ranges.Select(kv => $"{PropName(kv.Key)}~[{kv.Value.Min},{kv.Value.Max}]"));
        return $" {{{string.Join(' ', parts)}}}";
    }

    static string PropName(byte id) => id switch
    {
        WwisePropertyId.Volume => "vol",
        WwisePropertyId.Pitch => "pitch",
        WwisePropertyId.LowPass => "lpf",
        WwisePropertyId.FadeTime => "fade",
        WwisePropertyId.Loop => "loop",
        _ => $"p{id:X2}",
    };

    static string PropValue(byte id, uint raw)
    {
        float f = BitConverter.UInt32BitsToSingle(raw);
        // Small integers stored as ints read as denormals; print those as ints.
        return raw < 0x00800000 ? raw.ToString() : f.ToString("0.###");
    }

    /// <summary>The RIFF header of a WEM: the codec tag, channels, rate, the fmt chunk's size and the other chunks.</summary>
    public static string WemSummary(ReadOnlySpan<byte> wem)
    {
        if (wem.Length < 12 || !wem.StartsWith("RIFF"u8)) return "not RIFF";
        var sb = new StringBuilder();
        int p = 12;
        while (p + 8 <= wem.Length)
        {
            var tag = Encoding.ASCII.GetString(wem.Slice(p, 4));
            int len = BitConverter.ToInt32(wem.Slice(p + 4, 4));
            if (tag == "fmt ")
                sb.Append($"codec {BitConverter.ToUInt16(wem.Slice(p + 8, 2)):X4} ch {BitConverter.ToUInt16(wem.Slice(p + 10, 2))} {BitConverter.ToUInt32(wem.Slice(p + 12, 4))} Hz fmt {len}; ");
            else
                sb.Append($"{tag.Trim()} {len}; ");
            if (len < 0) break;
            p += 8 + len + (len & 1);
        }
        return sb.ToString().TrimEnd(' ', ';');
    }
}
