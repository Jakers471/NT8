# Project Instructions

## NinjaTrader Add-On Development

**IMPORTANT:** After ANY code changes to files in the `AddOns/` folder:
1. Compile in NinjaTrader (NinjaScript Editor → right-click → Compile)
2. **RESTART NinjaTrader completely** - Add-on changes only load at startup

Always remind the user: "Compile and **restart NinjaTrader** to test changes."

## File Structure
- `AddOns/RiskManager/` - Risk management add-on
- Core components load once at startup
- UI windows can be reopened, but core logic requires restart
