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
        private bool _isMiddleScrolling = false;
        private Point _middleScrollOrigin;
        private ScrollViewer _targetScrollViewer;
        private DispatcherTimer _scrollTimer;

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
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            ViewModel.OnClosing();
            base.OnClosing(e);
        }

        private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;

            // === ПЕРЕХВАТ ДВОЙНОГО КЛИКА ПО ЗАГОЛОВКУ (САМОЕ ВАЖНОЕ) ===
            if (e.ClickCount == 2 && IsChildOf(source, ErrorsHeaderPanel))
            {
                ToggleErrorsPanel();
                e.Handled = true; // Помечаем как обработанное
                return;           // Выходим, чтобы код ниже не снял выделение
            }
            // ============================================================

            // Старая логика снятия выделения
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

        // Логика раскрытия/сворачивания панели
        private void ToggleErrorsPanel()
        {
            if (ErrorsRow == null) return;

            // Если высота задана в звездах (*) или меньше 30 пикселей - считаем, что панель свернута
            bool isCollapsed = ErrorsRow.Height.IsStar || (ErrorsRow.Height.IsAbsolute && ErrorsRow.Height.Value <= 30);

            if (isCollapsed)
            {
                int errorCount = ViewModel.Errors.Count;
                if (errorCount == 0) return; // Если ошибок нет, не раскрываем

                // Примерная высота одного элемента (с учетом отступов)
                double itemHeight = 32;
                double headerHeight = 30;
                double calculatedHeight = headerHeight + (errorCount * itemHeight);

                // Ограничиваем высоту 40% от окна, но не менее 150px
                double maxHeight = Math.Max(150, this.ActualHeight * 0.4);

                ErrorsRow.Height = new GridLength(Math.Min(calculatedHeight, maxHeight));
            }
            else
            {
                // Сворачиваем обратно до минимальной высоты
                ErrorsRow.Height = new GridLength(20);
            }
        }

        // Вспомогательный метод: проверяет, является ли кликнутый элемент дочерним для нашего заголовка
        private bool IsChildOf(DependencyObject child, DependencyObject parent)
        {
            while (child != null)
            {
                if (child == parent) return true;
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
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
                    if (child is Visual || child is Visual3D)
                        parent = VisualTreeHelper.GetParent(child);
                    else
                        break;
                }
                if (parent is T typedParent)
                    return typedParent;
                child = parent;
            }
            return null;
        }

        private void ErrorsListBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var hit = VisualTreeHelper.HitTest(ErrorsListBox, e.GetPosition(ErrorsListBox));
            if (hit?.VisualHit is FrameworkElement element)
            {
                var item = FindParentOfType<ListBoxItem>(element);
                if (item == null)
                    ViewModel.SelectedError = null;
            }
        }

        private void ErrorsListBoxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is ErrorItem errorItem)
            {
                ViewModel.EditErrorCommand.Execute(errorItem);
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new System.Action(() =>
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

        private void RichTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
        }

        // --- ЛОГИКА СКРОЛЛА СРЕДНЕЙ КНОПКОЙ ---
        private void InitMiddleScroll()
        {
            _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _scrollTimer.Tick += ScrollTimer_Tick;

            // Подписываемся на PreviewMouseDown обоих ScrollViewer
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
                this.Cursor = Cursors.ScrollAll; // Меняем курсор
                _scrollTimer.Start();
                e.Handled = true; // Блокируем дальнейшую обработку, чтобы RichTextBox не мешал
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

            const double deadzone = 15.0; // Увеличили мертвую зону (убираем микро-дрожание)
            const double speed = 0.3;     // Уменьшили множитель (скролл станет плавнее)

            if (Math.Abs(deltaY) > deadzone)
            {
                double distance = Math.Abs(deltaY) - deadzone;
                double scrollY = (deltaY > 0 ? 1 : -1) * distance * speed;

                // Ограничиваем максимальную скорость скролла за один тик (не более 4 пикселей)
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

        // --- ЛОГИКА РАСКРЫТИЯ ЗАМЕЧАНИЙ ПО ДВОЙНОМУ КЛИКУ ---
        private void ErrorsHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Реагируем строго на двойной клик
            if (e.ClickCount == 2)
            {
                // Находим родительский MainGrid через дерево визуальных элементов
                var headerPanel = sender as FrameworkElement;
                var innerGrid = headerPanel?.Parent as Grid;
                var mainGrid = innerGrid?.Parent as Grid;

                if (mainGrid == null || mainGrid.RowDefinitions.Count <= 3) return;

                // RowDefinitions[3] - это строка, в которой находится список замечаний
                var errorsRow = mainGrid.RowDefinitions[3];

                // Проверяем, свернута ли сейчас панель (изначальное состояние Star или ручное <= 30px)
                bool isCollapsed = errorsRow.Height.IsStar || (errorsRow.Height.IsAbsolute && errorsRow.Height.Value <= 30);

                if (isCollapsed)
                {
                    int errorCount = ViewModel.Errors.Count;
                    if (errorCount == 0) return; // Если замечаний нет, не раскрываем

                    // Примерная высота одного элемента (с учетом отступов)
                    double itemHeight = 32;
                    double headerHeight = 30;
                    double calculatedHeight = headerHeight + (errorCount * itemHeight);

                    // Ограничиваем высоту 40% от окна, но не менее 150px
                    double maxHeight = Math.Max(150, this.ActualHeight * 0.4);

                    errorsRow.Height = new GridLength(Math.Min(calculatedHeight, maxHeight));
                }
                else
                {
                    // Сворачиваем обратно до минимальной высоты
                    errorsRow.Height = new GridLength(20);
                }

                e.Handled = true; // Помечаем событие как обработанное
            }
        }
    }
}