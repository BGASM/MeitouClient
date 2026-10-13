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
        ["wander"] = [0xBC8A8D2555342F64UL, 0x01B9F04CEECFC9DCUL, 0xC245D0A02BE9C3C8UL, 0xB75698182A691537UL, 0x14A433DA8F141ADBUL, 0xA33D262B7012C799UL],
        ["player"] = [0x77EDC81BF937F092UL, 0x8A67DEADA09CD14AUL, 0x14C401C9E389E083UL, 0xF4AB968B475D8509UL, 0xE5F80033999318F8UL, 0x9C86A9731033C5E9UL],
        ["roaming"] = [0x430FECD54DB9E882UL, 0x6173674F3396B63FUL, 0x3D8C94FDE7BA81AEUL, 0xD05C1323E6B1DB0CUL, 0x7A0C52EE8018D926UL, 0xDC75A9E4EC4DA1DFUL],
        ["bodies"] = [0x18A1C10FEAACA2B6UL, 0xD4940B65AAA84877UL, 0x22E84AB7D372DC41UL, 0x286440AAD535F604UL, 0x12D60B3A868E416DUL, 0x9AAC8F4693B4FE41UL, 0xF519502F469111BCUL],
        ["fight"] = [0x2557952221719BAFUL, 0xF68A07DF31FA196CUL, 0xBF5EDFD10C8CB54EUL, 0x70E74BD60098E25AUL, 0x21D3A6C5CFF59973UL],
        ["duels"] = [0x93FF705B23BF7AA7UL, 0x014160EF7BECC9F2UL, 0x385E394C150AF811UL, 0x794A365EA4399DE3UL, 0xA826F99D0141F3BCUL, 0x563E37C80BB9CDECUL],
        ["wander-parts"] = [0x326800801ABC5589UL, 0x21EE176FA3513AFDUL, 0xE6BF51A0B879E539UL, 0x98E5EB8154770A27UL],
        ["town-parts"] = [0xC3855E193F97441FUL, 0xB08E3142DB568DBAUL, 0x088F12A1A749C969UL, 0x27BFB98B9578D8DDUL],
        ["first-aid"] = [0xE266DA0ACE7DF122UL, 0x9251D7C3C8FB77BFUL, 0x628463501DA7B78DUL, 0x2321130E8C044E72UL, 0x8F41E078A2C641AEUL, 0x1554F7C5D7EBEB64UL],
        ["aggro"] = [0xAEB28C309A543A76UL, 0xE8604C064C418801UL, 0x4C07F8D322A48267UL, 0xAF295C80F0774749UL, 0x158C2C0F5E761BDDUL],
        ["ranged"] = [0x55829FBFFC6536BAUL, 0xDF510FFA17E0276EUL, 0x94205CBFCC9ED4EEUL, 0x3C2DD772F311F282UL, 0x6218849B5FFBDAD4UL],
        ["carry"] = [0xABCD1BD102FEBA6CUL, 0xCBF8D95CDC8ADA4CUL, 0x406C8D9A4F110019UL, 0x2A6C29409431CE5BUL, 0x545AF6B38D6E2B76UL, 0x29E0EC1D1FDB9F83UL, 0xC659906D510BF505UL],
        ["crime"] = [0xCB59A55C139FF9FCUL, 0xF690330AB0D1307CUL, 0x6EC7C7D45C876A63UL, 0xD84B61ECA9F7EA90UL, 0x0D656CFD4EE951C6UL, 0xCBC00FD31AE500BEUL],
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
