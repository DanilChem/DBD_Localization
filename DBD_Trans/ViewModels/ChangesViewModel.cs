using DBD_Trans.Base;
using DBD_Trans.Models;
using DBD_Trans.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace DBD_Trans.ViewModels
{
    /// <summary>
    /// Обёртка над одним ChangeItem для отображения в списке: готовые подписи,
    /// доступность перехода к строке в главном окне и т.д.
    /// </summary>
    public class ChangeEntryViewModel
    {
        public string Key { get; }
        public ChangeType Type { get; }

        public string TypeLabel
        {
            get
            {
                switch (Type)
                {
                    case ChangeType.Added: return "Добавлено";
                    case ChangeType.Updated: return "Изменено";
                    case ChangeType.Removed: return "Удалено";
                    default: return "";
                }
            }
        }

        // Готовые к биндингу флаги/значения — вся ветвистая логика "что и как показать"
        // посчитана один раз здесь, чтобы XAML оставался простым.
        public bool ShowEnglish { get; }
        public bool ShowEnglishDiff { get; }   // показываем пару "Было / Стало"
        public bool ShowEnglishSingle { get; } // показываем одно значение
        public string EnglishSingleValue { get; }
        public string OldEnglish { get; }
        public string NewEnglish { get; }

        public bool ShowRussian { get; }
        public bool ShowRussianDiff { get; }
        public bool ShowRussianSingle { get; }
        public string RussianSingleValue { get; }
        public string OldRussian { get; }
        public string NewRussian { get; }

        public bool CanNavigate { get; }

        public ChangeEntryViewModel(ChangeItem source, bool canNavigate)
        {
            Key = source.Key;
            Type = source.Type;
            CanNavigate = canNavigate;

            OldEnglish = source.OldEnglish;
            NewEnglish = source.NewEnglish;
            OldRussian = source.OldRussian;
            NewRussian = source.NewRussian;

            ComputeDisplay(
                source.Type, source.EnglishChanged, source.OldEnglish, source.NewEnglish,
                out bool showEn, out bool diffEn, out string singleEn);
            ShowEnglish = showEn;
            ShowEnglishDiff = diffEn;
            ShowEnglishSingle = showEn && !diffEn;
            EnglishSingleValue = singleEn;

            ComputeDisplay(
                source.Type, source.RussianChanged, source.OldRussian, source.NewRussian,
                out bool showRu, out bool diffRu, out string singleRu);
            ShowRussian = showRu;
            ShowRussianDiff = diffRu;
            ShowRussianSingle = showRu && !diffRu;
            RussianSingleValue = singleRu;
        }

        private static void ComputeDisplay(ChangeType type, bool changed, string oldValue, string newValue,
            out bool show, out bool showDiff, out string singleValue)
        {
            switch (type)
            {
                case ChangeType.Added:
                    show = !string.IsNullOrEmpty(newValue);
                    showDiff = false;
                    singleValue = newValue;
                    break;
                case ChangeType.Removed:
                    show = !string.IsNullOrEmpty(oldValue);
                    showDiff = false;
                    singleValue = oldValue;
                    break;
                default: // Updated — интересует только реально изменившийся язык
                    show = changed;
                    showDiff = changed;
                    singleValue = null;
                    break;
            }
        }
    }

    /// <summary>
    /// Один "патч" — группа изменений, обнаруженная за одну проверку файлов.
    /// </summary>
    public class ChangeSetViewModel : ObservableObject
    {
        public DateTime DetectedAt { get; }
        public string DateLabel => DetectedAt.ToString("dd.MM.yyyy HH:mm");
        public string SummaryText { get; }
        public ObservableCollection<ChangeEntryViewModel> Items { get; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public ChangeSetViewModel(ChangeSet source, Func<string, bool> entryExists)
        {
            DetectedAt = source.DetectedAt;

            var parts = new List<string>();
            if (source.AddedCount > 0) parts.Add($"добавлено: {source.AddedCount}");
            if (source.UpdatedCount > 0) parts.Add($"изменено: {source.UpdatedCount}");
            if (source.RemovedCount > 0) parts.Add($"удалено: {source.RemovedCount}");
            SummaryText = parts.Count > 0 ? string.Join(" · ", parts) : "нет изменений";

            Items = new ObservableCollection<ChangeEntryViewModel>(
                source.Changes.Select(c => new ChangeEntryViewModel(
                    c,
                    c.Type != ChangeType.Removed && entryExists(c.Key))));
        }
    }

    public class ChangesViewModel : ObservableObject
    {
        private readonly IChangeHistoryStorage _historyStorage;
        private readonly MainViewModel _mainViewModel;
        private List<ChangeSet> _allSets = new List<ChangeSet>();

        public ObservableCollection<ChangeSetViewModel> ChangeSets { get; } = new ObservableCollection<ChangeSetViewModel>();

        public string[] FilterOptions { get; } = { "Все", "Добавленные", "Изменённые", "Удалённые" };

        private string _selectedFilter = "Все";
        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (Set(ref _selectedFilter, value))
                    ApplyFilter();
            }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value))
                    ApplyFilter();
            }
        }

        private bool _hasHistory;
        public bool HasHistory
        {
            get => _hasHistory;
            set
            {
                if (Set(ref _hasHistory, value))
                {
                    OnPropertyChanged(nameof(ShowEmptyHistoryState));
                    OnPropertyChanged(nameof(ShowNoFilterResultsState));
                }
            }
        }

        private bool _hasVisibleResults;
        public bool HasVisibleResults
        {
            get => _hasVisibleResults;
            set
            {
                if (Set(ref _hasVisibleResults, value))
                    OnPropertyChanged(nameof(ShowNoFilterResultsState));
            }
        }

        /// <summary>История вообще пуста — ни одного патча ещё не обнаружено.</summary>
        public bool ShowEmptyHistoryState => !HasHistory;

        /// <summary>История не пуста, но под текущий фильтр/поиск ничего не подходит.</summary>
        public bool ShowNoFilterResultsState => HasHistory && !HasVisibleResults;

        public ICommand GoToCommand { get; }
        public ICommand ClearHistoryCommand { get; }

        /// <summary>Просит View закрыть окно (например, после перехода к строке).</summary>
        public event Action RequestClose;

        public ChangesViewModel(IChangeHistoryStorage historyStorage, MainViewModel mainViewModel)
        {
            _historyStorage = historyStorage;
            _mainViewModel = mainViewModel;

            GoToCommand = new RelayCommand<string>(GoTo);
            ClearHistoryCommand = new RelayCommand(_ => ClearHistory());

            LoadAll();
        }

        private void LoadAll()
        {
            _allSets = _historyStorage.GetHistory();
            HasHistory = _allSets.Count > 0;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            ChangeSets.Clear();

            ChangeType? typeFilter = null;
            if (SelectedFilter == "Добавленные") typeFilter = ChangeType.Added;
            else if (SelectedFilter == "Изменённые") typeFilter = ChangeType.Updated;
            else if (SelectedFilter == "Удалённые") typeFilter = ChangeType.Removed;

            string search = (SearchText ?? "").Trim();

            foreach (var set in _allSets)
            {
                var filteredChanges = set.Changes.Where(c =>
                        (typeFilter == null || c.Type == typeFilter) &&
                        (string.IsNullOrEmpty(search) ||
                         (c.Key != null && c.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                         (c.NewEnglish != null && c.NewEnglish.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                         (c.NewRussian != null && c.NewRussian.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)))
                    .ToList();

                if (filteredChanges.Count == 0) continue;

                var filteredSet = new ChangeSet
                {
                    Id = set.Id,
                    DetectedAt = set.DetectedAt,
                    IsViewed = set.IsViewed,
                    Changes = filteredChanges
                };

                ChangeSets.Add(new ChangeSetViewModel(filteredSet, _mainViewModel.EntryExists));
            }

            // Раскрываем по умолчанию только самый свежий патч — остальные свёрнуты,
            // чтобы при большой истории (или большом патче) окно не пыталось
            // отрисовать сразу все карточки.
            if (ChangeSets.Count > 0)
                ChangeSets[0].IsExpanded = true;

            HasVisibleResults = ChangeSets.Count > 0;
        }

        private void GoTo(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _mainViewModel.NavigateToKey(key);
            RequestClose?.Invoke();
        }

        private void ClearHistory()
        {
            var result = MessageBox.Show(
                "Удалить всю историю изменений? Текущее состояние строк останется точкой отсчёта для будущих сравнений.",
                "Очистить историю",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _historyStorage.ClearHistory();
                LoadAll();
            }
        }
    }
}
