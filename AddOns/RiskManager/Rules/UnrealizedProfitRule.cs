// UnrealizedProfitRule.cs
// Triggers FLATTEN when floating (unrealized) profit exceeds target
// NO LOCKOUT - just closes position to lock in gains
// Can trade again immediately after position is closed

#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Unrealized (Floating) Profit Rule
    /// - Only looks at OPEN POSITION P&L
    /// - Causes FLATTEN only - NO lockout
    /// - Can trade again immediately after flatten
    /// - Use this as a "take profit" across all positions
    /// </summary>
    public class UnrealizedProfitRule : RiskRule
    {
        public double ProfitTarget { get; set; } = 200;

        public UnrealizedProfitRule()
        {
            Name = "Unrealized Profit";
            Description = "Flattens when floating profit hits target (no lockout)";
            Action = RuleAction.FlattenOnly; // NOT Lockout!
            ResetSchedule = ResetSchedule.Never; // Always active, no reset needed
        }

        public override bool IsViolated(RiskContext context)
        {
            // ONLY check unrealized P&L (open positions)
            return context.UnrealizedPnL >= ProfitTarget;
        }

        public override string GetViolationMessage(RiskContext context)
        {
            return $"Floating profit target: ${context.UnrealizedPnL:F2} / ${ProfitTarget:F2} - TAKING PROFIT";
        }

        public override string GetStatusText(RiskContext context)
        {
            return $"Floating: ${context.UnrealizedPnL:F2}";
        }
    }
}
