// ActionHandler.cs
// Executes actions when rules are violated: flatten, cancel, lockout
// Persists lockout state to survive restarts

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Code;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Executes actions when rules are violated.
    /// Handles: Alerts, Flatten, Cancel, Lockout
    /// Persists lockouts to survive NT8 restarts.
    /// </summary>
    public class ActionHandler
    {
        // Lockout state per account (by name for persistence)
        private readonly Dictionary<string, LockoutState> _lockouts = new Dictionary<string, LockoutState>();
        private readonly object _lock = new object();

        // Cooldown after manual reset - prevents immediate re-trigger
        private DateTime _lastManualReset = DateTime.MinValue;
        private const int RESET_COOLDOWN_SECONDS = 5;

        // Use central state for fast lookups
        private RiskState State => RiskState.Instance;
        private TradeTracker Trades => RiskState.Instance.Trades;

        public ActionHandler()
        {
            // Load persisted lockouts on startup
            LoadPersistedLockouts();
        }

        /// <summary>
        /// Load lockouts from disk on startup
        /// </summary>
        private void LoadPersistedLockouts()
        {
            try
            {
                var persisted = StateManager.LoadLockouts();
                lock (_lock)
                {
                    foreach (var kvp in persisted)
                    {
                        var state = new LockoutState
                        {
                            IsLocked = kvp.Value.IsLocked,
                            Type = kvp.Value.Type,
                            StartedAt = kvp.Value.StartedAt,
                            ExpiresAt = kvp.Value.ExpiresAt,
                            ResetTime = kvp.Value.ResetTime,
                            Reason = kvp.Value.Reason
                        };
                        _lockouts[kvp.Key] = state;
                        // Sync to central state
                        State.SetLocked(kvp.Key, true);
                        LogWarning($"PERSISTED LOCKOUT LOADED: {kvp.Key} - {state.Reason}");
                    }
                }

                if (persisted.Count > 0)
                {
                    LogError($"*** {persisted.Count} ACTIVE LOCKOUT(S) RESTORED FROM PREVIOUS SESSION ***");
                }
                else
                {
                    LogInfo("No persisted lockouts found - clean start");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error loading persisted lockouts: {ex.Message}");
            }
        }

        /// <summary>
        /// Save current lockouts to disk
        /// </summary>
        private void PersistLockouts()
        {
            try
            {
                var data = new Dictionary<string, LockoutData>();
                lock (_lock)
                {
                    foreach (var kvp in _lockouts.Where(l => l.Value.IsLocked))
                    {
                        data[kvp.Key] = new LockoutData
                        {
                            AccountName = kvp.Key,
                            IsLocked = kvp.Value.IsLocked,
                            Type = kvp.Value.Type,
                            StartedAt = kvp.Value.StartedAt,
                            ExpiresAt = kvp.Value.ExpiresAt,
                            ResetTime = kvp.Value.ResetTime,
                            Reason = kvp.Value.Reason
                        };
                    }
                }
                StateManager.SaveLockouts(data);
            }
            catch (Exception ex)
            {
                LogError($"Error persisting lockouts: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if an account is currently locked out
        /// </summary>
        public bool IsLockedOut(Account account)
        {
            return IsLockedOut(account.Name);
        }

        /// <summary>
        /// Check if an account is currently locked out (by name)
        /// </summary>
        public bool IsLockedOut(string accountName)
        {
            // Allow trading during post-reset cooldown
            if (IsInResetCooldown())
                return false;

            lock (_lock)
            {
                if (!_lockouts.TryGetValue(accountName, out var state))
                    return false;

                // Check if timed lockout has expired
                if (state.Type == LockoutDuration.Timed && DateTime.Now >= state.ExpiresAt)
                {
                    _lockouts.Remove(accountName);
                    LogInfo($"Timed lockout expired for {accountName}");
                    PersistLockouts();
                    return false;
                }

                // Check if daily reset time has passed
                if (state.Type == LockoutDuration.UntilReset)
                {
                    var today = DateTime.Today;
                    var resetTime = today.Add(state.ResetTime);

                    // If we're past today's reset and lockout started before it
                    if (DateTime.Now >= resetTime && state.StartedAt < resetTime)
                    {
                        _lockouts.Remove(accountName);
                        LogInfo($"Daily reset passed - lockout cleared for {accountName}");
                        PersistLockouts();
                        return false;
                    }
                }

                return state.IsLocked;
            }
        }

        /// <summary>
        /// Execute the action based on rule evaluation result
        /// </summary>
        public void Execute(Account account, RuleResult result)
        {
            if (!result.HasViolations) return;

            switch (result.RequiredAction)
            {
                case RuleAction.Alert:
                    ShowAlert(result);
                    break;

                case RuleAction.BlockOrder:
                    ShowAlert(result);
                    break;

                case RuleAction.FlattenPosition:
                    // Flatten SINGLE position only
                    var instrument = result.ViolatingInstrument;
                    if (string.IsNullOrEmpty(instrument))
                    {
                        LogWarning("FlattenPosition called but no instrument specified");
                        return;
                    }

                    // Check cooldown per instrument
                    var tradeId = TradeTracker.GetTradeId(account.Name, instrument);
                    if (!Trades.CanActOn(tradeId))
                        return;

                    Trades.MarkClosing(tradeId);
                    State.RecordAction(account.Name);

                    // Get the specific rule that triggered this
                    var triggeringRule = result.Violations
                        .Where(v => v.Action == RuleAction.FlattenPosition)
                        .Select(v => v.Rule)
                        .FirstOrDefault();
                    var reason = result.Violations.FirstOrDefault()?.Message ?? "Rule violated";

                    // Get position P&L for logging
                    double positionPnL = result.ViolatingPositionPnL;
                    var pnlStr = positionPnL >= 0 ? $"+${positionPnL:F2}" : $"-${Math.Abs(positionPnL):F2}";

                    LogError("═══════════════════════════════════════════════════════");
                    LogError($"   POSITION CLOSED: {instrument}");
                    LogError($"   P&L: {pnlStr}");
                    LogError($"   Rule: {triggeringRule?.Name ?? "Unknown"}");
                    LogError($"   Reason: {reason}");
                    LogError("═══════════════════════════════════════════════════════");

                    // Record to closure history with position P&L
                    RecordClosureEvent(account, instrument, triggeringRule?.Name ?? "Unknown", reason, "FlattenPosition", positionPnL);

                    FlattenPosition(account, instrument);
                    ShowAlert(result);
                    break;

                case RuleAction.FlattenOnly:
                    // Check if we can take action (cooldown + trade state)
                    if (!State.CanTakeAction(account.Name))
                        return;

                    // Mark all positions as closing BEFORE flatten
                    MarkPositionsClosing(account);
                    State.RecordAction(account.Name);

                    var flattenRule = result.Violations
                        .Where(v => v.Action == RuleAction.FlattenOnly)
                        .Select(v => v.Rule)
                        .FirstOrDefault();
                    var flattenReason = result.Violations.FirstOrDefault()?.Message ?? "Rule violated";

                    LogError("═══════════════════════════════════════════════════════");
                    LogError($"   FLATTEN ALL TRIGGERED (no lockout)");
                    LogError($"   Rule: {flattenRule?.Name ?? "Unknown"}");
                    LogError($"   Reason: {flattenReason}");
                    LogError("═══════════════════════════════════════════════════════");

                    // Record to closure history
                    RecordClosureEvent(account, null, flattenRule?.Name ?? "Unknown", flattenReason, "FlattenAll");

                    FlattenAll(account);
                    ShowAlert(result);
                    break;

                case RuleAction.Lockout:
                    ExecuteLockout(account, result);
                    break;
            }
        }

        /// <summary>
        /// Full lockout: flatten, cancel, and block future orders
        /// </summary>
        private void ExecuteLockout(Account account, RuleResult result)
        {
            // Fast check - already locked?
            if (State.IsLocked(account.Name))
                return;

            // Don't re-trigger during post-reset cooldown
            if (IsInResetCooldown())
            {
                LogInfo("Lockout skipped - in post-reset cooldown");
                return;
            }

            lock (_lock)
            {
                // Double-check inside lock
                if (_lockouts.ContainsKey(account.Name) && _lockouts[account.Name].IsLocked)
                    return;

                // Get the rule that triggered this
                var triggerRule = result.Violations
                    .Where(v => v.Action == RuleAction.Lockout)
                    .Select(v => v.Rule)
                    .FirstOrDefault();

                // Set lockout state
                var state = new LockoutState
                {
                    IsLocked = true,
                    Type = triggerRule?.LockoutType ?? LockoutDuration.UntilReset,
                    StartedAt = DateTime.Now,
                    Reason = result.Violations.FirstOrDefault()?.Message ?? "Rule violated",
                    ResetTime = triggerRule?.DailyResetTime ?? new TimeSpan(18, 0, 0)
                };

                if (state.Type == LockoutDuration.Timed && triggerRule != null)
                {
                    state.ExpiresAt = DateTime.Now.AddMinutes(triggerRule.LockoutMinutes);
                }

                _lockouts[account.Name] = state;

                // Update central state for fast lookups
                State.SetLocked(account.Name, true);

                LogError("═══════════════════════════════════════════════════════");
                LogError($"   LOCKOUT ACTIVATED: {account.Name}");
                LogError($"   Reason: {state.Reason}");
                if (state.Type == LockoutDuration.Timed)
                    LogError($"   Expires: {state.ExpiresAt:HH:mm:ss}");
                else
                    LogError($"   Resets at: {state.ResetTime}");
                LogError("═══════════════════════════════════════════════════════");
            }

            // Persist to disk IMMEDIATELY
            PersistLockouts();

            // Record to closure history
            var lockoutRule = result.Violations
                .Where(v => v.Action == RuleAction.Lockout)
                .Select(v => v.Rule)
                .FirstOrDefault();
            var lockoutReason = result.Violations.FirstOrDefault()?.Message ?? "Rule violated";
            RecordClosureEvent(account, null, lockoutRule?.Name ?? "Unknown", lockoutReason, "Lockout");

            // Mark positions as closing, then execute
            MarkPositionsClosing(account);
            State.RecordAction(account.Name);

            // Execute lockout actions
            CancelAllOrders(account);
            FlattenAll(account);

            // Re-check for positions that filled during lockout (race condition)
            // Orders in flight at exchange may fill before cancel arrives
            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
            {
                try
                {
                    var openPositions = account.Positions.Where(p => p.MarketPosition != MarketPosition.Flat).ToList();
                    if (openPositions.Any())
                    {
                        LogWarning($"Race condition detected: {openPositions.Count} position(s) opened during lockout");
                        CancelAllOrders(account);
                        FlattenAll(account);
                    }
                }
                catch { }
            });

            ShowAlert(result, isLockout: true);
        }

        /// <summary>
        /// Mark all positions as closing before flatten
        /// </summary>
        private void MarkPositionsClosing(Account account)
        {
            try
            {
                foreach (var pos in account.Positions)
                {
                    if (pos.MarketPosition != MarketPosition.Flat)
                    {
                        var tradeId = TradeTracker.GetTradeId(account, pos.Instrument);
                        Trades.MarkClosing(tradeId);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Mark position as closed (call on position update)
        /// </summary>
        public void OnPositionClosed(Account account, Instrument instrument)
        {
            var tradeId = TradeTracker.GetTradeId(account, instrument);
            Trades.MarkClosed(tradeId);
        }

        /// <summary>
        /// Mark position as active (call on new position)
        /// </summary>
        public void OnPositionOpened(Account account, Instrument instrument)
        {
            var tradeId = TradeTracker.GetTradeId(account, instrument);
            Trades.MarkActive(tradeId);
        }

        /// <summary>
        /// Flatten a SINGLE position by instrument name
        /// </summary>
        public void FlattenPosition(Account account, string instrumentName)
        {
            try
            {
                LogWarning($"=== FLATTEN POSITION: {instrumentName} ===");

                var position = account.Positions
                    .FirstOrDefault(p => p.Instrument.FullName == instrumentName &&
                                        p.MarketPosition != MarketPosition.Flat);

                if (position != null)
                {
                    LogInfo($"  Flattening: {position.Instrument.FullName} {position.MarketPosition} {position.Quantity}");
                    account.Flatten(new[] { position.Instrument });
                }
                else
                {
                    LogInfo($"  Position not found or already flat: {instrumentName}");
                }

                LogWarning($"=== FLATTEN POSITION COMPLETED ===");
            }
            catch (Exception ex)
            {
                LogError($"Error flattening position {instrumentName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Flatten all positions on account
        /// </summary>
        public void FlattenAll(Account account)
        {
            try
            {
                LogWarning($"=== FLATTEN ALL STARTED for {account.Name} ===");

                var positionsToFlatten = account.Positions
                    .Where(p => p.MarketPosition != MarketPosition.Flat)
                    .ToList();

                LogInfo($"Positions to flatten: {positionsToFlatten.Count}");

                if (positionsToFlatten.Count > 0)
                {
                    foreach (var pos in positionsToFlatten)
                    {
                        LogInfo($"  Flattening: {pos.Instrument.FullName} {pos.MarketPosition} {pos.Quantity}");
                        try
                        {
                            account.Flatten(new[] { pos.Instrument });
                        }
                        catch (Exception ex)
                        {
                            LogError($"  Error flattening {pos.Instrument.FullName}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    LogInfo($"  No open positions found");
                }

                LogWarning($"=== FLATTEN COMPLETED ===");
            }
            catch (Exception ex)
            {
                LogError($"Error in FlattenAll: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel all pending orders on account
        /// </summary>
        public void CancelAllOrders(Account account)
        {
            try
            {
                var pendingOrders = account.Orders
                    .Where(o => o.OrderState == OrderState.Working ||
                               o.OrderState == OrderState.Submitted ||
                               o.OrderState == OrderState.Accepted)
                    .ToArray();

                if (pendingOrders.Length > 0)
                {
                    LogWarning($"Cancelling {pendingOrders.Length} pending orders on {account.Name}");
                    account.Cancel(pendingOrders);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error cancelling orders: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel a specific order
        /// </summary>
        public void CancelOrder(Order order)
        {
            try
            {
                LogWarning($"BLOCKED ORDER: {order.Instrument.FullName} {order.OrderAction} {order.Quantity}");
                order.Account.Cancel(new[] { order });
            }
            catch (Exception ex)
            {
                LogError($"Error cancelling order: {ex.Message}");
            }
        }

        /// <summary>
        /// Manually clear lockout for an account
        /// </summary>
        public void ClearLockout(Account account)
        {
            ClearLockout(account.Name);
        }

        /// <summary>
        /// Manually clear lockout for an account (by name)
        /// </summary>
        public void ClearLockout(string accountName)
        {
            lock (_lock)
            {
                if (_lockouts.Remove(accountName))
                {
                    State.SetLocked(accountName, false);
                    Trades.ClearAccount(accountName);
                    LogInfo($"Lockout manually cleared for {accountName}");
                    PersistLockouts();
                }
            }
        }

        /// <summary>
        /// Clear all lockouts
        /// </summary>
        public void ClearAllLockouts()
        {
            lock (_lock)
            {
                _lockouts.Clear();
                State.Reset();
                _lastManualReset = DateTime.Now; // Start cooldown
                LogInfo("All lockouts cleared (5s cooldown before re-trigger)");
            }
            StateManager.ClearLockouts();
        }

        /// <summary>
        /// Check if in post-reset cooldown
        /// </summary>
        public bool IsInResetCooldown()
        {
            return (DateTime.Now - _lastManualReset).TotalSeconds < RESET_COOLDOWN_SECONDS;
        }

        /// <summary>
        /// Get current lockout state for an account
        /// </summary>
        public LockoutState GetLockoutState(Account account)
        {
            return GetLockoutState(account.Name);
        }

        /// <summary>
        /// Get current lockout state for an account (by name)
        /// </summary>
        public LockoutState GetLockoutState(string accountName)
        {
            lock (_lock)
            {
                return _lockouts.TryGetValue(accountName, out var state) ? state : null;
            }
        }

        /// <summary>
        /// Record a closure event to history (persisted to disk)
        /// </summary>
        private void RecordClosureEvent(Account account, string instrument, string ruleName, string reason, string actionType, double positionPnL = 0)
        {
            try
            {
                // Get current account P&L
                double accountPnl = 0;
                try
                {
                    accountPnl = account.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar)
                        + account.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);
                }
                catch { }

                // Get position P&L if not provided
                if (positionPnL == 0 && !string.IsNullOrEmpty(instrument))
                {
                    try
                    {
                        var pos = account.Positions.FirstOrDefault(p => p.Instrument.FullName == instrument);
                        if (pos != null && pos.MarketPosition != MarketPosition.Flat)
                        {
                            positionPnL = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                        }
                    }
                    catch { }
                }

                var record = new ClosureRecord
                {
                    Timestamp = DateTime.Now,
                    Account = account.Name,
                    Instrument = instrument,
                    RuleName = ruleName,
                    Reason = reason,
                    ActionType = actionType,
                    PnLAtClosure = accountPnl,
                    PositionPnL = positionPnL
                };

                StateManager.RecordClosure(record);
                var pnlStr = positionPnL != 0 ? $" (P&L: {(positionPnL >= 0 ? "+" : "")}{positionPnL:F2})" : "";
                LogInfo($"Closure recorded: {actionType} | {instrument ?? "ALL"} | {ruleName}{pnlStr}");
            }
            catch (Exception ex)
            {
                LogError($"Error recording closure: {ex.Message}");
            }
        }

        private void ShowAlert(RuleResult result, bool isLockout = false)
        {
            var message = isLockout ? "*** LOCKOUT ***: " : "ALERT: ";
            message += string.Join(", ", result.Violations.Select(v => v.Message));

            if (isLockout)
                LogError(message);
            else
                LogWarning(message);

            // Play alert sound
            try
            {
                NinjaTrader.Core.Globals.PlaySound(
                    NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert4.wav");
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        // COLORED LOGGING
        // ═══════════════════════════════════════════════════════════════

        private void LogInfo(string message)
        {
            Output.Process($"[RiskManager] {DateTime.Now:HH:mm:ss} {message}", PrintTo.OutputTab1);
        }

        private void LogWarning(string message)
        {
            // Use OutputTab2 for warnings (can be colored differently in NT8)
            Output.Process($"[RiskManager] {DateTime.Now:HH:mm:ss} WARNING: {message}", PrintTo.OutputTab1);
        }

        private void LogError(string message)
        {
            // Prefix with *** for visibility
            Output.Process($"[RiskManager] {DateTime.Now:HH:mm:ss} *** {message} ***", PrintTo.OutputTab1);
            // Also log to file
            StateManager.LogToFile("ActionHandler", message);
        }

        private void LogException(string context, Exception ex)
        {
            StateManager.LogError("ActionHandler", $"{context}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Tracks lockout state for an account
    /// </summary>
    public class LockoutState
    {
        public bool IsLocked { get; set; }
        public LockoutDuration Type { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public TimeSpan ResetTime { get; set; } = new TimeSpan(18, 0, 0);
        public string Reason { get; set; }

        public TimeSpan TimeRemaining
        {
            get
            {
                if (Type != LockoutDuration.Timed) return TimeSpan.MaxValue;
                var remaining = ExpiresAt - DateTime.Now;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }
}
