# Risk Manager Add-On for NinjaTrader 8

## Overview
Automated risk management system that monitors account P&L and enforces configurable rules. Runs automatically when NinjaTrader starts.

## Features
- **Event-driven monitoring** - No polling, responds instantly to account changes
- **Persistent lockouts** - Survive NT8 restarts
- **Configurable rules** - Enable/disable and set thresholds via UI
- **SIM accounts only** - Will not affect live trading accounts

## Folder Structure
```
RiskManager/
├── Core/               # Main entry point and engine
│   └── RiskManagerAddOn.cs
├── Handlers/           # Event handlers and action execution
│   ├── AccountMonitor.cs
│   └── ActionHandler.cs
├── Rules/              # All risk rule implementations
│   ├── RiskRule.cs (base class)
│   ├── MaxLossRule.cs
│   ├── UnrealizedLossRule.cs
│   └── ...
├── Persistence/        # State and config persistence
│   └── StateManager.cs
├── UI/                 # User interface
│   └── RiskManagerWindow.cs
└── Models/             # Data models (in RiskContext.cs)
```

## Quick Start
1. Compile the add-on in NinjaTrader
2. Risk Manager auto-starts on NT8 launch
3. Access UI: Control Center → New → Risk Manager
4. Configure rules in the Config tab
5. Save and restart for changes to take effect

## Rule Types

| Rule | Trigger | Action | Reset |
|------|---------|--------|-------|
| Total Daily Loss | Realized + Unrealized P&L | LOCKOUT | Daily at 6 PM |
| Floating Stop Loss | Unrealized P&L only | FLATTEN only | Immediate |
| Take Profit | Unrealized P&L (positive) | FLATTEN only | Immediate |
| Realized Loss | Realized P&L only | LOCKOUT | Daily at 6 PM |

## Key Files

### Core/RiskManagerAddOn.cs
Entry point. Loads config, initializes components, creates menu item.

### Handlers/AccountMonitor.cs
Subscribes to NT8 account events. Evaluates rules on every P&L change.

### Handlers/ActionHandler.cs
Executes actions: flatten, cancel orders, lockout. Persists lockout state.

### Persistence/StateManager.cs
Saves/loads lockouts and config to XML files in Documents/NinjaTrader 8/RiskManager/

## Adding New Rules

1. Create new class in Rules/ folder
2. Inherit from `RiskRule`
3. Implement `IsViolated(RiskContext context)`
4. Add to RiskManagerAddOn.cs initialization
5. Add config options to RiskConfig class

## Contracts

### RiskContext
Data passed to rules for evaluation:
- `TotalDailyPnL` - Realized + Unrealized
- `RealizedPnL` - Closed trades only
- `UnrealizedPnL` - Open position P&L
- `OpenPositions` - Current positions
- `PendingOrder` - Order being evaluated (if applicable)

### RuleAction
- `None` - Log only
- `Alert` - Play sound
- `BlockOrder` - Cancel specific order
- `FlattenOnly` - Close positions, no lockout
- `Lockout` - Flatten + block all new orders

## Changelog
See CHANGELOG.md

## Support
Report issues at: https://github.com/anthropics/claude-code/issues
