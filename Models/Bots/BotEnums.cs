namespace AutoSignals.Models.Bots
{
    public enum BotType
    {
        DCA,
        Grid,
        Rebalance,
        ArbitrageScanner
    }

    public enum BotStatus
    {
        Idle,
        Running,
        Paused,
        Completed,
        Error,
        Stopping
    }

    public enum GridMode
    {
        Arithmetic,
        Geometric
    }
}
