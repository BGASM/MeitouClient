using Meitou.Data.Fcs;

namespace Meitou.Data.Gameplay.Economy;

/// <summary>
/// A town's or a faction's culture object (docs/game/economy.md section 7.1, the reader <c>FUN_1407fd9a0</c>): the ITEMS_CULTURE records of its <c>trade culture</c> list folded
/// into one. <c>trade prices</c> gives an item's price factor v0 / 100 with 0 meaning 100 (<b>Verified</b> against the data); when two listed cultures price the same item the later one
/// wins (a map assignment). <c>illegal goods</c> and <c>forbidden items</c> are kept as sets for the trade rules and the shop stock.
/// </summary>
public sealed class TradeCulture
{
    readonly Dictionary<string, float> prices = new(StringComparer.Ordinal);
    readonly HashSet<string> illegal = new(StringComparer.Ordinal);
    readonly HashSet<string> forbidden = new(StringComparer.Ordinal);

    /// <summary>Items with a price factor of their own.</summary>
    public IReadOnlyDictionary<string, float> Prices => prices;
    /// <summary><c>illegal goods</c>: contraband here.</summary>
    public IReadOnlySet<string> Illegal => illegal;
    /// <summary><c>forbidden items</c>: never spawn here.</summary>
    public IReadOnlySet<string> Forbidden => forbidden;

    /// <summary>The price factor of an item record, if this culture names it.</summary>
    public bool TryPrice(string itemId, out float factor) => prices.TryGetValue(itemId, out factor);

    /// <summary>
    /// The culture object of a <c>trade culture</c> list; null when the list has no ITEMS_CULTURE record (the game then has no object, and the price lookup falls through to the
    /// faction's, section 6).
    /// </summary>
    public static TradeCulture? From(GameDatabase db, IEnumerable<RecordLink> cultures)
    {
        TradeCulture? culture = null;
        foreach (var link in cultures)
        {
            if (db.Find(link.Id) is not { Type: FcsRecordType.ITEMS_CULTURE } record) continue;
            culture ??= new TradeCulture();
            foreach (var p in record.GetReferences("trade prices"))
                culture.prices[p.TargetStringId] = (p.Values.Value0 == 0 ? 100 : p.Values.Value0) * 0.01f;
            foreach (var p in record.GetReferences("illegal goods")) culture.illegal.Add(p.TargetStringId);
            foreach (var p in record.GetReferences("forbidden items")) culture.forbidden.Add(p.TargetStringId);
        }
        return culture;
    }
}
