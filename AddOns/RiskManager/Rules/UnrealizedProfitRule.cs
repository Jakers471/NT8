// UnrealizedProfitRule.cs
// PER-POSITION take profit - flatten ONLY that position
// NO LOCKOUT - just locks in the profit

#region Using declarations
using System;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Unrealized (Floating) Profit Rule - PER POSITION
    /// - Checks EACH position's unrealized P&L individually
    /// - Flattens ONLY the profitable position (not all)
    /// - NO lockout - can trade again immediately
    /// - Use this as a per-position take profit
    /// </summary>
    public class UnrealizedProfitRule : RiskRule
    {
        public double ProfitTarget { get; set; } = 200;

        public UnrealizedProfitRule()
        {
            Name = "Take Profit (Per Position)";
            Description = "Flattens position when its profit hits target";
            Action = RuleAction.FlattenPosition; // Flatten SINGLE position only
            ResetSchedule = ResetSchedule.Never;
        }

        public override bool IsViolated(RiskContext context)
        {
            if (context.OpenPositions == null) return false;

            // Check each position's unrealized P&L
            foreach (var pos in context.OpenPositions.Values)
            {
                if (pos.UnrealizedPnL >= ProfitTarget)
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
            return $"Take profit hit: {instrument} at +${pnl:F2} (target: +${ProfitTarget:F2})";
        }

        public override string GetStatusText(RiskContext context)
        {
            return $"Take profit per position: +${ProfitTarget:F2}";
        }
    }
}
