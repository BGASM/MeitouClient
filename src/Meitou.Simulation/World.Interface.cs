namespace Meitou.Simulation;

public sealed partial class World
{
    /// <summary>Applies inventory, selection and open-shop transactions between ticks while paused, without advancing time or running simulation phases.</summary>
    public void ApplyPausedInterfaceCommands()
    {
        if (Characters.Phase != TablePhase.Outside) throw new InvalidOperationException("interface commands require a tick boundary");
        var due = Commands.TakeDue(Tick, c => c is SelectCommand or InspectCommand or Items.MoveInventoryItem or Items.ArrangeInventory
            or Trading.BuyItem or Trading.SellItem or Trading.CloseTrade
            or Construction.PlaceBuildings or Construction.CancelSite or Construction.PauseSite);
        if (due.Count == 0) return;
        Characters.BeginTick();
        Characters.Phase = TablePhase.Inputs;
        try
        {
            foreach (var system in systems)
                if (system is PlayerSystem or Trading.TradeSystem or Construction.ConstructionSystem) system.Inputs(this, due);
        }
        finally
        {
            Characters.Phase = TablePhase.Outside;
            Characters.EndTick();
        }
        foreach (var trade in systems.OfType<Trading.TradeSystem>()) trade.RefreshView(this);
        if (Settings.PublishSnapshots)
        {
            var previous = PreviousSnapshot;
            Publish();
            PreviousSnapshot = previous;
        }
    }
}
