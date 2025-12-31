// TradeFrequencyRule.cs
// Triggers when too many trades in a rolling time window

#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Trade frequency rule with rolling window.
    /// Triggers when X trades occur within Y minutes.
    /// Supports timed lockout that auto-resets.
    /// </summary>
    public class TradeFrequencyRule : RiskRule
    {
        public int MaxTrades { get; set; } = 10;
        public int WindowMinutes { get; set; } = 30;

        public TradeFrequencyRule()
        {
            Name = "Trade Frequency";
            Description = "Limits trades within a rolling time window";
            Action = RuleAction.Lockout;
            LockoutType = LockoutDuration.Timed;
            LockoutMinutes = 5;  // Default 5 min cooldown
        }

        public override bool IsViolated(RiskContext context)
        {
            int tradesInWindow = context.GetTradeCountInWindow(WindowMinutes);
            return tradesInWindow >= MaxTrades;
        }

        public override string GetViolationMessage(RiskContext context)
        {
            int count = context.GetTradeCountInWindow(WindowMinutes);
            return $"Trade frequency exceeded: {count}/{MaxTrades} trades in {WindowMinutes} minutes";
        }

        public override string GetStatusText(RiskContext context)
        {
            int count = context.GetTradeCountInWindow(WindowMinutes);
            return $"{count}/{MaxTrades} trades in {WindowMinutes}m";
        }
    }
}
