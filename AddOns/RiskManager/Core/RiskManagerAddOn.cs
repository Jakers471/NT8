// RiskManagerAddOn.cs
// Main entry point - auto-starts, creates menu item, manages lifecycle
// Loads configuration from persistence on startup

#region Using declarations
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.Code;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    public class RiskManagerAddOn : AddOnBase
    {
        // Core components
        private AccountMonitor _accountMonitor;
        private RuleEngine _ruleEngine;
        private ActionHandler _actionHandler;

        // State
        private bool _isInitialized = false;
        private readonly object _lock = new object();
        private bool _menuItemAdded = false;
        private bool _positionMenuItemAdded = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Risk Manager - Monitors account and enforces risk rules";
                Name = "RiskManager";
            }
            else if (State == State.Active)
            {
                // Auto-start when NT8 loads
                InitializeRiskManager();
            }
            else if (State == State.Terminated)
            {
                Cleanup();
            }
        }

        private void InitializeRiskManager()
        {
            lock (_lock)
            {
                if (_isInitialized) return;

                try
                {
                    // Clear output log for fresh start
                    Output.Reset(PrintTo.OutputTab1);

                    // Log startup to file
                    StateManager.LogToFile("AddOn", "═══════════════════════════════════════════════════════");
                    StateManager.LogToFile("AddOn", "   RISK MANAGER STARTING");
                    StateManager.LogToFile("AddOn", "═══════════════════════════════════════════════════════");

                    Log("═══════════════════════════════════════════════════════");
                    Log("   RISK MANAGER STARTING");
                    Log("═══════════════════════════════════════════════════════");

                    // Load saved configuration
                    var config = StateManager.LoadConfig();
                    Log($"Configuration loaded from disk");

                    // Load closure history
                    StateManager.LoadClosureHistory();

                    // Create core components
                    _ruleEngine = new RuleEngine();
                    _actionHandler = new ActionHandler(); // Loads persisted lockouts automatically
                    _accountMonitor = new AccountMonitor(_ruleEngine, _actionHandler);

                    // ═══════════════════════════════════════════════════════════
                    // ACCOUNT-WIDE RULES (Lockout + Flatten ALL)
                    // ═══════════════════════════════════════════════════════════
                    Log("ACCOUNT RULES:");

                    // Total Daily Loss → LOCKOUT
                    if (config.TotalLossEnabled)
                    {
                        _ruleEngine.AddRule(new MaxLossRule
                        {
                            Name = "Total Daily Loss",
                            Enabled = true,
                            MaxLoss = config.TotalLossMax,
                            Action = RuleAction.Lockout,
                            LockoutType = LockoutDuration.UntilReset,
                            ResetSchedule = ResetSchedule.Daily,
                            DailyResetTime = new TimeSpan(config.TotalLossResetHour, 0, 0)
                        });
                        Log($"  [ON]  Total Daily Loss: ${config.TotalLossMax} → LOCKOUT");
                    }
                    else
                    {
                        Log($"  [OFF] Total Daily Loss");
                    }

                    // Daily Realized Loss → LOCKOUT
                    if (config.RealizedLossEnabled)
                    {
                        _ruleEngine.AddRule(new DailyRealizedLossRule
                        {
                            Name = "Realized Loss Limit",
                            Enabled = true,
                            MaxLoss = config.RealizedLossMax,
                            Action = RuleAction.Lockout,
                            LockoutType = LockoutDuration.UntilReset,
                            ResetSchedule = ResetSchedule.Daily,
                            DailyResetTime = new TimeSpan(config.RealizedLossResetHour, 0, 0)
                        });
                        Log($"  [ON]  Realized Loss Only: ${config.RealizedLossMax} → LOCKOUT");
                    }
                    else
                    {
                        Log($"  [OFF] Realized Loss Only");
                    }

                    // Daily Profit Target → LOCKOUT
                    if (config.DailyProfitEnabled)
                    {
                        _ruleEngine.AddRule(new DailyRealizedProfitRule
                        {
                            Name = "Daily Profit Target",
                            Enabled = true,
                            ProfitTarget = config.DailyProfitTarget,
                            Action = RuleAction.Lockout,
                            LockoutType = LockoutDuration.UntilReset,
                            ResetSchedule = ResetSchedule.Daily,
                            DailyResetTime = new TimeSpan(config.DailyProfitResetHour, 0, 0)
                        });
                        Log($"  [ON]  Daily Profit Target: ${config.DailyProfitTarget} → LOCKOUT");
                    }
                    else
                    {
                        Log($"  [OFF] Daily Profit Target");
                    }

                    // ═══════════════════════════════════════════════════════════
                    // PER-POSITION RULES (Flatten single position only)
                    // ═══════════════════════════════════════════════════════════
                    Log("POSITION RULES:");

                    // Per-Position Unrealized Loss → Flatten position (NOT lockout)
                    if (config.PositionStopEnabled)
                    {
                        _ruleEngine.AddRule(new UnrealizedLossRule
                        {
                            Name = "Per-Position Loss",
                            Enabled = true,
                            MaxLoss = config.PositionStopMax,
                            Action = RuleAction.FlattenPosition
                        });
                        Log($"  [ON]  Per-Position Loss: -${config.PositionStopMax} unrealized → FLATTEN POSITION (no lockout)");
                    }
                    else
                    {
                        Log($"  [OFF] Per-Position Loss");
                    }

                    // Per-Position Unrealized Profit → Flatten position (NOT lockout)
                    if (config.PositionTargetEnabled)
                    {
                        _ruleEngine.AddRule(new UnrealizedProfitRule
                        {
                            Name = "Per-Position Profit",
                            Enabled = true,
                            ProfitTarget = config.PositionTargetAmount,
                            Action = RuleAction.FlattenPosition
                        });
                        Log($"  [ON]  Position Target: ${config.PositionTargetAmount} → FLATTEN POSITION");
                    }
                    else
                    {
                        Log($"  [OFF] Position Target");
                    }

                    // Max Position Size → Flatten position (per symbol)
                    if (config.MaxPositionSizeEnabled)
                    {
                        var sizeRule = new MaxPositionSizeRule
                        {
                            Name = "Max Position Size",
                            Enabled = true,
                            DefaultMax = config.MaxPositionSizeDefault,
                            PerSymbolConfig = config.MaxPositionSizePerSymbol,
                            Action = RuleAction.FlattenPosition
                        };
                        sizeRule.ParseConfig();
                        _ruleEngine.AddRule(sizeRule);
                        Log($"  [ON]  Max Position Size: Default={config.MaxPositionSizeDefault}, Per-symbol={config.MaxPositionSizePerSymbol} → FLATTEN POSITION");
                    }
                    else
                    {
                        Log($"  [OFF] Max Position Size");
                    }

                    // ═══════════════════════════════════════════════════════════
                    // TRADE FREQUENCY RULE (Timed Lockout)
                    // ═══════════════════════════════════════════════════════════
                    Log("FREQUENCY RULES:");

                    // Trade Frequency → Timed lockout
                    if (config.TradeFrequencyEnabled)
                    {
                        _ruleEngine.AddRule(new TradeFrequencyRule
                        {
                            Name = "Trade Frequency",
                            Enabled = true,
                            MaxTrades = config.TradeFrequencyMaxTrades,
                            WindowMinutes = config.TradeFrequencyWindowMinutes,
                            LockoutMinutes = config.TradeFrequencyLockoutMinutes,
                            Action = RuleAction.Lockout,
                            LockoutType = LockoutDuration.Timed
                        });
                        Log($"  [ON]  Trade Frequency: {config.TradeFrequencyMaxTrades} trades/{config.TradeFrequencyWindowMinutes}min → {config.TradeFrequencyLockoutMinutes}min LOCKOUT");
                    }
                    else
                    {
                        Log($"  [OFF] Trade Frequency");
                    }

                    // ═══════════════════════════════════════════════════════════
                    // SYMBOL BLOCK LIST (Flatten blocked positions)
                    // ═══════════════════════════════════════════════════════════
                    Log("SYMBOL RULES:");

                    // Symbol Block → Flatten position
                    if (config.SymbolBlockEnabled && !string.IsNullOrWhiteSpace(config.SymbolBlockList))
                    {
                        var blockRule = new SymbolBlockRule
                        {
                            Name = "Symbol Block",
                            Enabled = true,
                            BlockListConfig = config.SymbolBlockList,
                            Action = RuleAction.FlattenPosition
                        };
                        blockRule.ParseConfig();
                        _ruleEngine.AddRule(blockRule);
                        Log($"  [ON]  Symbol Block: {config.SymbolBlockList} → FLATTEN POSITION");
                    }
                    else
                    {
                        Log($"  [OFF] Symbol Block");
                    }

                    Log("───────────────────────────────────────────────────────");

                    // Check for active lockouts
                    foreach (var account in Account.All.Where(a =>
                        a.Name.IndexOf("Sim", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        if (_actionHandler.IsLockedOut(account))
                        {
                            var state = _actionHandler.GetLockoutState(account);
                            Log($"*** ACTIVE LOCKOUT: {account.Name} ***");
                            Log($"    Reason: {state?.Reason}");
                        }
                    }

                    // Start monitoring all connected accounts
                    _accountMonitor.StartMonitoring();

                    _isInitialized = true;
                    Log("═══════════════════════════════════════════════════════");
                    Log("   RISK MANAGER ACTIVE - Monitoring SIM accounts");
                    Log("═══════════════════════════════════════════════════════");
                }
                catch (Exception ex)
                {
                    Log($"*** INIT FAILED: {ex.Message} ***");
                }
            }
        }

        private void Cleanup()
        {
            lock (_lock)
            {
                try
                {
                    _accountMonitor?.StopMonitoring();
                    _accountMonitor = null;
                    _ruleEngine = null;
                    _actionHandler = null;
                    _isInitialized = false;

                    Log("RiskManager: Shutdown complete");
                }
                catch (Exception ex)
                {
                    Log($"*** CLEANUP ERROR: {ex.Message} ***");
                }
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            if (window is ControlCenter && !_menuItemAdded)
            {
                var controlCenter = window as ControlCenter;
                var menu = controlCenter.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;

                if (menu != null)
                {
                    // Check if already exists
                    foreach (var item in menu.Items)
                    {
                        if (item is NTMenuItem menuItem && menuItem.Header?.ToString() == "Risk Manager")
                        {
                            _menuItemAdded = true;
                            return;
                        }
                    }

                    var riskMenuItem = new NTMenuItem
                    {
                        Header = "Risk Manager",
                        Style = Application.Current.TryFindResource("MainMenuItem") as Style
                    };
                    riskMenuItem.Click += OnRiskManagerMenuClick;
                    menu.Items.Add(riskMenuItem);
                    _menuItemAdded = true;

                    // Add Position Manager menu item
                    if (!_positionMenuItemAdded)
                    {
                        var posMenuItem = new NTMenuItem
                        {
                            Header = "Position Manager",
                            Style = Application.Current.TryFindResource("MainMenuItem") as Style
                        };
                        posMenuItem.Click += OnPositionManagerMenuClick;
                        menu.Items.Add(posMenuItem);
                        _positionMenuItemAdded = true;
                    }

                    // Auto-open windows at startup
                    controlCenter.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            // Find and click menu items to open windows
                            var newMenu = controlCenter.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
                            if (newMenu != null)
                            {
                                foreach (var item in newMenu.Items)
                                {
                                    if (item is NTMenuItem mi)
                                    {
                                        var header = mi.Header?.ToString() ?? "";
                                        if (header == "NinjaScript Output" ||
                                            header == "NinjaScript Editor" ||
                                            header == "Basic Entry")
                                        {
                                            mi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                                        }
                                    }
                                }
                            }

                            // Open Risk Manager window
                            var riskWindow = new RiskManagerWindow(_accountMonitor, _actionHandler, _ruleEngine);
                            riskWindow.Show();

                            Log("Startup windows auto-opened");
                        }
                        catch (Exception ex)
                        {
                            Log($"*** AUTO-OPEN ERROR: {ex.Message} ***");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        protected override void OnWindowDestroyed(Window window)
        {
            // Monitoring continues in background
        }

        private void OnRiskManagerMenuClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new RiskManagerWindow(_accountMonitor, _actionHandler, _ruleEngine);
                window.Show();
            }
            catch (Exception ex)
            {
                Log($"*** ERROR OPENING WINDOW: {ex.Message} ***");
            }
        }

        private void OnPositionManagerMenuClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new PositionManagerWindow(_ruleEngine);
                window.Show();
            }
            catch (Exception ex)
            {
                Log($"*** ERROR OPENING POSITION MANAGER: {ex.Message} ***");
            }
        }

        private void Log(string message)
        {
            Output.Process($"[RiskManager] {DateTime.Now:HH:mm:ss} {message}", PrintTo.OutputTab1);
        }
    }
}
