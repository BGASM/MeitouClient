using System.Buffers.Binary;
using System.Text;

namespace Meitou.Data.Audio;

/// <summary>
/// A Wwise sound bank (<c>.bnk</c>), read clean-room from the layout in docs/formats/wwise-bnk.md: the header, the embedded media
/// (<c>DIDX</c>/<c>DATA</c>), the bank-name table (<c>STID</c>) and the object hierarchy (<c>HIRC</c>). Kenshi's banks are version 112
/// except <c>SFX_Bank.bnk</c> (88), whose objects are kept undecoded.
/// </summary>
public sealed class WwiseBank
{
    /// <summary>The bank version whose objects the reader decodes.</summary>
    public const uint DecodedVersion = 112;

    readonly byte[] data;
    readonly Dictionary<uint, WwiseObject> byId = [];
    readonly Dictionary<uint, WwiseMedia> mediaById = [];

    WwiseBank(byte[] data) => this.data = data;

    public uint Version { get; private set; }
    public uint Id { get; private set; }
    public uint LanguageId { get; private set; }
    /// <summary>The sections in file order: tag, offset of the body, body length.</summary>
    public IReadOnlyList<(string Tag, int Offset, int Length)> Sections { get; private set; } = [];
    public IReadOnlyList<WwiseMedia> Media { get; private set; } = [];
    /// <summary>The <c>STID</c> table: bank ids and names (Kenshi's banks list only themselves).</summary>
    public IReadOnlyDictionary<uint, string> BankNames { get; private set; } = new Dictionary<uint, string>();
    public IReadOnlyList<WwiseObject> Objects { get; private set; } = [];

    public WwiseObject? Find(uint id) => byId.GetValueOrDefault(id);
    public T? Find<T>(uint id) where T : WwiseObject => byId.GetValueOrDefault(id) as T;
    public WwiseMedia? FindMedia(uint id) => mediaById.TryGetValue(id, out var m) ? m : null;

    /// <summary>The bytes of an embedded WEM.</summary>
    public ReadOnlyMemory<byte> MediaBytes(WwiseMedia media) => data.AsMemory(media.Offset, media.Length);

    public static WwiseBank ReadFile(string path) => Read(File.ReadAllBytes(path));

    public static WwiseBank Read(byte[] data)
    {
        var bank = new WwiseBank(data);
        var sections = new List<(string, int, int)>();
        int dataOffset = -1;
        var index = new List<(uint Id, uint Offset, uint Length)>();
        int p = 0;
        while (p < data.Length)
        {
            if (p + 8 > data.Length) throw new InvalidDataException($"Truncated section header at {p}");
            string tag = Encoding.ASCII.GetString(data, p, 4);
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 4));
            int body = p + 8;
            if (length < 0 || body + (long)length > data.Length) throw new InvalidDataException($"Section {tag} at {p} runs past the end of the file");
            sections.Add((tag, body, length));
            var span = data.AsSpan(body, length);
            switch (tag)
            {
                case "BKHD":
                    if (p != 0) throw new InvalidDataException("BKHD is not the first section");
                    bank.Version = BinaryPrimitives.ReadUInt32LittleEndian(span);
                    bank.Id = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
                    bank.LanguageId = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
                    break;
                case "DIDX":
                    if (length % 12 != 0) throw new InvalidDataException("DIDX length is not a multiple of 12");
                    for (int i = 0; i < length; i += 12)
                        index.Add((BinaryPrimitives.ReadUInt32LittleEndian(span[i..]), BinaryPrimitives.ReadUInt32LittleEndian(span[(i + 4)..]),
                            BinaryPrimitives.ReadUInt32LittleEndian(span[(i + 8)..])));
                    break;
                case "DATA":
                    dataOffset = body;
                    break;
                case "STID":
                    bank.BankNames = ReadStringTable(span);
                    break;
            }
            p = body + length;
        }
        if (sections.Count == 0 || sections[0].Item1 != "BKHD") throw new InvalidDataException("Not a Wwise bank (no BKHD)");
        bank.Sections = sections;

        if (index.Count > 0)
        {
            var dataSection = sections.FirstOrDefault(s => s.Item1 == "DATA");
            if (dataOffset < 0) throw new InvalidDataException("DIDX without DATA");
            var media = new List<WwiseMedia>(index.Count);
            foreach (var (id, offset, len) in index)
            {
                if (offset + (long)len > dataSection.Item3) throw new InvalidDataException($"Media {id:X8} runs past DATA");
                var m = new WwiseMedia(id, dataOffset + (int)offset, (int)len, (int)offset);
                media.Add(m);
                bank.mediaById[id] = m;
            }
            bank.Media = media;
        }

        foreach (var (tag, offset, length) in sections)
            if (tag == "HIRC") bank.Objects = WwiseHierarchyReader.Read(data.AsSpan(offset, length), bank.Version == DecodedVersion);
        foreach (var o in bank.Objects) bank.byId.TryAdd(o.Id, o);
        return bank;
    }

    /// <summary><c>STID</c>: u32 string type (1), u32 count, then per entry u32 bank id, u8 length, the name (ASCII, no terminator).</summary>
    static Dictionary<uint, string> ReadStringTable(ReadOnlySpan<byte> span)
    {
        var names = new Dictionary<uint, string>();
        int count = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
        int p = 8;
        for (int i = 0; i < count; i++)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(span[p..]);
            int len = span[p + 4];
            names[id] = Encoding.ASCII.GetString(span.Slice(p + 5, len));
            p += 5 + len;
        }
        if (p != span.Length) throw new InvalidDataException("STID has trailing bytes");
        return names;
    }
}

/// <summary>An embedded WEM: its source id, where its bytes are in the bank file, and its offset inside <c>DATA</c> (what sounds refer to).</summary>
public readonly record struct WwiseMedia(uint Id, int Offset, int Length, int DataOffset);
