using Meitou.Tests.Combat;

namespace Meitou.Tests.Simulation;

/// <summary>
/// Pins the state hashes of the scripted scenarios the other tests use, so a refactor that is meant to change nothing is checked to
/// change nothing (the thread-count tests only check that the threads agree with each other). A change of behaviour that is meant must
/// update these numbers in the same commit and say so.
/// </summary>
public class GoldenHashTests(ITestOutputHelper output)
{
    static readonly (string Name, Func<List<ulong>> Run)[] Scenarios =
    [
        ("wander", () => DeterminismTests.Hashes(7, 4)),
        ("player", () => PlayerTests.Scripted(4)),
        ("roaming", () => RoamingTests.Scripted(4)),
        ("bodies", () => BodyWiringTests.Hashes(9, 4, 2000)),
        ("fight", () => FightWiringTests.Hashes(4)),
        ("duels", () => DuelTests.Hashes(5, 4, out _)),
        ("wander-parts", () => Parts(WanderSystem.Create(11, 4, 200, minPartition: 4), 1, 30, 90, 300)),
        ("town-parts", () => Parts(TownParts(), 1, 40, 200, 700)),
        ("first-aid", () => FirstAidTests.Scripted(4)),
        ("aggro", () => AggroTests.Hashes(4)),
        ("ranged", () => RangedTests.Hashes(4)),
        ("carry", () => CarryTests.Scripted(4)),
        ("crime", () => CrimeTests.Scripted(4)),
        ("build", () => ConstructionTests.Scripted(4)),
    ];

    /// <summary>A roaming town with bodies and feeding, cut into many partitions (the other worlds here are mostly one partition at the default size).</summary>
    static Meitou.Simulation.World TownParts()
    {
        var db = SyntheticTown.Database(roaming: true, anatomy: true, items: true);
        return SyntheticTown.World(21, 4, db: db, bodies: true, bodyTimeScale: 2000, feed: new Meitou.Simulation.Items.FeedSettings(), minPartition: 4);
    }

    static List<ulong> Parts(Meitou.Simulation.World w, params int[] checkpoints)
    {
        using var world = w;
        var hashes = new List<ulong>();
        int ran = 0;
        foreach (int target in checkpoints)
        {
            world.RunTicks(target - ran);
            ran = target;
            hashes.Add(world.StateHash());
        }
        return hashes;
    }

    static readonly Dictionary<string, ulong[]> Golden = new()
    {
        ["wander"] = [0x9C9775CE6597DD6BUL, 0xFC7CDBEE899281FCUL, 0xBB6C16EF02A628E9UL, 0xBDFB49EB74CDADFBUL, 0xF598C41B6B09487BUL, 0x67FB41FE12600A56UL],
        ["player"] = [0xB2ADCA8F1B7A4529UL, 0xDE539C175ABB6E27UL, 0xD3D2CAF6B9244502UL, 0xBF9647CE38C09ED2UL, 0x3995CAB0915273E1UL, 0xE06133A3D30AD7E7UL],
        ["roaming"] = [0x430FECD54DB9E882UL, 0x6173674F3396B63FUL, 0x3D8C94FDE7BA81AEUL, 0xD05C1323E6B1DB0CUL, 0x7A0C52EE8018D926UL, 0xDC75A9E4EC4DA1DFUL],
        ["bodies"] = [0x18A1C10FEAACA2B6UL, 0xD4940B65AAA84877UL, 0x22E84AB7D372DC41UL, 0x286440AAD535F604UL, 0x12D60B3A868E416DUL, 0x9AAC8F4693B4FE41UL, 0xF519502F469111BCUL],
        ["fight"] = [0xAFD100B5D5A103EFUL, 0x4EA620A36534D64FUL, 0xF5C15A07B4AB6B36UL, 0xB32F1235AB25E062UL, 0x0F5D21B044D881E3UL],
        ["duels"] = [0xB95ECA48BE4D6B67UL, 0xBDF3C811FA72B3E9UL, 0x3678377EAC3F88CAUL, 0x2E9CF179BA55D4FEUL, 0x3DA3B7D34F5586B6UL, 0xD4C3C8E4BC1B8B38UL],
        ["wander-parts"] = [0x3543B26C51534781UL, 0x8B2D54DAA6E46871UL, 0x0A60CEAEB398B8A0UL, 0x147F1D52DE1D4B4AUL],
        ["town-parts"] = [0xC3855E193F97441FUL, 0xB08E3142DB568DBAUL, 0x088F12A1A749C969UL, 0x27BFB98B9578D8DDUL],
        ["first-aid"] = [0xBF17302ED6B23714UL, 0x9CF95520B6C2E14AUL, 0x3D9E0AA38A43E24FUL, 0x9A88B43871791D7EUL, 0xC5737A885FB0FC73UL, 0x53629744CB4F14EDUL],
        ["aggro"] = [0xC195D045D841D14EUL, 0x1C83DE9535DB1DCEUL, 0xF65452202387C102UL, 0x63EFB71657520CC1UL, 0x9B7F6D3FA0ECBB52UL],
        ["ranged"] = [0xCDB87B3C94CC14ACUL, 0x1D779480FC376D8EUL, 0x0DF5A302BB29A205UL, 0xDCAFFD8B368042F1UL, 0x574223B4CFCFA1ACUL],
        ["carry"] = [0xFB6457CC83EE9BDAUL, 0xF0258E9C2EFF2424UL, 0x18B0FB44D757DE4AUL, 0xF880D6722A2F50EBUL, 0xB3384CED264D88C7UL, 0x8FA8E4ADD75B4453UL, 0xD7EC0874FB54F71BUL],
        ["crime"] = [0x21C5A7B717D594DBUL, 0xC1126C11F921A5AEUL, 0x87F9DC29E54A73D8UL, 0x71E450EF7D33EC3CUL, 0x934CB003F5B82491UL, 0x7BC6E071DE99673AUL],
        ["build"] = [0x65D4C75216A7692CUL, 0xA8C813F2CD3ECC26UL, 0x4C77BC946AC3E9D8UL, 0x5E86BB43B164AAC7UL, 0xB1D7B4CE0B74E759UL, 0xAA9839F9FD80A9C1UL, 0x0002677BB86FF1E8UL, 0x2B97AC9EF598FEBBUL],
    };

    [Fact]
    public void Scripted_scenarios_hash_as_pinned()
    {
        var failures = new List<string>();
        foreach (var (name, run) in Scenarios)
        {
            var got = run();
            output.WriteLine($"[\"{name}\"] = [{string.Join(", ", got.Select(h => $"0x{h:X16}UL"))}],");
            if (!Golden.TryGetValue(name, out var want)) failures.Add($"{name}: not pinned");
            else if (!want.SequenceEqual(got)) failures.Add($"{name}: hashes changed");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
