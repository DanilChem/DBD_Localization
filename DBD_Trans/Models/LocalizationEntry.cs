using DBD_Trans.Base;

namespace DBD_Trans.Models
{
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
            set => Set(ref _status, value);
        }

        private bool _hasErrors;
        public bool HasErrors
        {
            get => _hasErrors;
            set => Set(ref _hasErrors, value);
        }

        private int _errorCount;
        public int ErrorCount
        {
            get => _errorCount;
            set => Set(ref _errorCount, value);
        }
    }
}