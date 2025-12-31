// RiskState.cs
// Central state machine - single source of truth for risk status
// Priority: Lockout > FlattenOnly > Alert
// Fast lookups, no redundant checks

#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.Code;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Central risk state manager. Single source of truth.
    /// Handles priority: if locked out, no other checks matter.
    /// </summary>
    public class RiskState
    {
        // Singleton for global access
        private static RiskState _instance;
        private static readonly object _instanceLock = new object();

        public static RiskState Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                            _instance = new RiskState();
                    }
                }
                return _instance;
            }
        }

        // Core state
        private readonly HashSet<string> _lockedAccounts = new HashSet<string>();
        private readonly TradeTracker _tradeTracker = new TradeTracker();
        private readonly Dictionary<string, DateTime> _lastActionTime = new Dictionary<string, DateTime>();
        private readonly object _lock = new object();

        // Minimum time between any action on same account
        private const int ACTION_COOLDOWN_MS = 500;

        public TradeTracker Trades => _tradeTracker;

        private RiskState() { }

        /// <summary>
        /// Check if account is locked - O(1)
        /// </summary>
        public bool IsLocked(string accountName)
        {
            lock (_lock)
            {
                return _lockedAccounts.Contains(accountName);
            }
        }

        /// <summary>
        /// Set account locked state
        /// </summary>
        public void SetLocked(string accountName, bool locked)
        {
            lock (_lock)
            {
                if (locked)
                    _lockedAccounts.Add(accountName);
                else
                    _lockedAccounts.Remove(accountName);
            }
        }

        /// <summary>
        /// Check if we should process rules for this account.
        /// Returns false if locked (skip all rule checks).
        /// </summary>
        public bool ShouldEvaluateRules(string accountName)
        {
            lock (_lock)
            {
                // If locked, no rule evaluation needed
                if (_lockedAccounts.Contains(accountName))
                    return false;

                return true;
            }
        }

        /// <summary>
        /// Check if we can take action on account (cooldown check)
        /// </summary>
        public bool CanTakeAction(string accountName)
        {
            lock (_lock)
            {
                if (_lastActionTime.TryGetValue(accountName, out var lastTime))
                {
                    if ((DateTime.Now - lastTime).TotalMilliseconds < ACTION_COOLDOWN_MS)
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Record that an action was taken
        /// </summary>
        public void RecordAction(string accountName)
        {
            lock (_lock)
            {
                _lastActionTime[accountName] = DateTime.Now;
            }
        }

        /// <summary>
        /// Full state check - should we act on this P&L update?
        /// Returns action to take, or None if nothing to do.
        /// </summary>
        public RiskAction GetRequiredAction(
            string accountName,
            string instrumentName,
            bool isLockedOut,
            bool hasViolation,
            RuleAction violationAction)
        {
            lock (_lock)
            {
                // Priority 1: Already locked out - no action needed
                if (isLockedOut || _lockedAccounts.Contains(accountName))
                {
                    return RiskAction.None; // Already handled
                }

                // Priority 2: No violation - nothing to do
                if (!hasViolation)
                {
                    return RiskAction.None;
                }

                // Priority 3: Check if we can act on this trade
                var tradeId = TradeTracker.GetTradeId(accountName, instrumentName);
                if (!_tradeTracker.CanActOn(tradeId))
                {
                    return RiskAction.None; // Trade already being closed
                }

                // Priority 4: Check cooldown
                if (!CanTakeAction(accountName))
                {
                    return RiskAction.None; // In cooldown
                }

                // Map rule action to risk action
                switch (violationAction)
                {
                    case RuleAction.Lockout:
                        return RiskAction.Lockout;
                    case RuleAction.FlattenOnly:
                        return RiskAction.Flatten;
                    case RuleAction.Alert:
                        return RiskAction.Alert;
                    default:
                        return RiskAction.None;
                }
            }
        }

        /// <summary>
        /// Clear all state (for testing/reset)
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _lockedAccounts.Clear();
                _tradeTracker.Clear();
                _lastActionTime.Clear();
            }
        }

        /// <summary>
        /// Get summary of current state
        /// </summary>
        public string GetStateSummary()
        {
            lock (_lock)
            {
                return $"Locked: {_lockedAccounts.Count}, ClosingTrades: {_tradeTracker.GetClosingCount("")}";
            }
        }
    }

    /// <summary>
    /// Actions the system can take
    /// </summary>
    public enum RiskAction
    {
        None,       // No action needed
        Alert,      // Just alert
        Flatten,    // Flatten positions only
        Lockout     // Full lockout
    }
}
