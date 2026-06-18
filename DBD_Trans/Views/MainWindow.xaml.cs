using DBD_Trans.Helpers;
using DBD_Trans.Models;
using DBD_Trans.ViewModels;
using System;
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
        private const double ScrollDeadzone = 15.0; // Мертвая зона в пикселях (чтобы не было рывков)
        private const double ScrollSpeedMultiplier = 0.3; // Множитель скорости (чем больше, тем быстрее)

        public MainWindow()
        {
            InitializeComponent();

            DarkTitleBarHelper.ApplyDarkTitleBar(this);
            // Подписка на события после полной загрузки окна (DataContext уже будет установлен)
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

            // Снятие выделения при повторном клике по строке
            LocalizationGrid.MouseLeftButtonDown += (s, e) =>
            {
                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is DataGridRow))
                    dep = VisualTreeHelper.GetParent(dep);

                if (dep is DataGridRow row && row.IsSelected)
                {
                    var vm = DataContext as MainViewModel;
                    if (vm != null)
                        vm.SelectedEntry = null;
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
                    var vm = DataContext as MainViewModel;
                    if (vm != null)
                        vm.SelectedEntry = entry;
                }
            };

            _scrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _scrollTimer.Tick += ScrollTimer_Tick;

            this.PreviewMouseDown += MainWindow_PreviewMouseDown;
            this.PreviewMouseUp += MainWindow_PreviewMouseUp;
            this.Deactivated += (s, e) => StopMiddleScroll();
        }

        private void OnScrollToItem(LocalizationEntry entry)
        {
            LocalizationGrid.ScrollIntoView(entry);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new System.Action(() =>
                {
                    if (LocalizationGrid.ItemContainerGenerator.ContainerFromItem(entry) is DataGridRow row)
                        row.BringIntoView();
                }));
        }

        private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindParentOfType<DataGrid>(e.OriginalSource as DependencyObject) == LocalizationGrid)
                return;

            var vm = DataContext as MainViewModel;
            if (vm != null)
                vm.SelectedEntry = null;
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
            // Активируем только если нажата средняя кнопка и клик был внутри DataGrid
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
                        this.Cursor = Cursors.ScrollAll; // Меняем курсор на "скроллер"
                        _scrollTimer.Start();
                        e.Handled = true; // Блокируем стандартное поведение средней кнопки
                    }
                }
            }
            // Если мы в режиме скролла и нажата ЛЮБАЯ другая кнопка (ЛКМ, ПКМ) - выходим из режима
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
            double deltaX = currentPos.X - _middleScrollOrigin.X;

            // Увеличим мертвую зону до 20 пикселей, чтобы убрать микро-дрожание
            const double deadzone = 20.0;
            const double speed = 0.5; // Чуть увеличим скорость, чтобы компенсировать мертвую зону

            double scrollY = 0;
            if (Math.Abs(deltaY) > deadzone)
            {
                double distance = Math.Abs(deltaY) - deadzone;
                scrollY = (deltaY > 0 ? 1 : -1) * distance * speed;
            }

            // Применяем ТОЛЬКО если смещение существенное (больше 1 пикселя)
            if (Math.Abs(scrollY) > 1.0)
            {
                double newOffsetY = _targetScrollViewer.VerticalOffset + scrollY;
                _targetScrollViewer.ScrollToVerticalOffset(newOffsetY);
            }

            // Если горизонтальный скролл не используется, можно вообще убрать deltaX для экономии CPU
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

        // Вспомогательный метод для поиска ScrollViewer внутри DataGrid
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

        // --------------------------------------
    }
}