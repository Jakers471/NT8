// RiskManagerWindow.cs
// Main UI window with status display and rule configuration

#region Using declarations
using System;
using System.Linq;
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
        private readonly AccountMonitor _accountMonitor;
        private readonly ActionHandler _actionHandler;
        private readonly RuleEngine _ruleEngine;

        // UI Elements - Status
        private TextBlock _statusText;
        private TextBlock _pnlText;
        private TextBlock _lockoutText;
        private TextBlock _countdownText;
        private Border _statusBorder;
        private ListBox _historyList;

        // UI Elements - Account Rules
        private CheckBox _totalLossCheck;
        private TextBox _totalLossBox;
        private CheckBox _realizedLossCheck;
        private TextBox _realizedLossBox;
        private CheckBox _dailyProfitCheck;
        private TextBox _dailyProfitBox;

        // UI Elements - Position Rules
        private CheckBox _positionStopCheck;
        private TextBox _positionStopBox;
        private CheckBox _positionTargetCheck;
        private TextBox _positionTargetBox;
        private CheckBox _maxSizeCheck;
        private TextBox _maxSizeDefaultBox;
        private TextBox _maxSizePerSymbolBox;

        // UI Elements - Trade Frequency
        private CheckBox _tradeFreqCheck;
        private TextBox _tradeFreqMaxBox;
        private TextBox _tradeFreqWindowBox;
        private TextBox _tradeFreqLockoutBox;

        // UI Elements - Symbol Block
        private CheckBox _symbolBlockCheck;
        private TextBox _symbolBlockListBox;

        private DispatcherTimer _updateTimer;
        private RiskConfig _config;

        public RiskManagerWindow(AccountMonitor accountMonitor, ActionHandler actionHandler, RuleEngine ruleEngine)
        {
            _accountMonitor = accountMonitor;
            _actionHandler = actionHandler;
            _ruleEngine = ruleEngine;

            _config = StateManager.LoadConfig();

            Title = "Risk Manager";
            Width = 420;
            Height = 550;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 380;
            MinHeight = 500;

            BuildUI();
            StartUpdateTimer();
        }

        private void BuildUI()
        {
            var tabControl = new TabControl { Margin = new Thickness(5) };

            // TAB 1: STATUS
            tabControl.Items.Add(BuildStatusTab());

            // TAB 2: P&L RULES (Lockout + Per-position)
            tabControl.Items.Add(BuildAccountRulesTab());

            // TAB 3: SIZE RULES (Position size)
            tabControl.Items.Add(BuildPositionRulesTab());

            // TAB 4: FREQUENCY (Trade limits)
            tabControl.Items.Add(BuildFrequencyTab());

            // TAB 5: SYMBOLS (Block list)
            tabControl.Items.Add(BuildSymbolsTab());

            Content = tabControl;
        }

        private TabItem BuildStatusTab()
        {
            var tab = new TabItem { Header = "Status" };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Margin = new Thickness(15) };

            // Status banner
            _statusBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 0)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 10)
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
            panel.Children.Add(_statusBorder);

            // Countdown timer (hidden when not locked)
            _countdownText = new TextBlock
            {
                Text = "",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Orange,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(_countdownText);

            // P&L
            panel.Children.Add(new TextBlock { Text = "Daily P&L:", FontSize = 12, Foreground = Brushes.Gray });
            _pnlText = new TextBlock
            {
                Text = "$0.00",
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.LimeGreen,
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(_pnlText);

            // Lockout info/reason
            _lockoutText = new TextBlock
            {
                Text = "Monitoring...",
                FontSize = 12,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            };
            panel.Children.Add(_lockoutText);

            // Buttons
            var buttonPanel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };

            var resetLockoutBtn = CreateButton("Reset Lockout", OnResetLockoutClick);
            buttonPanel.Children.Add(resetLockoutBtn);

            var resetPnLBtn = CreateButton("Reset P&L", OnResetPnLClick);
            buttonPanel.Children.Add(resetPnLBtn);

            var flattenBtn = CreateButton("Flatten All", OnFlattenClick);
            flattenBtn.Background = new SolidColorBrush(Color.FromRgb(180, 80, 0));
            buttonPanel.Children.Add(flattenBtn);

            panel.Children.Add(buttonPanel);

            // Recent Closures Header
            panel.Children.Add(new TextBlock
            {
                Text = "RECENT ENFORCEMENT ACTIONS",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 20, 0, 5)
            });

            // Closure history list
            _historyList = new ListBox
            {
                Height = 120,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray
            };
            panel.Children.Add(_historyList);

            // Clear history button
            var clearHistoryBtn = new Button
            {
                Content = "Clear History",
                Height = 25,
                FontSize = 10,
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Width = 80
            };
            clearHistoryBtn.Click += (s, e) =>
            {
                StateManager.ClearClosureHistory();
                RefreshHistoryList();
            };
            panel.Children.Add(clearHistoryBtn);

            scroll.Content = panel;
            tab.Content = scroll;
            return tab;
        }

        private TabItem BuildAccountRulesTab()
        {
            var tab = new TabItem { Header = "P&L Rules" };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Margin = new Thickness(15) };

            // LOCKOUT RULES SECTION
            panel.Children.Add(new TextBlock
            {
                Text = "LOCKOUT RULES (Flatten All + Block Trading)",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = Brushes.Red,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Total Daily Loss
            panel.Children.Add(CreateRuleRow(
                "Total Daily Loss (Realized + Unrealized)",
                "Max loss: $",
                _config.TotalLossEnabled,
                _config.TotalLossMax,
                out _totalLossCheck,
                out _totalLossBox));

            // Realized Loss Only
            panel.Children.Add(CreateRuleRow(
                "Realized Loss Only (Closed Trades)",
                "Max realized loss: $",
                _config.RealizedLossEnabled,
                _config.RealizedLossMax,
                out _realizedLossCheck,
                out _realizedLossBox));

            // Daily Profit Target
            panel.Children.Add(CreateRuleRow(
                "Daily Profit Target (Stop Trading)",
                "Lock after profit: $",
                _config.DailyProfitEnabled,
                _config.DailyProfitTarget,
                out _dailyProfitCheck,
                out _dailyProfitBox));

            // SEPARATOR
            panel.Children.Add(new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 15, 0, 15)
            });

            // PER-POSITION P&L RULES
            panel.Children.Add(new TextBlock
            {
                Text = "PER-POSITION RULES (Flatten That Position Only)",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = Brushes.Orange,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Unrealized Loss (per position)
            panel.Children.Add(CreateRuleRow(
                "Unrealized Loss (Per Position)",
                "Max floating loss: $",
                _config.PositionStopEnabled,
                _config.PositionStopMax,
                out _positionStopCheck,
                out _positionStopBox));

            // Unrealized Profit (per position)
            panel.Children.Add(CreateRuleRow(
                "Unrealized Profit (Per Position)",
                "Take profit at: $",
                _config.PositionTargetEnabled,
                _config.PositionTargetAmount,
                out _positionTargetCheck,
                out _positionTargetBox));

            // Save button
            var saveBtn = new Button
            {
                Content = "Save P&L Rules",
                Height = 35,
                Margin = new Thickness(0, 20, 0, 0),
                FontWeight = FontWeights.Bold
            };
            saveBtn.Click += OnSaveConfigClick;
            panel.Children.Add(saveBtn);

            panel.Children.Add(new TextBlock
            {
                Text = "* Requires restart to apply",
                FontSize = 11,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 5, 0, 0)
            });

            scroll.Content = panel;
            tab.Content = scroll;
            return tab;
        }

        private TabItem BuildPositionRulesTab()
        {
            var tab = new TabItem { Header = "Size" };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Margin = new Thickness(15) };

            // Header
            panel.Children.Add(new TextBlock
            {
                Text = "MAX POSITION SIZE (Per Symbol)",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Enable checkbox
            var enablePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            _maxSizeCheck = new CheckBox
            {
                IsChecked = _config.MaxPositionSizeEnabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            enablePanel.Children.Add(_maxSizeCheck);
            enablePanel.Children.Add(new TextBlock { Text = "Enable Max Position Size Rule", FontWeight = FontWeights.Bold });
            panel.Children.Add(enablePanel);

            // Default max
            var defaultPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 5, 0, 5) };
            defaultPanel.Children.Add(new TextBlock { Text = "Default max (if symbol not listed): ", VerticalAlignment = VerticalAlignment.Center });
            _maxSizeDefaultBox = new TextBox
            {
                Text = _config.MaxPositionSizeDefault.ToString(),
                Width = 50,
                Height = 25,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            defaultPanel.Children.Add(_maxSizeDefaultBox);
            defaultPanel.Children.Add(new TextBlock { Text = " contracts", VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(defaultPanel);

            // Per-symbol config label
            panel.Children.Add(new TextBlock
            {
                Text = "Per-Symbol Limits:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(20, 10, 0, 5)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Format: SYMBOL=MAX, SYMBOL=MAX (e.g., GC=2, ES=3, NQ=2)",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(20, 0, 0, 5)
            });

            // Per-symbol text box
            _maxSizePerSymbolBox = new TextBox
            {
                Text = _config.MaxPositionSizePerSymbol,
                Height = 60,
                Margin = new Thickness(20, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            panel.Children.Add(_maxSizePerSymbolBox);

            panel.Children.Add(new TextBlock
            {
                Text = "Flattens position if size exceeds limit.\nDoes NOT lockout - can trade again.",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 15, 0, 0)
            });

            // Save button
            var saveBtn = new Button
            {
                Content = "Save Size Rules",
                Height = 35,
                Margin = new Thickness(0, 20, 0, 0),
                FontWeight = FontWeights.Bold
            };
            saveBtn.Click += OnSaveConfigClick;
            panel.Children.Add(saveBtn);

            panel.Children.Add(new TextBlock
            {
                Text = "* Requires restart to apply",
                FontSize = 11,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 5, 0, 0)
            });

            scroll.Content = panel;
            tab.Content = scroll;
            return tab;
        }

        private TabItem BuildFrequencyTab()
        {
            var tab = new TabItem { Header = "Frequency" };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Margin = new Thickness(15) };

            // Header
            panel.Children.Add(new TextBlock
            {
                Text = "TRADE FREQUENCY LIMIT",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = Brushes.Orange,
                Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Locks out trading if too many trades in rolling window.\nLockout persists across restarts.",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            });

            // Enable checkbox
            var enablePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
            _tradeFreqCheck = new CheckBox
            {
                IsChecked = _config.TradeFrequencyEnabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            enablePanel.Children.Add(_tradeFreqCheck);
            enablePanel.Children.Add(new TextBlock { Text = "Enable Trade Frequency Limit", FontWeight = FontWeights.Bold });
            panel.Children.Add(enablePanel);

            // Max trades
            var maxPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 5, 0, 5) };
            maxPanel.Children.Add(new TextBlock { Text = "Max trades allowed: ", VerticalAlignment = VerticalAlignment.Center });
            _tradeFreqMaxBox = new TextBox
            {
                Text = _config.TradeFrequencyMaxTrades.ToString(),
                Width = 50,
                Height = 25,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            maxPanel.Children.Add(_tradeFreqMaxBox);
            panel.Children.Add(maxPanel);

            // Rolling window
            var windowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 5, 0, 5) };
            windowPanel.Children.Add(new TextBlock { Text = "Rolling window: ", VerticalAlignment = VerticalAlignment.Center });
            _tradeFreqWindowBox = new TextBox
            {
                Text = _config.TradeFrequencyWindowMinutes.ToString(),
                Width = 50,
                Height = 25,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            windowPanel.Children.Add(_tradeFreqWindowBox);
            windowPanel.Children.Add(new TextBlock { Text = " minutes", VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(windowPanel);

            // Lockout duration
            var lockoutPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 5, 0, 5) };
            lockoutPanel.Children.Add(new TextBlock { Text = "Lockout duration: ", VerticalAlignment = VerticalAlignment.Center });
            _tradeFreqLockoutBox = new TextBox
            {
                Text = _config.TradeFrequencyLockoutMinutes.ToString(),
                Width = 50,
                Height = 25,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            lockoutPanel.Children.Add(_tradeFreqLockoutBox);
            lockoutPanel.Children.Add(new TextBlock { Text = " minutes", VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(lockoutPanel);

            // Example
            panel.Children.Add(new TextBlock
            {
                Text = "Example: 5 trades in 10 min → 30 min lockout",
                FontSize = 11,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(20, 10, 0, 0)
            });

            // Save button
            var saveBtn = new Button
            {
                Content = "Save Frequency Rules",
                Height = 35,
                Margin = new Thickness(0, 25, 0, 0),
                FontWeight = FontWeights.Bold
            };
            saveBtn.Click += OnSaveConfigClick;
            panel.Children.Add(saveBtn);

            panel.Children.Add(new TextBlock
            {
                Text = "* Requires restart to apply",
                FontSize = 11,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 5, 0, 0)
            });

            scroll.Content = panel;
            tab.Content = scroll;
            return tab;
        }

        private TabItem BuildSymbolsTab()
        {
            var tab = new TabItem { Header = "Symbols" };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Margin = new Thickness(15) };

            // Header
            panel.Children.Add(new TextBlock
            {
                Text = "SYMBOL BLOCK LIST",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = Brushes.Red,
                Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Immediately closes any position in blocked symbols.\nBlocks new orders on these symbols.",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            });

            // Enable checkbox
            var enablePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            _symbolBlockCheck = new CheckBox
            {
                IsChecked = _config.SymbolBlockEnabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            enablePanel.Children.Add(_symbolBlockCheck);
            enablePanel.Children.Add(new TextBlock { Text = "Enable Symbol Block List", FontWeight = FontWeights.Bold });
            panel.Children.Add(enablePanel);

            // Block list label
            panel.Children.Add(new TextBlock
            {
                Text = "Blocked Symbols (comma-separated):",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(20, 10, 0, 5)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Format: CL, NG, HO, ES",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(20, 0, 0, 5)
            });

            // Block list text box
            _symbolBlockListBox = new TextBox
            {
                Text = _config.SymbolBlockList,
                Height = 80,
                Margin = new Thickness(20, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            panel.Children.Add(_symbolBlockListBox);

            // Warning
            panel.Children.Add(new TextBlock
            {
                Text = "WARNING: Positions in blocked symbols will be\nflattened IMMEDIATELY when detected!",
                FontSize = 11,
                Foreground = Brushes.Red,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 15, 0, 0)
            });

            // Save button
            var saveBtn = new Button
            {
                Content = "Save Symbol Rules",
                Height = 35,
                Margin = new Thickness(0, 20, 0, 0),
                FontWeight = FontWeights.Bold
            };
            saveBtn.Click += OnSaveConfigClick;
            panel.Children.Add(saveBtn);

            panel.Children.Add(new TextBlock
            {
                Text = "* Requires restart to apply",
                FontSize = 11,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 5, 0, 0)
            });

            scroll.Content = panel;
            tab.Content = scroll;
            return tab;
        }

        private Border CreateRuleRow(string title, string label, bool enabled, double value,
            out CheckBox checkBox, out TextBox textBox, bool isContracts = false)
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
            header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold });
            panel.Children.Add(header);

            // Value input
            var inputPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(22, 8, 0, 0)
            };

            inputPanel.Children.Add(new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center
            });

            textBox = new TextBox
            {
                Text = isContracts ? ((int)value).ToString() : value.ToString("F0"),
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

        private void StartUpdateTimer()
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _updateTimer.Tick += (s, e) => UpdateUI();
            _updateTimer.Start();
        }

        private int _lastHistoryCount = -1;

        private void UpdateUI()
        {
            try
            {
                bool isLockedOut = false;
                double totalPnL = 0;
                string lockoutReason = "";
                LockoutState lockoutState = null;

                foreach (var account in NinjaTrader.Cbi.Account.All)
                {
                    if (account.Name.IndexOf("Sim", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (_actionHandler?.IsLockedOut(account) == true)
                        {
                            isLockedOut = true;
                            lockoutState = _actionHandler.GetLockoutState(account);
                            lockoutReason = lockoutState?.Reason ?? "Unknown";
                        }

                        totalPnL = account.Get(NinjaTrader.Cbi.AccountItem.RealizedProfitLoss, NinjaTrader.Cbi.Currency.UsDollar)
                                 + account.Get(NinjaTrader.Cbi.AccountItem.UnrealizedProfitLoss, NinjaTrader.Cbi.Currency.UsDollar);
                        break;
                    }
                }

                if (isLockedOut)
                {
                    _statusText.Text = "■ LOCKED OUT";
                    _statusBorder.Background = new SolidColorBrush(Color.FromRgb(180, 0, 0));
                    _lockoutText.Text = lockoutReason;
                    _lockoutText.Foreground = Brushes.Red;

                    // Show countdown for timed lockouts
                    if (lockoutState != null && lockoutState.Type == LockoutDuration.Timed)
                    {
                        var remaining = lockoutState.TimeRemaining;
                        if (remaining > TimeSpan.Zero)
                        {
                            _countdownText.Text = $"Unlocks in: {remaining.Minutes:D2}:{remaining.Seconds:D2}";
                            _countdownText.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            _countdownText.Text = "";
                            _countdownText.Visibility = Visibility.Collapsed;
                        }
                    }
                    else if (lockoutState != null && lockoutState.Type == LockoutDuration.UntilReset)
                    {
                        _countdownText.Text = $"Resets at: {lockoutState.ResetTime:hh\\:mm}";
                        _countdownText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _countdownText.Text = "";
                        _countdownText.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    _statusText.Text = "● ACTIVE";
                    _statusBorder.Background = new SolidColorBrush(Color.FromRgb(0, 120, 0));
                    _lockoutText.Text = $"Max Loss: ${_config.TotalLossMax:F0} | Position Stop: ${_config.PositionStopMax:F0}";
                    _lockoutText.Foreground = Brushes.Gray;
                    _countdownText.Text = "";
                    _countdownText.Visibility = Visibility.Collapsed;
                }

                var sign = totalPnL >= 0 ? "+" : "";
                _pnlText.Text = $"{sign}${totalPnL:F2}";
                _pnlText.Foreground = totalPnL >= 0 ? Brushes.LimeGreen :
                    totalPnL > -_config.TotalLossMax * 0.5 ? Brushes.Yellow : Brushes.Red;

                // Refresh history list only when count changes
                var history = StateManager.GetClosureHistory();
                if (history.Count != _lastHistoryCount)
                {
                    _lastHistoryCount = history.Count;
                    RefreshHistoryList();
                }
            }
            catch { }
        }

        private void RefreshHistoryList()
        {
            try
            {
                _historyList.Items.Clear();
                var history = StateManager.GetClosureHistory();
                foreach (var record in history.Take(10)) // Show last 10
                {
                    var item = new ListBoxItem
                    {
                        Content = $"{record.Timestamp:HH:mm:ss} | {record.ActionType} | {record.Instrument ?? "ALL"} | {record.RuleName}",
                        ToolTip = record.Reason,
                        Foreground = record.ActionType == "Lockout" ? Brushes.Red :
                                    record.ActionType == "FlattenAll" ? Brushes.Orange : Brushes.Yellow
                    };
                    _historyList.Items.Add(item);
                }

                if (history.Count == 0)
                {
                    _historyList.Items.Add(new ListBoxItem
                    {
                        Content = "No enforcement actions yet",
                        Foreground = Brushes.Gray,
                        IsEnabled = false
                    });
                }
            }
            catch { }
        }

        private void OnResetLockoutClick(object sender, RoutedEventArgs e)
        {
            NinjaTrader.Code.Output.Process("[RiskManager] Reset Lockout button clicked", NinjaTrader.NinjaScript.PrintTo.OutputTab1);

            if (_accountMonitor != null)
            {
                NinjaTrader.Code.Output.Process("[RiskManager] Calling ResetAllBaselines...", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
                try { _accountMonitor.ResetAllBaselines(); }
                catch (Exception ex)
                {
                    NinjaTrader.Code.Output.Process($"[RiskManager] ERROR: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
                }
            }

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
                    _actionHandler?.FlattenAll(account);
            }
        }

        private void OnSaveConfigClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Account Rules
                _config.TotalLossEnabled = _totalLossCheck.IsChecked ?? false;
                _config.RealizedLossEnabled = _realizedLossCheck.IsChecked ?? false;
                _config.DailyProfitEnabled = _dailyProfitCheck.IsChecked ?? false;

                if (double.TryParse(_totalLossBox.Text, out var tl)) _config.TotalLossMax = tl;
                if (double.TryParse(_realizedLossBox.Text, out var rl)) _config.RealizedLossMax = rl;
                if (double.TryParse(_dailyProfitBox.Text, out var dp)) _config.DailyProfitTarget = dp;

                // Position Rules
                _config.PositionStopEnabled = _positionStopCheck.IsChecked ?? false;
                _config.PositionTargetEnabled = _positionTargetCheck.IsChecked ?? false;
                _config.MaxPositionSizeEnabled = _maxSizeCheck.IsChecked ?? false;

                if (double.TryParse(_positionStopBox.Text, out var ps)) _config.PositionStopMax = ps;
                if (double.TryParse(_positionTargetBox.Text, out var pt)) _config.PositionTargetAmount = pt;
                if (int.TryParse(_maxSizeDefaultBox.Text, out var ms)) _config.MaxPositionSizeDefault = ms;
                _config.MaxPositionSizePerSymbol = _maxSizePerSymbolBox.Text ?? "";

                // Trade Frequency Rules
                _config.TradeFrequencyEnabled = _tradeFreqCheck.IsChecked ?? false;
                if (int.TryParse(_tradeFreqMaxBox.Text, out var tfMax)) _config.TradeFrequencyMaxTrades = tfMax;
                if (int.TryParse(_tradeFreqWindowBox.Text, out var tfWin)) _config.TradeFrequencyWindowMinutes = tfWin;
                if (int.TryParse(_tradeFreqLockoutBox.Text, out var tfLock)) _config.TradeFrequencyLockoutMinutes = tfLock;

                // Symbol Block Rules
                _config.SymbolBlockEnabled = _symbolBlockCheck.IsChecked ?? false;
                _config.SymbolBlockList = _symbolBlockListBox.Text ?? "";

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
