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

                    Log("═══════════════════════════════════════════════════════");
                    Log("   RISK MANAGER STARTING");
                    Log("═══════════════════════════════════════════════════════");

                    // Load saved configuration
                    var config = StateManager.LoadConfig();
                    Log($"Configuration loaded from disk");

                    // Create core components
                    _ruleEngine = new RuleEngine();
                    _actionHandler = new ActionHandler(); // Loads persisted lockouts automatically
                    _accountMonitor = new AccountMonitor(_ruleEngine, _actionHandler);

                    // ═══════════════════════════════════════════════════════════
                    // CONFIGURE RULES FROM SAVED CONFIG
                    // ═══════════════════════════════════════════════════════════

                    // RULE 1: Total Daily Loss (Realized + Unrealized) → LOCKOUT
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
                        Log($"  [OFF] Total Daily Loss: disabled");
                    }

                    // RULE 2: Unrealized Loss (Floating P&L) → FLATTEN ONLY
                    if (config.FloatingStopEnabled)
                    {
                        _ruleEngine.AddRule(new UnrealizedLossRule
                        {
                            Name = "Floating Stop Loss",
                            Enabled = true,
                            MaxLoss = config.FloatingStopMax,
                            Action = RuleAction.FlattenOnly
                        });
                        Log($"  [ON]  Floating Stop Loss: ${config.FloatingStopMax} → FLATTEN");
                    }
                    else
                    {
                        Log($"  [OFF] Floating Stop Loss: disabled");
                    }

                    // RULE 3: Unrealized Profit (Floating P&L) → FLATTEN ONLY
                    if (config.TakeProfitEnabled)
                    {
                        _ruleEngine.AddRule(new UnrealizedProfitRule
                        {
                            Name = "Floating Take Profit",
                            Enabled = true,
                            ProfitTarget = config.TakeProfitTarget,
                            Action = RuleAction.FlattenOnly
                        });
                        Log($"  [ON]  Take Profit: ${config.TakeProfitTarget} → FLATTEN");
                    }
                    else
                    {
                        Log($"  [OFF] Take Profit: disabled");
                    }

                    // RULE 4: Daily Realized Loss → LOCKOUT (optional)
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
                        Log($"  [ON]  Realized Loss: ${config.RealizedLossMax} → LOCKOUT");
                    }
                    else
                    {
                        Log($"  [OFF] Realized Loss: disabled");
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

        private void Log(string message)
        {
            Output.Process($"[RiskManager] {DateTime.Now:HH:mm:ss} {message}", PrintTo.OutputTab1);
        }
    }
}
