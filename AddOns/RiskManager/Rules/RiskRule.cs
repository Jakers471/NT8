// RiskRule.cs
// Base class for all risk rules - supports lockout, flatten-only, and block-only actions

#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Actions that can be taken when a rule is violated
    /// Ordered by severity (higher = more severe)
    /// </summary>
    public enum RuleAction
    {
        None = 0,           // Just log, no action
        Alert = 1,          // Show alert, continue trading
        BlockOrder = 2,     // Block the specific order only
        FlattenPosition = 3,// Flatten SINGLE position only (per-position rules)
        FlattenOnly = 4,    // Flatten ALL positions but NO lockout - can trade again
        Lockout = 5         // Full lockout - flatten + block all new positions
    }

    /// <summary>
    /// How long a lockout lasts
    /// </summary>
    public enum LockoutDuration
    {
        UntilReset,         // Manual reset or timer reset
        Timed               // X minutes then auto-reset
    }

    /// <summary>
    /// When a rule's tracking should reset
    /// </summary>
    public enum ResetSchedule
    {
        Never,              // Never auto-reset (manual only)
        Daily,              // Reset at specific time each day
        Rolling             // Rolling window (e.g., last 30 minutes)
    }

    /// <summary>
    /// Base class for all risk rules.
    /// Inherit this and implement IsViolated() to create new rules.
    /// </summary>
    public abstract class RiskRule
    {
        // ═══════════════════════════════════════════════════════════════
        // IDENTITY
        // ═══════════════════════════════════════════════════════════════
        public string Name { get; set; } = "Unnamed Rule";
        public string Description { get; set; } = "";
        public bool Enabled { get; set; } = true;

        // ═══════════════════════════════════════════════════════════════
        // ACTION CONFIGURATION
        // ═══════════════════════════════════════════════════════════════
        public RuleAction Action { get; set; } = RuleAction.Alert;

        // Lockout settings (only used if Action == Lockout)
        public LockoutDuration LockoutType { get; set; } = LockoutDuration.UntilReset;
        public int LockoutMinutes { get; set; } = 0; // 0 = until reset

        // ═══════════════════════════════════════════════════════════════
        // RESET CONFIGURATION
        // ═══════════════════════════════════════════════════════════════
        public ResetSchedule ResetSchedule { get; set; } = ResetSchedule.Never;
        public TimeSpan DailyResetTime { get; set; } = new TimeSpan(18, 0, 0); // 6 PM default
        public int RollingWindowMinutes { get; set; } = 30;

        // Track when rule was last reset
        public DateTime LastResetTime { get; set; } = DateTime.MinValue;

        // ═══════════════════════════════════════════════════════════════
        // ABSTRACT METHODS - Implement in derived classes
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Override this to define when the rule is violated.
        /// Return true if the rule is currently being broken.
        /// </summary>
        public abstract bool IsViolated(RiskContext context);

        /// <summary>
        /// Override this to provide a custom violation message.
        /// </summary>
        public virtual string GetViolationMessage(RiskContext context)
        {
            return $"Rule '{Name}' violated";
        }

        /// <summary>
        /// Override this to provide current status text for UI.
        /// </summary>
        public virtual string GetStatusText(RiskContext context)
        {
            return Enabled ? "Active" : "Disabled";
        }

        // ═══════════════════════════════════════════════════════════════
        // RESET LOGIC
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Check if the rule should reset based on schedule
        /// </summary>
        public bool ShouldReset()
        {
            if (ResetSchedule == ResetSchedule.Never)
                return false;

            if (ResetSchedule == ResetSchedule.Daily)
            {
                var now = DateTime.Now;
                var todayReset = now.Date + DailyResetTime;

                // If we haven't reset today and we're past the reset time
                if (LastResetTime.Date < now.Date && now.TimeOfDay >= DailyResetTime)
                    return true;

                // Or if reset time already passed today but we haven't reset yet
                if (LastResetTime < todayReset && now >= todayReset)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Mark the rule as reset
        /// </summary>
        public virtual void Reset()
        {
            LastResetTime = DateTime.Now;
        }
    }
}
