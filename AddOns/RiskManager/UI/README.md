# UI

The UI folder contains the graphical user interface components for the Risk Manager add-on.

## Purpose

This folder provides a visual interface for traders to monitor their risk status, view current P&L, manage lockouts, and configure rule settings without editing code.

## Files

### RiskManagerWindow.cs

Main UI window for the Risk Manager. Extends `NTWindow` (NinjaTrader's base window class) to integrate with the NinjaTrader UI framework.

**Key Responsibilities:**
- Display real-time status (Active or Locked Out)
- Show current P&L with color-coded feedback
- Provide lockout management controls
- Allow rule configuration through a user-friendly interface
- Auto-update via timer (every 500ms)

## Window Properties

- **Title:** "Risk Manager"
- **Default Size:** 400 x 450 pixels
- **Minimum Size:** 350 x 400 pixels
- **Resizable:** Yes
- **Position:** Center of screen on open

## Tabbed Interface

The window uses a `TabControl` with two tabs:

### Tab 1: Status

Displays real-time monitoring status:

**Components:**
- **Status Banner** - Large colored banner showing:
  - Green "ACTIVE" when monitoring normally
  - Red "LOCKED OUT" when account is locked

- **P&L Display** - Large numeric display of current daily P&L with color coding:
  - Green: Positive P&L
  - Yellow: Negative but within 50% of max loss
  - Red: Approaching or exceeding loss limit

- **Lockout Info** - Text showing either:
  - Current monitoring status and max loss limit
  - Lockout reason when locked out

- **Action Buttons:**
  - **Reset Lockout** - Clear active lockouts
  - **Reset P&L** - Reset P&L baseline tracking
  - **Flatten All** - Emergency flatten all positions (orange button)

### Tab 2: Config

Configuration panel for rule settings:

**Rule Panels:**
Each rule has a bordered panel containing:
- Checkbox to enable/disable the rule
- Label describing the rule
- Dollar amount input field

**Configurable Rules:**
1. **Total Daily Loss (Lockout)** - Max total loss before lockout
2. **Floating Stop Loss (Flatten Only)** - Max unrealized loss before flatten
3. **Take Profit (Flatten Only)** - Profit target before flatten

**Save Button:**
- Saves configuration to disk via `StateManager.SaveConfig()`
- Displays confirmation message
- Note: Changes require NinjaTrader restart to take effect

## Button Handlers

### OnResetLockoutClick
```csharp
private void OnResetLockoutClick(object sender, RoutedEventArgs e)
{
    _actionHandler?.ClearAllLockouts();
    StateManager.ClearLockouts();
    UpdateUI();
}
```
Clears all active lockouts and removes the persistence file.

### OnResetPnLClick
```csharp
private void OnResetPnLClick(object sender, RoutedEventArgs e)
{
    _accountMonitor?.ResetAllBaselines();
    UpdateUI();
}
```
Resets the P&L tracking baseline for all accounts. Rules will measure from the new baseline.

### OnFlattenClick
```csharp
private void OnFlattenClick(object sender, RoutedEventArgs e)
{
    foreach (var account in NinjaTrader.Cbi.Account.All)
    {
        if (account.Name.IndexOf("Sim", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _actionHandler?.FlattenAll(account);
        }
    }
}
```
Emergency flatten - closes all positions on all SIM accounts immediately.

### OnSaveConfigClick
```csharp
private void OnSaveConfigClick(object sender, RoutedEventArgs e)
{
    // Parse values from UI controls
    _config.TotalLossEnabled = _totalLossCheck.IsChecked ?? false;
    _config.TotalLossMax = double.Parse(_totalLossBox.Text);
    // ... more parsing ...

    StateManager.SaveConfig(_config);
    MessageBox.Show("Configuration saved! Restart NinjaTrader for changes to take effect.");
}
```
Reads values from UI controls and persists to disk.

## Auto-Update Timer

The window uses a `DispatcherTimer` for automatic UI updates:

```csharp
private void StartUpdateTimer()
{
    _updateTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(500)  // 2x per second
    };
    _updateTimer.Tick += OnTimerTick;
    _updateTimer.Start();
}
```

**UpdateUI Method:**
1. Iterates through all accounts looking for SIM accounts
2. Checks lockout status via `_actionHandler.IsLockedOut()`
3. Retrieves current P&L from account
4. Updates status banner color and text
5. Updates P&L display with color coding
6. Updates lockout info text

## UI Components

**Key WPF Controls Used:**
- `TabControl` / `TabItem` - Tabbed interface
- `StackPanel` / `WrapPanel` - Layout containers
- `Border` - Styled containers with rounded corners
- `TextBlock` - Labels and status text
- `TextBox` - Input fields for dollar amounts
- `CheckBox` - Rule enable/disable toggles
- `Button` - Action buttons
- `ScrollViewer` - Scrollable config panel

## Constructor

```csharp
public RiskManagerWindow(
    AccountMonitor accountMonitor,
    ActionHandler actionHandler,
    RuleEngine ruleEngine)
```

Receives references to core components for:
- Monitoring account state
- Managing lockouts
- Accessing rule configuration

## Dependencies

- **Core** - `RuleEngine` for rule access
- **Handlers** - `AccountMonitor` for P&L reset, `ActionHandler` for lockout management
- **Persistence** - `StateManager` for loading/saving configuration

## How to Open the Window

The window is opened from the NinjaTrader Control Center menu:

1. **New > Risk Manager** menu item (added by `RiskManagerAddOn`)
2. Clicking the menu item triggers `OnRiskManagerMenuClick`:
```csharp
private void OnRiskManagerMenuClick(object sender, RoutedEventArgs e)
{
    var window = new RiskManagerWindow(_accountMonitor, _actionHandler, _ruleEngine);
    window.Show();
}
```

## Color Scheme

**Status Banner:**
- Active: `Color.FromRgb(0, 120, 0)` (dark green)
- Locked Out: `Color.FromRgb(180, 0, 0)` (dark red)

**P&L Display:**
- Positive: `Brushes.LimeGreen`
- Warning (< 50% of max loss): `Brushes.Yellow`
- Critical (approaching limit): `Brushes.Red`

**Flatten Button:**
- Background: `Color.FromRgb(180, 80, 0)` (orange - indicates caution)

## Window Lifecycle

```csharp
protected override void OnClosed(EventArgs e)
{
    _updateTimer?.Stop();  // Stop timer to prevent resource leaks
    base.OnClosed(e);
}
```

The timer is stopped when the window closes to prevent memory leaks and unnecessary processing.
