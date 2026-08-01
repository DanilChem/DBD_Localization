using DBD_Trans.Base;
using DBD_Trans.Helpers;
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
    /// подсветка изменившихся слов и доступность перехода к строке в главном окне.
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
        public bool ShowEnglishDiff { get; }   // показываем пару "Было / Стало" (для Updated)
        public bool ShowEnglishSingle { get; } // показываем одно значение целиком (Added/Removed)
        public string EnglishSingleValue { get; }
        public List<DiffSegment> OldEnglishSegments { get; } // строка "Было" — с подсветкой убранных слов
        public List<DiffSegment> NewEnglishSegments { get; } // строка "Стало" — с подсветкой добавленных слов

        public bool ShowRussian { get; }
        public bool ShowRussianDiff { get; }
        public bool ShowRussianSingle { get; }
        public string RussianSingleValue { get; }
        public List<DiffSegment> OldRussianSegments { get; }
        public List<DiffSegment> NewRussianSegments { get; }

        public bool CanNavigate { get; }

        public ChangeEntryViewModel(ChangeItem source, bool canNavigate)
        {
            Key = source.Key;
            Type = source.Type;
            CanNavigate = canNavigate;

            ComputeDisplay(
                source.Type, source.EnglishChanged, source.OldEnglish, source.NewEnglish,
                out bool showEn, out bool diffEn, out string singleEn,
                out List<DiffSegment> oldSegEn, out List<DiffSegment> newSegEn);
            ShowEnglish = showEn;
            ShowEnglishDiff = diffEn;
            ShowEnglishSingle = showEn && !diffEn;
            EnglishSingleValue = singleEn;
            OldEnglishSegments = oldSegEn;
            NewEnglishSegments = newSegEn;

            ComputeDisplay(
                source.Type, source.RussianChanged, source.OldRussian, source.NewRussian,
                out bool showRu, out bool diffRu, out string singleRu,
                out List<DiffSegment> oldSegRu, out List<DiffSegment> newSegRu);
            ShowRussian = showRu;
            ShowRussianDiff = diffRu;
            ShowRussianSingle = showRu && !diffRu;
            RussianSingleValue = singleRu;
            OldRussianSegments = oldSegRu;
            NewRussianSegments = newSegRu;
        }

        private static void ComputeDisplay(ChangeType type, bool changed, string oldValue, string newValue,
            out bool show, out bool showDiff, out string singleValue,
            out List<DiffSegment> oldSegments, out List<DiffSegment> newSegments)
        {
            oldSegments = null;
            newSegments = null;
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
                    if (changed)
                    {
                        // Один и тот же word-level diff, но для строки "Было" убираем
                        // добавленные токены (получаем обратно старый текст с подсветкой
                        // того, что из него убрали), а для "Стало" — убираем удалённые
                        // (получаем новый текст с подсветкой того, что в него добавили).
                        var all = WordDiffer.Diff(oldValue, newValue);
                        oldSegments = all.Where(s => s.Type != DiffSegmentType.Added).ToList();
                        newSegments = all.Where(s => s.Type != DiffSegmentType.Removed).ToList();
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Один "патч" — группа изменений, обнаруженная за одну проверку файлов.
    /// Список карточек (Items) строится ЛЕНИВО — только когда группу разворачивают,
    /// а не сразу для всей истории, иначе при накопившейся истории окно открывается
    /// заметно медленнее с каждым патчем.
    /// </summary>
    public class ChangeSetViewModel : ObservableObject
    {
        private readonly List<ChangeItem> _rawChanges;
        private readonly Func<string, bool> _entryExists;
        private bool _itemsBuilt;

        public DateTime DetectedAt { get; }
        public string DateLabel => DetectedAt.ToString("dd.MM.yyyy HH:mm");
        public string SummaryText { get; }
        public int ItemCount { get; }
        public ObservableCollection<ChangeEntryViewModel> Items { get; } = new ObservableCollection<ChangeEntryViewModel>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (Set(ref _isExpanded, value) && value)
                    EnsureItemsBuilt();
            }
        }

        public ChangeSetViewModel(ChangeSet source, Func<string, bool> entryExists)
        {
            DetectedAt = source.DetectedAt;
            _rawChanges = source.Changes;
            _entryExists = entryExists;
            ItemCount = source.Changes.Count;

            var parts = new List<string>();
            if (source.AddedCount > 0) parts.Add($"добавлено: {source.AddedCount}");
            if (source.UpdatedCount > 0) parts.Add($"изменено: {source.UpdatedCount}");
            if (source.RemovedCount > 0) parts.Add($"удалено: {source.RemovedCount}");
            SummaryText = parts.Count > 0 ? string.Join(" · ", parts) : "нет изменений";
        }

        /// <summary>Строит карточки строк для этой группы, если они ещё не построены.</summary>
        public void EnsureItemsBuilt()
        {
            if (_itemsBuilt) return;
            _itemsBuilt = true;

            foreach (var c in _rawChanges)
            {
                Items.Add(new ChangeEntryViewModel(c, c.Type != ChangeType.Removed && _entryExists(c.Key)));
            }
        }
    }

    public class ChangesViewModel : ObservableObject
    {
        private readonly IChangeHistoryStorage _historyStorage;
        private readonly MainViewModel _mainViewModel;
        private readonly HashSet<string> _existingKeys;
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
        public ICommand ExpandAllCommand { get; }
        public ICommand CollapseAllCommand { get; }

        /// <summary>Просит View закрыть окно (например, после перехода к строке).</summary>
        public event Action RequestClose;

        public ChangesViewModel(IChangeHistoryStorage historyStorage, MainViewModel mainViewModel)
        {
            _historyStorage = historyStorage;
            _mainViewModel = mainViewModel;

            // Снимок существующих ключей берём один раз (O(N)), вместо того чтобы для
            // КАЖДОЙ строки истории заново линейно сканировать всю таблицу (было главной
            // причиной медленного открытия окна при большой истории).
            _existingKeys = mainViewModel.GetAllKeys();

            GoToCommand = new RelayCommand<string>(GoTo);
            ClearHistoryCommand = new RelayCommand(_ => ClearHistory());
            ExpandAllCommand = new RelayCommand(_ => SetAllExpanded(true));
            CollapseAllCommand = new RelayCommand(_ => SetAllExpanded(false));

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

            // Это единственный проход, который обязан пробежать по ВСЕЙ истории — но он
            // работает с "сырыми" ChangeItem (просто сравнение строк), без построения
            // ViewModel-ей и без diff'а, поэтому остаётся быстрым даже на тысячах записей.
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

                ChangeSets.Add(new ChangeSetViewModel(filteredSet, _existingKeys.Contains));
            }

            // Раскрываем по умолчанию только самый свежий патч — карточки для остальных
            // (свёрнутых) групп вообще не строятся, пока пользователь их не откроет.
            if (ChangeSets.Count > 0)
                ChangeSets[0].IsExpanded = true;

            HasVisibleResults = ChangeSets.Count > 0;
        }

        private void SetAllExpanded(bool expanded)
        {
            foreach (var set in ChangeSets)
                set.IsExpanded = expanded;
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
