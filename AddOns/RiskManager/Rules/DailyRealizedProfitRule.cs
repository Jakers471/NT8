// DailyRealizedProfitRule.cs
// Triggers LOCKOUT when REALIZED (closed trade) profits exceed target
// "Lock in your gains" - stop trading after hitting daily target
// Resets daily at configurable time

#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Daily Realized Profit Rule
    /// - Only counts CLOSED trades (realized P&L)
    /// - Causes LOCKOUT when profit target hit (stop trading while ahead)
    /// - Resets at configurable time each day
    /// </summary>
    public class DailyRealizedProfitRule : RiskRule
    {
        public double ProfitTarget { get; set; } = 1000;

        public DailyRealizedProfitRule()
        {
            Name = "Daily Realized Profit";
            Description = "Locks out when realized profits hit target (lock in gains)";
            Action = RuleAction.Lockout;
            ResetSchedule = ResetSchedule.Daily;
            DailyResetTime = new TimeSpan(18, 0, 0); // 6 PM default
            LockoutType = LockoutDuration.UntilReset;
        }

        public override bool IsViolated(RiskContext context)
        {
            // ONLY check realized P&L (closed trades)
            return context.RealizedPnL >= ProfitTarget;
        }

        public override string GetViolationMessage(RiskContext context)
        {
            return $"Daily profit target reached: ${context.RealizedPnL:F2} / ${ProfitTarget:F2}";
        }

        public override string GetStatusText(RiskContext context)
        {
            var toTarget = ProfitTarget - context.RealizedPnL;
            return $"Realized: ${context.RealizedPnL:F2} | ${toTarget:F2} to target";
        }
    }
}
