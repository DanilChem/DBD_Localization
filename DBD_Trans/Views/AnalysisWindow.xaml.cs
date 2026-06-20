using DBD_Trans.Helpers;
using DBD_Trans.Models;
using DBD_Trans.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace DBD_Trans.Views
{
    public partial class AnalysisWindow : Window
    {
        private AnalysisViewModel ViewModel => (AnalysisViewModel)DataContext;

        // --- Поля для скролла средней кнопкой ---
        private bool _isMiddleScrolling = false;
        private Point _middleScrollOrigin;
        private ScrollViewer _targetScrollViewer;
        private DispatcherTimer _scrollTimer;

        // --- Поля для единого ToolTip ---
        private ToolTip _sharedToolTip;
        private TextBlock _toolTipTextBlock;

        public AnalysisWindow()
        {
            InitializeComponent();
            Loaded += AnalysisWindow_Loaded;
            PreviewMouseLeftButtonDown += OnWindowPreviewMouseLeftButtonDown;
            DarkTitleBarHelper.ApplyDarkTitleBar(this);
            InitMiddleScroll();
        }

        private void AnalysisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.EnglishRichTextBox = EnglishRichTextBox;
            ViewModel.RussianRichTextBox = RussianRichTextBox;
            ViewModel.InitializeDocuments();

            // Инициализируем единый ToolTip
            InitSharedToolTip();
        }

        private void InitSharedToolTip()
        {
            _toolTipTextBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
                Foreground = (Brush)FindResource("ForegroundPrimary")
            };

            _sharedToolTip = new ToolTip
            {
                Background = (Brush)FindResource("BackgroundLight"),
                Foreground = (Brush)FindResource("ForegroundPrimary"),
                BorderBrush = (Brush)FindResource("BorderDark"),
                Padding = new Thickness(8, 4, 8, 4),
                Content = _toolTipTextBlock,
                // === ИЗМЕНЕНИЕ ЗДЕСЬ ===
                Placement = PlacementMode.Mouse // Тултип будет следовать за курсором
            };

            ToolTipService.SetInitialShowDelay(_sharedToolTip, 0);
            ToolTipService.SetBetweenShowDelay(_sharedToolTip, 0);
            ToolTipService.SetShowDuration(_sharedToolTip, 10000);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            ViewModel.OnClosing();
            base.OnClosing(e);
        }

        private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;

            if (e.ClickCount == 2 && IsChildOf(source, ErrorsHeaderPanel))
            {
                ToggleErrorsPanel();
                e.Handled = true;
                return;
            }

            if (FindParentOfType<ListBox>(source) == ErrorsListBox) return;
            if (FindParentOfType<Button>(source) != null ||
                FindParentOfType<TextBox>(source) != null ||
                FindParentOfType<RichTextBox>(source) != null ||
                FindParentOfType<ScrollBar>(source) != null ||
                FindParentOfType<Thumb>(source) != null)
                return;

            ViewModel.SelectedError = null;
            e.Handled = true;
        }

        private void ToggleErrorsPanel()
        {
            if (ErrorsRow == null) return;

            bool isCollapsed = ErrorsRow.Height.IsStar || (ErrorsRow.Height.IsAbsolute && ErrorsRow.Height.Value <= 30);
            if (isCollapsed)
            {
                int errorCount = ViewModel.Errors.Count;
                if (errorCount == 0) return;

                double itemHeight = 32;
                double headerHeight = 30;
                double calculatedHeight = headerHeight + (errorCount * itemHeight);
                double maxHeight = Math.Max(150, this.ActualHeight * 0.4);
                ErrorsRow.Height = new GridLength(Math.Min(calculatedHeight, maxHeight));
            }
            else
            {
                ErrorsRow.Height = new GridLength(20);
            }
        }

        private bool IsChildOf(DependencyObject child, DependencyObject parent)
        {
            while (child != null)
            {
                if (child == parent) return true;
                DependencyObject next = LogicalTreeHelper.GetParent(child);
                if (next == null)
                {
                    if (child is Visual || child is Visual3D) next = VisualTreeHelper.GetParent(child);
                    else if (child is TextElement te) next = te.Parent as DependencyObject;
                    else break;
                }
                child = next;
            }
            return false;
        }

        private static T FindParentOfType<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                DependencyObject parent = LogicalTreeHelper.GetParent(child);
                if (parent == null)
                {
                    if (child is Visual || child is Visual3D) parent = VisualTreeHelper.GetParent(child);
                    else break;
                }
                if (parent is T typedParent) return typedParent;
                child = parent;
            }
            return null;
        }

        private void ErrorsListBoxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is ErrorItem errorItem)
            {
                ViewModel.EditErrorCommand.Execute(errorItem);
                Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(() =>
                    {
                        var textBox = FindVisualChild<TextBox>(item);
                        textBox?.Focus();
                        textBox?.SelectAll();
                    }));
                e.Handled = true;
            }
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void EditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
            {
                errorItem.IsEditing = false;
                if (string.IsNullOrWhiteSpace(errorItem.Text))
                    ViewModel.DeleteErrorCommand.Execute(errorItem);
                else
                    ViewModel.SaveChanges();
            }
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
                {
                    errorItem.IsEditing = false;
                    if (string.IsNullOrWhiteSpace(errorItem.Text))
                        ViewModel.DeleteErrorCommand.Execute(errorItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
                    errorItem.IsEditing = false;
                e.Handled = true;
            }
        }

        private void NewErrorTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel.AddErrorCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void RichTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null || !ViewModel.IsMarkerActive) return;

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                TextPointer pos = rtb.GetPositionFromPoint(e.GetPosition(rtb), true);
                if (pos != null)
                {
                    ViewModel.RemoveHighlightAtPosition(rtb, pos);
                    e.Handled = true;
                }
            }
        }

        private void RichTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null || !ViewModel.IsMarkerActive) return;

            if (!rtb.Selection.IsEmpty)
            {
                ViewModel.ApplyMarkerToSelection(rtb);
                Mouse.Capture(null);
                e.Handled = true;
                rtb.Selection.Select(rtb.Selection.Start, rtb.Selection.Start);
            }
        }

        private void RichTextBox_LostFocus(object sender, RoutedEventArgs e) { }

        // ==========================================
        // ЛОГИКА ТУЛТИПОВ (TOOLTIP)
        // ==========================================

        private void RichTextBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_sharedToolTip == null) return;

            var rtb = sender as RichTextBox;
            if (rtb == null) return;

            if (ViewModel.IsMarkerActive)
            {
                HideToolTip();
                return;
            }

            var pos = e.GetPosition(rtb);
            var pointer = rtb.GetPositionFromPoint(pos, true);
            var error = GetErrorAtPointer(rtb, pointer);

            if (error != null)
            {
                if (_toolTipTextBlock.Text != error.Text)
                {
                    _toolTipTextBlock.Text = error.Text;
                }

                // === ИЗМЕНЕНИЕ ЗДЕСЬ ===
                // Удаляем привязку к PlacementTarget, в режиме Mouse она не нужна 
                // и именно она заставляла тултип "уезжать" к краям RichTextBox.
                /*
                if (_sharedToolTip.PlacementTarget != rtb)
                {
                    _sharedToolTip.PlacementTarget = rtb;
                }
                */

                if (!_sharedToolTip.IsOpen)
                {
                    _sharedToolTip.IsOpen = true;
                }
            }
            else
            {
                HideToolTip();
            }
        }

        private void RichTextBox_MouseLeave(object sender, MouseEventArgs e)
        {
            HideToolTip();
        }

        private void HideToolTip()
        {
            if (_sharedToolTip != null && _sharedToolTip.IsOpen)
            {
                _sharedToolTip.IsOpen = false;
            }
        }

        private ErrorItem GetErrorAtPointer(RichTextBox rtb, TextPointer pointer)
        {
            if (pointer == null) return null;

            var nextContext = pointer.GetNextContextPosition(LogicalDirection.Forward);
            if (nextContext == null) return null;

            // Создаем микро-диапазон для проверки цвета фона
            var checkRange = new TextRange(pointer, nextContext);
            var bg = checkRange.GetPropertyValue(TextElement.BackgroundProperty);

            if (bg is SolidColorBrush brush)
            {
                Color permColor = (Color)ColorConverter.ConvertFromString("#33CA5100");
                Color selColor = (Color)ColorConverter.ConvertFromString("#FFCA5100");

                if (brush.Color == permColor || brush.Color == selColor)
                {
                    int index = GetTextIndexFromPointer(rtb.Document, pointer);
                    if (index >= 0)
                    {
                        bool isEnglish = (rtb == EnglishRichTextBox);
                        var vm = DataContext as AnalysisViewModel;
                        if (vm == null) return null;

                        foreach (var error in vm.Errors)
                        {
                            var highlights = isEnglish ? error.EnglishHighlights : error.RussianHighlights;
                            if (highlights != null)
                            {
                                foreach (var h in highlights)
                                {
                                    if (index >= h.StartIndex && index < h.StartIndex + h.Length)
                                    {
                                        return error;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return null;
        }

        private int GetTextIndexFromPointer(FlowDocument doc, TextPointer target)
        {
            int index = 0;
            var pointer = doc.ContentStart;

            while (pointer != null && pointer.CompareTo(target) < 0)
            {
                var context = pointer.GetPointerContext(LogicalDirection.Forward);
                if (context == TextPointerContext.Text)
                {
                    var next = pointer.GetNextContextPosition(LogicalDirection.Forward);
                    if (next != null)
                    {
                        if (next.CompareTo(target) > 0)
                        {
                            var range = new TextRange(pointer, target);
                            index += range.Text.Length;
                            return index;
                        }
                        else
                        {
                            var range = new TextRange(pointer, next);
                            index += range.Text.Length;
                            pointer = next;
                        }
                    }
                    else break;
                }
                else
                {
                    pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
            return index;
        }

        // ==========================================
        // ЛОГИКА СКРОЛЛА СРЕДНЕЙ КНОПКОЙ
        // ==========================================

        private void InitMiddleScroll()
        {
            _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _scrollTimer.Tick += ScrollTimer_Tick;

            EnglishScrollViewer.PreviewMouseDown += MiddleScroll_PreviewMouseDown;
            RussianScrollViewer.PreviewMouseDown += MiddleScroll_PreviewMouseDown;
            this.PreviewMouseUp += MiddleScroll_PreviewMouseUp;
            this.Deactivated += (s, e) => StopMiddleScroll();
        }

        private void MiddleScroll_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed && !_isMiddleScrolling)
            {
                _isMiddleScrolling = true;
                _middleScrollOrigin = Mouse.GetPosition(this);
                _targetScrollViewer = sender as ScrollViewer;
                this.Cursor = Cursors.ScrollAll;
                _scrollTimer.Start();
                e.Handled = true;
            }
            else if (_isMiddleScrolling && e.ChangedButton != MouseButton.Middle)
            {
                StopMiddleScroll();
            }
        }

        private void MiddleScroll_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _isMiddleScrolling)
            {
                StopMiddleScroll();
                e.Handled = true;
            }
        }

        private void ScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!_isMiddleScrolling || _targetScrollViewer == null) return;

            Point currentPos = Mouse.GetPosition(this);
            double deltaY = currentPos.Y - _middleScrollOrigin.Y;

            const double deadzone = 15.0;
            const double speed = 0.3;

            if (Math.Abs(deltaY) > deadzone)
            {
                double distance = Math.Abs(deltaY) - deadzone;
                double scrollY = (deltaY > 0 ? 1 : -1) * distance * speed;
                scrollY = Math.Max(-4.0, Math.Min(4.0, scrollY));

                if (Math.Abs(scrollY) > 0.5)
                {
                    double newOffsetY = _targetScrollViewer.VerticalOffset + scrollY;
                    _targetScrollViewer.ScrollToVerticalOffset(newOffsetY);
                }
            }
        }

        private void StopMiddleScroll()
        {
            if (_isMiddleScrolling)
            {
                _isMiddleScrolling = false;
                _scrollTimer.Stop();
                this.Cursor = Cursors.Arrow;
                Mouse.Capture(null);
            }
        }
    }
}