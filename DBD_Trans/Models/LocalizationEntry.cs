using DBD_Trans.Base;

namespace DBD_Trans.Models
{
    public enum RowDisplayState
    {
        Normal,
        NoTranslation,
        CompletedNoErrors,
        CompletedWithErrors,
        InProgressWithErrors
    }
    public class LocalizationEntry : ObservableObject
    {
        private int _index;
        public int Index
        {
            get => _index;
            set => Set(ref _index, value);
        }

        private string _key;
        public string Key
        {
            get => _key;
            set => Set(ref _key, value);
        }

        private string _english;
        public string English
        {
            get => _english;
            set => Set(ref _english, value);
        }

        private string _russian;
        public string Russian
        {
            get => _russian;
            set => Set(ref _russian, value);
        }

        private bool _hasTranslation;
        public bool HasTranslation
        {
            get => _hasTranslation;
            set => Set(ref _hasTranslation, value);
        }

        private ItemStatus _status = ItemStatus.InProgress;
        public ItemStatus Status
        {
            get => _status;
            set
            {
                if (Set(ref _status, value))
                    OnPropertyChanged(nameof(DisplayState));
            }
        }

        private bool _hasErrors;
        public bool HasErrors
        {
            get => _hasErrors;
            set
            {
                if (Set(ref _hasErrors, value))
                    OnPropertyChanged(nameof(DisplayState));
            }
        }

        private int _errorCount;
        public int ErrorCount
        {
            get => _errorCount;
            set => Set(ref _errorCount, value);
        }

        public RowDisplayState DisplayState
        {
            get
            {
                if (!HasTranslation) return RowDisplayState.NoTranslation;
                if (Status == ItemStatus.Completed && !HasErrors) return RowDisplayState.CompletedNoErrors;
                if (Status == ItemStatus.Completed && HasErrors) return RowDisplayState.CompletedWithErrors;
                if (Status == ItemStatus.InProgress && HasErrors) return RowDisplayState.InProgressWithErrors;
                return RowDisplayState.Normal;
            }
        }
    }
}