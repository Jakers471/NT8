// CopyTraderWindow.cs
// UI for configuring leader/follower accounts and viewing activity

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

namespace NinjaTrader.NinjaScript.AddOns.CopyTrader
{
    public class CopyTraderWindow : NTWindow
    {
        private readonly PositionMirror _mirror;
        private CopyTraderConfig _config;

        // UI Elements - Status
        private TextBlock _statusText;
        private Border _statusBorder;

        // UI Elements - Config
        private CheckBox _enabledCheck;
        private ComboBox _leaderCombo;
        private StackPanel _followersPanel;
        private ListBox _activityLog;

        private DispatcherTimer _updateTimer;
        private int _lastLogCount = -1;

        public CopyTraderWindow(PositionMirror mirror)
        {
            _mirror = mirror;
            _config = ConfigManager.LoadConfig();

            Title = "Copy Trader";
            Width = 450;
            Height = 550;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 400;
            MinHeight = 450;

            BuildUI();
            StartUpdateTimer();
        }

        private void BuildUI()
        {
            var tabControl = new TabControl { Margin = new Thickness(5) };

            tabControl.Items.Add(BuildStatusTab());
            tabControl.Items.Add(BuildConfigTab());
            tabControl.Items.Add(BuildLogTab());

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
                Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 15)
            };

            _statusText = new TextBlock
            {
                Text = "● DISABLED",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _statusBorder.Child = _statusText;
            panel.Children.Add(_statusBorder);

            // Enable toggle
            var enablePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
            _enabledCheck = new CheckBox
            {
                IsChecked = _config.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            _enabledCheck.Checked += OnEnabledChanged;
            _enabledCheck.Unchecked += OnEnabledChanged;
            enablePanel.Children.Add(_enabledCheck);
            enablePanel.Children.Add(new TextBlock
            {
                Text = "Enable Copy Trading",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(enablePanel);

            // Leader info
            panel.Children.Add(new TextBlock
            {
                Text = "LEADER ACCOUNT",
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.LimeGreen,
                Margin = new Thickness(0, 10, 0, 5)
            });

            var leaderInfo = new TextBlock
            {
                Text = string.IsNullOrEmpty(_config.LeaderAccount) ? "(not set)" : _config.LeaderAccount,
                FontSize = 16,
                Margin = new Thickness(10, 0, 0, 15)
            };
            panel.Children.Add(leaderInfo);

            // Follower summary
            panel.Children.Add(new TextBlock
            {
                Text = "FOLLOWER ACCOUNTS",
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Orange,
                Margin = new Thickness(0, 0, 0, 5)
            });

            foreach (var f in _config.Followers)
            {
                var status = f.Enabled ? "●" : "○";
                var color = f.Enabled ? Brushes.LimeGreen : Brushes.Gray;
                panel.Children.Add(new TextBlock
                {
                    Text = $"  {status} {f.AccountName} (x{f.Multiplier})",
                    Foreground = color,
                    FontSize = 14,
                    Margin = new Thickness(10, 2, 0, 2)
                });
            }

            if (_config.Followers.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "  (no followers configured)",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(10, 0, 0, 0)
                });
            }

            // Buttons
            var buttonPanel = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) };

