namespace Meitou.Data.Audio;

/// <summary>Reads Vorbis-style bit fields: least significant bit first within each byte, fields least significant bit first.</summary>
ref struct VorbisBitReader(ReadOnlySpan<byte> data)
{
    readonly ReadOnlySpan<byte> data = data;

    public int Position { get; private set; }
    public readonly int Length => data.Length * 8;
    public readonly int Remaining => Length - Position;

    public uint Read(int bits)
    {
        if (bits > Remaining) throw new InvalidDataException($"Read of {bits} bits at bit {Position} runs past the packet ({Length} bits)");
        uint value = 0;
        for (int i = 0; i < bits; i++, Position++)
            value |= (uint)((data[Position >> 3] >> (Position & 7)) & 1) << i;
        return value;
    }

    public bool Flag() => Read(1) != 0;
}

/// <summary>Writes Vorbis-style bit fields (the order of <see cref="VorbisBitReader"/>).</summary>
sealed class VorbisBitWriter
{
    readonly List<byte> bytes = [];
    int bit;

    public void Write(uint value, int bits)
    {
        for (int i = 0; i < bits; i++)
        {
            if ((bit & 7) == 0) bytes.Add(0);
            if (((value >> i) & 1) != 0) bytes[^1] |= (byte)(1 << (bit & 7));
            bit++;
        }
    }

    public void Write(bool flag) => Write(flag ? 1u : 0u, 1);

    /// <summary>Copies <paramref name="bits"/> bits from the reader.</summary>
    public void Copy(ref VorbisBitReader reader, int bits)
    {
        while (bits > 0)
        {
            int n = Math.Min(bits, 32);
            Write(reader.Read(n), n);
            bits -= n;
        }
    }

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        foreach (var b in data) Write(b, 8);
    }

    public byte[] ToArray() => [.. bytes];

    /// <summary>The number of bits needed for <paramref name="v"/> (Vorbis <c>ilog</c>).</summary>
    public static int ILog(uint v)
    {
        int r = 0;
        while (v != 0)
        {
            r++;
            v >>= 1;
        }
        return r;
    }
}
