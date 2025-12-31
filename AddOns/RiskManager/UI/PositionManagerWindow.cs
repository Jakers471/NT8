// PositionManagerWindow.cs
// Separate UI window for per-position management
// OCO orders (stop/take profit), break-even, close positions

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    public class PositionManagerWindow : NTWindow
    {
        private readonly PositionManagerHandler _handler;
        private readonly RuleEngine _ruleEngine;

        // UI Elements - Entry
        private ComboBox _symbolCombo;
        private TextBox _quantityBox;
        private TextBox _stopLossBox;
        private TextBox _takeProfitBox;
        private TextBlock _riskDisplay;
        private TextBlock _validationText;

        // UI Elements - Positions
        private StackPanel _positionsPanel;
        private ComboBox _accountCombo;

        private DispatcherTimer _updateTimer;
        private Account _selectedAccount;

        public PositionManagerWindow(RuleEngine ruleEngine)
        {
            _ruleEngine = ruleEngine;
            _handler = new PositionManagerHandler(ruleEngine);

            Title = "Position Manager";
            Width = 450;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 400;
            MinHeight = 400;

            BuildUI();
            StartUpdateTimer();
            LoadAccounts();
        }

        private void BuildUI()
        {
            var mainPanel = new StackPanel { Margin = new Thickness(10) };

            // Account selector
            var accountPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            accountPanel.Children.Add(new TextBlock
            {
                Text = "Account: ",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            });
            _accountCombo = new ComboBox { Width = 200, Height = 28 };
            _accountCombo.SelectionChanged += OnAccountChanged;
            accountPanel.Children.Add(_accountCombo);
            mainPanel.Children.Add(accountPanel);

            // SECTION: OCO Order Entry
            mainPanel.Children.Add(CreateEntrySection());

            // SECTION: Active Positions
            mainPanel.Children.Add(CreatePositionsSection());

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = mainPanel
            };

            Content = scroll;
        }

        private Border CreateEntrySection()
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

            // Header
            panel.Children.Add(new TextBlock
            {
                Text = "OCO ORDER SETUP",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = Brushes.LightBlue,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Symbol selector
            var symbolPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            symbolPanel.Children.Add(new TextBlock { Text = "Symbol: ", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            _symbolCombo = new ComboBox { Width = 150, Height = 28 };
            _symbolCombo.SelectionChanged += OnSymbolChanged;
            symbolPanel.Children.Add(_symbolCombo);
            panel.Children.Add(symbolPanel);

            // Quantity
            var qtyPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            qtyPanel.Children.Add(new TextBlock { Text = "Quantity: ", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            _quantityBox = new TextBox { Width = 80, Height = 25, Text = "1" };
            _quantityBox.TextChanged += OnInputChanged;
            qtyPanel.Children.Add(_quantityBox);

            // Validation message
            _validationText = new TextBlock
            {
                Text = "",
                Foreground = Brushes.Orange,
                FontSize = 11,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            qtyPanel.Children.Add(_validationText);
            panel.Children.Add(qtyPanel);

            // Stop Loss (in $)
            var stopPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            stopPanel.Children.Add(new TextBlock { Text = "Stop Loss $: ", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            _stopLossBox = new TextBox { Width = 80, Height = 25, Text = "100" };
            _stopLossBox.TextChanged += OnInputChanged;
            stopPanel.Children.Add(_stopLossBox);
            stopPanel.Children.Add(new TextBlock
            {
                Text = "(max loss in dollars)",
                Foreground = Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(stopPanel);

            // Take Profit (in $)
            var tpPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            tpPanel.Children.Add(new TextBlock { Text = "Take Profit $: ", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            _takeProfitBox = new TextBox { Width = 80, Height = 25, Text = "200" };
            _takeProfitBox.TextChanged += OnInputChanged;
            tpPanel.Children.Add(_takeProfitBox);
            tpPanel.Children.Add(new TextBlock
            {
                Text = "(target profit in dollars)",
                Foreground = Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(tpPanel);

            // Risk display
            _riskDisplay = new TextBlock
            {
                Text = "Risk: $0.00",
                FontSize = 12,
                Foreground = Brushes.Yellow,
                Margin = new Thickness(0, 10, 0, 10)
            };
            panel.Children.Add(_riskDisplay);

            // Place OCO Button
            var placeBtn = new Button
            {
                Content = "Place OCO Orders on Current Position",
                Height = 35,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0, 100, 150)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 5, 0, 0)
            };
            placeBtn.Click += OnPlaceOcoClick;
            panel.Children.Add(placeBtn);

            panel.Children.Add(new TextBlock
            {
                Text = "Applies to EXISTING position in selected symbol",
                FontSize = 10,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 3, 0, 0)
            });

            border.Child = panel;
            return border;
        }

        private Border CreatePositionsSection()
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

            // Header with Close All button
            var headerPanel = new Grid();
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerText = new TextBlock
            {
                Text = "ACTIVE POSITIONS",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = Brushes.LightGreen,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headerText, 0);
            headerPanel.Children.Add(headerText);

            var closeAllBtn = new Button
            {
                Content = "Close All",
                Width = 70,
                Height = 25,
                Background = new SolidColorBrush(Color.FromRgb(180, 50, 50)),
                Foreground = Brushes.White,
                FontSize = 11
            };
            closeAllBtn.Click += OnCloseAllClick;
            Grid.SetColumn(closeAllBtn, 1);
            headerPanel.Children.Add(closeAllBtn);

            panel.Children.Add(headerPanel);

            // Positions panel (dynamic)
            _positionsPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(_positionsPanel);

            border.Child = panel;
            return border;
        }

        private void LoadAccounts()
        {
            _accountCombo.Items.Clear();
            foreach (var account in Account.All)
            {
                // Only show Sim accounts for safety
                if (account.Name.IndexOf("Sim", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _accountCombo.Items.Add(account.Name);
                }
            }

            if (_accountCombo.Items.Count > 0)
                _accountCombo.SelectedIndex = 0;
        }

        private void OnAccountChanged(object sender, SelectionChangedEventArgs e)
        {
            var accountName = _accountCombo.SelectedItem?.ToString();
            _selectedAccount = Account.All.FirstOrDefault(a => a.Name == accountName);
            UpdateSymbolList();
            UpdatePositionsList();
        }

        private void UpdateSymbolList()
        {
            _symbolCombo.Items.Clear();

            if (_selectedAccount == null) return;

            // Add symbols from active positions
            var positions = _handler.GetActivePositions(_selectedAccount);
            foreach (var pos in positions)
            {
                if (!_symbolCombo.Items.Contains(pos.Instrument))
                    _symbolCombo.Items.Add(pos.Instrument);
            }

            // Add common futures
            var commonSymbols = new[] { "ES", "NQ", "CL", "GC", "SI", "ZB", "ZN", "RTY", "YM", "MES", "MNQ" };
            foreach (var sym in commonSymbols)
            {
                if (!_symbolCombo.Items.Cast<string>().Any(s => s.StartsWith(sym)))
                    _symbolCombo.Items.Add(sym);
            }

            if (_symbolCombo.Items.Count > 0)
                _symbolCombo.SelectedIndex = 0;
        }

        private void OnSymbolChanged(object sender, SelectionChangedEventArgs e)
        {
            ValidateQuantity();
        }

        private void OnInputChanged(object sender, TextChangedEventArgs e)
        {
            ValidateQuantity();
            UpdateRiskDisplay();
        }

        private void ValidateQuantity()
        {
            if (!int.TryParse(_quantityBox.Text, out int qty))
            {
                _validationText.Text = "";
                return;
            }

            var symbol = _symbolCombo.SelectedItem?.ToString() ?? "";
            var result = _handler.ValidatePositionSize(symbol, qty);

            if (!result.IsValid)
            {
                _validationText.Text = result.Message;
                _validationText.Foreground = Brushes.Red;
            }
            else
            {
                _validationText.Text = "";
            }
        }

        private void UpdateRiskDisplay()
        {
            if (double.TryParse(_stopLossBox.Text, out double stopDollars))
            {
                _riskDisplay.Text = $"Risk: ${stopDollars:F2}";
            }
        }

        private void OnPlaceOcoClick(object sender, RoutedEventArgs e)
        {
            if (_selectedAccount == null)
            {
                MessageBox.Show("Select an account first", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var symbolName = _symbolCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(symbolName))
            {
                MessageBox.Show("Select a symbol first", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Find the position for this symbol
            var positions = _handler.GetActivePositions(_selectedAccount);
            var position = positions.FirstOrDefault(p => p.Instrument.StartsWith(symbolName.Split(' ')[0]));

            if (position == null)
            {
                MessageBox.Show($"No active position found for {symbolName}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Parse inputs
            if (!double.TryParse(_stopLossBox.Text, out double stopDollars))
            {
                MessageBox.Show("Invalid stop loss amount", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double.TryParse(_takeProfitBox.Text, out double tpDollars);

            bool isLong = position.Direction == "Long";

            // Calculate stop price from dollar risk
            double? stopPrice = null;
            if (stopDollars > 0)
            {
                stopPrice = _handler.CalculateStopPrice(
                    position.InstrumentObj,
                    position.Quantity,
                    position.AvgPrice,
                    stopDollars,
                    isLong);
            }

            // Calculate target price from dollar profit
            double? targetPrice = null;
            if (tpDollars > 0)
            {
                targetPrice = _handler.CalculateStopPrice(
                    position.InstrumentObj,
                    position.Quantity,
                    position.AvgPrice,
                    tpDollars,
                    !isLong); // Invert for profit direction
            }

            // Place OCO orders
            var success = _handler.PlaceOcoOrders(
                _selectedAccount,
                position.InstrumentObj,
                position.Quantity,
                isLong,
                stopPrice,
                targetPrice);

            if (success)
            {
                MessageBox.Show($"OCO orders placed!\nStop: {stopPrice:F2}\nTarget: {targetPrice:F2}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdatePositionsList()
        {
            _positionsPanel.Children.Clear();

            if (_selectedAccount == null)
            {
                _positionsPanel.Children.Add(new TextBlock
                {
                    Text = "Select an account",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            var positions = _handler.GetActivePositions(_selectedAccount);

            if (positions.Count == 0)
            {
                _positionsPanel.Children.Add(new TextBlock
                {
                    Text = "No active positions",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            foreach (var pos in positions)
            {
                _positionsPanel.Children.Add(CreatePositionRow(pos));
            }
        }

        private Border CreatePositionRow(PositionInfo position)
        {
            var border = new Border
            {
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 5),
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40))
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Position info
            var infoPanel = new StackPanel();

            // Symbol and direction
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = position.Instrument.Split(' ')[0],
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = Brushes.White
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = $"  {position.Quantity} {position.Direction}",
                FontSize = 12,
                Foreground = position.Direction == "Long" ? Brushes.LimeGreen : Brushes.Salmon,
                VerticalAlignment = VerticalAlignment.Center
            });
            infoPanel.Children.Add(headerPanel);

            // P&L
            var pnlColor = position.UnrealizedPnL >= 0 ? Brushes.LimeGreen : Brushes.Red;
            var sign = position.UnrealizedPnL >= 0 ? "+" : "";
            infoPanel.Children.Add(new TextBlock
            {
                Text = $"P&L: {sign}${position.UnrealizedPnL:F2}  |  Entry: {position.AvgPrice:F2}",
                FontSize = 11,
                Foreground = pnlColor
            });

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            // Action buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Break-Even button
            var beBtn = new Button
            {
                Content = "BE",
                Width = 40,
                Height = 28,
                Margin = new Thickness(0, 0, 5, 0),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(80, 80, 0)),
                Foreground = Brushes.White,
                ToolTip = "Move stop to break-even"
            };
            beBtn.Tag = position;
            beBtn.Click += OnBreakEvenClick;
            buttonPanel.Children.Add(beBtn);

            // Close button
            var closeBtn = new Button
            {
                Content = "X",
                Width = 30,
                Height = 28,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(150, 50, 50)),
                Foreground = Brushes.White,
                ToolTip = "Close position"
            };
            closeBtn.Tag = position;
            closeBtn.Click += OnClosePositionClick;
            buttonPanel.Children.Add(closeBtn);

            Grid.SetColumn(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            border.Child = grid;
            return border;
        }

        private void OnBreakEvenClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var position = button?.Tag as PositionInfo;

            if (position != null && _selectedAccount != null)
            {
                var success = _handler.MoveToBreakEven(_selectedAccount, position);
                if (success)
                {
                    // Flash the button
                    button.Background = Brushes.Green;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    timer.Tick += (s, args) =>
                    {
                        button.Background = new SolidColorBrush(Color.FromRgb(80, 80, 0));
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
        }

        private void OnClosePositionClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var position = button?.Tag as PositionInfo;

            if (position != null && _selectedAccount != null)
            {
                var result = MessageBox.Show(
                    $"Close {position.Quantity} {position.Direction} {position.Instrument}?",
                    "Confirm Close",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _handler.ClosePosition(_selectedAccount, position);
                }
            }
        }

        private void OnCloseAllClick(object sender, RoutedEventArgs e)
        {
            if (_selectedAccount == null) return;

            var positions = _handler.GetActivePositions(_selectedAccount);
            if (positions.Count == 0) return;

            var result = MessageBox.Show(
                $"Close ALL {positions.Count} positions?",
                "Confirm Close All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _handler.CloseAllPositions(_selectedAccount);
            }
        }

        private void StartUpdateTimer()
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _updateTimer.Tick += (s, e) => UpdatePositionsList();
            _updateTimer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
