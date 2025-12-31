// TradeFrequencyRule.cs
// Limits trades within a rolling time window
// If limit hit → timed lockout that persists

#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Trade frequency rule with rolling window.
    /// Triggers when X trades occur within Y minutes.
    /// Results in timed lockout that persists across restarts.
    /// </summary>
    public class TradeFrequencyRule : RiskRule
    {
        public int MaxTrades { get; set; } = 5;
        public int WindowMinutes { get; set; } = 10;

        public TradeFrequencyRule()
        {
            Name = "Trade Frequency";
            Description = "Lockout if too many trades in rolling window";
            Action = RuleAction.Lockout;
            LockoutType = LockoutDuration.Timed;
            LockoutMinutes = 30;  // Default 30 min lockout
        }

        public override bool IsViolated(RiskContext context)
        {
            int tradesInWindow = context.GetTradeCountInWindow(WindowMinutes);
            return tradesInWindow >= MaxTrades;
        }

        public override string GetViolationMessage(RiskContext context)
        {
            int count = context.GetTradeCountInWindow(WindowMinutes);
            return $"TRADE FREQUENCY LIMIT: {count} trades in {WindowMinutes} min (max: {MaxTrades}). " +
                   $"LOCKED OUT for {LockoutMinutes} minutes.";
        }

        public override string GetStatusText(RiskContext context)
        {
            int count = context.GetTradeCountInWindow(WindowMinutes);
            return $"{count}/{MaxTrades} trades in {WindowMinutes}m window";
        }
    }
}
