# Rules

The Rules folder contains the rule architecture and all individual rule implementations for the Risk Manager.

## Purpose

This folder defines the extensible rule system that allows different types of risk conditions to be monitored and enforced. All rules inherit from a common base class and can be configured with different actions and reset schedules.

## Files

### RiskRule.cs (Base Class)

Abstract base class that all risk rules must inherit from. Defines the contract and common configuration options for all rules.

**Key Properties:**

Identity:
- `Name` - Display name for the rule
- `Description` - Detailed description
- `Enabled` - Whether the rule is active

Action Configuration:
- `Action` - What happens when violated (`RuleAction` enum)
- `LockoutType` - How long lockout lasts (`LockoutDuration` enum)
- `LockoutMinutes` - Duration for timed lockouts

Reset Configuration:
- `ResetSchedule` - When rule tracking resets (`ResetSchedule` enum)
- `DailyResetTime` - Time of day for daily reset (default: 6 PM)
- `RollingWindowMinutes` - Window size for rolling resets
- `LastResetTime` - Tracks when rule was last reset

**Abstract Methods to Implement:**
```csharp
// Return true when the rule condition is violated
public abstract bool IsViolated(RiskContext context);

// Optional: Custom violation message
public virtual string GetViolationMessage(RiskContext context);

// Optional: Status text for UI display
public virtual string GetStatusText(RiskContext context);
```

### Enums

**RuleAction** (ordered by severity):
```csharp
None = 0,        // Just log, no action
Alert = 1,       // Show alert, continue trading
BlockOrder = 2,  // Block the specific order only
FlattenOnly = 3, // Flatten positions but NO lockout - can trade again
Lockout = 4      // Full lockout - flatten + block all new positions
```

**LockoutDuration:**
```csharp
UntilReset,  // Manual reset or timer reset
Timed        // X minutes then auto-reset
```

**ResetSchedule:**
```csharp
Never,    // Never auto-reset (manual only)
Daily,    // Reset at specific time each day
Rolling   // Rolling window (e.g., last 30 minutes)
```

## Rule Implementations

### MaxLossRule.cs
**Purpose:** Total Daily Loss (Realized + Unrealized combined)

Triggers LOCKOUT when total P&L falls below the limit. This is the "nuclear option" that counts everything - both closed trades AND open position P&L.

```csharp
// Triggers when TotalDailyPnL <= -MaxLoss
public double MaxLoss { get; set; } = 500;
```

Default action: `Lockout` with daily reset at 6 PM.

### UnrealizedLossRule.cs
**Purpose:** Floating Stop Loss

Triggers FLATTEN ONLY when unrealized (floating) loss exceeds limit. Does NOT cause lockout - you can trade again immediately after positions are closed. Use this as a "position stop loss" across all positions.

```csharp
// Triggers when UnrealizedPnL <= -MaxLoss
public double MaxLoss { get; set; } = 100;
```

Default action: `FlattenOnly` (no lockout).

### UnrealizedProfitRule.cs
**Purpose:** Floating Take Profit

Triggers FLATTEN ONLY when unrealized profit reaches target. Locks in floating gains by closing positions. Does NOT cause lockout.

```csharp
// Triggers when UnrealizedPnL >= ProfitTarget
public double ProfitTarget { get; set; } = 200;
```

Default action: `FlattenOnly` (no lockout).

### DailyRealizedLossRule.cs
**Purpose:** Daily Realized Loss Limit

Triggers LOCKOUT when realized (closed trade) losses exceed limit. Only counts closed trades, ignoring floating P&L.

```csharp
// Triggers when RealizedPnL <= -MaxLoss
public double MaxLoss { get; set; } = 500;
```

Default action: `Lockout` with daily reset at 6 PM.

### DailyRealizedProfitRule.cs
**Purpose:** Daily Realized Profit Target (Lock in Gains)

Triggers LOCKOUT when realized profits reach target. Stops trading while ahead to prevent giving back gains.

```csharp
// Triggers when RealizedPnL >= ProfitTarget
public double ProfitTarget { get; set; } = 1000;
```

Default action: `Lockout` with daily reset at 6 PM.

### TradeFrequencyRule.cs
**Purpose:** Overtrading Prevention

Triggers when too many trades occur within a rolling time window. Helps prevent revenge trading or emotional overtrading.

```csharp
public int MaxTrades { get; set; } = 10;
public int WindowMinutes { get; set; } = 30;
```

Default action: `Lockout` with 5-minute timed duration.

### SymbolBlockRule.cs
**Purpose:** Instrument Block List

Blocks orders on specific instruments. Uses partial matching (e.g., "ES" matches "ES 03-25").

```csharp
public List<string> BlockedSymbols { get; set; } = new List<string>();
```

Default action: `BlockOrder` (blocks specific order, no flatten).

## How to Add a New Rule

1. Create a new file in the Rules folder (e.g., `MyCustomRule.cs`)

2. Inherit from `RiskRule`:
```csharp
public class MyCustomRule : RiskRule
{
    // Rule-specific parameters
    public double MyThreshold { get; set; } = 100;

    public MyCustomRule()
    {
        Name = "My Custom Rule";
        Description = "Description of what this rule does";
        Action = RuleAction.Alert; // Default action
        ResetSchedule = ResetSchedule.Never;
    }

    public override bool IsViolated(RiskContext context)
    {
        // Return true when the rule is violated
        return context.SomeValue > MyThreshold;
    }

    public override string GetViolationMessage(RiskContext context)
    {
        return $"My rule violated: {context.SomeValue} > {MyThreshold}";
    }
}
```

3. Add the rule in `RiskManagerAddOn.cs`:
```csharp
_ruleEngine.AddRule(new MyCustomRule
{
    MyThreshold = 100,
    Action = RuleAction.Lockout,
    Enabled = true
});
```

4. Optionally, add UI configuration in `RiskManagerWindow.cs` and persistence in `RiskConfig`

## Dependencies

- **Core** - Rules are evaluated by `RuleEngine` and receive `RiskContext` data
- **Handlers** - `ActionHandler` executes actions when rules are violated

## Rule Evaluation Flow

1. `AccountMonitor` detects an event and builds `RiskContext`
2. `RuleEngine.Evaluate(context)` iterates through all enabled rules
3. Each rule's `IsViolated(context)` is called
4. Violations are collected with their actions
5. The highest-severity action is determined
6. `ActionHandler.Execute()` takes appropriate action

## Best Practices

- Always set a descriptive `Name` and `Description`
- Choose the appropriate `Action` severity (prefer less severe when possible)
- Implement `GetViolationMessage()` for clear user feedback
- Consider reset schedules carefully (daily vs. never vs. rolling)
- Test rules thoroughly before using in production
