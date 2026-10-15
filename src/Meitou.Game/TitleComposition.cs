using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Rendering;

namespace Meitou.Game;

/// <summary>A staged interpretation of the poster using installed content; never a playable-world placement.</summary>
sealed class TitleComposition
{
    internal const float Yaw = -65 * MathF.PI / 180, Pitch = -6 * MathF.PI / 180, FieldOfView = 42 * MathF.PI / 180;
    readonly Vector3 origin;
    readonly Func<float, float, float> ground;
    readonly GameDatabase database;
    public Vector3 Eye { get; }
    public Vector3 Wanderer { get; }
    public Vector3 Tower { get; }
    public List<PlacedMesh> Meshes { get; } = [];
    public CharacterAppearance Appearance { get; }

    public TitleComposition(GameInstall install, GameDatabase database, Vector3 origin, Func<float, float, float> ground, string character)
    {
        (this.database, this.origin, this.ground) = (database, origin, ground);
        Eye = At(0, 0) + Vector3.UnitY * 13;
        Wanderer = At(-1.4f, 8);
        Tower = At(26, 100);
        var gear = new[] { "Iron Hat", "Dustcoat", "Drifter's Leather Pants", "Drifter's Boots", "Katana", "Small Backpack" }
            .Where(n => database.Records.Values.Any(r => r.Name == n)).ToArray();
        Appearance = CharacterAppearance.Build(database, install.Root, character, new CharacterOptions { Female = false, Naked = true, Equip = gear });
        Building("4104-D-TwoStorey.mod", "poster-tower", Tower + new Vector3(0, 51, 0), new Vector3(1, 0.85f, 1), -1.4f);
        // A broken crown and smaller rubble keep the tower from reading as a tidy inhabited outpost.
        Rock("Rock MenhirStone 01", "left-spire-1", -49, 110, new(0.7f, 2.2f, 0.7f), 0.4f);
        Rock("Rock MenhirType 08", "left-spire-2", -65, 123, new(0.8f, 2.75f, 0.8f), 1.9f);
        Rock("UpthrustRocks01", "left-ridge", -95, 145, new(0.65f), 0.2f);
        Rock("Rock MenhirType 08", "left-spire-3", -60, 156, new(1.1f, 3.8f, 1.1f), 2.7f);
        Rock("DesertRocks_Piece01", "foreground-rock", -4, 12, new(0.025f), 0.8f);
        Rock("DesertRocks_Piece02", "foreground-rubble", 4, 17, new(0.035f), 2.1f);
        Rock("Rock MenhirStone 01", "tower-rubble", 34, 95, new(0.12f, 0.16f, 0.12f), 1.2f);
        Rock("Rock MenhirType 08", "distant-spire-1", 510, 1500, new(12, 18, 12), 2.1f);
        Rock("UpthrustRocks01", "distant-ridge", 670, 1750, new(6.5f), 0.7f);
    }

    internal Vector3 At(float right, float forward)
    {
        var facing = new Vector3(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        var side = Vector3.Cross(facing, Vector3.UnitY);
        var at = origin + side * (right * 10) + facing * (forward * 10);
        at.Y = ground(at.X, at.Z);
        return at;
    }

    internal static WorldScene CreateTerrain(Vector3 origin, GameDatabase database, SkyClock clock)
    {
        const int cells = 256;
        var sample = WorldLayout.ToSample(origin.X, origin.Z);
        int c0 = (int)Math.Round(sample.Column) - cells / 2, r0 = (int)Math.Round(sample.Row) - cells / 2;
        ushort raw = (ushort)Math.Clamp((int)Math.Round(origin.Y * ushort.MaxValue / WorldLayout.MaxHeight), 0, ushort.MaxValue);
        int coarseSize = (WorldLayout.HeightmapSize - 1) / WorldFrame.CoarseStep + 1;
        var coarse = new ushort[coarseSize * coarseSize];
        Array.Fill(coarse, raw);
        var samples = new ushort[(cells + 1) * (cells + 1)];
        for (int row = 0; row <= cells; row++)
            for (int column = 0; column <= cells; column++)
            {
                // Shallow wind-worn undulations, with the outer window meeting the flat coarse field.
                float edge = Math.Clamp(Math.Min(Math.Min(column, cells - column), Math.Min(row, cells - row)) / 24f, 0, 1);
                float ripple = 20 * MathF.Sin(column * 0.31f) * MathF.Sin(row * 0.19f) * edge;
                samples[row * (cells + 1) + column] = (ushort)Math.Clamp(raw + (int)ripple, 0, ushort.MaxValue);
            }
        var window = new HeightWindow(c0, r0, 1, cells + 1, cells + 1, samples);
        var (x0, z0) = window.WorldOf(0, 0);
        var (x1, z1) = window.WorldOf(cells, cells);
        return new WorldScene { Window = window, Coarse = coarse, CoarseSize = coarseSize, Focus = origin, FlatHeight = WorldLayout.RawToHeight(raw),
            X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Database = database, Clock = clock };
    }

    void Building(string record, string id, Vector3 position, Vector3 scale, float yaw)
    {
        if (database.Find(record) is not { } building) return;
        var transform = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(position);
        foreach (var part in WorldObjectLayout.Building(database, building, id, Vector3.Zero, Quaternion.Identity))
        {
            // Use the shipped damaged upper facade and remove its intact roof to expose the broken crown.
            if (part.MeshPath.Contains("ROOF", StringComparison.OrdinalIgnoreCase) || part.MeshPath.Contains("SPIKECOVERS", StringComparison.OrdinalIgnoreCase) ||
                part.MeshPath.Contains("Interior_04_BLACK", StringComparison.OrdinalIgnoreCase)) continue;
            var damaged = part.Source.GetPath("destroyed mesh");
            Meshes.Add(part with { MeshPath = damaged.Length > 0 ? damaged : part.MeshPath, Transform = part.Transform * transform });
        }
    }

    void Rock(string name, string id, float right, float forward, Vector3 scale, float yaw)
    {
        var record = database.OfType(FcsRecordType.MAP_FEATURES).FirstOrDefault(r => r.Name == name);
        if (record is null) return;
        var feature = new MapFeature(record.StringId, At(right, forward), scale, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
        if (WorldObjectLayout.Feature(database, feature, id) is { } mesh) Meshes.Add(mesh);
    }

    public void Fill(CharacterDrawList list, float seconds) =>
        list.Add(new CharacterInstance(long.MinValue + 1, Appearance, Wanderer, Yaw + MathF.PI, [new("idle_stand_relax", seconds, 1)]));
}
