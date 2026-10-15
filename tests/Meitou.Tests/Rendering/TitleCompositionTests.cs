using System.Numerics;
using Meitou.Data.World;
using Meitou.Game;
using Meitou.Rendering;

namespace Meitou.Tests.Rendering;

public class TitleCompositionTests
{
    [Fact]
    public void TheSpotIsACameraCodeAndTheSunIsTurnedIntoTheRightOfItsView()
    {
        var camera = new WorldCamera();
        TitleComposition.Place(camera);
        var clock = new SkyClock(54, 5, 23);
        float hour = clock.Sunrise + 0.75f;
        var sun = (clock with { SunTurn = TitleComposition.SunTurn(clock, hour, camera) }).SunDirection(hour);
        // Same height as the game's sun at that hour; only its bearing moves, to the right of the heading.
        Assert.Equal(clock.SunDirection(hour).Y, sun.Y, 5);
        var ahead = Vector3.Normalize(camera.Forward with { Y = 0 });
        var flat = Vector3.Normalize(sun with { Y = 0 });
        Assert.InRange(MathF.Acos(Vector3.Dot(ahead, flat)) * 180 / MathF.PI, 20, 32);
        Assert.True(Vector3.Dot(flat, Vector3.Cross(ahead, Vector3.UnitY)) > 0, "the sun is right of the view");
    }

    [Fact]
    public void WordmarkIsEmbeddedWithInkOnATranslucentBand()
    {
        var logo = TitleLogo.Load();
        Assert.NotNull(logo);
        Assert.Equal((1470, 690), (logo!.Width, logo.Height));
        byte Alpha(int x, int y) => logo.Pixels[(y * logo.Width + x) * 4 + 3];
        Assert.Equal(0, Alpha(5, 5));   // clear round the mark
        Assert.InRange(Alpha(60, 390), 100, 160);   // the band, left of the lettering, half see-through
        int ink = 0;
        for (int i = 0; i < logo.Pixels.Length; i += 4)
            if (logo.Pixels[i + 3] > 240 && logo.Pixels[i] < 40) ink++;
        Assert.InRange(ink, 40000, 300000);
    }
}
