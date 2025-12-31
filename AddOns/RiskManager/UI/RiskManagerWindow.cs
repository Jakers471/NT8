// RiskManagerWindow.cs
// Main UI window with status display and rule configuration

#region Using declarations
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Gui.Tools;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    public class RiskManagerWindow : NTWindow
    {
        // References to core components
        private readonly AccountMonitor _accountMonitor;
        private readonly ActionHandler _actionHandler;
        private readonly RuleEngine _ruleEngine;

        // UI Elements - Status
        private TextBlock _statusText;
        private TextBlock _pnlText;
        private TextBlock _lockoutText;
        private Border _statusBorder;

        // UI Elements - Config
        private CheckBox _totalLossCheck;
        private TextBox _totalLossBox;
        private CheckBox _floatingStopCheck;
        private TextBox _floatingStopBox;
        private CheckBox _takeProfitCheck;
        private TextBox _takeProfitBox;

        // Update timer
        private DispatcherTimer _updateTimer;

        // Current config
        private RiskConfig _config;

        public RiskManagerWindow(AccountMonitor accountMonitor, ActionHandler actionHandler, RuleEngine ruleEngine)
        {
            _accountMonitor = accountMonitor;
            _actionHandler = actionHandler;
            _ruleEngine = ruleEngine;

            // Load saved config
            _config = StateManager.LoadConfig();

            // Window settings - BIGGER!
            Title = "Risk Manager";
            Width = 400;
            Height = 450;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 350;
            MinHeight = 400;

            BuildUI();
            StartUpdateTimer();
        }

        private void BuildUI()
        {
            // Main container with tabs
            var tabControl = new TabControl();
            tabControl.Margin = new Thickness(5);

            // ═══════════════════════════════════════════════════════════════
            // TAB 1: STATUS
            // ═══════════════════════════════════════════════════════════════
            var statusTab = new TabItem { Header = "Status" };
            var statusPanel = new StackPanel { Margin = new Thickness(15) };

            // Status banner
            _statusBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 0)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 15)
            };

            _statusText = new TextBlock
            {
                Text = "● ACTIVE",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _statusBorder.Child = _statusText;
            statusPanel.Children.Add(_statusBorder);

            // P&L Section
            var pnlLabel = new TextBlock
            {
                Text = "Daily P&L:",
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 5)
            };
            statusPanel.Children.Add(pnlLabel);

            _pnlText = new TextBlock
            {
                Text = "$0.00",
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.LimeGreen,
                Margin = new Thickness(0, 0, 0, 15)
            };
            statusPanel.Children.Add(_pnlText);

            // Lockout info
            _lockoutText = new TextBlock
            {
                Text = "Monitoring...",
                FontSize = 13,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            };
            statusPanel.Children.Add(_lockoutText);

            // Buttons - horizontal wrap panel for flexibility
            var buttonPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var resetLockoutBtn = CreateButton("Reset Lockout", OnResetLockoutClick);
            buttonPanel.Children.Add(resetLockoutBtn);

            var resetPnLBtn = CreateButton("Reset P&L", OnResetPnLClick);
            buttonPanel.Children.Add(resetPnLBtn);

            var flattenBtn = CreateButton("Flatten All", OnFlattenClick);
            flattenBtn.Background = new SolidColorBrush(Color.FromRgb(180, 80, 0));
            buttonPanel.Children.Add(flattenBtn);

            statusPanel.Children.Add(buttonPanel);

            statusTab.Content = statusPanel;
            tabControl.Items.Add(statusTab);

            // ═══════════════════════════════════════════════════════════════
            // TAB 2: CONFIGURATION
            // ═══════════════════════════════════════════════════════════════
            var configTab = new TabItem { Header = "Config" };
            var configScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var configPanel = new StackPanel { Margin = new Thickness(15) };

            // Total Daily Loss
            var totalLossPanel = CreateRulePanel(
                "Total Daily Loss (Lockout)",
                "Max total loss before lockout:",
                _config.TotalLossEnabled,
                _config.TotalLossMax,
                out _totalLossCheck,
                out _totalLossBox);
            configPanel.Children.Add(totalLossPanel);

            // Floating Stop Loss
            var floatingStopPanel = CreateRulePanel(
                "Floating Stop Loss (Flatten Only)",
                "Max unrealized loss before flatten:",
                _config.FloatingStopEnabled,
                _config.FloatingStopMax,
                out _floatingStopCheck,
                out _floatingStopBox);
            configPanel.Children.Add(floatingStopPanel);

            // Take Profit
            var takeProfitPanel = CreateRulePanel(
                "Take Profit (Flatten Only)",
                "Profit target before flatten:",
                _config.TakeProfitEnabled,
                _config.TakeProfitTarget,
                out _takeProfitCheck,
                out _takeProfitBox);
            configPanel.Children.Add(takeProfitPanel);

            // Save button
            var saveBtn = new Button
            {
                Content = "Save Configuration",
                Height = 35,
                Margin = new Thickness(0, 20, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.Bold
            };
            saveBtn.Click += OnSaveConfigClick;
            configPanel.Children.Add(saveBtn);

            // Note
            var noteText = new TextBlock
            {
                Text = "* Changes require restart to take effect",
                FontSize = 11,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 10, 0, 0)
            };
            configPanel.Children.Add(noteText);

            configScroll.Content = configPanel;
            configTab.Content = configScroll;
            tabControl.Items.Add(configTab);

            Content = tabControl;
        }

        private Button CreateButton(string text, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = text,
                MinWidth = 90,
                Height = 32,
                Margin = new Thickness(3),
                FontSize = 12,
                Padding = new Thickness(8, 0, 8, 0)
            };
            btn.Click += handler;
            return btn;
        }

        private Border CreateRulePanel(string title, string label, bool enabled, double value,
            out CheckBox checkBox, out TextBox textBox)
        {
            var border = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var panel = new StackPanel();

            // Title with checkbox
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            checkBox = new CheckBox
            {
                IsChecked = enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            header.Children.Add(checkBox);

            var titleText = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 13
            };
            header.Children.Add(titleText);
            panel.Children.Add(header);

            // Value input
            var inputPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(22, 8, 0, 0)
            };

            var labelText = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            inputPanel.Children.Add(labelText);

            var dollarSign = new TextBlock
            {
                Text = "$",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            };
            inputPanel.Children.Add(dollarSign);

            textBox = new TextBox
            {
                Text = value.ToString("F0"),
                Width = 80,
                Height = 25,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(5, 0, 5, 0)
            };
            inputPanel.Children.Add(textBox);

            panel.Children.Add(inputPanel);
            border.Child = panel;
            return border;
        }

        private void StartUpdateTimer()
        {
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _updateTimer.Tick += OnTimerTick;
            _updateTimer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            UpdateUI();
        }

        private void UpdateUI()
        {
            try
            {
                bool isLockedOut = false;
                double totalPnL = 0;
                string lockoutReason = "";

                foreach (var account in NinjaTrader.Cbi.Account.All)
                {
                    if (account.Name.IndexOf("Sim", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (_actionHandler != null && _actionHandler.IsLockedOut(account))
                        {
                            isLockedOut = true;
                            var state = _actionHandler.GetLockoutState(account);
                            lockoutReason = state?.Reason ?? "Unknown";
                        }

                        totalPnL = account.Get(NinjaTrader.Cbi.AccountItem.RealizedProfitLoss, NinjaTrader.Cbi.Currency.UsDollar)
                                 + account.Get(NinjaTrader.Cbi.AccountItem.UnrealizedProfitLoss, NinjaTrader.Cbi.Currency.UsDollar);
                        break;
                    }
                }

                // Update status display
                if (isLockedOut)
                {
                    _statusText.Text = "■ LOCKED OUT";
                    _statusBorder.Background = new SolidColorBrush(Color.FromRgb(180, 0, 0));
                    _lockoutText.Text = lockoutReason;
                    _lockoutText.Foreground = Brushes.Red;
                }
                else
                {
                    _statusText.Text = "● ACTIVE";
                    _statusBorder.Background = new SolidColorBrush(Color.FromRgb(0, 120, 0));
                    _lockoutText.Text = $"Max Loss: ${_config.TotalLossMax:F0} | Monitoring...";
                    _lockoutText.Foreground = Brushes.Gray;
                }

                // Update P&L with color
                _pnlText.Text = $"${totalPnL:F2}";
                if (totalPnL >= 0)
                    _pnlText.Foreground = Brushes.LimeGreen;
                else if (totalPnL > -_config.TotalLossMax * 0.5)
                    _pnlText.Foreground = Brushes.Yellow;
                else
                    _pnlText.Foreground = Brushes.Red;
            }
            catch (Exception ex)
            {
                _lockoutText.Text = $"Error: {ex.Message}";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // BUTTON HANDLERS
        // ═══════════════════════════════════════════════════════════════

        private void OnResetLockoutClick(object sender, RoutedEventArgs e)
        {
            _actionHandler?.ClearAllLockouts();
            StateManager.ClearLockouts();
            UpdateUI();
        }

        private void OnResetPnLClick(object sender, RoutedEventArgs e)
        {
            _accountMonitor?.ResetAllBaselines();
            UpdateUI();
        }

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

        private void OnSaveConfigClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Parse values
                _config.TotalLossEnabled = _totalLossCheck.IsChecked ?? false;
                _config.FloatingStopEnabled = _floatingStopCheck.IsChecked ?? false;
                _config.TakeProfitEnabled = _takeProfitCheck.IsChecked ?? false;

                if (double.TryParse(_totalLossBox.Text, out double totalLoss))
                    _config.TotalLossMax = totalLoss;
                if (double.TryParse(_floatingStopBox.Text, out double floatingStop))
                    _config.FloatingStopMax = floatingStop;
                if (double.TryParse(_takeProfitBox.Text, out double takeProfit))
                    _config.TakeProfitTarget = takeProfit;

                // Save to disk
                StateManager.SaveConfig(_config);

                MessageBox.Show("Configuration saved!\n\nRestart NinjaTrader for changes to take effect.",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
