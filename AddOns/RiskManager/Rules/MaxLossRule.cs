// MaxLossRule.cs
// Triggers LOCKOUT when TOTAL P&L (Realized + Unrealized) exceeds limit
// This is the "nuclear option" - counts everything

#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Total Daily Loss Rule (Realized + Unrealized combined)
    /// - Counts BOTH closed trades AND open position P&L
    /// - If floating loss pushes total over limit, triggers lockout
    /// - Use DailyRealizedLossRule if you only want to count closed trades
    /// </summary>
    public class MaxLossRule : RiskRule
    {
        public double MaxLoss { get; set; } = 500;

        public MaxLossRule()
        {
            Name = "Total Daily Loss";
            Description = "Lockout when total P&L (realized + unrealized) exceeds limit";
            Action = RuleAction.Lockout;
            ResetSchedule = ResetSchedule.Daily;
            DailyResetTime = new TimeSpan(18, 0, 0); // 6 PM default
        }

        public override bool IsViolated(RiskContext context)
        {
            // Check TOTAL P&L (realized + unrealized)
            // If floating loss pushes you over, you're out
            return context.TotalDailyPnL <= -MaxLoss;
        }

        public override string GetViolationMessage(RiskContext context)
        {
            return $"Total daily loss limit: ${Math.Abs(context.TotalDailyPnL):F2} / ${MaxLoss:F2}";
        }

        public override string GetStatusText(RiskContext context)
        {
            var remaining = MaxLoss + context.TotalDailyPnL;
            return $"Total: ${context.TotalDailyPnL:F2} | ${remaining:F2} remaining";
        }
    }
}
