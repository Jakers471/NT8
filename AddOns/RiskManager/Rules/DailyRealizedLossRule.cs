// DailyRealizedLossRule.cs
// Triggers LOCKOUT when REALIZED (closed trade) losses exceed limit
// Resets daily at configurable time

#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Daily Realized Loss Rule
    /// - Only counts CLOSED trades (realized P&L)
    /// - Does NOT include floating/unrealized P&L
    /// - Causes LOCKOUT when triggered
    /// - Resets at configurable time each day
    /// </summary>
    public class DailyRealizedLossRule : RiskRule
    {
        public double MaxLoss { get; set; } = 500;

        public DailyRealizedLossRule()
        {
            Name = "Daily Realized Loss";
            Description = "Locks out when realized (closed) losses exceed limit";
            Action = RuleAction.Lockout;
            ResetSchedule = ResetSchedule.Daily;
            DailyResetTime = new TimeSpan(18, 0, 0); // 6 PM default (end of futures day)
            LockoutType = LockoutDuration.UntilReset;
        }

        public override bool IsViolated(RiskContext context)
        {
            // ONLY check realized P&L (closed trades)
            // Unrealized (floating) P&L is NOT included
            return context.RealizedPnL <= -MaxLoss;
        }

        public override string GetViolationMessage(RiskContext context)
        {
            return $"Daily realized loss limit: ${Math.Abs(context.RealizedPnL):F2} / ${MaxLoss:F2}";
        }

        public override string GetStatusText(RiskContext context)
        {
            var remaining = MaxLoss + context.RealizedPnL;
            return $"Realized: ${context.RealizedPnL:F2} | ${remaining:F2} remaining";
        }
    }
}
