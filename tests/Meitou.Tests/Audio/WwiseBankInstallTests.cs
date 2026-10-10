using Meitou.Data.Audio;

namespace Meitou.Tests.Audio;

/// <summary>The Wwise bank reader against the install's 24 banks in data/audio (docs/formats/wwise-bnk.md).</summary>
[Slow]
public class WwiseBankInstallTests
{
    static readonly string[] Biomes = ["Ashlands", "Boneyard", "Canyons", "Coast", "Desert", "Forest", "Jungle", "Plains", "Swamplands", "Tarsands"];

    static readonly Lazy<List<(string Name, WwiseBank Bank)>?> banks = new(() => InstallData.Install is { } install
        ? Directory.GetFiles(Path.Combine(install.DataDirectory, "audio"), "*.bnk").Order(StringComparer.OrdinalIgnoreCase)
            .Select(f => (Path.GetFileNameWithoutExtension(f), WwiseBank.ReadFile(f))).ToList()
        : null);

    static List<(string Name, WwiseBank Bank)> Banks()
    {
        Assert.SkipWhen(banks.Value is null, "Kenshi install not found");
        return banks.Value!;
    }

    static WwiseBank Bank(string name) => Banks().Single(b => b.Name == name).Bank;

    [Fact]
    public void Every_bank_is_version_112_except_SFX_Bank_and_is_named_by_the_hash_of_its_file_name()
    {
        var all = Banks();
        Assert.Equal(24, all.Count);
        foreach (var (name, bank) in all)
        {
            Assert.Equal(name == "SFX_Bank" ? 88u : 112u, bank.Version);
            Assert.Equal(WwiseHash.Id(name), bank.Id);
            Assert.Equal(0u, bank.LanguageId);
            // STID lists the bank itself, plus banks its events play from (MT_Music_Bank also names SFX_General_Bank).
            if (bank.BankNames.Count > 0) Assert.Equal(name, bank.BankNames[bank.Id], ignoreCase: true);
            foreach (var (id, other) in bank.BankNames) Assert.Equal(WwiseHash.Id(other), id);
        }
    }

    [Fact]
    public void Every_version_112_object_of_a_decoded_type_uses_exactly_its_size()
    {
        WwiseObjectType[] decoded = [WwiseObjectType.Event, WwiseObjectType.Action, WwiseObjectType.Sound, WwiseObjectType.ActorMixer,
            WwiseObjectType.RandomSequenceContainer, WwiseObjectType.SwitchContainer, WwiseObjectType.LayerContainer];
        int count = 0;
        foreach (var (name, bank) in Banks().Where(b => b.Bank.Version == WwiseBank.DecodedVersion))
            foreach (var o in bank.Objects)
            {
                if (o is WwiseRawObject raw)
                {
                    Assert.True(raw.Error is null, $"{name} {raw.Type} {raw.Id:X8}: {raw.Error}");
                    Assert.DoesNotContain(raw.Type, decoded);
                }
                else count++;
            }
        Assert.Equal(2503, count); // with the 11 buses (partly decoded)
    }

    [Fact]
    public void Every_sound_plays_a_WEM_embedded_in_its_own_bank_at_the_stored_file_offset()
    {
        foreach (var (name, bank) in Banks().Where(b => b.Bank.Version == WwiseBank.DecodedVersion))
            foreach (var s in bank.Objects.OfType<WwiseSound>())
            {
                Assert.Equal(0, s.Source.StreamType);
                if (!s.Source.IsVorbis)
                {
                    Assert.Equal(0x00650002u, s.Source.PluginId); // the silence source: no media
                    continue;
                }
                Assert.Equal(bank.Id, s.Source.FileId);
                var media = bank.FindMedia(s.Source.SourceId);
                Assert.True(media is not null, $"{name} sound {s.Id:X8} has no media {s.Source.SourceId:X8}");
                Assert.Equal(media.Value.Offset, (int)s.Source.Offset);
                Assert.Equal(media.Value.Length, (int)s.Source.Size);
            }
    }

    [Fact]
    public void Ambience_WEMs_are_Wwise_Vorbis_with_the_setup_in_a_66_byte_fmt_chunk()
    {
        foreach (var (name, bank) in Banks().Where(b => b.Name.StartsWith("SFX_Amb_", StringComparison.Ordinal)))
            foreach (var m in bank.Media)
            {
                var wem = bank.MediaBytes(m).Span;
                Assert.True(wem.StartsWith("RIFF"u8) && wem[8..12].SequenceEqual("WAVE"u8), $"{name} {m.Id:X8}");
                Assert.True(wem[12..16].SequenceEqual("fmt "u8));
                Assert.Equal(66, BitConverter.ToInt32(wem[16..20]));
                Assert.Equal(0xFFFF, BitConverter.ToUInt16(wem[20..22]));
                Assert.True(wem[(20 + 66)..(24 + 66)].SequenceEqual("data"u8), $"{name} {m.Id:X8}: no data chunk right after fmt");
            }
    }

    [Fact]
    public void Each_biome_bank_has_day_and_night_enter_events_and_a_leave_event()
    {
        foreach (var biome in Biomes)
        {
            var bank = Bank($"SFX_Amb_{biome}");
            var events = bank.Objects.OfType<WwiseEvent>().Select(e => e.Id).ToHashSet();
            Assert.Equal(new HashSet<uint> { WwiseHash.Id($"Enter_{biome}_Day"), WwiseHash.Id($"Enter_{biome}_Night"), WwiseHash.Id($"Leave_{biome}") }, events);
            foreach (var time in new[] { "Day", "Night" })
            {
                var plays = bank.Find<WwiseEvent>(WwiseHash.Id($"Enter_{biome}_{time}"))!.Actions.Select(a => bank.Find<WwiseAction>(a)!)
                    .Where(a => a.Kind == WwiseActionKind.Play).ToList();
                Assert.NotEmpty(plays);
                Assert.All(plays, p => Assert.Equal(bank.Id, p.BankId));
            }
        }
        var indoors = Bank("SFX_Amb_Indoors_Bank").Objects.OfType<WwiseEvent>().Select(e => e.Id).ToHashSet();
        Assert.Equal(new HashSet<uint> { WwiseHash.Id("Enter_Building"), WwiseHash.Id("Enter_Building_Big"), WwiseHash.Id("Leave_Building") }, indoors);
    }

    [Fact]
    public void The_weather_bank_has_the_rain_and_wind_start_events_but_no_wind_stop()
    {
        var events = Bank("SFX_Amb_Weather_Bank").Objects.OfType<WwiseEvent>().Select(e => e.Id).ToHashSet();
        Assert.Contains(WwiseHash.Id("Weather_Rain_Start"), events);
        Assert.Contains(WwiseHash.Id("Weather_Rain_End"), events);
        Assert.Contains(WwiseHash.Id("Weather_Wind_Start"), events);
        Assert.DoesNotContain(WwiseHash.Id("Weather_Wind_Stop"), events);
        Assert.Equal(4, events.Count);
    }
}
