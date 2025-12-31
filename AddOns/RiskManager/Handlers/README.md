# Handlers

The Handlers folder contains components responsible for monitoring account activity and executing actions when rules are violated.

## Purpose

These handlers form the event-driven heart of the Risk Manager. They subscribe to NinjaTrader account events, evaluate rules in real-time, and execute protective actions such as flattening positions or enforcing lockouts.

## Files

### AccountMonitor.cs

Event-driven account monitoring system with NO POLLING. Subscribes to native NinjaTrader events for instant response.

**Key Responsibilities:**
- Subscribes to account events: `AccountItemUpdate`, `OrderUpdate`, `PositionUpdate`, `ExecutionUpdate`
- Filters to monitor only SIM accounts (production safety)
- Builds `RiskContext` on each event and triggers rule evaluation
- Tracks P&L state, trade history, and open positions per account
- Throttles logging to prevent spam (3-second intervals or $10+ P&L changes)
- Supports P&L baseline reset for "fresh start" tracking

**Event Subscription Model:**
```csharp
// Subscribed when account connects
account.AccountItemUpdate += OnAccountItemUpdate;  // P&L changes (every tick with positions)
account.OrderUpdate += OnOrderUpdate;              // Order state changes
account.PositionUpdate += OnPositionUpdate;        // Position changes
account.ExecutionUpdate += OnExecutionUpdate;      // Trade fills
```

**Key Features:**
- **Lockout Order Handling**: During lockout, closing orders are ALLOWED but new position orders are BLOCKED
- **Trade History Tracking**: Maintains 24-hour rolling history for frequency rules
- **P&L Baseline**: Allows "reset" of P&L tracking without affecting actual account

**Supporting Types Defined Here:**
- `TriggerType` - Enum for event types (AccountUpdate, OrderSubmitted, PositionUpdate, Execution, Timer)
- `AccountState` - Per-account state tracking (P&L, positions, baselines)
- `PositionInfo` - Position details (instrument, direction, quantity, avg price)
- `TradeRecord` - Trade history entry (time, instrument, action, quantity, price, account)

### ActionHandler.cs

Executes protective actions when rules are violated and manages lockout state persistence.

**Key Responsibilities:**
- Executes actions based on `RuleAction` severity: Alert, BlockOrder, FlattenOnly, Lockout
- Manages lockout state per account
- Persists lockouts to disk to survive NinjaTrader restarts
- Handles lockout expiration (timed or daily reset)
- Provides methods to clear lockouts manually

**Action Hierarchy (by severity):**
1. `Alert` - Show alert, continue trading
2. `BlockOrder` - Block specific order only
3. `FlattenOnly` - Flatten all positions, NO lockout (can trade again immediately)
4. `Lockout` - Full lockout: flatten + cancel orders + block all new positions

**How Lockouts Work:**

1. **Triggering**: When `RuleEngine` returns a result with `RequiredAction == Lockout`, the `Execute()` method calls `ExecuteLockout()`
2. **State Storage**: Lockout state is stored in `_lockouts` dictionary keyed by account name
3. **Actions Taken**:
   - Cancel all pending orders
   - Flatten all open positions
   - Play alert sound
   - Set lockout state with reason and expiration
4. **Persistence**: Lockouts are immediately saved to disk via `StateManager.SaveLockouts()`
5. **Order Blocking**: `AccountMonitor` checks `IsLockedOut()` before processing new orders

**Lockout Duration Types:**
- `UntilReset` - Lockout remains until daily reset time (e.g., 6 PM) or manual clear
- `Timed` - Lockout expires after X minutes (auto-reset)

**Persistence Mechanism:**

Lockouts survive NinjaTrader restarts:
```csharp
// On startup
LoadPersistedLockouts();  // Reads from StateManager, validates expiration

// On lockout activation
PersistLockouts();  // Saves to disk immediately

// On lockout clear
PersistLockouts();  // Updates disk with cleared state
```

**Key Public Methods:**
- `IsLockedOut(account)` - Check if account is locked (handles expiration checks)
- `Execute(account, result)` - Execute action based on rule evaluation result
- `FlattenAll(account)` - Close all positions
- `CancelAllOrders(account)` - Cancel all pending orders
- `CancelOrder(order)` - Cancel a specific order
- `ClearLockout(account)` - Manually clear lockout for an account
- `ClearAllLockouts()` - Clear all lockouts and delete persistence file
- `GetLockoutState(account)` - Get current lockout details

**LockoutState Properties:**
- `IsLocked` - Whether lockout is active
- `Type` - `UntilReset` or `Timed`
- `StartedAt` - When lockout began
- `ExpiresAt` - When timed lockout expires
- `ResetTime` - Daily reset time (TimeSpan)
- `Reason` - Why lockout was triggered
- `TimeRemaining` - Computed time until timed lockout expires

## Dependencies

- **Core** - `RuleEngine` for rule evaluation, `RiskContext` for context building
- **Persistence** - `StateManager` for lockout persistence
- **Rules** - Rule violations trigger actions

## Event Flow

```
NinjaTrader Event (P&L, Order, Position, Execution)
    |
    v
AccountMonitor (OnXxxUpdate)
    |
    v
Build RiskContext
    |
    v
RuleEngine.Evaluate(context)
    |
    v
RuleResult (violations + required action)
    |
    v
ActionHandler.Execute(account, result)
    |
    v
Take Action (Alert, Block, Flatten, Lockout)
```

## Thread Safety

Both `AccountMonitor` and `ActionHandler` use lock objects (`_lock`) to ensure thread-safe access to shared state. NinjaTrader events can fire from multiple threads, so all state mutations are protected.