            var startBtn = new Button
            {
                Content = "Start/Restart",
                MinWidth = 90,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0)
            };
            startBtn.Click += (s, e) => {
                _config.Enabled = true;
                _enabledCheck.IsChecked = true;
                ConfigManager.SaveConfig(_config);
                _mirror.Reload();
            };
            buttonPanel.Children.Add(startBtn);

            var stopBtn = new Button
            {
                Content = "Stop",
                MinWidth = 90,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0)
            };
            stopBtn.Click += (s, e) => {
                _config.Enabled = false;
                _enabledCheck.IsChecked = false;
                ConfigManager.SaveConfig(_config);
                _mirror.Stop();
            };
            buttonPanel.Children.Add(stopBtn);

            panel.Children.Add(buttonPanel);

            scroll.Content = panel;
            tab.Content = scroll;
            return tab;
        }

        private TabItem BuildConfigTab()
        {
            var tab = new TabItem { Header = "Configure" };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Margin = new Thickness(15) };

            // Leader selection
            panel.Children.Add(new TextBlock
            {
                Text = "LEADER ACCOUNT",
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.LimeGreen,
                Margin = new Thickness(0, 0, 0, 5)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Positions on this account will be copied to followers",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 5)
            });

            _leaderCombo = new ComboBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 20)
            };
            RefreshAccountList();
            panel.Children.Add(_leaderCombo);

            // Followers section
            panel.Children.Add(new TextBlock
            {
                Text = "FOLLOWER ACCOUNTS",
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Orange,
                Margin = new Thickness(0, 0, 0, 5)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "These accounts will mirror the leader's positions",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Add follower button
            var addBtn = new Button
            {
                Content = "+ Add Follower",
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(15, 0, 15, 0),
                Margin = new Thickness(0, 0, 0, 10)
            };
            addBtn.Click += OnAddFollowerClick;
            panel.Children.Add(addBtn);

            // Followers list
            _followersPanel = new StackPanel();
            RefreshFollowersList();
            panel.Children.Add(_followersPanel);

            // Save button
            var saveBtn = new Button
            {
                Content = "Save Configuration",
                Height = 35,
                Margin = new Thickness(0, 20, 0, 0),
                FontWeight = FontWeights.Bold
            };
            saveBtn.Click += OnSaveClick;
            panel.Children.Add(saveBtn);

            panel.Children.Add(new TextBlock
            {
                Text = "* Click Start/Restart on Status tab to apply changes",
                FontSize = 11,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 5, 0, 0)
            });

            scroll.Content = panel;
            tab.Content = scroll;
            return tab;
        }

        private TabItem BuildLogTab()
        {
            var tab = new TabItem { Header = "Activity Log" };
            var panel = new Grid { Margin = new Thickness(10) };
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _activityLog = new ListBox
            {
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray
            };
            Grid.SetRow(_activityLog, 0);
            panel.Children.Add(_activityLog);

            var clearBtn = new Button
            {
                Content = "Clear Log",
                Height = 28,
                Width = 100,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 0, 0)
            };
            clearBtn.Click += (s, e) => {
                ConfigManager.ClearLog();
                RefreshLog();
            };
            Grid.SetRow(clearBtn, 1);
            panel.Children.Add(clearBtn);

            RefreshLog();

            tab.Content = panel;
            return tab;
        }

        private void RefreshAccountList()
        {
            _leaderCombo.Items.Clear();
            _leaderCombo.Items.Add("(Select leader account)");

            foreach (var account in Account.All.OrderBy(a => a.Name))
            {
                _leaderCombo.Items.Add(account.Name);
            }

            // Select current leader
            if (!string.IsNullOrEmpty(_config.LeaderAccount))
            {
                var idx = _leaderCombo.Items.IndexOf(_config.LeaderAccount);
                _leaderCombo.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else
            {
                _leaderCombo.SelectedIndex = 0;
            }
        }

        private void RefreshFollowersList()
        {
            _followersPanel.Children.Clear();

            foreach (var follower in _config.Followers)
            {
                _followersPanel.Children.Add(CreateFollowerRow(follower));
            }

            if (_config.Followers.Count == 0)
            {
                _followersPanel.Children.Add(new TextBlock
                {
                    Text = "No followers added yet",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 10, 0, 10)
                });
            }
        }

        private Border CreateFollowerRow(FollowerConfig follower)
        {
            var border = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 5)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Enable checkbox
            var enableCheck = new CheckBox
            {
                IsChecked = follower.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            enableCheck.Checked += (s, e) => follower.Enabled = true;
            enableCheck.Unchecked += (s, e) => follower.Enabled = false;
            Grid.SetColumn(enableCheck, 0);
            grid.Children.Add(enableCheck);

            // Account name dropdown
            var accountCombo = new ComboBox
            {
                MinWidth = 150,
                Height = 25,
                Margin = new Thickness(0, 0, 10, 0)
            };
            foreach (var account in Account.All.OrderBy(a => a.Name))
            {
                accountCombo.Items.Add(account.Name);
            }
            var selIdx = accountCombo.Items.IndexOf(follower.AccountName);
            accountCombo.SelectedIndex = selIdx >= 0 ? selIdx : 0;
            accountCombo.SelectionChanged += (s, e) => {
                if (accountCombo.SelectedItem != null)
                    follower.AccountName = accountCombo.SelectedItem.ToString();
            };
            Grid.SetColumn(accountCombo, 1);
            grid.Children.Add(accountCombo);

            // Multiplier label
            var multLabel = new TextBlock
            {
                Text = "x",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 3, 0)
            };
            Grid.SetColumn(multLabel, 2);
            grid.Children.Add(multLabel);

            // Multiplier textbox
            var multBox = new TextBox
            {
                Text = follower.Multiplier.ToString("F1"),
                Width = 45,
                Height = 25,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            multBox.TextChanged += (s, e) => {
                if (double.TryParse(multBox.Text, out var mult))
                    follower.Multiplier = mult;
            };
            Grid.SetColumn(multBox, 3);
            grid.Children.Add(multBox);

            // Remove button
            var removeBtn = new Button
            {
                Content = "X",
                Width = 25,
                Height = 25,
                Background = new SolidColorBrush(Color.FromRgb(150, 50, 50)),
                Foreground = Brushes.White
            };
            removeBtn.Click += (s, e) => {
                _config.Followers.Remove(follower);
                RefreshFollowersList();
            };
            Grid.SetColumn(removeBtn, 4);
            grid.Children.Add(removeBtn);

            border.Child = grid;
            return border;
        }

        private void OnAddFollowerClick(object sender, RoutedEventArgs e)
        {
            // Get available accounts (not leader, not already a follower)
            var usedAccounts = new HashSet<string>(_config.Followers.Select(f => f.AccountName));
            if (!string.IsNullOrEmpty(_config.LeaderAccount))
                usedAccounts.Add(_config.LeaderAccount);

            var available = Account.All.Where(a => !usedAccounts.Contains(a.Name)).ToList();

            if (available.Count == 0)
            {
                MessageBox.Show("No more accounts available to add as followers.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _config.Followers.Add(new FollowerConfig
            {
                AccountName = available.First().Name,
                Multiplier = 1.0,
                Enabled = true
            });

            RefreshFollowersList();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // Update leader from combo
            if (_leaderCombo.SelectedIndex > 0)
            {
                _config.LeaderAccount = _leaderCombo.SelectedItem.ToString();
            }
            else
            {
                _config.LeaderAccount = "";
            }

            // Validate
            if (string.IsNullOrEmpty(_config.LeaderAccount) && _config.Enabled)
            {
                MessageBox.Show("Please select a leader account.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Remove leader from followers if present
            _config.Followers.RemoveAll(f => f.AccountName == _config.LeaderAccount);

            ConfigManager.SaveConfig(_config);
            MessageBox.Show("Configuration saved!\n\nClick Start/Restart to apply.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnEnabledChanged(object sender, RoutedEventArgs e)
        {
            _config.Enabled = _enabledCheck.IsChecked ?? false;
        }

        private void RefreshLog()
        {
            _activityLog.Items.Clear();
            var entries = ConfigManager.GetLogEntries();

            foreach (var entry in entries.Take(50))
            {
                var color = entry.Status == "Success" ? Brushes.LimeGreen :
                           entry.Status == "Failed" ? Brushes.Red : Brushes.Yellow;

                _activityLog.Items.Add(new ListBoxItem
                {
                    Content = $"{entry.Timestamp:HH:mm:ss} [{entry.Action}] {entry.Instrument} -> {entry.FollowerAccount} ({entry.Status})",
                    Foreground = color,
                    ToolTip = entry.Message
                });
            }

            if (entries.Count == 0)
            {
                _activityLog.Items.Add(new ListBoxItem
                {
                    Content = "No activity yet",
                    Foreground = Brushes.Gray,
                    IsEnabled = false
                });
            }
        }

        private void StartUpdateTimer()
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _updateTimer.Tick += (s, e) => UpdateUI();
            _updateTimer.Start();
        }

        private void UpdateUI()
        {
            try
            {
                // Update status
                if (_mirror != null && _mirror.IsActive)
                {
                    _statusText.Text = "● ACTIVE";
                    _statusBorder.Background = new SolidColorBrush(Color.FromRgb(0, 120, 0));
                }
                else
                {
                    _statusText.Text = "● STOPPED";
                    _statusBorder.Background = new SolidColorBrush(Color.FromRgb(80, 80, 80));
                }

                // Refresh log if changed
                var entries = ConfigManager.GetLogEntries();
                if (entries.Count != _lastLogCount)
                {
                    _lastLogCount = entries.Count;
                    RefreshLog();
                }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
