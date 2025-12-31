# Changelog

All notable changes to the Risk Manager add-on.

## [1.0.0] - 2024-12-31

### Added
- Initial release
- Core risk management framework
- Event-driven account monitoring (no polling)
- Persistent lockout state (survives NT8 restarts)
- Configurable rules via UI

### Rules Implemented
- **Total Daily Loss** - Lockout when realized + unrealized P&L exceeds limit
- **Floating Stop Loss** - Flatten only when unrealized loss exceeds limit
- **Floating Take Profit** - Flatten only when unrealized profit reaches target
- **Daily Realized Loss** - Lockout when realized-only P&L exceeds limit
- **Symbol Block** - Block orders on specific instruments
- **Trade Frequency** - Block excessive trading

### Features
- Auto-start when NinjaTrader loads
- Menu item in Control Center → New → Risk Manager
- Tabbed UI with Status and Config panels
- Reset Lockout button
- Reset P&L baseline button
- Flatten All button
- Configuration saved to XML file
- Lockout state persisted to XML file

### Architecture
- Modular folder structure:
  - Core/ - Entry point and engine
  - Handlers/ - Event handling and actions
  - Rules/ - Rule implementations
  - Persistence/ - State management
  - UI/ - Window and controls

### Technical Details
- SIM accounts only (safety filter)
- Event subscriptions: AccountItemUpdate, OrderUpdate, PositionUpdate, ExecutionUpdate
- Throttled logging (3 second intervals, $10 threshold)
- Allows closing orders during lockout
- Cancels orders before flatten (prevents self-cancellation)

---

## Future Enhancements (Planned)
- [ ] Trailing drawdown rule
- [ ] Max position size rule
- [ ] Time-based trading windows
- [ ] Multiple account support improvements
- [ ] Hot reload of configuration
- [ ] Rule violation history/audit log
