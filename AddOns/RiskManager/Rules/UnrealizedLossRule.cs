// UnrealizedLossRule.cs
// Triggers FLATTEN when floating (unrealized) loss exceeds limit
// NO LOCKOUT - just closes position to stop the bleeding
// Can trade again immediately after position is closed

#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Unrealized (Floating) Loss Rule
    /// - Only looks at OPEN POSITION P&L
    /// - Does NOT include realized (closed) P&L
    /// - Causes FLATTEN only - NO lockout
    /// - Can trade again immediately after flatten
    /// - Use this as a "position stop loss" across all positions
    /// </summary>
    public class UnrealizedLossRule : RiskRule
    {
        public double MaxLoss { get; set; } = 100;

        public UnrealizedLossRule()
        {
            Name = "Unrealized Loss";
            Description = "Flattens when floating loss exceeds limit (no lockout)";
            Action = RuleAction.FlattenOnly; // NOT Lockout!
            ResetSchedule = ResetSchedule.Never; // Always active, no reset needed
        }

        public override bool IsViolated(RiskContext context)
        {
            // ONLY check unrealized P&L (open positions)
            // Realized (closed) P&L is NOT included
            return context.UnrealizedPnL <= -MaxLoss;
        }

        public override string GetViolationMessage(RiskContext context)
        {
            return $"Floating loss limit: ${Math.Abs(context.UnrealizedPnL):F2} / ${MaxLoss:F2} - FLATTENING";
        }

        public override string GetStatusText(RiskContext context)
        {
            return $"Floating: ${context.UnrealizedPnL:F2}";
        }
    }
}
