using Meitou.Data.Gameplay;

namespace Meitou.Tests.Gameplay;

public class ItemIconTests
{
    [Theory]
    [InlineData("1012-gamedata.base.686-gamedata.base", "1012-gamedata.base", "686-gamedata.base", "")]
    [InlineData("1020-gamedata.base.1059-gamedata.base.1254-gamedata.base", "1020-gamedata.base", "1059-gamedata.base", "1254-gamedata.base")]
    [InlineData("100-limbs.mod.120-limbs.mod", "100-limbs.mod", "120-limbs.mod", "")]
    [InlineData("1020-gamedata.base.52293-rebirth.mod", "1020-gamedata.base", "52293-rebirth.mod", "")]
    public void A_cache_file_name_splits_into_item_material_and_colour(string name, string item, string material, string colour) =>
        Assert.Equal((item, material, colour), ItemIconCache.Parse(name));

    [Theory]
    [InlineData("1000-gamedata.base")]
    [InlineData("notes")]
    public void Other_names_are_not_icons(string name) => Assert.Null(ItemIconCache.Parse(name));

    [Fact]
    public void The_cache_prefers_the_exact_material_then_any_colour_then_any_file_of_the_item()
    {
        string dir = Directory.CreateTempSubdirectory("meitou-icons").FullName;
        try
        {
            foreach (string name in new[] { "1-a.mod.2-a.mod.png", "1-a.mod.3-a.mod.9-a.mod.png", "1-a.mod.3-a.mod.png" }) File.WriteAllBytes(Path.Combine(dir, name), []);
            var cache = new ItemIconCache(dir);
            Assert.EndsWith("1-a.mod.3-a.mod.png", cache.Find("1-a.mod", "3-a.mod"));
            Assert.EndsWith("1-a.mod.2-a.mod.png", cache.Find("1-a.mod", "2-a.mod"));
            Assert.NotNull(cache.Find("1-a.mod", "7-a.mod"));
            Assert.Null(cache.Find("2-a.mod", ""));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Every_icon_file_a_base_game_record_names_exists()
    {
        Assert.SkipWhen(InstallData.Install is null || InstallData.BaseGame is null, "no Kenshi install");
        var locator = new Meitou.Rendering.AssetLocator(InstallData.Install!);
        var named = InstallData.BaseGame!.Records.Values.Where(r => ItemIcons.StaticIconOf(r).Length > 0).ToList();
        Assert.NotEmpty(named);
        Assert.All(named, r => Assert.NotNull(locator.Find(ItemIcons.StaticIconOf(r))));
    }
}
