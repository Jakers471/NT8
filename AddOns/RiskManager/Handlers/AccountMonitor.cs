// AccountMonitor.cs
// Event-driven account monitoring - NO POLLING
// Subscribes to: Account updates, Order updates, Position updates, Execution updates

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Code;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    public class AccountMonitor
    {
        // Dependencies
        private readonly RuleEngine _ruleEngine;
        private readonly ActionHandler _actionHandler;

        // State
        private readonly Dictionary<Account, AccountState> _accountStates = new Dictionary<Account, AccountState>();
        private readonly object _lock = new object();
        private bool _isMonitoring = false;

        // Track trade history for frequency rules
        private readonly List<TradeRecord> _tradeHistory = new List<TradeRecord>();

        // Throttle logging - prevent spam
        private DateTime _lastPnLLogTime = DateTime.MinValue;
        private double _lastLoggedPnL = double.MinValue;
        private const double PNL_LOG_INTERVAL_SECONDS = 3.0;
        private const double PNL_CHANGE_THRESHOLD = 10.0; // Log if P&L changes by $10+

        // Throttle flatten retries
        private DateTime _lastFlattenAttempt = DateTime.MinValue;
        private const double FLATTEN_RETRY_SECONDS = 2.0;

        public AccountMonitor(RuleEngine ruleEngine, ActionHandler actionHandler)
        {
            _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
            _actionHandler = actionHandler ?? throw new ArgumentNullException(nameof(actionHandler));
        }

        public void StartMonitoring()
        {
            lock (_lock)
            {
                if (_isMonitoring) return;

                // Subscribe to account connection events
                Account.AccountStatusUpdate += OnAccountStatusUpdate;

                // Subscribe to SIM accounts only (filter out live accounts)
                foreach (var account in Account.All.Where(a =>
                    a.Connection != null &&
                    a.Connection.Status == ConnectionStatus.Connected &&
                    a.Name.IndexOf("Sim", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    SubscribeToAccount(account);
                }

                _isMonitoring = true;
                Log("AccountMonitor: Started - listening to SIM accounts only");
            }
        }

        public void StopMonitoring()
        {
            lock (_lock)
            {
                if (!_isMonitoring) return;

                // Unsubscribe from all
                Account.AccountStatusUpdate -= OnAccountStatusUpdate;

                foreach (var account in _accountStates.Keys.ToList())
                {
                    UnsubscribeFromAccount(account);
                }

                _accountStates.Clear();
                _isMonitoring = false;
                Log("AccountMonitor: Stopped");
            }
        }

        private void SubscribeToAccount(Account account)
        {
            if (account == null || _accountStates.ContainsKey(account)) return;

            // Create state tracker for this account
            var state = new AccountState
            {
                Account = account,
                DayStartBalance = GetCurrentBalance(account),
                LastUpdated = DateTime.Now
            };
            _accountStates[account] = state;

            // EVENT SUBSCRIPTIONS - This is where the magic happens
            // NO POLLING - all event-driven!
            account.AccountItemUpdate += OnAccountItemUpdate;
            account.OrderUpdate += OnOrderUpdate;
            account.PositionUpdate += OnPositionUpdate;
            account.ExecutionUpdate += OnExecutionUpdate;

            Log($"AccountMonitor: Subscribed to account {account.Name}");
        }

        private void UnsubscribeFromAccount(Account account)
        {
            if (account == null) return;

            account.AccountItemUpdate -= OnAccountItemUpdate;
            account.OrderUpdate -= OnOrderUpdate;
            account.PositionUpdate -= OnPositionUpdate;
            account.ExecutionUpdate -= OnExecutionUpdate;

            _accountStates.Remove(account);
            Log($"AccountMonitor: Unsubscribed from account {account.Name}");
        }

        // ═══════════════════════════════════════════════════════════════
        // EVENT HANDLERS - Triggered instantly when something happens
        // ═══════════════════════════════════════════════════════════════

        private void OnAccountStatusUpdate(object sender, AccountStatusEventArgs e)
        {
            // New account connected or disconnected
            if (e.Status == ConnectionStatus.Connected)
            {
                // Only subscribe to Sim accounts
                if (e.Account.Name.IndexOf("Sim", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SubscribeToAccount(e.Account);
                }
            }
            else if (e.Status == ConnectionStatus.Disconnected)
            {
                UnsubscribeFromAccount(e.Account);
            }
        }

        private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
        {
            // P&L changed, balance changed, etc.
            // This fires on EVERY tick if you have a position - MUST throttle logging

            if (!_accountStates.TryGetValue(e.Account, out var state)) return;

            // Update state (always update, just throttle logging)
            state.RealizedPnL = e.Account.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
            state.UnrealizedPnL = e.Account.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);
            state.TotalPnL = state.RealizedPnL + state.UnrealizedPnL;
            state.LastUpdated = DateTime.Now;

            // Refresh per-position P&L data
            RefreshPositionPnL(e.Account, state);

            // THROTTLED LOGGING - only log if:
            // 1. Enough time has passed (3 seconds), OR
            // 2. P&L changed significantly ($10+)
            bool shouldLog = false;
            double timeSinceLastLog = (DateTime.Now - _lastPnLLogTime).TotalSeconds;
            double pnlChange = Math.Abs(state.TotalPnL - _lastLoggedPnL);

            if (timeSinceLastLog >= PNL_LOG_INTERVAL_SECONDS || pnlChange >= PNL_CHANGE_THRESHOLD)
            {
                shouldLog = true;
                _lastPnLLogTime = DateTime.Now;
                _lastLoggedPnL = state.TotalPnL;
            }

            if (shouldLog)
            {
                Log($"P&L: ${state.TotalPnL:F2} (Realized: ${state.RealizedPnL:F2}, Unrealized: ${state.UnrealizedPnL:F2})");
            }

            // Track peak for trailing drawdown
            if (state.TotalPnL > state.PeakPnL)
                state.PeakPnL = state.TotalPnL;

            // Check rules (this is fast, no logging here)
            EvaluateRules(e.Account, state, TriggerType.AccountUpdate);
        }

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            // Order state changed: Submitted, Working, Filled, Cancelled, Rejected

            if (!_accountStates.TryGetValue(e.Order.Account, out var state)) return;

            // Only log significant order states (not every intermediate state)
            if (e.Order.OrderState == OrderState.Submitted ||
                e.Order.OrderState == OrderState.Filled ||
                e.Order.OrderState == OrderState.Cancelled ||
                e.Order.OrderState == OrderState.Rejected)
            {
                Log($"Order: {e.Order.Instrument.FullName} {e.Order.OrderAction} {e.Order.Quantity} - {e.Order.OrderState}");
            }

            // ═══════════════════════════════════════════════════════════════
            // LOCKOUT ORDER HANDLING
            // ALWAYS allow closing orders, ALWAYS block opening orders
            // ═══════════════════════════════════════════════════════════════
            if (e.Order.OrderState == OrderState.Submitted)
            {
                // First check: Is this a closing order? ALWAYS ALLOW regardless of lockout
                if (IsClosingOrder(e.Order, state))
                {
                    Log($"ALLOWED: Closing order permitted");
                    // Don't block - let it through
                }
                else if (_actionHandler.IsLockedOut(e.Order.Account))
                {
                    // This order would OPEN or INCREASE a position during lockout - BLOCK IT
                    Log($"BLOCKED: Order would open/increase position during lockout");
                    _actionHandler.CancelOrder(e.Order);
                    return;
                }
            }

            // Check rules on order submission (only if not locked out)
            if (e.Order.OrderState == OrderState.Submitted && !_actionHandler.IsLockedOut(e.Order.Account))
            {
                var context = BuildContext(state, e.Order);
                EvaluateRules(e.Order.Account, state, TriggerType.OrderSubmitted, context);
            }
        }

        private void OnPositionUpdate(object sender, PositionEventArgs e)
        {
            // Position changed: new position, closed position, size changed

            if (!_accountStates.TryGetValue(e.Position.Account, out var state)) return;

            Log($"Position: {e.Position.Instrument.FullName} {e.Position.MarketPosition} {e.Position.Quantity}");

            // Update position tracking in state
            state.UpdatePosition(e.Position);

            // Track trade state for duplicate action prevention
            if (e.Position.MarketPosition == MarketPosition.Flat)
            {
                // Position closed - update trade tracker
                _actionHandler.OnPositionClosed(e.Position.Account, e.Position.Instrument);

                if (_actionHandler.IsLockedOut(e.Position.Account))
                {
                    Log($"Position closed successfully during lockout");
                }
            }
            else
            {
                // Position opened/changed - track it
                _actionHandler.OnPositionOpened(e.Position.Account, e.Position.Instrument);

                // CRITICAL: If locked out and a position just opened, flatten it immediately
                // This catches orders that filled at exchange before cancel arrived
                if (_actionHandler.IsLockedOut(e.Position.Account))
                {
                    Log($"*** RACE CONDITION: Position opened during lockout - flattening immediately ***");
                    _actionHandler.FlattenPosition(e.Position.Account, e.Position.Instrument.FullName);
                    return;
                }
            }

            // Skip rule evaluation if locked out
            if (_actionHandler.IsLockedOut(e.Position.Account))
                return;

            EvaluateRules(e.Position.Account, state, TriggerType.PositionUpdate);
        }

        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            // Trade executed (fill)

            if (!_accountStates.TryGetValue(e.Execution.Account, out var state)) return;

            Log($"Execution: {e.Execution.Instrument.FullName} {e.Execution.MarketPosition} {e.Execution.Quantity} @ {e.Execution.Price}");

            // Track trade for frequency rules
            _tradeHistory.Add(new TradeRecord
            {
                Time = e.Execution.Time,
                Instrument = e.Execution.Instrument.FullName,
                Action = e.Execution.MarketPosition.ToString(),
                Quantity = e.Execution.Quantity,
                Price = e.Execution.Price,
                Account = e.Execution.Account.Name
            });

            // Keep only last 24 hours
            var cutoff = DateTime.Now.AddHours(-24);
            _tradeHistory.RemoveAll(t => t.Time < cutoff);

            // Skip rule evaluation if locked out
            if (_actionHandler.IsLockedOut(e.Execution.Account))
                return;

            EvaluateRules(e.Execution.Account, state, TriggerType.Execution);
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPER: Determine if order would CLOSE a position
        // ═══════════════════════════════════════════════════════════════

        private bool IsClosingOrder(Order order, AccountState state)
        {
            // Check REAL account positions, not our tracked state
            var account = order.Account;
            var instrument = order.Instrument;

            // Find actual position from account
            var realPosition = account.Positions.FirstOrDefault(p => p.Instrument == instrument);

            // If no real position, this order would OPEN - not closing
            if (realPosition == null || realPosition.MarketPosition == MarketPosition.Flat)
                return false;

            // Check if order direction is opposite to position direction
            // LONG position + SELL = CLOSING
            // SHORT position + BUY = CLOSING

            if (realPosition.MarketPosition == MarketPosition.Long)
            {
                // Selling when long = closing
                return order.OrderAction == OrderAction.Sell ||
                       order.OrderAction == OrderAction.SellShort;
            }

            if (realPosition.MarketPosition == MarketPosition.Short)
            {
                // Buying when short = closing
                return order.OrderAction == OrderAction.Buy ||
                       order.OrderAction == OrderAction.BuyToCover;
            }

            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        // RULE EVALUATION
        // ═══════════════════════════════════════════════════════════════

        private void EvaluateRules(Account account, AccountState state, TriggerType trigger, RiskContext context = null)
        {
            try
            {
                // FAST PATH: Skip if locked out (O(1) lookup)
                if (RiskState.Instance.IsLocked(account.Name))
                    return;

                // FAST PATH: Skip if in cooldown
                if (!RiskState.Instance.CanTakeAction(account.Name))
                    return;

                // Build context if not provided
                if (context == null)
                    context = BuildContext(state);

                // Evaluate all rules
                var result = _ruleEngine.Evaluate(context);

                // Handle violations
                if (result.HasViolations)
                {
                    foreach (var violation in result.Violations)
                    {
                        Log($"RULE VIOLATED: {violation.Rule.Name} - {violation.Message}");
                    }

                    // Execute the most severe action
                    _actionHandler.Execute(account, result);
                }
            }
            catch (Exception ex)
            {
                Log($"Error evaluating rules: {ex.Message}");
            }
        }

        private RiskContext BuildContext(AccountState state, Order pendingOrder = null)
        {
            return new RiskContext
            {
                Account = state.Account,
                // Use ADJUSTED P&L (from baseline) for rule evaluation
                TotalDailyPnL = state.AdjustedTotalPnL,
                RealizedPnL = state.AdjustedRealizedPnL,
                UnrealizedPnL = state.UnrealizedPnL, // Unrealized doesn't use baseline
                PeakPnL = state.PeakPnL,
                TradeHistory = _tradeHistory.Where(t => t.Account == state.Account.Name).ToList(),
                OpenPositions = state.Positions,
                PendingOrder = pendingOrder,
                ConsecutiveLosses = state.ConsecutiveLosses
            };
        }

        /// <summary>
        /// Reset the P&L baseline for an account - rules will measure from this point
        /// </summary>
        public void ResetPnLBaseline(Account account)
        {
            if (_accountStates.TryGetValue(account, out var state))
            {
                state.ResetBaseline();
                Log($"P&L baseline reset for {account.Name}. New baseline: ${state.RealizedPnLBaseline:F2}");
            }
        }

        /// <summary>
        /// Reset P&L baseline for all monitored accounts
        /// </summary>
        public void ResetAllBaselines()
        {
            Log($"ResetAllBaselines called - {_accountStates.Count} accounts tracked");
            if (_accountStates.Count == 0)
            {
                Log("WARNING: No accounts being tracked! Baseline reset failed.");
                return;
            }
            foreach (var state in _accountStates.Values)
            {
                var oldBaseline = state.RealizedPnLBaseline;
                state.ResetBaseline();
                Log($"P&L baseline reset for {state.Account.Name}: ${oldBaseline:F2} -> ${state.RealizedPnLBaseline:F2}");
                Log($"  Adjusted P&L now: ${state.AdjustedTotalPnL:F2} (was ${state.TotalPnL - oldBaseline:F2})");
            }
        }

        private void RefreshPositionPnL(Account account, AccountState state)
        {
            try
            {
                foreach (var pos in account.Positions)
                {
                    if (pos.MarketPosition == MarketPosition.Flat) continue;

                    var key = pos.Instrument.FullName;
                    if (state.Positions.TryGetValue(key, out var posInfo))
                    {
                        posInfo.UnrealizedPnL = pos.GetUnrealizedProfitLoss(NinjaTrader.Cbi.PerformanceUnit.Currency);
                    }
                }
            }
            catch { }
        }

        private double GetCurrentBalance(Account account)
        {
            return account.Get(AccountItem.CashValue, Currency.UsDollar);
        }

        private void Log(string message)
        {
            Output.Process($"[RiskManager] {DateTime.Now:HH:mm:ss.fff} {message}", PrintTo.OutputTab1);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SUPPORTING TYPES
    // ═══════════════════════════════════════════════════════════════

    public enum TriggerType
    {
        AccountUpdate,
        OrderSubmitted,
        PositionUpdate,
        Execution,
        Timer
    }

    public class AccountState
    {
        public Account Account { get; set; }
        public double DayStartBalance { get; set; }
        public double RealizedPnL { get; set; }
        public double UnrealizedPnL { get; set; }
        public double TotalPnL { get; set; }
        public double PeakPnL { get; set; }
        public int ConsecutiveLosses { get; set; }
        public DateTime LastUpdated { get; set; }
        public Dictionary<string, PositionInfo> Positions { get; } = new Dictionary<string, PositionInfo>();

        // Baseline for P&L tracking - allows "reset" without changing actual account
        public double RealizedPnLBaseline { get; set; } = 0;
        public double AdjustedRealizedPnL => RealizedPnL - RealizedPnLBaseline;
        public double AdjustedTotalPnL => TotalPnL - RealizedPnLBaseline;

        public void ResetBaseline()
        {
            RealizedPnLBaseline = RealizedPnL;
        }

        public void UpdatePosition(Position position)
        {
            var key = position.Instrument.FullName;
            if (position.MarketPosition == MarketPosition.Flat)
            {
                Positions.Remove(key);
            }
            else
            {
                // Get per-position unrealized P&L
                double positionPnL = 0;
                try
                {
                    positionPnL = position.GetUnrealizedProfitLoss(NinjaTrader.Cbi.PerformanceUnit.Currency);
                }
                catch { }

                Positions[key] = new PositionInfo
                {
                    Instrument = key,
                    Direction = position.MarketPosition,
                    Quantity = position.Quantity,
                    AvgPrice = position.AveragePrice,
                    UnrealizedPnL = positionPnL
                };
            }
        }
    }

    public class PositionInfo
    {
        public string Instrument { get; set; }
        public MarketPosition Direction { get; set; }
        public int Quantity { get; set; }
        public double AvgPrice { get; set; }
        public double UnrealizedPnL { get; set; }
    }

    public class TradeRecord
    {
        public DateTime Time { get; set; }
        public string Instrument { get; set; }
        public string Action { get; set; }
        public int Quantity { get; set; }
        public double Price { get; set; }
        public string Account { get; set; }
    }
}
