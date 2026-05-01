using DBD_Trans.Helpers;
using DBD_Trans.Models;
using DBD_Trans.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DBD_Trans.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => DataContext as MainViewModel;

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
    }
}