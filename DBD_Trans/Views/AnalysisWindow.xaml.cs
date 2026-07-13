using DBD_Trans.Helpers;
using DBD_Trans.Models;
using DBD_Trans.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
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

        private bool _isMiddleScrolling = false;
        private Point _middleScrollOrigin;
        private ScrollViewer _targetScrollViewer;
        private DispatcherTimer _scrollTimer;

        private ErrorItem _currentToolTipError;
        private DispatcherTimer _cursorHideTimer;
        private RichTextBox _targetRtbForCursor;
        private const int CursorHideDelayMs = 40;

        private ToolTip _sharedToolTip;
        private TextBlock _toolTipTextBlock;

        public AnalysisWindow()
        {
            InitializeComponent();
            this.SourceInitialized += (s, e) =>
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SendMessage(handle, 0x80, IntPtr.Zero, IntPtr.Zero);
                SendMessage(handle, 0x80, IntPtr.Zero, new IntPtr(1));
            };
            Loaded += AnalysisWindow_Loaded;
            PreviewMouseLeftButtonDown += OnWindowPreviewMouseLeftButtonDown;
            DarkTitleBarHelper.ApplyDarkTitleBar(this);
            InitMiddleScroll();

            _cursorHideTimer = new DispatcherTimer();
            _cursorHideTimer.Interval = TimeSpan.FromMilliseconds(CursorHideDelayMs);
            _cursorHideTimer.Tick += CursorHideTimer_Tick;
        }

        private void AnalysisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.EnglishRichTextBox = EnglishRichTextBox;
            ViewModel.RussianRichTextBox = RussianRichTextBox;
            ViewModel.InitializeDocuments();
            InitSharedToolTip();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private void InitSharedToolTip()
        {
            _toolTipTextBlock = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Foreground = (Brush)FindResource("ForegroundPrimary") };
            _sharedToolTip = new ToolTip
            {
                Background = (Brush)FindResource("BackgroundLight"),
                Foreground = (Brush)FindResource("ForegroundPrimary"),
                BorderBrush = (Brush)FindResource("BorderDark"),
                Padding = new Thickness(8, 4, 8, 4),
                Content = _toolTipTextBlock,
                Placement = PlacementMode.Mouse
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

        private void CursorHideTimer_Tick(object sender, EventArgs e)
        {
            _cursorHideTimer.Stop();
            if (_targetRtbForCursor != null) _targetRtbForCursor.Cursor = Cursors.None;
        }

        private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (e.ClickCount == 2 && IsChildOf(source, ErrorsHeaderPanel)) { ToggleErrorsPanel(); e.Handled = true; return; }
            if (FindParentOfType<ListBox>(source) == ErrorsListBox) return;
            if (FindParentOfType<Button>(source) != null || FindParentOfType<ToggleButton>(source) != null ||
                FindParentOfType<TextBox>(source) != null || FindParentOfType<RichTextBox>(source) != null ||
                FindParentOfType<ScrollBar>(source) != null || FindParentOfType<Thumb>(source) != null) return;

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
                double calculatedHeight = 30 + (errorCount * 32);
                ErrorsRow.Height = new GridLength(Math.Min(calculatedHeight, Math.Max(150, this.ActualHeight * 0.4)));
            }
            else ErrorsRow.Height = new GridLength(20);
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
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    var textBox = FindVisualChild<TextBox>(item);
                    textBox?.Focus(); textBox?.SelectAll();
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
                if (string.IsNullOrWhiteSpace(errorItem.Text)) ViewModel.DeleteErrorCommand.Execute(errorItem);
                else { ViewModel.SaveChanges(); ViewModel.SelectedError = null; }
            }
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
                {
                    errorItem.IsEditing = false;
                    if (string.IsNullOrWhiteSpace(errorItem.Text)) ViewModel.DeleteErrorCommand.Execute(errorItem);
                    else { ViewModel.SelectedError = null; ViewModel.SaveChanges(); Keyboard.ClearFocus(); }
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
                {
                    errorItem.IsEditing = false; ViewModel.SelectedError = null; Keyboard.ClearFocus();
                }
                e.Handled = true;
            }
        }

        private void NewErrorTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { ViewModel.AddErrorCommand.Execute(null); Keyboard.ClearFocus(); e.Handled = true; }
        }

        private void RichTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null || !ViewModel.IsMarkerActive) return;
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                TextPointer pos = rtb.GetPositionFromPoint(e.GetPosition(rtb), true);
                if (pos != null) { ViewModel.RemoveHighlightAtPosition(rtb, pos); e.Handled = true; }
            }
        }

        private void RichTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null || !ViewModel.IsMarkerActive) return;
            if (!rtb.Selection.IsEmpty)
            {
                ViewModel.ApplyMarkerToSelection(rtb);
                Mouse.Capture(null); e.Handled = true;
                rtb.Selection.Select(rtb.Selection.Start, rtb.Selection.Start);
            }
        }

        // --- НОВОЕ: Обработка правого клика для объединения предложений ---
        private void RichTextBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!ViewModel.IsSplitBySentences) return;

            var rtb = sender as RichTextBox;
            var pos = e.GetPosition(rtb);
            var pointer = rtb.GetPositionFromPoint(pos, false);

            if (pointer != null)
            {
                var para = pointer.Paragraph;
                if (para != null && para.Tag is List<SentenceInfo> paraSentences && paraSentences.Count > 0)
                {
                    int cursorIndex = GetTextIndexFromPointer(rtb.Document, pointer);

                    SentenceInfo targetSentence = null;
                    foreach (var s in paraSentences)
                    {
                        if (cursorIndex >= s.StartIndex && cursorIndex <= s.StartIndex + s.Length)
                        {
                            targetSentence = s;
                            break;
                        }
                    }

                    if (targetSentence == null)
                    {
                        targetSentence = paraSentences[paraSentences.Count - 1];
                    }

                    if (targetSentence != null)
                    {
                        var menu = new ContextMenu();
                        var prevItem = new MenuItem { Header = "⬆️ Объединить с предыдущим" };
                        prevItem.Click += (s, ev) => ViewModel.MergeWithPreviousCommand.Execute(targetSentence);
                        var nextItem = new MenuItem { Header = "⬇️ Объединить со следующим" };
                        nextItem.Click += (s, ev) => ViewModel.MergeWithNextCommand.Execute(targetSentence);
                        var splitItem = new MenuItem { Header = "↕️ Выделить в отдельную строку" };
                        splitItem.Click += (s, ev) => ViewModel.SplitSentenceCommand.Execute(targetSentence);

                        menu.Items.Add(prevItem);
                        menu.Items.Add(nextItem);
                        menu.Items.Add(new Separator());
                        menu.Items.Add(splitItem);

                        menu.PlacementTarget = rtb;
                        menu.IsOpen = true;
                        e.Handled = true;
                    }
                }
            }
        }

        private void RichTextBox_LostFocus(object sender, RoutedEventArgs e) { }

        private void RichTextBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_sharedToolTip == null) return;
            var rtb = sender as RichTextBox;
            if (rtb == null) return;

            if (ViewModel.IsMarkerActive)
            {
                if (rtb.Cursor == Cursors.None) rtb.Cursor = Cursors.IBeam;
                _cursorHideTimer.Stop(); _targetRtbForCursor = null; HideToolTip(); return;
            }

            var pos = e.GetPosition(rtb);
            var pointer = rtb.GetPositionFromPoint(pos, false);
            var error = GetErrorAtPointer(rtb, pointer);

            if (error != null)
            {
                if (_targetRtbForCursor != rtb || !_cursorHideTimer.IsEnabled)
                {
                    _targetRtbForCursor = rtb; _cursorHideTimer.Stop(); _cursorHideTimer.Start();
                }
                if (_toolTipTextBlock.Text != error.Text) _toolTipTextBlock.Text = error.Text;

                Rect charRect = pointer.GetCharacterRect(LogicalDirection.Forward);
                Point bottomLeftInWindow = rtb.TransformToAncestor(this).Transform(new Point(charRect.Left, charRect.Bottom));

                if (!_sharedToolTip.IsOpen || _currentToolTipError != error)
                {
                    _sharedToolTip.PlacementTarget = this;
                    _sharedToolTip.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
                    _sharedToolTip.VerticalOffset = bottomLeftInWindow.Y + 2;
                    _sharedToolTip.HorizontalOffset = bottomLeftInWindow.X;
                    _currentToolTipError = error;
                    if (!_sharedToolTip.IsOpen) _sharedToolTip.IsOpen = true;
                }
            }
            else
            {
                _cursorHideTimer.Stop(); _targetRtbForCursor = null;
                if (rtb.Cursor == Cursors.None) rtb.Cursor = Cursors.IBeam;
                HideToolTip();
            }
        }

        private void RichTextBox_MouseLeave(object sender, MouseEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb != null && rtb.Cursor == Cursors.None) rtb.Cursor = Cursors.IBeam;
            _cursorHideTimer.Stop(); _targetRtbForCursor = null; HideToolTip();
        }

        private void HideToolTip()
        {
            if (_sharedToolTip != null && _sharedToolTip.IsOpen) _sharedToolTip.IsOpen = false;
            _currentToolTipError = null;
        }

        private ErrorItem GetErrorAtPointer(RichTextBox rtb, TextPointer pointer)
        {
            if (pointer == null) return null;
            var context = pointer.GetPointerContext(LogicalDirection.Forward);
            if (context != TextPointerContext.Text) return null;
            var nextContext = pointer.GetNextContextPosition(LogicalDirection.Forward);
            if (nextContext == null) return null;

            var checkRange = new TextRange(pointer, nextContext);
            string currentText = checkRange.Text;

            // ИСПРАВЛЕНИЕ: Используем IsNullOrEmpty вместо IsNullOrWhiteSpace, 
            // чтобы тултипы работали на пробелах (которые заменяют \n в режиме разделения).
            if (string.IsNullOrEmpty(currentText)) return null;

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
                                    if (index >= h.StartIndex && index < h.StartIndex + h.Length) return error;
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
            var targetParagraph = target.Paragraph;
            if (targetParagraph == null) return -1;

            var paraSentences = targetParagraph.Tag as List<SentenceInfo>;
            int localIndex = 0;
            var pointer = targetParagraph.ContentStart;

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
                            string text = range.Text;
                            int len = text.Length;
                            if (text.EndsWith("\r\n")) len -= 2;
                            else if (text.EndsWith("\n") || text.EndsWith("\r")) len -= 1;

                            localIndex += len;
                            break;
                        }
                        else
                        {
                            var range = new TextRange(pointer, next);
                            string text = range.Text;
                            int len = text.Length;
                            if (text.EndsWith("\r\n")) len -= 2;
                            else if (text.EndsWith("\n") || text.EndsWith("\r")) len -= 1;

                            localIndex += len;
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

            if (paraSentences == null)
            {
                return localIndex; // Обычный режим
            }

            // Режим разделения: маппим localIndex на предложение
            int offsetInPara = localIndex;
            foreach (var s in paraSentences)
            {
                if (offsetInPara <= s.Length)
                {
                    return s.StartIndex + offsetInPara;
                }
                offsetInPara -= (s.Length + 1); // Пропускаем предложение и пробел
            }

            var lastSentence = paraSentences[paraSentences.Count - 1];
            return lastSentence.StartIndex + lastSentence.Length;
        }
        private void InitMiddleScroll()
        {
            _scrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
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
                _isMiddleScrolling = true; _middleScrollOrigin = Mouse.GetPosition(this);
                _targetScrollViewer = sender as ScrollViewer; this.Cursor = Cursors.ScrollAll;
                _scrollTimer.Start(); e.Handled = true;
            }
            else if (_isMiddleScrolling && e.ChangedButton != MouseButton.Middle) StopMiddleScroll();
        }

        private void MiddleScroll_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _isMiddleScrolling) { StopMiddleScroll(); e.Handled = true; }
        }

        private void ScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!_isMiddleScrolling || _targetScrollViewer == null) return;
            Point currentPos = Mouse.GetPosition(this);
            double deltaY = currentPos.Y - _middleScrollOrigin.Y;
            const double deadzone = 15.0; const double speed = 0.8; const double maxScroll = 40.0;

            if (Math.Abs(deltaY) > deadzone)
            {
                double distance = Math.Abs(deltaY) - deadzone;
                double scrollY = (deltaY > 0 ? 1 : -1) * distance * speed;
                if (scrollY > maxScroll) scrollY = maxScroll; else if (scrollY < -maxScroll) scrollY = -maxScroll;
                scrollY *= 0.6;

                if (Math.Abs(scrollY) > 0.1)
                {
                    double currentOffset = _targetScrollViewer.VerticalOffset;
                    double newOffsetY = currentOffset + scrollY;
                    double maxOffset = _targetScrollViewer.ExtentHeight - _targetScrollViewer.ViewportHeight;
                    if (maxOffset < 0) maxOffset = 0;
                    if (newOffsetY < 0) newOffsetY = 0; else if (newOffsetY > maxOffset) newOffsetY = maxOffset;

                    if (Math.Abs(newOffsetY - currentOffset) > 0.01) _targetScrollViewer.ScrollToVerticalOffset(newOffsetY);
                }
            }
        }

        private void StopMiddleScroll()
        {
            if (_isMiddleScrolling) { _isMiddleScrolling = false; _scrollTimer.Stop(); this.Cursor = Cursors.Arrow; Mouse.Capture(null); }
        }

        private void NewErrorTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.SelectedError = null;
        }

        private void RichTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null) return;
            ScrollViewer sv = (rtb == EnglishRichTextBox) ? EnglishScrollViewer : RussianScrollViewer;
            if (sv == null) return;

            double scrollAmount = e.Delta / 8.0;
            double newOffset = sv.VerticalOffset - scrollAmount;
            sv.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }
    }
}