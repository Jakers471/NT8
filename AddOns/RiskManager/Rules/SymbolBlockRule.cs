// SymbolBlockRule.cs
// Blocks trading on specific instruments

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Symbol block list rule.
    /// Prevents orders on specified instruments.
    /// </summary>
    public class SymbolBlockRule : RiskRule
    {
        public List<string> BlockedSymbols { get; set; } = new List<string>();

        public SymbolBlockRule()
        {
            Name = "Symbol Block";
            Description = "Blocks trading on specific instruments";
            Action = RuleAction.BlockOrder;
        }

        public override bool IsViolated(RiskContext context)
        {
            // Only check if there's a pending order
            if (string.IsNullOrEmpty(context.PendingOrderSymbol))
                return false;

            // Check if the symbol is in the block list
            // Use contains for partial matching (e.g., "ES" matches "ES 03-25")
            return BlockedSymbols.Any(blocked =>
                context.PendingOrderSymbol.IndexOf(blocked, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public override string GetViolationMessage(RiskContext context)
        {
            return $"Symbol blocked: {context.PendingOrderSymbol}";
        }

        public override string GetStatusText(RiskContext context)
        {
            return $"{BlockedSymbols.Count} symbols blocked";
        }
    }
}
