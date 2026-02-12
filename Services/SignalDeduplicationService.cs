// Services/SignalDeduplicationService.cs
using AutoSignals.Models;
using System.Collections.Concurrent;

namespace AutoSignals.Services
{
    public class SignalDeduplicationService
    {
        // Key: TelegramGroupId, Value: Dictionary for that group
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Queue<Signal>>>
            _groupDeduplicationQueues = new();

        // Default capacity per symbol
        private const int MaxEntriesPerSymbol = 3;

        // Get or create deduplication queue for a specific Telegram group
        public ConcurrentDictionary<string, Queue<Signal>> GetOrCreateGroupQueue(string telegramGroupId)
        {
            return _groupDeduplicationQueues.GetOrAdd(telegramGroupId,
                _ => new ConcurrentDictionary<string, Queue<Signal>>());
        }

        // Check if a signal is a duplicate within a specific group
        public bool IsDuplicate(Signal newSignal, string telegramGroupId)
        {
            var groupQueue = GetOrCreateGroupQueue(telegramGroupId);
            var symbolKey = newSignal.Symbol.Replace("/USDT:USDT", "USDT");

            if (groupQueue.TryGetValue(symbolKey, out var symbolQueue))
            {
                // Check if we have a signal with similar entry and stoploss (within tolerance)
                return symbolQueue.Any(existingSignal =>
                    Math.Abs(existingSignal.Entry - newSignal.Entry) < 0.0001f &&
                    Math.Abs(existingSignal.Stoploss - newSignal.Stoploss) < 0.0001f &&
                    existingSignal.Provider == newSignal.Provider);
            }

            return false;
        }

        // Add a signal to the deduplication queue
        public void AddSignal(Signal signal, string telegramGroupId)
        {
            var groupQueue = GetOrCreateGroupQueue(telegramGroupId);
            var symbolKey = signal.Symbol.Replace("/USDT:USDT", "USDT");

            var symbolQueue = groupQueue.GetOrAdd(symbolKey, _ => new Queue<Signal>());

            // Add the new signal
            symbolQueue.Enqueue(signal);

            // Maintain maximum size
            while (symbolQueue.Count > MaxEntriesPerSymbol)
            {
                symbolQueue.Dequeue();
            }
        }

        // Clean up old queues (optional, for memory management)
        public void CleanupInactiveGroups(TimeSpan maxInactivity)
        {
            // Implementation depends on your needs
            // You could track last access time for each group
        }

        // Get statistics (for monitoring/debugging)
        public DeduplicationStats GetStats()
        {
            var stats = new DeduplicationStats
            {
                TotalGroups = _groupDeduplicationQueues.Count,
                TotalSymbols = _groupDeduplicationQueues.Values.Sum(g => g.Count),
                TotalSignals = _groupDeduplicationQueues.Values.Sum(g =>
                    g.Values.Sum(q => q.Count))
            };

            return stats;
        }

        public class DeduplicationStats
        {
            public int TotalGroups { get; set; }
            public int TotalSymbols { get; set; }
            public int TotalSignals { get; set; }
        }
    }
}