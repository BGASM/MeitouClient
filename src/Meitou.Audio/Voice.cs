namespace Meitou.Audio;

/// <summary>
/// One playing WEM: decodes its Ogg Vorbis stream as it goes (NVorbis), loops it, resamples it to the output rate with the pitch, runs it
/// through a one-pole low-pass and adds it to the mix with a gain per side, ramped across the block so changes do not click.
/// </summary>
sealed class Voice : IDisposable
{
    readonly MediaClip clip;

    public MediaClip Clip => clip;
    NVorbis.VorbisReader reader;
    readonly float[] decoded;
    int decodedFrames;
    double position;
    int playsLeft;
    bool ended;
    float lastLeft, lastRight;
    bool started;
    readonly float[] lowPass = new float[2];

    /// <summary><paramref name="plays"/>: how many times to play the clip; 0 = forever.</summary>
    public Voice(MediaClip clip, int plays)
    {
        this.clip = clip;
        playsLeft = plays;
        reader = Open();
        decoded = new float[4096 * clip.Channels];
    }

    NVorbis.VorbisReader Open() => new(new MemoryStream(clip.Ogg, writable: false), closeOnDispose: true);

    public bool Ended => ended;

    /// <summary>
    /// Adds <paramref name="frames"/> frames to <paramref name="mix"/> (stereo). <paramref name="rate"/> is source frames per output frame;
    /// <paramref name="lowPass"/> the one-pole coefficient (1 = no filtering).
    /// </summary>
    public void Mix(float[] mix, int frames, double rate, float left, float right, float lowPass)
    {
        if (ended) return;
        if (!started)
        {
            lastLeft = left;
            lastRight = right;
            started = true;
        }
        int channels = clip.Channels;
        for (int f = 0; f < frames; f++)
        {
            int i = (int)position;
            if (i + 1 >= decodedFrames && !Refill())
            {
                ended = true;
                break;
            }
            i = (int)position;
            float t = (float)(position - i);
            float l = decoded[i * channels] + (decoded[(i + 1) * channels] - decoded[i * channels]) * t;
            float r = channels > 1 ? decoded[i * channels + 1] + (decoded[(i + 1) * channels + 1] - decoded[i * channels + 1]) * t : l;
            if (lowPass < 1)
            {
                this.lowPass[0] += lowPass * (l - this.lowPass[0]);
                this.lowPass[1] += lowPass * (r - this.lowPass[1]);
                l = this.lowPass[0];
                r = this.lowPass[1];
            }
            float k = (f + 1f) / frames;
            mix[2 * f] += l * (lastLeft + (left - lastLeft) * k);
            mix[2 * f + 1] += r * (lastRight + (right - lastRight) * k);
            position += rate;
        }
        lastLeft = left;
        lastRight = right;
    }

    /// <summary>Keeps the last decoded frame (for interpolation) and decodes more after it, looping at the end. False when the clip is done.</summary>
    bool Refill()
    {
        int channels = clip.Channels;
        int keep = Math.Min(decodedFrames, 1);
        int from = decodedFrames - keep;
        if (keep > 0) Array.Copy(decoded, from * channels, decoded, 0, keep * channels);
        position -= from;
        decodedFrames = keep;
        while (decodedFrames < decoded.Length / channels)
        {
            int n = reader.ReadSamples(decoded, decodedFrames * channels, decoded.Length - decodedFrames * channels);
            if (n > 0)
            {
                decodedFrames += n / channels;
                continue;
            }
            if (playsLeft == 1) break;
            if (playsLeft > 1) playsLeft--;
            reader.Dispose();
            reader = Open();
        }
        return position + 1 < decodedFrames;
    }

    public void Dispose() => reader.Dispose();
}

/// <summary>A WEM converted to Ogg Vorbis, ready to play.</summary>
sealed record MediaClip(uint Id, byte[] Ogg, int Channels, int SampleRate, int Frames);
