# Persistence

The Persistence folder contains components for saving and loading Risk Manager state to survive NinjaTrader restarts.

## Purpose

This folder provides the persistence layer that ensures lockout states and configuration survive application restarts. Without persistence, a trader could simply restart NinjaTrader to bypass a lockout - defeating the purpose of risk management.

## Files

### StateManager.cs

Static class that handles all persistence operations for the Risk Manager. Saves data as XML files in the NinjaTrader data directory.

**Key Responsibilities:**
- Save and load lockout state (survives restarts)
- Save and load user configuration
- Validate lockout expiration on load (expired lockouts are not restored)
- Thread-safe file operations

## File Storage Location

Files are stored in:
```
%USERPROFILE%\Documents\NinjaTrader 8\RiskManager\
```

Example: `C:\Users\Username\Documents\NinjaTrader 8\RiskManager\`

**Files:**
- `lockouts.xml` - Active lockout states
- `config.xml` - User configuration (rule settings)

## XML File Formats

### lockouts.xml

Contains active lockout states per account:

```xml
<?xml version="1.0" encoding="utf-8"?>
<LockoutStorage xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Lockouts>
    <LockoutData>
      <AccountName>Sim101</AccountName>
      <IsLocked>true</IsLocked>
      <Type>UntilReset</Type>
      <StartedAt>2024-01-15T14:30:00</StartedAt>
      <ExpiresAt>0001-01-01T00:00:00</ExpiresAt>
      <ResetTime>PT18H</ResetTime>
      <Reason>Total daily loss limit: $512.50 / $500.00</Reason>
    </LockoutData>
  </Lockouts>
</LockoutStorage>
```

### config.xml

Contains user configuration for rule settings:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RiskConfig xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <TotalLossEnabled>true</TotalLossEnabled>
  <TotalLossMax>500</TotalLossMax>
  <TotalLossResetHour>18</TotalLossResetHour>
  <FloatingStopEnabled>true</FloatingStopEnabled>
  <FloatingStopMax>150</FloatingStopMax>
  <TakeProfitEnabled>false</TakeProfitEnabled>
  <TakeProfitTarget>300</TakeProfitTarget>
  <RealizedLossEnabled>false</RealizedLossEnabled>
  <RealizedLossMax>400</RealizedLossMax>
  <RealizedLossResetHour>18</RealizedLossResetHour>
  <SoundEnabled>true</SoundEnabled>
</RiskConfig>
```

## Data Classes

### LockoutStorage
Container for serializing multiple lockouts:
```csharp
public List<LockoutData> Lockouts { get; set; }
```

### LockoutData
Serializable lockout state:
```csharp
public string AccountName { get; set; }
public bool IsLocked { get; set; }
public LockoutDuration Type { get; set; }
public DateTime StartedAt { get; set; }
public DateTime ExpiresAt { get; set; }
public TimeSpan ResetTime { get; set; }
public string Reason { get; set; }
```

### RiskConfig
User configuration settings:
```csharp
// Total Daily Loss Rule
public bool TotalLossEnabled { get; set; } = true;
public double TotalLossMax { get; set; } = 500;
public int TotalLossResetHour { get; set; } = 18;

// Floating Stop Loss Rule
public bool FloatingStopEnabled { get; set; } = true;
public double FloatingStopMax { get; set; } = 150;

// Take Profit Rule
public bool TakeProfitEnabled { get; set; } = false;
public double TakeProfitTarget { get; set; } = 300;

// Realized Loss Rule
public bool RealizedLossEnabled { get; set; } = false;
public double RealizedLossMax { get; set; } = 400;
public int RealizedLossResetHour { get; set; } = 18;

// Global Settings
public bool SoundEnabled { get; set; } = true;
```

## Key Methods

### Lockout Persistence

```csharp
// Save all active lockouts to disk
StateManager.SaveLockouts(Dictionary<string, LockoutData> lockouts);

// Load lockouts from disk (validates expiration)
Dictionary<string, LockoutData> lockouts = StateManager.LoadLockouts();

// Delete the lockouts file
StateManager.ClearLockouts();
```

### Configuration Persistence

```csharp
// Save configuration to disk
StateManager.SaveConfig(RiskConfig config);

// Load configuration (returns defaults if file missing)
RiskConfig config = StateManager.LoadConfig();
```

## What Data is Persisted

**Lockout State:**
- Account name (which account is locked)
- Lock status (active or not)
- Lockout type (UntilReset or Timed)
- Start time (when lockout began)
- Expiration time (for timed lockouts)
- Reset time (daily reset time)
- Reason (why lockout was triggered)

**Configuration:**
- Rule enable/disable states
- Rule threshold values (max loss, profit targets)
- Reset hours
- Sound preference

## Lockout Validation on Load

When lockouts are loaded from disk, they are validated:

1. **Timed lockouts**: If `DateTime.Now >= ExpiresAt`, the lockout is skipped (expired)
2. **UntilReset lockouts**: If `DateTime.Now >= todayResetTime` AND `StartedAt < todayResetTime`, the lockout is skipped (daily reset passed)

This prevents stale lockouts from persisting indefinitely.

## Thread Safety

All file operations are protected by a lock object:
```csharp
private static readonly object _lock = new object();
```

This ensures thread-safe access when multiple events might trigger saves simultaneously.

## Error Handling

- If lockouts file doesn't exist, returns empty dictionary
- If config file doesn't exist, returns default `RiskConfig`
- All exceptions are caught and logged, preventing crashes
- Missing or corrupt files result in safe defaults

## Dependencies

- **Handlers** - `ActionHandler` calls `StateManager` to persist/load lockouts
- **Core** - `RiskManagerAddOn` calls `StateManager` to load configuration on startup
- **UI** - `RiskManagerWindow` calls `StateManager` to save configuration changes

## Usage Examples

**Loading configuration on startup:**
```csharp
var config = StateManager.LoadConfig();
if (config.TotalLossEnabled)
{
    _ruleEngine.AddRule(new MaxLossRule { MaxLoss = config.TotalLossMax });
}
```

**Persisting a lockout:**
```csharp
var data = new Dictionary<string, LockoutData>
{
    ["Sim101"] = new LockoutData
    {
        AccountName = "Sim101",
        IsLocked = true,
        Type = LockoutDuration.UntilReset,
        StartedAt = DateTime.Now,
        ResetTime = new TimeSpan(18, 0, 0),
        Reason = "Total loss exceeded $500"
    }
};
StateManager.SaveLockouts(data);
```

**Clearing all lockouts:**
```csharp
StateManager.ClearLockouts();  // Deletes the lockouts.xml file
```
