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
    long toSkip, limit;
    bool ended, final;
    float lastLeft, lastRight;
    bool started;
    readonly float[] lowPass = new float[2];

    /// <summary>
    /// <paramref name="plays"/>: how many times to play the clip; 0 = forever. <paramref name="skipFrames"/> source frames are dropped from the
    /// start and, when <paramref name="limitFrames"/> is not negative, only that many are played after them (music clips' trims).
    /// <paramref name="startPhase"/>: how far into the first source frame (in source frames) the first output frame lies, for a start between two output frames.
    /// </summary>
    public Voice(MediaClip clip, int plays, long skipFrames = 0, long limitFrames = -1, double startPhase = 0)
    {
        this.clip = clip;
        playsLeft = plays;
        toSkip = skipFrames;
        limit = limitFrames;
        position = startPhase;
        reader = Open();
        decoded = new float[4096 * clip.Channels];
    }

    NVorbis.VorbisReader Open() => new(new MemoryStream(clip.Ogg, writable: false), closeOnDispose: true);

    public bool Ended => ended;

    /// <summary>
    /// Adds <paramref name="frames"/> frames to <paramref name="mix"/> (stereo), the first <paramref name="first"/> of them left silent (a
    /// start inside the block). <paramref name="rate"/> is source frames per output frame; <paramref name="lowPass"/> the one-pole
    /// coefficient (1 = no filtering).
    /// </summary>
    public void Mix(float[] mix, int frames, double rate, float left, float right, float lowPass, int first = 0)
    {
        if (ended) return;
        if (!started)
        {
            lastLeft = left;
            lastRight = right;
            started = true;
        }
        int channels = clip.Channels;
        for (int f = first; f < frames; f++)
        {
            int i = (int)position;
            if (i + 1 >= decodedFrames && !Refill())
            {
                ended = true;
                break;
            }
            i = (int)position;
            float t = (float)(position - i);
            bool atEnd = i + 1 >= decodedFrames;
            float l = decoded[i * channels] + ((atEnd ? 0 : decoded[(i + 1) * channels]) - decoded[i * channels]) * t;
            float r = channels > 1 ? decoded[i * channels + 1] + ((atEnd ? 0 : decoded[(i + 1) * channels + 1]) - decoded[i * channels + 1]) * t : l;
            if (lowPass < 1)
            {
                this.lowPass[0] += lowPass * (l - this.lowPass[0]);
                this.lowPass[1] += lowPass * (r - this.lowPass[1]);
                l = this.lowPass[0];
                r = this.lowPass[1];
            }
            float k = (f - first + 1f) / (frames - first);
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
                int got = n / channels;
                if (toSkip > 0)
                {
                    int skip = (int)Math.Min(toSkip, got);
                    toSkip -= skip;
                    Array.Copy(decoded, (decodedFrames + skip) * channels, decoded, decodedFrames * channels, (got - skip) * channels);
                    got -= skip;
                }
                if (limit >= 0)
                {
                    got = (int)Math.Min(got, limit);
                    limit -= got;
                }
                decodedFrames += got;
                if (limit == 0)
                {
                    final = true;
                    break;
                }
                continue;
            }
            if (playsLeft == 1)
            {
                final = true;
                break;
            }
            if (playsLeft > 1) playsLeft--;
            reader.Dispose();
            reader = Open();
        }
        // At the end the clip fades into silence over its last frame, so a clip that follows it sample for sample leaves no gap.
        return position + 1 < decodedFrames || (final && position < decodedFrames);
    }

    public void Dispose() => reader.Dispose();
}

/// <summary>A WEM converted to Ogg Vorbis, ready to play.</summary>
sealed record MediaClip(uint Id, byte[] Ogg, int Channels, int SampleRate, int Frames);
