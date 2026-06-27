using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OtoProgram
{
    public partial class RecentActionsWindow : Window
    {
        public RecentItem SelectedItem { get; private set; }

        public RecentActionsWindow()
        {
            InitializeComponent();
            LoadItems();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LoadItems()
        {
            pnlItems.Children.Clear();
            var list = RecentManager.LoadRecent();
            
            if (list == null || list.Count == 0)
            {
                pnlItems.Children.Add(new TextBlock
                {
                    Text = "Kayıtlı son işlem bulunmuyor.",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 50, 0, 0),
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            foreach (var item in list)
            {
                var rowBorder = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFrom("#F8FAFC"),
                    BorderBrush = (Brush)new BrushConverter().ConvertFrom("#E2E8F0"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 10),
                    Cursor = Cursors.Hand
                };

                rowBorder.Resources.Add(typeof(Border), new Style(typeof(Border))
                {
                    Triggers = {
                        new Trigger {
                            Property = UIElement.IsMouseOverProperty,
                            Value = true,
                            Setters = {
                                new Setter(Border.BorderBrushProperty, (Brush)new BrushConverter().ConvertFrom(item.Color)),
                                new Setter(Border.BackgroundProperty, (Brush)new BrushConverter().ConvertFrom("#F1F5F9"))
                            }
                        }
                    }
                });

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var iconBorder = new Border
                {
                    Width = 36,
                    Height = 36,
                    Background = (Brush)new BrushConverter().ConvertFrom(item.Color + "1A"),
                    CornerRadius = new CornerRadius(18),
                    Margin = new Thickness(0, 0, 12, 0)
                };
                var iconText = new TextBlock
                {
                    Text = item.Icon,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBorder.Child = iconText;
                Grid.SetColumn(iconBorder, 0);
                grid.Children.Add(iconBorder);

                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var titleText = new TextBlock
                {
                    Text = item.Title,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = (Brush)new BrushConverter().ConvertFrom("#1E293B")
                };
                var subtitleText = new TextBlock
                {
                    Text = item.Subtitle,
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                textStack.Children.Add(titleText);
                textStack.Children.Add(subtitleText);
                Grid.SetColumn(textStack, 1);
                grid.Children.Add(textStack);

                var timeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                var timeText = new TextBlock
                {
                    Text = item.When.ToString("HH:mm"),
                    FontSize = 10,
                    Foreground = Brushes.DarkGray,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                var dateText = new TextBlock
                {
                    Text = item.When.ToString("dd.MM.yyyy"),
                    FontSize = 8,
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                timeStack.Children.Add(timeText);
                timeStack.Children.Add(dateText);
                Grid.SetColumn(timeStack, 2);
                grid.Children.Add(timeStack);

                rowBorder.Child = grid;

                rowBorder.MouseDown += (s, e) =>
                {
                    if (e.ChangedButton == MouseButton.Left)
                    {
                        SelectedItem = item;
                        DialogResult = true;
                        Close();
                    }
                };

                pnlItems.Children.Add(rowBorder);
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string recentPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OtoProgram",
                    "recent.json");
                if (System.IO.File.Exists(recentPath))
                {
                    System.IO.File.Delete(recentPath);
                }
            }
            catch { }
            LoadItems();
        }
    }
}
