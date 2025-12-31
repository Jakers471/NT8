// PositionManagerHandler.cs
// Core logic for position management - OCO orders, break-even, close positions
// Modular: Does NOT modify RiskManager backend, only reads from it for validation

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Code;
using NinjaTrader.Core;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Handles per-position management: OCO orders, break-even, close
    /// </summary>
    public class PositionManagerHandler
    {
        private readonly object _lock = new object();

        // Track OCO orders we've placed (so we can cancel/modify)
        private Dictionary<string, OcoOrderSet> _ocoOrders = new Dictionary<string, OcoOrderSet>();

        // Reference to rule engine for validation (read-only)
        private RuleEngine _ruleEngine;

        public PositionManagerHandler(RuleEngine ruleEngine = null)
        {
            _ruleEngine = ruleEngine;
        }

        #region Position Info

        /// <summary>
        /// Get all active positions for an account
        /// </summary>
        public List<PositionData> GetActivePositions(Account account)
        {
            var positions = new List<PositionData>();
            if (account == null) return positions;

            try
            {
                foreach (var pos in account.Positions)
                {
                    if (pos.MarketPosition != MarketPosition.Flat)
                    {
                        positions.Add(new PositionData
                        {
                            Instrument = pos.Instrument.FullName,
                            InstrumentObj = pos.Instrument,
                            Quantity = pos.Quantity,
                            Direction = pos.MarketPosition == MarketPosition.Long ? "Long" : "Short",
                            AvgPrice = pos.AveragePrice,
                            UnrealizedPnL = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency, double.NaN),
                            Account = account
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error getting positions: {ex.Message}");
            }

            return positions;
        }

        #endregion

        #region OCO Orders

        /// <summary>
        /// Place OCO stop loss and take profit orders for a position
        /// </summary>
        public bool PlaceOcoOrders(Account account, Instrument instrument, int quantity,
            bool isLong, double? stopPrice, double? targetPrice)
        {
            if (account == null || instrument == null) return false;
            if (!stopPrice.HasValue && !targetPrice.HasValue) return false;

            try
            {
                lock (_lock)
                {
                    var orders = new List<Order>();
                    string ocoId = $"PM_{instrument.FullName}_{DateTime.Now.Ticks}";

                    // Stop Loss order
                    if (stopPrice.HasValue)
                    {
                        var stopAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                        var stopOrder = account.CreateOrder(
                            instrument,
                            stopAction,
                            OrderType.StopMarket,
                            OrderEntry.Manual,
                            TimeInForce.Gtc,
                            quantity,
                            0,              // limit price (not used)
                            stopPrice.Value,
                            ocoId,
                            "PM_Stop",
                            Globals.MaxDate,
                            null
                        );
                        orders.Add(stopOrder);
                    }

                    // Take Profit order
                    if (targetPrice.HasValue)
                    {
                        var tpAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                        var tpOrder = account.CreateOrder(
                            instrument,
                            tpAction,
                            OrderType.Limit,
                            OrderEntry.Manual,
                            TimeInForce.Gtc,
                            quantity,
                            targetPrice.Value,
                            0,              // stop price (not used)
                            ocoId,
                            "PM_Target",
                            Globals.MaxDate,
                            null
                        );
                        orders.Add(tpOrder);
                    }

                    if (orders.Count > 0)
                    {
                        account.Submit(orders.ToArray());

                        // Track the OCO set
                        _ocoOrders[instrument.FullName] = new OcoOrderSet
                        {
                            OcoId = ocoId,
                            Instrument = instrument.FullName,
                            StopPrice = stopPrice,
                            TargetPrice = targetPrice
                        };

                        Log($"OCO orders placed for {instrument.FullName}: Stop={stopPrice}, Target={targetPrice}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error placing OCO orders: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Cancel existing OCO orders for an instrument
        /// </summary>
        public void CancelOcoOrders(Account account, string instrumentName)
        {
            if (account == null) return;

            try
            {
                lock (_lock)
                {
                    if (_ocoOrders.TryGetValue(instrumentName, out var ocoSet))
                    {
                        // Cancel all orders with this OCO ID
                        foreach (var order in account.Orders)
                        {
                            if (order.Oco == ocoSet.OcoId &&
                                (order.OrderState == OrderState.Working ||
                                 order.OrderState == OrderState.Accepted))
                            {
                                account.Cancel(new[] { order });
                            }
                        }
                        _ocoOrders.Remove(instrumentName);
                        Log($"OCO orders cancelled for {instrumentName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error cancelling OCO orders: {ex.Message}");
            }
        }

        #endregion

        #region Bracket Order (Entry + OCO)

        /// <summary>
        /// Place a bracket order: Market entry + OCO (stop + target)
        /// </summary>
        public bool PlaceBracketOrder(Account account, string symbolName, int quantity,
            bool isLong, double stopDollars, double? targetDollars)
        {
            if (account == null || string.IsNullOrEmpty(symbolName)) return false;

            try
            {
                lock (_lock)
                {
                    // Resolve instrument
                    var instrument = ResolveInstrument(symbolName);
                    if (instrument == null)
                    {
                        Log($"Could not resolve instrument: {symbolName}");
                        return false;
                    }

                    // Get current price for stop/target calculation
                    double currentPrice = instrument.MarketData.Last.Price;
                    if (currentPrice <= 0)
                    {
                        currentPrice = instrument.MarketData.Bid.Price;
                    }
                    if (currentPrice <= 0)
                    {
                        Log($"No price data for {symbolName}");
                        return false;
                    }

                    // Calculate stop and target prices
                    double tickSize = instrument.MasterInstrument.TickSize;
                    double tickValue = instrument.MasterInstrument.PointValue * tickSize;

                    double stopTicks = stopDollars / (tickValue * quantity);
                    double stopDistance = stopTicks * tickSize;

                    double stopPrice = isLong ? currentPrice - stopDistance : currentPrice + stopDistance;
                    stopPrice = RoundToTick(stopPrice, tickSize);

                    double? targetPrice = null;
                    if (targetDollars.HasValue && targetDollars.Value > 0)
                    {
                        double targetTicks = targetDollars.Value / (tickValue * quantity);
                        double targetDistance = targetTicks * tickSize;
                        targetPrice = isLong ? currentPrice + targetDistance : currentPrice - targetDistance;
                        targetPrice = RoundToTick(targetPrice.Value, tickSize);
                    }

                    string ocoId = $"PM_{symbolName}_{DateTime.Now.Ticks}";

                    // Entry order
                    var entryAction = isLong ? OrderAction.Buy : OrderAction.SellShort;
                    var entryOrder = account.CreateOrder(
                        instrument,
                        entryAction,
                        OrderType.Market,
                        OrderEntry.Manual,
                        TimeInForce.Gtc,
                        quantity,
                        0, 0, "", "PM_Entry",
                        Globals.MaxDate,
                        null
                    );

                    // Stop order
                    var stopAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                    var stopOrder = account.CreateOrder(
                        instrument,
                        stopAction,
                        OrderType.StopMarket,
                        OrderEntry.Manual,
                        TimeInForce.Gtc,
                        quantity,
                        0,
                        stopPrice,
                        ocoId,
                        "PM_Stop",
                        Globals.MaxDate,
                        null
                    );

                    var orders = new List<Order> { entryOrder, stopOrder };

                    // Target order (if specified)
                    if (targetPrice.HasValue)
                    {
                        var tpOrder = account.CreateOrder(
                            instrument,
                            stopAction,
                            OrderType.Limit,
                            OrderEntry.Manual,
                            TimeInForce.Gtc,
                            quantity,
                            targetPrice.Value,
                            0,
                            ocoId,
                            "PM_Target",
                            Globals.MaxDate,
                            null
                        );
                        orders.Add(tpOrder);
                    }

                    // Submit all orders
                    account.Submit(orders.ToArray());

                    Log($"Bracket order placed: {(isLong ? "BUY" : "SELL")} {quantity} {symbolName} @ Market, Stop={stopPrice:F2}, Target={targetPrice:F2}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"Error placing bracket order: {ex.Message}");
            }

            return false;
        }

        private Instrument ResolveInstrument(string symbolName)
        {
            try
            {
                // Try to find existing instrument
                var instrument = Instrument.GetInstrument(symbolName);
                if (instrument != null) return instrument;

                // Try common futures formats
                var root = symbolName.Split(' ')[0].ToUpper();

                // Try to get front month contract
                foreach (var inst in Instrument.All)
                {
                    if (inst.FullName.StartsWith(root + " ") || inst.FullName == root)
                    {
                        return inst;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error resolving instrument: {ex.Message}");
            }

            return null;
        }

        private double RoundToTick(double price, double tickSize)
        {
            return Math.Round(price / tickSize) * tickSize;
        }

        #endregion

        #region Break-Even

        /// <summary>
        /// Move stop to break-even (entry price) for a position
        /// </summary>
        public bool MoveToBreakEven(Account account, PositionData position)
        {
            if (account == null || position == null) return false;

            try
            {
                lock (_lock)
                {
                    // Find existing stop order for this instrument
                    Order existingStop = null;
                    foreach (var order in account.Orders)
                    {
                        if (order.Instrument.FullName == position.Instrument &&
                            order.OrderType == OrderType.StopMarket &&
                            (order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted))
                        {
                            existingStop = order;
                            break;
                        }
                    }

                    double breakEvenPrice = position.AvgPrice;

                    bool isLong = position.Direction == "Long";
                    var stopAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;

                    if (existingStop != null)
                    {
                        // Cancel existing stop first
                        account.Cancel(new[] { existingStop });
                    }

                    // Create new stop at break-even
                    var stopOrder = account.CreateOrder(
                        position.InstrumentObj,
                        stopAction,
                        OrderType.StopMarket,
                        OrderEntry.Manual,
                        TimeInForce.Gtc,
                        position.Quantity,
                        0, // limit price
                        breakEvenPrice, // stop price
                        "",
                        "PM_BE_Stop",
                        Globals.MaxDate,
                        null
                    );
                    account.Submit(new[] { stopOrder });
                    Log($"BE stop set for {position.Instrument} at {breakEvenPrice}");

                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"Error moving to break-even: {ex.Message}");
            }

            return false;
        }

        #endregion

        #region Close Position

        /// <summary>
        /// Close a specific position immediately
        /// </summary>
        public bool ClosePosition(Account account, PositionData position)
        {
            if (account == null || position == null) return false;

            try
            {
                lock (_lock)
                {
                    // Cancel any pending orders for this instrument first
                    CancelOcoOrders(account, position.Instrument);

                    // Flatten the position
                    bool isLong = position.Direction == "Long";
                    var closeAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;

                    var closeOrder = account.CreateOrder(
                        position.InstrumentObj,
                        closeAction,
                        OrderType.Market,
                        OrderEntry.Manual,
                        TimeInForce.Gtc,
                        position.Quantity,
                        0, // limit price
                        0, // stop price
                        "",
                        "PM_Close",
                        Globals.MaxDate,
                        null
                    );
                    account.Submit(new[] { closeOrder });

                    Log($"Position closed: {position.Instrument} {position.Quantity} {position.Direction}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"Error closing position: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Close all positions for an account
        /// </summary>
        public void CloseAllPositions(Account account)
        {
            if (account == null) return;

            var positions = GetActivePositions(account);
            foreach (var pos in positions)
            {
                ClosePosition(account, pos);
            }
        }

        #endregion

        #region Validation

        /// <summary>
        /// Check if a position size is within limits (reads from MaxPositionSizeRule if configured)
        /// </summary>
        public ValidationResult ValidatePositionSize(string instrumentName, int quantity)
        {
            var result = new ValidationResult { IsValid = true };

            if (_ruleEngine == null) return result;

            try
            {
                // Find MaxPositionSizeRule
                foreach (var rule in _ruleEngine.Rules)
                {
                    if (rule is MaxPositionSizeRule sizeRule && rule.Enabled)
                    {
                        // Use reflection or expose method to get max for symbol
                        // For now, we'll parse the config directly
                        var maxForSymbol = GetMaxFromRule(sizeRule, instrumentName);

                        if (quantity > maxForSymbol)
                        {
                            result.IsValid = false;
                            result.Message = $"Size {quantity} exceeds max {maxForSymbol} for {instrumentName}";
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error validating position size: {ex.Message}");
            }

            return result;
        }

        private int GetMaxFromRule(MaxPositionSizeRule rule, string instrumentName)
        {
            // Extract symbol root
            var root = instrumentName.Split(' ')[0].ToUpper();

            // Parse per-symbol config
            if (!string.IsNullOrWhiteSpace(rule.PerSymbolConfig))
            {
                var pairs = rule.PerSymbolConfig.Split(',');
                foreach (var pair in pairs)
                {
                    var parts = pair.Trim().Split('=');
                    if (parts.Length == 2 && parts[0].Trim().ToUpper() == root)
                    {
                        if (int.TryParse(parts[1].Trim(), out int max))
                            return max;
                    }
                }
            }

            return rule.DefaultMax;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Calculate dollar risk for a stop
        /// </summary>
        public double CalculateRisk(Instrument instrument, int quantity, double entryPrice, double stopPrice)
        {
            if (instrument == null) return 0;

            try
            {
                double tickSize = instrument.MasterInstrument.TickSize;
                double tickValue = instrument.MasterInstrument.PointValue * tickSize;
                double ticks = Math.Abs(entryPrice - stopPrice) / tickSize;
                return ticks * tickValue * quantity;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Calculate price from dollar risk
        /// </summary>
        public double CalculateStopPrice(Instrument instrument, int quantity, double entryPrice,
            double riskDollars, bool isLong)
        {
            if (instrument == null || quantity == 0) return entryPrice;

            try
            {
                double tickSize = instrument.MasterInstrument.TickSize;
                double tickValue = instrument.MasterInstrument.PointValue * tickSize;
                double ticksToRisk = riskDollars / (tickValue * quantity);
                double priceDistance = ticksToRisk * tickSize;

                return isLong ? entryPrice - priceDistance : entryPrice + priceDistance;
            }
            catch
            {
                return entryPrice;
            }
        }

        private void Log(string message)
        {
            Output.Process($"[PositionManager] {DateTime.Now:HH:mm:ss} {message}", PrintTo.OutputTab1);
        }

        #endregion
    }

    #region Supporting Classes

    public class PositionData
    {
        public string Instrument { get; set; }
        public Instrument InstrumentObj { get; set; }
        public int Quantity { get; set; }
        public string Direction { get; set; }
        public double AvgPrice { get; set; }
        public double UnrealizedPnL { get; set; }
        public Account Account { get; set; }
    }

    public class OcoOrderSet
    {
        public string OcoId { get; set; }
        public string Instrument { get; set; }
        public double? StopPrice { get; set; }
        public double? TargetPrice { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
    }

    #endregion
}
