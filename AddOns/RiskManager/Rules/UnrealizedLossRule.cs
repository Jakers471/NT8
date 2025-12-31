// UnrealizedLossRule.cs
// PER-POSITION floating loss limit - flatten ONLY that position
// NO LOCKOUT - just closes the losing position

#region Using declarations
using System;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Unrealized (Floating) Loss Rule - PER POSITION
    /// - Checks EACH position's unrealized P&L individually
    /// - Flattens ONLY the violating position (not all)
    /// - NO lockout - can trade again immediately
    /// - Use this as a per-position stop loss
    /// </summary>
    public class UnrealizedLossRule : RiskRule
    {
        public double MaxLoss { get; set; } = 100;

        public UnrealizedLossRule()
        {
            Name = "Unrealized Loss (Per Position)";
            Description = "Flattens position when its floating loss exceeds limit";
            Action = RuleAction.FlattenPosition; // Flatten SINGLE position only
            ResetSchedule = ResetSchedule.Never;
        }

        public override bool IsViolated(RiskContext context)
        {
            if (context.OpenPositions == null) return false;

            // Check each position's unrealized P&L
            foreach (var pos in context.OpenPositions.Values)
            {
                if (pos.UnrealizedPnL <= -MaxLoss)
                {
                    context.ViolatingInstrument = pos.Instrument;
                    context.ViolatingPositionPnL = pos.UnrealizedPnL;
                    return true;
                }
            }
            return false;
        }

        public override string GetViolationMessage(RiskContext context)
        {
            var instrument = context.ViolatingInstrument ?? "Unknown";
            var pnl = context.ViolatingPositionPnL;
            return $"Position loss limit: {instrument} at ${pnl:F2} (max: -${MaxLoss:F2})";
        }

        public override string GetStatusText(RiskContext context)
        {
            return $"Max loss per position: -${MaxLoss:F2}";
        }
    }
}
