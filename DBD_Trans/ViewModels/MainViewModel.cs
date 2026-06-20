using DBD_Trans.Base;
using DBD_Trans.Helpers;
using DBD_Trans.Models;
using DBD_Trans.Services;
using DBD_Trans.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace DBD_Trans.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly IFileService _fileService;
        private readonly IErrorStorage _errorStorage;
        private readonly IAppSettings _appSettings;
        private readonly IStatusStorage _statusStorage;
        private readonly string _dataDirectory;
        private readonly DispatcherTimer _searchTimer;

        private int _missingCountCache;
        private int _completedCountCache;
        private int _totalErrorCountCache;

        public FastObservableCollection<LocalizationEntry> AllEntries { get; } = new FastObservableCollection<LocalizationEntry>();
        public ICollectionView FilteredEntries { get; }

        // --- Новые свойства для фильтра ---
        public string[] FilterOptions { get; } = { "Все", "Выполненные", "С ошибками (по убыванию)" };

        private string _selectedFilter = "Все";
        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (Set(ref _selectedFilter, value))
                {
                    ApplyFilterAndSort();
                    
                }
            }
        }
        // ----------------------------------

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value))
                {
                    // --- ИЗМЕНЕННАЯ ЛОГИКА ---
                    // При каждом изменении текста сбрасываем и запускаем таймер заново
                    _searchTimer.Stop();
                    _searchTimer.Start();
                    // -------------------------
                }
            }
        }

        private LocalizationEntry _selectedEntry;
        public LocalizationEntry SelectedEntry
        {
            get => _selectedEntry;
            set => Set(ref _selectedEntry, value);
        }

        private int _totalCount;
        public int TotalCount { get => _totalCount; set => Set(ref _totalCount, value); }

        private int _displayedCount;
        public int DisplayedCount { get => _displayedCount; set => Set(ref _displayedCount, value); }

        private int _missingCount;
        public int MissingCount { get => _missingCount; set => Set(ref _missingCount, value); }

        private string _resultCountText;
        public string ResultCountText { get => _resultCountText; set => Set(ref _resultCountText, value); }

        private int _completedCount;
        public int CompletedCount { get => _completedCount; set => Set(ref _completedCount, value); }

        private int _totalErrorCount;
        public int TotalErrorCount { get => _totalErrorCount; set => Set(ref _totalErrorCount, value); }

        public ICommand AnalyzeCommand { get; }
        public ICommand GoToCommand { get; }
        public ICommand ClearSelectionCommand { get; }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => Set(ref _isLoading, value);
        }

        public MainViewModel(IFileService fileService, IErrorStorage errorStorage, IStatusStorage statusStorage, IAppSettings appSettings, string dataDirectory)
        {
            _fileService = fileService;
            _errorStorage = errorStorage;
            _appSettings = appSettings;
            _statusStorage = statusStorage;
            _dataDirectory = dataDirectory;

            FilteredEntries = CollectionViewSource.GetDefaultView(AllEntries);
            FilteredEntries.Filter = FilterEntry;

            AnalyzeCommand = new RelayCommand<LocalizationEntry>(AnalyzeEntry, CanAnalyze);
            GoToCommand = new RelayCommand<LocalizationEntry>(GoToEntry, CanGoTo);
            ClearSelectionCommand = new RelayCommand(_ => SelectedEntry = null);

            BindingOperations.EnableCollectionSynchronization(AllEntries, new object());

            _searchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchTimer.Tick += SearchTimer_Tick;

            IsLoading = true;
            Task.Run(() =>
            {
                LoadData();
                Application.Current.Dispatcher.Invoke(() => IsLoading = false);
            });
        }

        // --- Логика применения фильтра и сортировки ---
        private void ApplyFilterAndSort()
        {
            FilteredEntries.SortDescriptions.Clear();

            if (_selectedFilter == "С ошибками (по убыванию)")
            {
                FilteredEntries.SortDescriptions.Add(new SortDescription("ErrorCount", ListSortDirection.Descending));
            }

            FilteredEntries.Refresh();
            UpdateStatistics();
        }
        // ------------------------------------------------

        private void LoadData()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string enPath = Path.Combine(_dataDirectory, "Dbd-En.json");
            string ruPath = Path.Combine(_dataDirectory, "Dbd-Ru.json");

            var enJson = _fileService.LoadJson(enPath);
            var ruJson = _fileService.LoadJson(ruPath);

            if (enJson == null)
            {
                Application.Current.Dispatcher.Invoke(() => IsLoading = false);
                return;
            }

            var enEntries = JsonFlattener.FlattenToOrderedList(enJson);
            var ruDict = ruJson != null ? JsonFlattener.FlattenToDictionary(ruJson) : new Dictionary<string, string>();

            int index = 1;
            var tempEntries = new List<LocalizationEntry>();

            foreach (var en in enEntries)
            {
                string key = en.Key;
                string english = HtmlStripper.StripHtmlTags(en.Value);
                string russian = ruDict.TryGetValue(key, out var ruValue)
                    ? HtmlStripper.StripHtmlTags(ruValue)
                    : "[нет перевода]";

                var entry = new LocalizationEntry
                {
                    Index = index++,
                    Key = key,
                    English = english,
                    Russian = russian,
                    HasTranslation = ruDict.ContainsKey(key)
                };

                var errors = _errorStorage.GetErrors(entry.Key);
                entry.HasErrors = errors.Count > 0;
                entry.ErrorCount = errors.Count;
                entry.Status = _statusStorage.GetStatus(entry.Key);

                tempEntries.Add(entry);
            }

            var onlyRussian = ruDict.Keys.Except(enEntries.Select(e => e.Key));
            foreach (var key in onlyRussian)
            {
                var entry = new LocalizationEntry
                {
                    Index = index++,
                    Key = key,
                    English = "[нет перевода]",
                    Russian = HtmlStripper.StripHtmlTags(ruDict[key]),
                    HasTranslation = false
                };

                var errors = _errorStorage.GetErrors(entry.Key);
                entry.HasErrors = errors.Count > 0;
                entry.ErrorCount = errors.Count;
                entry.Status = _statusStorage.GetStatus(entry.Key);

                tempEntries.Add(entry);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                AllEntries.AddRange(tempEntries);

                FilteredEntries.Refresh();
                CalculateTotalStatistics();
                UpdateStatistics();
                IsLoading = false;
            });
        }

        private bool FilterEntry(object obj)
        {
            if (!(obj is LocalizationEntry entry)) return false;

            // 1. Быстрый фильтр по статусу
            if (_selectedFilter == "Выполненные" && entry.Status != ItemStatus.Completed) return false;
            if (_selectedFilter == "С ошибками (по убыванию)" && !entry.HasErrors) return false;

            // 2. Оптимизированный фильтр по поисковой строке
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var s = _searchText.Trim();
                // IndexOf работает в разы быстрее, чем Contains или Regex
                bool matches = (entry.Key != null && entry.Key.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) ||
                               (entry.English != null && entry.English.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) ||
                               (entry.Russian != null && entry.Russian.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!matches) return false;
            }
            return true;
        }

        private void UpdateStatistics()
        {
            TotalCount = AllEntries.Count;
            DisplayedCount = FilteredEntries.Cast<LocalizationEntry>().Count();
            MissingCount = _missingCountCache;
            CompletedCount = _completedCountCache;
            TotalErrorCount = _totalErrorCountCache;
            ResultCountText = DisplayedCount != TotalCount ? $"(найдено: {DisplayedCount})" : "";
        }

        private bool CanAnalyze(LocalizationEntry entry) => entry != null;
        private bool CanGoTo(LocalizationEntry entry) => entry != null;

        private void AnalyzeEntry(LocalizationEntry entry)
        {
            var errors = _errorStorage.GetErrors(entry.Key);
            var vm = new AnalysisViewModel(entry, errors, _errorStorage, _statusStorage, _appSettings);
            var window = new AnalysisWindow();
            window.DataContext = vm;
            window.Owner = App.Current.MainWindow;
            window.ShowDialog();
            CalculateTotalStatistics();
            UpdateStatistics();
        }

        // 添加一个属性供 View 层判断当前是否处于代码跳转状态
        public bool IsNavigating { get; private set; }

        private void GoToEntry(LocalizationEntry entry)
        {
            _searchTimer.Stop(); // 1. 立即停止可能正在计时的搜索 Timer
            IsNavigating = true;

            SearchText = string.Empty;
            _searchTimer.Stop(); // 2. 【关键】再次停止！因为上面的 setter 会 Start() 它，防止 300ms 后的重复 Refresh

            SelectedFilter = "Все";

            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                FilteredEntries.Refresh();
                UpdateStatistics();
                var target = AllEntries.FirstOrDefault(e => e.Key == entry.Key);
                if (target != null)
                {
                    SelectedEntry = target;
                    ScrollToItemRequested?.Invoke(target);
                }
                IsNavigating = false;
            }));
        }

        private CancellationTokenSource _searchCts;

        private void SearchTimer_Tick(object sender, EventArgs e)
        {
            _searchTimer.Stop();
            // Просто обновляем стандартное представление
            FilteredEntries.Refresh();
            UpdateStatistics();
        }


        private void CalculateTotalStatistics()
        {
            _missingCountCache = 0;
            _completedCountCache = 0;
            _totalErrorCountCache = 0;
            foreach (var entry in AllEntries)
            {
                if (!entry.HasTranslation) _missingCountCache++;
                if (entry.Status == ItemStatus.Completed) _completedCountCache++;
                _totalErrorCountCache += entry.ErrorCount;
            }
        }




        public event Action<LocalizationEntry> ScrollToItemRequested;
    }
}