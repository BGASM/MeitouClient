using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.World;
using static Meitou.Tests.Save.SaveSamples;

namespace Meitou.Tests.Save;

/// <summary>Reading saves: the file layer on synthetic data, then the user's saves in place (docs/formats/save.md).</summary>
public class SaveReadTests
{
    [Fact]
    public void Strings_that_are_not_utf8_survive_a_round_trip()
    {
        // The ids of TERRAIN_DECALS instances are raw 4-byte integers in a string slot.
        var file = new SaveFile();
        var decals = new FcsRecord { Type = (int)FcsRecordType.TERRAIN_DECALS, StringId = "1--INGAME" };
        decals.Instances.Add(new FcsInstance { Id = LosslessUtf8.GetString([0x81, 0, 0, 0]), Target = "-" });
        decals.Instances.Add(new FcsInstance { Id = LosslessUtf8.GetString([0xC3, 0xA9, 0xFF, 0xFE]), Target = "é" });
        file.Records.Add(decals);
        var bytes = file.ToBytes();
        var back = SaveFile.Read(bytes);
        Assert.Equal(bytes, back.ToBytes());
        Assert.Equal("é\udcff\udcfe", back.Records[0].Instances[1].Id);
    }
}
