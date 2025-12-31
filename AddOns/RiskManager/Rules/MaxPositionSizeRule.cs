// MaxPositionSizeRule.cs
// Per-symbol position size limit - flatten ONLY that position if exceeded

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Maximum position size per symbol.
    /// If position exceeds limit, flatten ONLY that position (not all).
    /// Supports per-symbol limits: "GC=2, ES=3, NQ=2"
    /// </summary>
    public class MaxPositionSizeRule : RiskRule
    {
        public int DefaultMax { get; set; } = 5;
        public string PerSymbolConfig { get; set; } = "";

        // Parsed limits - symbol root -> max contracts
        private Dictionary<string, int> _symbolLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public MaxPositionSizeRule()
        {
            Name = "Max Position Size";
            Description = "Flatten position if size exceeds per-symbol limit";
            Action = RuleAction.FlattenPosition;
        }

        /// <summary>
        /// Parse the per-symbol config string: "GC=2, ES=3, NQ=2"
        /// </summary>
        public void ParseConfig()
        {
            _symbolLimits.Clear();
            if (string.IsNullOrWhiteSpace(PerSymbolConfig)) return;

            var pairs = PerSymbolConfig.Split(',');
            foreach (var pair in pairs)
            {
                var parts = pair.Trim().Split('=');
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int max))
                {
                    _symbolLimits[parts[0].Trim().ToUpper()] = max;
                }
            }
        }

        /// <summary>
        /// Get max contracts for a symbol (checks symbol root like "GC" from "GC 02-26")
        /// </summary>
        private int GetMaxForSymbol(string instrumentName)
        {
            // Extract symbol root (e.g., "GC" from "GC 02-26")
            var root = instrumentName.Split(' ')[0].ToUpper();

            if (_symbolLimits.TryGetValue(root, out int limit))
                return limit;

            // Also try full name
            if (_symbolLimits.TryGetValue(instrumentName.ToUpper(), out limit))
                return limit;

            return DefaultMax;
        }

        public override bool IsViolated(RiskContext context)
        {
            if (context.OpenPositions == null) return false;

            // Check each position against its symbol-specific limit
            foreach (var pos in context.OpenPositions.Values)
            {
                int maxForSymbol = GetMaxForSymbol(pos.Instrument);
                if (pos.Quantity > maxForSymbol)
                {
                    context.ViolatingInstrument = pos.Instrument;
                    return true;
                }
            }
            return false;
        }

        public override string GetViolationMessage(RiskContext context)
        {
            var instrument = context.ViolatingInstrument ?? "Unknown";
            var qty = context.OpenPositions?.Values
                .FirstOrDefault(p => p.Instrument == instrument)?.Quantity ?? 0;
            var max = GetMaxForSymbol(instrument);
            return $"Position size exceeded: {instrument} has {qty} contracts (max: {max})";
        }

        public override string GetStatusText(RiskContext context)
        {
            if (_symbolLimits.Count > 0)
                return $"Per-symbol limits configured";
            return $"Default max: {DefaultMax} contracts";
        }
    }
}
