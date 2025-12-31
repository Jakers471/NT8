// SymbolBlockRule.cs
// Blocks trading on specific instruments and flattens existing positions

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Symbol block list rule.
    /// If position exists in blocked symbol → flatten immediately.
    /// Also blocks new orders on blocked symbols.
    /// </summary>
    public class SymbolBlockRule : RiskRule
    {
        public string BlockListConfig { get; set; } = "";
        private HashSet<string> _blockedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public SymbolBlockRule()
        {
            Name = "Symbol Block";
            Description = "Block and flatten specified symbols";
            Action = RuleAction.FlattenPosition;  // Flatten blocked position
        }

        /// <summary>
        /// Parse the comma-separated block list config: "CL, NG, HO"
        /// </summary>
        public void ParseConfig()
        {
            _blockedSymbols.Clear();
            if (string.IsNullOrWhiteSpace(BlockListConfig)) return;

            var symbols = BlockListConfig.Split(',');
            foreach (var sym in symbols)
            {
                var trimmed = sym.Trim().ToUpper();
                if (!string.IsNullOrEmpty(trimmed))
                    _blockedSymbols.Add(trimmed);
            }
        }

        /// <summary>
        /// Check if a symbol is in the block list (matches root)
        /// </summary>
        private bool IsBlocked(string instrumentName)
        {
            if (string.IsNullOrEmpty(instrumentName)) return false;

            // Extract symbol root (e.g., "GC" from "GC 02-26")
            var root = instrumentName.Split(' ')[0].ToUpper();

            // Check exact match on root
            if (_blockedSymbols.Contains(root))
                return true;

            // Also check if any blocked symbol is contained in full name
            return _blockedSymbols.Any(blocked =>
                instrumentName.IndexOf(blocked, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public override bool IsViolated(RiskContext context)
        {
            if (_blockedSymbols.Count == 0) return false;
            if (context.OpenPositions == null) return false;

            // Check if any open position is in a blocked symbol
            foreach (var pos in context.OpenPositions.Values)
            {
                if (IsBlocked(pos.Instrument))
                {
                    context.ViolatingInstrument = pos.Instrument;
                    return true;
                }
            }

            // Also check pending orders
            if (!string.IsNullOrEmpty(context.PendingOrderSymbol) && IsBlocked(context.PendingOrderSymbol))
            {
                context.ViolatingInstrument = context.PendingOrderSymbol;
                return true;
            }

            return false;
        }

        public override string GetViolationMessage(RiskContext context)
        {
            var instrument = context.ViolatingInstrument ?? "Unknown";
            return $"SYMBOL BLOCKED: {instrument} is on block list. Position closed.";
        }

        public override string GetStatusText(RiskContext context)
        {
            if (_blockedSymbols.Count == 0)
                return "No symbols blocked";
            return $"Blocked: {string.Join(", ", _blockedSymbols)}";
        }
    }
}
