using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.World;
using Meitou.Game;
using Meitou.Rendering;

namespace Meitou.Tests.Rendering;

public class TitleCompositionTests
{
    [Fact]
    public void DummyTerrainHasAnIndependentShallowSurfaceAndMeetsTheCoarseFieldAtItsEdges()
    {
        using var scene = TitleComposition.CreateTerrain(new(-50000, 1500, 3000), new GameDatabase(), SkyClock.Fallback);
        Assert.Null(scene.Heightmap);
        Assert.All(scene.Coarse, raw => Assert.Equal(scene.FlatHeight, WorldLayout.RawToHeight(raw)));
        for (int i = 0; i < scene.Window.Columns; i++)
        {
            Assert.Equal(scene.FlatHeight, scene.Window.Height(i, 0));
            Assert.Equal(scene.FlatHeight, scene.Window.Height(i, scene.Window.Rows - 1));
            Assert.Equal(scene.FlatHeight, scene.Window.Height(0, i));
            Assert.Equal(scene.FlatHeight, scene.Window.Height(scene.Window.Columns - 1, i));
        }
        Assert.InRange(scene.Window.Raw.Max() - scene.Window.Raw.Min(), 1, 40);
    }

    [Fact, Slow]
    public void PosterSubjectsAreFramedAndStagedWithoutWritingWorldRecords()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "No Kenshi install configured");
        var db = InstallData.BaseGame!;
        int records = db.Records.Count;
        var composition = new TitleComposition(install!, db, new(-50000, 1500, 3000), (_, _) => 1500, "Greenlander");
        var camera = new WorldCamera();
        SkyArrival.Place(camera, composition.Eye, TitleComposition.Yaw, TitleComposition.Pitch, 100, TitleComposition.FieldOfView);
        Vector2 Screen(Vector3 position)
        {
            var p = Vector4.Transform(new Vector4(position, 1), camera.View * camera.Projection(16f / 9, 1, 30000));
            Assert.True(p.W > 0);
            return new(p.X / p.W * 0.5f + 0.5f, 0.5f - p.Y / p.W * 0.5f);
        }
        Assert.InRange(Screen(composition.Wanderer).X, 0.3f, 0.4f);
        Assert.InRange(Screen(composition.Wanderer).Y, 0.75f, 0.9f);
        Assert.InRange(Screen(composition.Tower).X, 0.65f, 0.75f);
        Assert.InRange(Screen(SkyPlanet.All[0].Towards * 100000 + camera.Eye).X, 0.1f, 0.3f);
        Assert.Contains(composition.Meshes, m => m.MeshPath.Contains("WALLS_Destroyed", StringComparison.Ordinal));
        using var staged = WorldObjects.Stage(install!, db, composition.Meshes);
        Assert.Equal(composition.Meshes.Count, staged.PopulatedZones.Sum(z => staged.BuildZone(z).Items.Count));
        Assert.Empty(staged.Levels.Zones);
        Assert.Empty(staged.Levels.Level.Records);
        Assert.Equal(records, db.Records.Count);
    }
}
