// CopyTraderAddOn.cs
// Main entry point - auto-starts, creates menu item, manages lifecycle

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

namespace NinjaTrader.NinjaScript.AddOns.CopyTrader
{
    public class CopyTraderAddOn : AddOnBase
    {
        private static PositionMirror _positionMirror;
        private bool _menuItemAdded = false;

        public static PositionMirror PositionMirror => _positionMirror;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Copy Trader - Mirror positions from leader to follower accounts";
                Name = "CopyTrader";
            }
            else if (State == State.Active)
            {
                Initialize();
            }
            else if (State == State.Terminated)
            {
                Cleanup();
            }
        }

        private void Initialize()
        {
            try
            {
                Output.Reset(PrintTo.OutputTab1);
                Log("═══════════════════════════════════════════════════════");
                Log("   COPY TRADER STARTING");
                Log("═══════════════════════════════════════════════════════");

                // Load saved log
                ConfigManager.LoadLog();

                // Create position mirror
                _positionMirror = new PositionMirror();

                // Load config and start if enabled
                var config = ConfigManager.LoadConfig();
                if (config.Enabled)
                {
                    Log($"Auto-starting: Leader={config.LeaderAccount}, Followers={config.Followers.Count(f => f.Enabled)}");
                    _positionMirror.Start();
                }
                else
                {
                    Log("Disabled - use window to configure and enable");
                }

                Log("═══════════════════════════════════════════════════════");
                Log("   COPY TRADER READY");
                Log("═══════════════════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                Log($"*** INIT FAILED: {ex.Message} ***");
            }
        }

        private void Cleanup()
        {
            try
            {
                _positionMirror?.Stop();
                _positionMirror = null;
                Log("CopyTrader: Shutdown complete");
            }
            catch (Exception ex)
            {
                Log($"*** CLEANUP ERROR: {ex.Message} ***");
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
                        if (item is NTMenuItem menuItem && menuItem.Header?.ToString() == "Copy Trader")
                        {
                            _menuItemAdded = true;
                            return;
                        }
                    }

                    var copyMenuItem = new NTMenuItem
                    {
                        Header = "Copy Trader",
                        Style = Application.Current.TryFindResource("MainMenuItem") as Style
                    };
                    copyMenuItem.Click += OnCopyTraderMenuClick;
                    menu.Items.Add(copyMenuItem);
                    _menuItemAdded = true;
                }
            }
        }

        protected override void OnWindowDestroyed(Window window)
        {
            // Mirror continues in background
        }

        private void OnCopyTraderMenuClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new CopyTraderWindow(_positionMirror);
                window.Show();
            }
            catch (Exception ex)
            {
                Log($"*** ERROR OPENING WINDOW: {ex.Message} ***");
            }
        }

        private void Log(string message)
        {
            Output.Process($"[CopyTrader] {DateTime.Now:HH:mm:ss} {message}", PrintTo.OutputTab1);
        }
    }
}
