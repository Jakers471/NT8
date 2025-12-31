// PositionMirror.cs
// Core logic for detecting leader position changes and mirroring to followers

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Code;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.CopyTrader
{
    public class PositionMirror
    {
        private readonly object _lock = new object();
        private CopyTraderConfig _config;
        private Account _leaderAccount;
        private Dictionary<string, PositionSnapshot> _lastSnapshot = new Dictionary<string, PositionSnapshot>();
        private bool _isActive = false;

        public event Action<string> OnLog;
        public event Action<CopyLogEntry> OnCopyAction;

        public bool IsActive => _isActive;
        public string LeaderAccountName => _leaderAccount?.Name ?? "(none)";

        public PositionMirror()
        {
            _config = ConfigManager.LoadConfig();
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isActive) return;

                _config = ConfigManager.LoadConfig();
                if (!_config.Enabled || string.IsNullOrEmpty(_config.LeaderAccount))
                {
                    Log("Not starting - disabled or no leader set");
                    return;
                }

                // Find leader account
                _leaderAccount = Account.All.FirstOrDefault(a => a.Name == _config.LeaderAccount);
                if (_leaderAccount == null)
                {
                    Log($"Leader account '{_config.LeaderAccount}' not found");
                    return;
                }

                // Take initial snapshot of leader positions
                TakeSnapshot();

                // Subscribe to position updates
                _leaderAccount.PositionUpdate += OnLeaderPositionUpdate;

                _isActive = true;
                Log($"Started - Leader: {_leaderAccount.Name}, Followers: {_config.Followers.Count(f => f.Enabled)}");
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isActive) return;

                if (_leaderAccount != null)
                {
                    _leaderAccount.PositionUpdate -= OnLeaderPositionUpdate;
                    _leaderAccount = null;
                }

                _lastSnapshot.Clear();
                _isActive = false;
                Log("Stopped");
            }
        }

        public void Reload()
        {
            Stop();
            Start();
        }

        public void UpdateConfig(CopyTraderConfig config)
        {
            lock (_lock)
            {
                _config = config;
                ConfigManager.SaveConfig(config);
                Reload();
            }
        }

        private void TakeSnapshot()
        {
            _lastSnapshot.Clear();
            if (_leaderAccount == null) return;

            foreach (var pos in _leaderAccount.Positions)
            {
                if (pos.MarketPosition != MarketPosition.Flat)
                {
                    _lastSnapshot[pos.Instrument.FullName] = new PositionSnapshot
                    {
                        Instrument = pos.Instrument.FullName,
                        Quantity = pos.Quantity,
                        MarketPosition = pos.MarketPosition,
                        AvgPrice = pos.AveragePrice,
                        Timestamp = DateTime.Now
                    };
                }
            }
        }

        private void OnLeaderPositionUpdate(object sender, PositionEventArgs e)
        {
            try
            {
                ProcessPositionChange(e.Position);
            }
            catch (Exception ex)
            {
                Log($"Error processing position update: {ex.Message}");
            }
        }

        private void ProcessPositionChange(Position leaderPos)
        {
            lock (_lock)
            {
                if (!_isActive || leaderPos == null) return;

                var instrument = leaderPos.Instrument.FullName;
                var currentQty = leaderPos.Quantity;
                var currentSide = leaderPos.MarketPosition;

                // Get previous state
                _lastSnapshot.TryGetValue(instrument, out var previous);
                var prevQty = previous?.Quantity ?? 0;
                var prevSide = previous?.MarketPosition ?? MarketPosition.Flat;

                // Detect what changed
                if (currentSide == MarketPosition.Flat && prevSide != MarketPosition.Flat)
                {
                    // Position closed
                    MirrorClose(instrument, prevSide, prevQty);
                    _lastSnapshot.Remove(instrument);
                }
                else if (currentSide != MarketPosition.Flat && prevSide == MarketPosition.Flat)
                {
                    // New position opened
                    MirrorOpen(leaderPos.Instrument, currentSide, currentQty);
                    UpdateSnapshot(leaderPos);
                }
                else if (currentSide != MarketPosition.Flat && prevSide != MarketPosition.Flat)
                {
                    // Position modified (size change or reversal)
                    if (currentSide != prevSide)
                    {
                        // Reversal - close old, open new
                        MirrorClose(instrument, prevSide, prevQty);
                        MirrorOpen(leaderPos.Instrument, currentSide, currentQty);
                    }
                    else if (currentQty != prevQty)
                    {
                        // Size change
                        MirrorSizeChange(leaderPos.Instrument, currentSide, prevQty, currentQty);
                    }
                    UpdateSnapshot(leaderPos);
                }
            }
        }

        private void UpdateSnapshot(Position pos)
        {
            _lastSnapshot[pos.Instrument.FullName] = new PositionSnapshot
            {
                Instrument = pos.Instrument.FullName,
                Quantity = pos.Quantity,
                MarketPosition = pos.MarketPosition,
                AvgPrice = pos.AveragePrice,
                Timestamp = DateTime.Now
            };
        }

        private void MirrorOpen(Instrument instrument, MarketPosition side, int qty)
        {
            Log($"MIRROR OPEN: {instrument.FullName} {side} {qty}");

            foreach (var follower in _config.Followers.Where(f => f.Enabled))
            {
                var followerAccount = Account.All.FirstOrDefault(a => a.Name == follower.AccountName);
                if (followerAccount == null)
                {
                    LogAction("OPEN", follower.AccountName, instrument.FullName, qty, side.ToString(), "Failed", "Account not found");
                    continue;
                }

                var followerQty = (int)Math.Max(1, Math.Round(qty * follower.Multiplier));
                SubmitOrder(followerAccount, instrument, side, followerQty, "OPEN");
            }
        }

        private void MirrorClose(string instrumentName, MarketPosition prevSide, int prevQty)
        {
            Log($"MIRROR CLOSE: {instrumentName}");

            foreach (var follower in _config.Followers.Where(f => f.Enabled))
            {
                var followerAccount = Account.All.FirstOrDefault(a => a.Name == follower.AccountName);
                if (followerAccount == null)
                {
                    LogAction("CLOSE", follower.AccountName, instrumentName, 0, "", "Failed", "Account not found");
                    continue;
                }

                // Flatten the position on follower
                FlattenPosition(followerAccount, instrumentName);
            }
        }

        private void MirrorSizeChange(Instrument instrument, MarketPosition side, int oldQty, int newQty)
        {
            var diff = newQty - oldQty;
            Log($"MIRROR SIZE: {instrument.FullName} {oldQty} -> {newQty} ({(diff > 0 ? "+" : "")}{diff})");

            foreach (var follower in _config.Followers.Where(f => f.Enabled))
            {
                var followerAccount = Account.All.FirstOrDefault(a => a.Name == follower.AccountName);
                if (followerAccount == null) continue;

                var scaledDiff = (int)Math.Round(diff * follower.Multiplier);
                if (scaledDiff == 0) continue;

                if (scaledDiff > 0)
                {
                    // Adding to position
                    SubmitOrder(followerAccount, instrument, side, scaledDiff, "ADD");
                }
                else
                {
                    // Reducing position - submit opposite side order
                    var closeSide = side == MarketPosition.Long ? MarketPosition.Short : MarketPosition.Long;
                    SubmitOrder(followerAccount, instrument, closeSide, Math.Abs(scaledDiff), "REDUCE");
                }
            }
        }

        private void SubmitOrder(Account account, Instrument instrument, MarketPosition side, int qty, string action)
        {
            try
            {
                var orderAction = side == MarketPosition.Long ? OrderAction.Buy : OrderAction.SellShort;

                account.Submit(new[] { account.CreateOrder(
                    instrument,
                    orderAction,
                    OrderType.Market,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    qty,
                    0, 0, "", "CopyTrader", DateTime.MaxValue, null) });

                LogAction(action, account.Name, instrument.FullName, qty, side.ToString(), "Success", "Order submitted");
                Log($"  -> {account.Name}: {orderAction} {qty} {instrument.FullName}");
            }
            catch (Exception ex)
            {
                LogAction(action, account.Name, instrument.FullName, qty, side.ToString(), "Failed", ex.Message);
                Log($"  -> {account.Name}: FAILED - {ex.Message}");
            }
        }

        private void FlattenPosition(Account account, string instrumentName)
        {
            try
            {
                var pos = account.Positions.FirstOrDefault(p => p.Instrument.FullName == instrumentName);
                if (pos == null || pos.MarketPosition == MarketPosition.Flat)
                {
                    LogAction("CLOSE", account.Name, instrumentName, 0, "", "Skipped", "No position");
                    return;
                }

                account.Flatten(new[] { pos.Instrument });
                LogAction("CLOSE", account.Name, instrumentName, pos.Quantity, pos.MarketPosition.ToString(), "Success", "Flattened");
                Log($"  -> {account.Name}: Flattened {instrumentName}");
            }
            catch (Exception ex)
            {
                LogAction("CLOSE", account.Name, instrumentName, 0, "", "Failed", ex.Message);
                Log($"  -> {account.Name}: FLATTEN FAILED - {ex.Message}");
            }
        }

        private void LogAction(string action, string follower, string instrument, int qty, string direction, string status, string message)
        {
            var entry = new CopyLogEntry
            {
                Timestamp = DateTime.Now,
                Action = action,
                LeaderAccount = _leaderAccount?.Name ?? "",
                FollowerAccount = follower,
                Instrument = instrument,
                Quantity = qty,
                Direction = direction,
                Status = status,
                Message = message
            };

            ConfigManager.AddLogEntry(entry);
            OnCopyAction?.Invoke(entry);
        }

        private void Log(string message)
        {
            Output.Process($"[CopyTrader] {DateTime.Now:HH:mm:ss} {message}", PrintTo.OutputTab1);
            OnLog?.Invoke(message);
        }
    }
}
