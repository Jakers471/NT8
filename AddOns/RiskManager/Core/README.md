# Core

The Core folder contains the main entry point and central orchestration components of the Risk Manager add-on.

## Purpose

This folder houses the foundational classes that initialize the Risk Manager, evaluate rules, and provide context data for rule evaluation. These components work together to form the backbone of the risk management system.

## Files

### RiskManagerAddOn.cs

The main entry point for the Risk Manager add-on. This class extends `AddOnBase` and is automatically loaded when NinjaTrader 8 starts.

**Key Responsibilities:**
- Auto-starts when NinjaTrader loads (in `State.Active`)
- Loads configuration from persistence via `StateManager`
- Creates and wires up core components (`RuleEngine`, `ActionHandler`, `AccountMonitor`)
- Configures rules from saved settings (Total Loss, Floating Stop, Take Profit, Realized Loss)
- Adds "Risk Manager" menu item to NinjaTrader's Control Center
- Opens the `RiskManagerWindow` when the menu item is clicked
- Handles graceful cleanup on shutdown (`State.Terminated`)

**Lifecycle:**
```
State.SetDefaults -> Configure name/description
State.Active -> Initialize all components, start monitoring
State.Terminated -> Stop monitoring, cleanup resources
```

### RuleEngine.cs

Central rule evaluation engine that processes all active rules against the current account context.

**Key Responsibilities:**
- Maintains a thread-safe list of `RiskRule` instances
- Evaluates all enabled rules against a `RiskContext`
- Collects violations and determines the highest-severity action required
- Returns a `RuleResult` containing all violations and the required action

**Key Classes:**
- `RuleEngine` - The main evaluation engine
- `RuleResult` - Contains list of violations and the `RequiredAction`
- `RuleViolation` - Details of a single rule violation (rule, action, message, timestamp)

**Usage:**
```csharp
var engine = new RuleEngine();
engine.AddRule(new MaxLossRule { MaxLoss = 500 });

var context = new RiskContext { TotalDailyPnL = -600 };
var result = engine.Evaluate(context);

if (result.HasViolations)
{
    // result.RequiredAction contains the most severe action needed
    // result.Violations contains all violated rules
}
```

### RiskContext.cs

A data container that holds all current state needed for rule evaluation. A fresh context is built on each account event and passed to all rules.

**Key Properties:**
- `Account` - Reference to the NinjaTrader account
- `TotalDailyPnL` - Combined realized + unrealized P&L
- `RealizedPnL` - P&L from closed trades only
- `UnrealizedPnL` - P&L from open positions (floating)
- `PeakPnL` - Highest P&L reached (for drawdown calculations)
- `DrawdownFromPeak` - Computed property showing current drawdown
- `TradeHistory` - List of recent trades (for frequency rules)
- `OpenPositions` - Dictionary of current positions by instrument
- `PendingOrder` - Order being evaluated (for pre-submission checks)
- `ConsecutiveLosses` / `ConsecutiveWins` - Streak tracking

**Helper Methods:**
- `GetTradeCountInWindow(minutes)` - Count trades in rolling time window
- `HasPositionIn(symbol)` - Check if a position exists for an instrument

## Dependencies

- **Persistence** - `StateManager` for loading/saving configuration
- **Handlers** - `AccountMonitor` and `ActionHandler` for event handling and action execution
- **Rules** - All rule classes inherit from `RiskRule` and are evaluated by `RuleEngine`

## Architecture Notes

The Core components follow a dependency injection pattern:
1. `RiskManagerAddOn` creates instances of `RuleEngine`, `ActionHandler`, and `AccountMonitor`
2. `AccountMonitor` receives references to `RuleEngine` and `ActionHandler` in its constructor
3. When account events occur, `AccountMonitor` builds a `RiskContext` and calls `RuleEngine.Evaluate()`
4. Results are passed to `ActionHandler.Execute()` for action execution
