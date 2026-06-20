using DBD_Trans.Helpers;
using DBD_Trans.Models;
using DBD_Trans.ViewModels;
using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DBD_Trans.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => DataContext as MainViewModel;
        private bool _isMiddleScrolling = false;
        private Point _middleScrollOrigin;
        private ScrollViewer _targetScrollViewer;
        private DispatcherTimer _scrollTimer;
        private const double ScrollDeadzone = 15.0;
        private const double ScrollSpeedMultiplier = 0.3;

        public MainWindow()
        {
            InitializeComponent();
            DarkTitleBarHelper.ApplyDarkTitleBar(this);

            this.Loaded += (s, e) =>
            {
                var vm = DataContext as MainViewModel;
                if (vm != null)
                {
                    vm.ScrollToItemRequested += OnScrollToItem;
                }
            };

            // Снятие выделения при клике вне DataGrid
            this.PreviewMouseLeftButtonDown += OnWindowPreviewMouseLeftButtonDown;

            // Снятие выделения при повторном клике по строке (ИСПРАВЛЕНО для поддержки Ctrl/Shift)
            LocalizationGrid.MouseLeftButtonDown += (s, e) =>
            {
                // Если зажаты Ctrl или Shift, НЕ вмешиваемся, даем DataGrid самому обработать множественное выделение
                if (Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == ModifierKeys.Shift)
                    return;

                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is DataGridRow))
                    dep = VisualTreeHelper.GetParent(dep);

                if (dep is DataGridRow row && row.IsSelected)
                {
                    row.IsSelected = false;
                    var vm = DataContext as MainViewModel;
                    if (vm != null)
                        vm.SelectedEntry = LocalizationGrid.SelectedItem as LocalizationEntry;
                    e.Handled = true;
                }
            };

            // Выделение строки перед правым кликом для контекстного меню
            LocalizationGrid.PreviewMouseRightButtonDown += (s, e) =>
            {
                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is DataGridRow))
                    dep = VisualTreeHelper.GetParent(dep);

                if (dep is DataGridRow row && row.Item is LocalizationEntry entry)
                {
                    // Если клик по невыделенной строке - снимаем все остальные выделения
                    if (!row.IsSelected)
                    {
                        LocalizationGrid.SelectedItems.Clear();
                        row.IsSelected = true;
                        var vm = DataContext as MainViewModel;
                        if (vm != null) vm.SelectedEntry = entry;
                    }
                }
            };

            // --- ДОБАВЛЯЕМ ОБРАБОТКУ КОПИРОВАНИЯ (Ctrl+C) ---
            LocalizationGrid.PreviewKeyDown += LocalizationGrid_PreviewKeyDown;

            _scrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _scrollTimer.Tick += ScrollTimer_Tick;

            this.PreviewMouseDown += MainWindow_PreviewMouseDown;
            this.PreviewMouseUp += MainWindow_PreviewMouseUp;
            this.Deactivated += (s, e) => StopMiddleScroll();
        }

        // --- НОВЫЕ МЕТОДЫ ДЛЯ КОПИРОВАНИЯ ---
        private void LocalizationGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopySelectedRowsToClipboard();
                e.Handled = true;
            }
        }

        private void CopySelectedRowsToClipboard()
        {
            var selectedItems = LocalizationGrid.SelectedItems.Cast<LocalizationEntry>().ToList();
            if (selectedItems.Count == 0) return;

            var sb = new StringBuilder();
            foreach (var item in selectedItems)
            {
                // Заменяем переносы строк и табуляции на пробелы, чтобы не ломать таблицу при вставке в Excel
                string ru = (item.Russian ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
                string en = (item.English ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");

                // Формат: Ключ [TAB] Русский [TAB] Английский
                sb.AppendLine($"{item.Key}\t{ru}\t{en}");
            }

            Clipboard.SetText(sb.ToString());
        }

        // --- ИСПРАВЛЕННЫЙ МЕТОД СНЯТИЯ ВЫДЕЛЕНИЯ ПРИ КЛИКЕ ВНЕ ТАБЛИЦЫ ---
        private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindParentOfType<DataGrid>(e.OriginalSource as DependencyObject) == LocalizationGrid)
                return;

            // Очищаем ВСЕ выделенные строки
            LocalizationGrid.SelectedItems.Clear();

            var vm = DataContext as MainViewModel;
            if (vm != null)
                vm.SelectedEntry = null;
        }

        private void OnScrollToItem(LocalizationEntry entry)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(LocalizationGrid);
            if (scrollViewer == null) return;

            LocalizationGrid.ScrollIntoView(entry);

            // 【修改】使用 Render 优先级，确保 DataGrid 在大数据量下完成了虚拟化容器的生成和布局测量
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                var row = LocalizationGrid.ItemContainerGenerator.ContainerFromItem(entry) as DataGridRow;
                if (row != null)
                {
                    row.UpdateLayout(); // 【新增】强制确保布局已更新，获取绝对准确的坐标
                    var transform = row.TransformToAncestor(scrollViewer);
                    var position = transform.Transform(new Point(0, 0));

                    double targetOffset = scrollViewer.VerticalOffset + position.Y;
                    double maxOffset = Math.Max(0, scrollViewer.ExtentHeight - scrollViewer.ViewportHeight);
                    targetOffset = Math.Max(0, Math.Min(targetOffset, maxOffset));

                    scrollViewer.ScrollToVerticalOffset(targetOffset);
                }
            }));
        }

        private static T FindParentOfType<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
                parent = VisualTreeHelper.GetParent(parent);
            return parent as T;
        }

        // --- ЛОГИКА СКРОЛЛА СРЕДНЕЙ КНОПКОЙ ---
        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed && !_isMiddleScrolling)
            {
                var hit = VisualTreeHelper.HitTest(LocalizationGrid, e.GetPosition(LocalizationGrid));
                if (hit != null)
                {
                    _isMiddleScrolling = true;
                    _middleScrollOrigin = Mouse.GetPosition(this);
                    _targetScrollViewer = FindVisualChild<ScrollViewer>(LocalizationGrid);
                    if (_targetScrollViewer != null)
                    {
                        this.Cursor = Cursors.ScrollAll;
                        _scrollTimer.Start();
                        e.Handled = true;
                    }
                }
            }
            else if (_isMiddleScrolling && e.ChangedButton != MouseButton.Middle)
            {
                StopMiddleScroll();
            }
        }

        private void MainWindow_PreviewMouseUp(object sender, MouseButtonEventArgs e)
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

            const double deadzone = 20.0;
            const double speed = 0.5;

            double scrollY = 0;
            if (Math.Abs(deltaY) > deadzone)
            {
                double distance = Math.Abs(deltaY) - deadzone;
                scrollY = (deltaY > 0 ? 1 : -1) * distance * speed;
            }

            if (Math.Abs(scrollY) > 1.0)
            {
                double newOffsetY = _targetScrollViewer.VerticalOffset + scrollY;
                _targetScrollViewer.ScrollToVerticalOffset(newOffsetY);
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

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;

            // 如果是代码触发的跳转，跳过 ScrollToTop，防止与自定义滚动逻辑冲突
            var vm = DataContext as MainViewModel;
            if (vm != null && vm.IsNavigating) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(LocalizationGrid);
                scrollViewer?.ScrollToTop();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // Добавьте этот метод в класс MainWindow
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;

            // Если это программное изменение текста (например, очистка при вызове GoToEntry),
            // то не скроллим наверх, чтобы не конфликтовать с логикой перехода к целевой строке.
            var vm = DataContext as MainViewModel;
            if (vm != null && vm.IsNavigating) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(LocalizationGrid);
                scrollViewer?.ScrollToTop();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}