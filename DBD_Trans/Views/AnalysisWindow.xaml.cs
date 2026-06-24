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
        // Кэшируем текущее замечание, чтобы понимать, когда нужно пересчитать координаты
        private ErrorItem _currentToolTipError;
        private DispatcherTimer _cursorHideTimer;
        private RichTextBox _targetRtbForCursor;
        // Задержка в миллисекундах перед скрытием курсора (можно настроить под себя)
        private const int CursorHideDelayMs = 40;

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
            // Инициализация таймера для плавного скрытия курсора
            _cursorHideTimer = new DispatcherTimer();
            _cursorHideTimer.Interval = TimeSpan.FromMilliseconds(CursorHideDelayMs);
            _cursorHideTimer.Tick += CursorHideTimer_Tick;
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

        private void CursorHideTimer_Tick(object sender, EventArgs e)
        {
            _cursorHideTimer.Stop();
            // Если мышь всё ещё над тем же RichTextBox, скрываем курсор
            if (_targetRtbForCursor != null)
            {
                _targetRtbForCursor.Cursor = Cursors.None;
            }
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
                {
                    ViewModel.SaveChanges();
                    ViewModel.SelectedError = null;
                }
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
                    {
                        ViewModel.DeleteErrorCommand.Execute(errorItem);
                    }
                    else
                    {
                        ViewModel.SelectedError = null;
                        ViewModel.SaveChanges();
                        Keyboard.ClearFocus();
                    }
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
                {
                    errorItem.IsEditing = false;
                    ViewModel.SelectedError = null;
                    Keyboard.ClearFocus();
                }
                e.Handled = true;
            }
        }

        private void NewErrorTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel.AddErrorCommand.Execute(null);
                Keyboard.ClearFocus();
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
            var pointer = rtb.GetPositionFromPoint(pos, false);
            var error = GetErrorAtPointer(rtb, pointer);

            if (error != null)
            {
                // === ЛОГИКА ЗАДЕРЖКИ СКРЫТИЯ КУРСОРА ===
                // Если курсор еще виден, запускаем таймер
                if (rtb.Cursor != Cursors.None) 
                {
                    // Если мы перешли на новый RichTextBox или только зашли на маркер
                    if (_targetRtbForCursor != rtb || !_cursorHideTimer.IsEnabled)
                    {
                        _targetRtbForCursor = rtb;
                        _cursorHideTimer.Stop();
                        _cursorHideTimer.Start(); // Запускаем обратный отсчет
                    }
                }

                if (_toolTipTextBlock.Text != error.Text)
                {
                    _toolTipTextBlock.Text = error.Text;
                }

                Rect charRect = pointer.GetCharacterRect(LogicalDirection.Forward);
                Point bottomLeftInWindow = rtb.TransformToAncestor(this).Transform(new Point(charRect.Left, charRect.Bottom));

                if (!_sharedToolTip.IsOpen || _currentToolTipError != error)
                {
                    _sharedToolTip.PlacementTarget = this;
                    _sharedToolTip.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
                    _sharedToolTip.VerticalOffset = bottomLeftInWindow.Y + 2;
                    _sharedToolTip.HorizontalOffset = bottomLeftInWindow.X;
                    _currentToolTipError = error;

                    if (!_sharedToolTip.IsOpen)
                    {
                        _sharedToolTip.IsOpen = true;
                    }
                }
            }
            else
            {
                // === МГНОВЕННЫЙ ВОЗВРАТ КУРСОРА ===
                // Если ушли с маркера, отменяем таймер и сразу возвращаем курсор
                _cursorHideTimer.Stop();
                if (rtb.Cursor == Cursors.None)
                {
                    rtb.Cursor = Cursors.IBeam;
                }
                _targetRtbForCursor = null;
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
            // Сбрасываем кэш, чтобы при следующем наведении координаты пересчитались заново
            _currentToolTipError = null;
        }

        private ErrorItem GetErrorAtPointer(RichTextBox rtb, TextPointer pointer)
        {
            if (pointer == null) return null;

            // === ИСПРАВЛЕНИЕ 1 ===
            // Проверяем, что указатель находится в контексте реального текста, 
            // а не на границе элементов (например, в конце абзаца).
            var context = pointer.GetPointerContext(LogicalDirection.Forward);
            if (context != TextPointerContext.Text)
                return null;

            var nextContext = pointer.GetNextContextPosition(LogicalDirection.Forward);
            if (nextContext == null) return null;

            // Получаем текст от текущего указателя до следующего контекста
            var checkRange = new TextRange(pointer, nextContext);
            string currentText = checkRange.Text;

            // === ИСПРАВЛЕНИЕ 2 ===
            // Если это пробел, перенос строки (\r\n) или пустая строка (пустое место в конце абзаца),
            // то прерываем выполнение. Это полностью уберет "фантомные" тултипы в пустоте.
            if (string.IsNullOrWhiteSpace(currentText))
                return null;

            // Проверяем цвет фона
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
                                    // Строгая проверка границ
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
            // === ОПТИМИЗАЦИЯ 1: Приоритет Render ===
            // Синхронизируем таймер с движком отрисовки WPF. 
            // Это гарантирует, что скролл происходит ровно перед отрисовкой кадра, 
            // что полностью убирает микро-фризы и рассинхрон.
            _scrollTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
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
            const double speed = 0.8;
            const double maxScroll = 40.0;

            if (Math.Abs(deltaY) > deadzone)
            {
                double distance = Math.Abs(deltaY) - deadzone;
                double scrollY = (deltaY > 0 ? 1 : -1) * distance * speed;

                // Ограничиваем максимум (убираем Math.Max/Min для микро-ускорения)
                if (scrollY > maxScroll) scrollY = maxScroll;
                else if (scrollY < -maxScroll) scrollY = -maxScroll;

                // === ОПТИМИЗАЦИЯ 2: Сглаживание (Инерция) ===
                // Умножаем на коэффициент, чтобы сгладить резкие скачки значений.
                // Это убирает "дрожание" текста при быстром движении мыши.
                scrollY *= 0.6;

                if (Math.Abs(scrollY) > 0.1)
                {
                    double currentOffset = _targetScrollViewer.VerticalOffset;
                    double newOffsetY = currentOffset + scrollY;

                    // === ОПТИМИЗАЦИЯ 3: Ручное ограничение (Clamping) ===
                    // ScrollViewer внутри себя тоже ограничивает значения, но если мы передаем 
                    // "невалидное" значение (больше максимума или меньше 0), WPF тратит время 
                    // на внутренние пересчеты layout. Мы задаем корректные границы СРАЗУ.
                    double maxOffset = _targetScrollViewer.ExtentHeight - _targetScrollViewer.ViewportHeight;
                    if (maxOffset < 0) maxOffset = 0; // Защита от пустого документа

                    if (newOffsetY < 0) newOffsetY = 0;
                    else if (newOffsetY > maxOffset) newOffsetY = maxOffset;

                    // === ОПТИМИЗАЦИЯ 4: Защита от холостых вызовов ===
                    // Вызываем ScrollToVerticalOffset ТОЛЬКО если смещение реально изменилось.
                    // Это спасает UI-поток от лишних перерисовок, когда мышь почти не двигается.
                    if (Math.Abs(newOffsetY - currentOffset) > 0.01)
                    {
                        _targetScrollViewer.ScrollToVerticalOffset(newOffsetY);
                    }
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

        private void NewErrorTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.SelectedError = null;
            }
        }

        private void RichTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null) return;

            // Определяем, какой именно ScrollViewer нужно крутить
            ScrollViewer sv = (rtb == EnglishRichTextBox) ? EnglishScrollViewer : RussianScrollViewer;
            if (sv == null) return;

            // e.Delta обычно равен 120 за один щелчок колёсика.
            // Делим на 3.0, чтобы получить комфортную скорость (40 пикселей за щелчок).
            // Если хочешь крутить быстрее/медленнее, измени делитель (например, на 2.0 или 4.0).
            double scrollAmount = e.Delta / 8.0;

            // Вычисляем новое смещение
            double newOffset = sv.VerticalOffset - scrollAmount;

            // Применяем попиксельный скролл
            sv.ScrollToVerticalOffset(newOffset);

            // === САМОЕ ГЛАВНОЕ ===
            // Помечаем событие как обработанное. 
            // Это блокирует внутренний движок RichTextBox, который пытался бы 
            // прокрутить текст на 3 строки вниз/вверх поверх нашего попиксельного скролла.
            e.Handled = true;
        }
    }
}