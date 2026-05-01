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
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows;

namespace DBD_Trans.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly IFileService _fileService;
        private readonly IErrorStorage _errorStorage;
        private readonly IAppSettings _appSettings;
        private readonly IStatusStorage _statusStorage;
        private readonly string _dataDirectory;

        public ObservableCollection<LocalizationEntry> AllEntries { get; } = new ObservableCollection<LocalizationEntry>();
        public ICollectionView FilteredEntries { get; }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value))
                {
                    FilteredEntries.Refresh();
                    UpdateStatistics();
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
        public int TotalCount
        {
            get => _totalCount;
            set => Set(ref _totalCount, value);
        }

        private int _displayedCount;
        public int DisplayedCount
        {
            get => _displayedCount;
            set => Set(ref _displayedCount, value);
        }

        private int _missingCount;
        public int MissingCount
        {
            get => _missingCount;
            set => Set(ref _missingCount, value);
        }

        private string _resultCountText;
        public string ResultCountText
        {
            get => _resultCountText;
            set => Set(ref _resultCountText, value);
        }

        private int _completedCount;
        public int CompletedCount
        {
            get => _completedCount;
            set => Set(ref _completedCount, value);
        }

        private int _totalErrorCount;
        public int TotalErrorCount
        {
            get => _totalErrorCount;
            set => Set(ref _totalErrorCount, value);
        }

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

            IsLoading = true;
            Task.Run(() =>
            {
                LoadData();
                Application.Current.Dispatcher.Invoke(() => IsLoading = false);
            });

        }

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

                // Создаём запись, потом задаём статус
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

            foreach (var entry in tempEntries)
                AllEntries.Add(entry);

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                FilteredEntries.Refresh();
                UpdateStatistics();
                IsLoading = false;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool FilterEntry(object obj)
        {
            if (!(obj is LocalizationEntry entry)) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            var filter = SearchText.Trim();
            return (entry.Key?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (entry.English?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (entry.Russian?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void UpdateStatistics()
        {
            TotalCount = AllEntries.Count;
            var displayed = FilteredEntries.Cast<LocalizationEntry>().ToList();
            DisplayedCount = displayed.Count;
            MissingCount = AllEntries.Count(e => !e.HasTranslation);
            CompletedCount = AllEntries.Count(e => e.Status == ItemStatus.Completed);
            TotalErrorCount = AllEntries.Sum(e => e.ErrorCount);
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

            UpdateStatistics();   // обновляем счётчики
                                  // FilteredEntries.Refresh();  // <-- УДАЛЯЕМ эту строку
        }

        private void GoToEntry(LocalizationEntry entry)
        {
            SearchText = string.Empty;

            var target = AllEntries.FirstOrDefault(e => e.Key == entry.Key);
            if (target != null)
            {
                SelectedEntry = target;
                ScrollToItemRequested?.Invoke(target);
            }
        }

        public event Action<LocalizationEntry> ScrollToItemRequested;
    }
}