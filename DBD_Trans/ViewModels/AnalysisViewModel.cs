using DBD_Trans.Base;
using DBD_Trans.Models;
using DBD_Trans.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DBD_Trans.ViewModels
{
    public class AnalysisViewModel : ObservableObject
    {
        private readonly IErrorStorage _errorStorage;
        private readonly IAppSettings _appSettings;
        private readonly string _key;
        private readonly IStatusStorage _statusStorage;
        private readonly LocalizationEntry _entry;

        public bool IsCompleted
        {
            get => _entry.Status == ItemStatus.Completed;
            set
            {
                if (_entry.Status == ItemStatus.Completed != value)
                {
                    _entry.Status = value ? ItemStatus.Completed : ItemStatus.InProgress;
                    _statusStorage.SetStatus(_key, _entry.Status);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CompletedButtonBrush));
                }
            }
        }

        public bool HasErrors => Errors.Count > 0;

        public Brush CompletedButtonBrush
        {
            get
            {
                if (IsCompleted && HasErrors)
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF750000"));
                if (IsCompleted && !HasErrors)
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF004A7C"));
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        public ICommand ToggleCompletedCommand { get; }

        public IEnumerable<ItemStatus> StatusOptions { get; } = Enum.GetValues(typeof(ItemStatus)).Cast<ItemStatus>();

        private static readonly SolidColorBrush TemporaryHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCA5100"));
        private static readonly SolidColorBrush PermanentHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33CA5100"));
        private static readonly SolidColorBrush SelectedPermanentHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCA5100"));
        private static readonly SolidColorBrush SearchHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2f9cd6"));

        public string EnglishText { get; }
        public string RussianText { get; }
        public string Title { get; }

        public ObservableCollection<ErrorItem> Errors { get; } = new ObservableCollection<ErrorItem>();

        private string _newErrorText;
        public string NewErrorText
        {
            get => _newErrorText;
            set => Set(ref _newErrorText, value);
        }

        private ErrorItem _selectedError;
        public ErrorItem SelectedError
        {
            get => _selectedError;
            set
            {
                if (Set(ref _selectedError, value))
                {
                    RebuildDocuments();
                }
            }
        }

        private bool _isMarkerActive;
        public bool IsMarkerActive
        {
            get => _isMarkerActive;
            set => Set(ref _isMarkerActive, value);
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value))
                {
                    RebuildDocuments();
                }
            }
        }

        public double FontSize
        {
            get => _appSettings.AnalysisFontSize;
            set
            {
                _appSettings.AnalysisFontSize = value;
                OnPropertyChanged();
                _appSettings.Save();
            }
        }

        public ICommand IncreaseFontCommand { get; }
        public ICommand DecreaseFontCommand { get; }
        public ICommand AddErrorCommand { get; }
        public ICommand DeleteErrorCommand { get; }
        public ICommand EditErrorCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ToggleMarkerCommand { get; }

        public RichTextBox EnglishRichTextBox { get; set; }
        public RichTextBox RussianRichTextBox { get; set; }

        public AnalysisViewModel(LocalizationEntry entry, List<ErrorItem> existingErrors,
            IErrorStorage errorStorage, IStatusStorage statusStorage, IAppSettings appSettings)
        {
            _key = entry.Key;
            EnglishText = entry.English;
            RussianText = entry.Russian;
            Title = $"Анализ строки: {_key}";
            _errorStorage = errorStorage;
            _appSettings = appSettings;
            _entry = entry;
            _statusStorage = statusStorage;

            Errors.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasErrors));
                OnPropertyChanged(nameof(CompletedButtonBrush));
                _entry.HasErrors = HasErrors;
                _entry.ErrorCount = Errors.Count;
                RebuildDocuments();
            };

            foreach (var err in existingErrors)
                Errors.Add(err);

            IncreaseFontCommand = new RelayCommand(_ => FontSize += 1);
            DecreaseFontCommand = new RelayCommand(_ => FontSize -= 1, _ => FontSize > 8);
            AddErrorCommand = new RelayCommand(AddError, _ => !string.IsNullOrWhiteSpace(NewErrorText));
            DeleteErrorCommand = new RelayCommand<ErrorItem>(DeleteError, item => item != null);
            EditErrorCommand = new RelayCommand<ErrorItem>(EditError);
            SaveCommand = new RelayCommand(_ => SaveChanges());
            ToggleMarkerCommand = new RelayCommand(_ => ToggleMarker());
            ToggleCompletedCommand = new RelayCommand(_ => IsCompleted = !IsCompleted);
        }

        public void InitializeDocuments()
        {
            RebuildDocuments();
        }

        private class HighlightSegment
        {
            public int Start { get; set; }
            public int Length { get; set; }
            public Brush Brush { get; set; }
            public int Priority { get; set; }
        }

        private void RebuildDocuments()
        {
            if (EnglishRichTextBox == null || RussianRichTextBox == null) return;

            var searchHighlightsEng = FindMatches(EnglishText, _searchText);
            var searchHighlightsRus = FindMatches(RussianText, _searchText);

            var allEngPermanent = Errors.SelectMany(e => e.EnglishHighlights).ToList();
            var allRusPermanent = Errors.SelectMany(e => e.RussianHighlights).ToList();

            var selectedEng = SelectedError?.EnglishHighlights;
            var selectedRus = SelectedError?.RussianHighlights;

            var engSegments = BuildSegments(allEngPermanent, selectedEng, searchHighlightsEng);
            var rusSegments = BuildSegments(allRusPermanent, selectedRus, searchHighlightsRus);

            EnglishRichTextBox.Document = BuildDocument(EnglishText, engSegments);
            RussianRichTextBox.Document = BuildDocument(RussianText, rusSegments);

            ScrollToFirstMatch(EnglishRichTextBox, searchHighlightsEng);
            ScrollToFirstMatch(RussianRichTextBox, searchHighlightsRus);
        }

        private List<HighlightSegment> BuildSegments(List<TextRangeInfo> permanent, List<TextRangeInfo> selected, List<TextRangeInfo> search)
        {
            var segments = new List<HighlightSegment>();

            if (permanent != null)
                foreach (var h in permanent)
                    segments.Add(new HighlightSegment { Start = h.StartIndex, Length = h.Length, Brush = PermanentHighlightBrush, Priority = 1 });

            if (selected != null)
                foreach (var h in selected)
                    segments.Add(new HighlightSegment { Start = h.StartIndex, Length = h.Length, Brush = SelectedPermanentHighlightBrush, Priority = 2 });

            if (search != null)
                foreach (var h in search)
                    segments.Add(new HighlightSegment { Start = h.StartIndex, Length = h.Length, Brush = SearchHighlightBrush, Priority = 3 });

            return segments;
        }

        private FlowDocument BuildDocument(string text, List<HighlightSegment> highlights)
        {
            var doc = new FlowDocument();
            var para = new Paragraph();

            if (string.IsNullOrEmpty(text))
            {
                doc.Blocks.Add(para);
                return doc;
            }

            var brushes = new Brush[text.Length];

            var sorted = highlights.OrderBy(h => h.Priority).ToList();
            foreach (var h in sorted)
            {
                int end = Math.Min(h.Start + h.Length, text.Length);
                for (int i = h.Start; i < end; i++)
                {
                    brushes[i] = h.Brush;
                }
            }

            int lastPos = 0;
            Brush currentBrush = brushes.Length > 0 ? brushes[0] : null;

            for (int i = 1; i <= text.Length; i++)
            {
                Brush nextBrush = i < text.Length ? brushes[i] : null;

                if (nextBrush != currentBrush || i == text.Length)
                {
                    int length = i - lastPos;
                    if (length > 0)
                    {
                        var run = new Run(text.Substring(lastPos, length));
                        if (currentBrush != null)
                        {
                            run.Background = currentBrush;
                        }
                        para.Inlines.Add(run);
                    }

                    lastPos = i;
                    currentBrush = nextBrush;
                }
            }

            doc.Blocks.Add(para);
            return doc;
        }

        private List<TextRangeInfo> FindMatches(string text, string search)
        {
            var matches = new List<TextRangeInfo>();
            if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(text)) return matches;

            int index = 0;
            while ((index = text.IndexOf(search, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                matches.Add(new TextRangeInfo { StartIndex = index, Length = search.Length });
                index += search.Length;
            }
            return matches;
        }

        private void ScrollToFirstMatch(RichTextBox rtb, List<TextRangeInfo> matches)
        {
            if (matches == null || matches.Count == 0) return;

            var firstMatch = matches[0];
            var pointer = GetTextPointerByIndex(rtb.Document, firstMatch.StartIndex);

            if (pointer != null)
            {
                if (pointer.Parent is FrameworkContentElement element)
                {
                    element.BringIntoView();
                }
            }
        }

        private TextPointer GetTextPointerByIndex(FlowDocument doc, int index)
        {
            var pointer = doc.ContentStart;
            int currentIndex = 0;
            while (pointer != null)
            {
                if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var next = pointer.GetNextContextPosition(LogicalDirection.Forward);
                    if (next != null)
                    {
                        var range = new TextRange(pointer, next);
                        int len = range.Text.Length;
                        if (currentIndex + len > index)
                        {
                            return pointer.GetPositionAtOffset(index - currentIndex, LogicalDirection.Forward);
                        }
                        currentIndex += len;
                        pointer = next;
                    }
                    else break;
                }
                else
                {
                    pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
            return null;
        }

        private void ToggleMarker()
        {
            IsMarkerActive = !IsMarkerActive;
        }

        public void ApplyMarkerToSelection(RichTextBox rtb)
        {
            if (rtb == null || rtb.Selection.IsEmpty || !IsMarkerActive) return;
            var range = new TextRange(rtb.Selection.Start, rtb.Selection.End);
            range.ApplyPropertyValue(TextElement.BackgroundProperty, TemporaryHighlightBrush);
            rtb.Selection.Select(rtb.Selection.Start, rtb.Selection.Start);
        }

        public void RemoveHighlightAtPosition(RichTextBox rtb, TextPointer position)
        {
            if (rtb == null || position == null || !IsMarkerActive) return;
            var start = position.GetPositionAtOffset(-1);
            var end = position.GetPositionAtOffset(1);
            if (start != null && end != null)
            {
                var checkRange = new TextRange(start, end);
                var bg = checkRange.GetPropertyValue(TextElement.BackgroundProperty);
                if (bg is SolidColorBrush brush && brush.Color == TemporaryHighlightBrush.Color)
                {
                    var redStart = FindBoundary(start, LogicalDirection.Backward, TemporaryHighlightBrush.Color);
                    var redEnd = FindBoundary(end, LogicalDirection.Forward, TemporaryHighlightBrush.Color);
                    if (redStart != null && redEnd != null)
                    {
                        var redRange = new TextRange(redStart, redEnd);
                        redRange.ClearAllProperties();
                    }
                }
            }
        }

        private TextPointer FindBoundary(TextPointer pointer, LogicalDirection direction, Color targetColor)
        {
            var current = pointer;
            while (current != null)
            {
                var context = current.GetPointerContext(direction);
                if (context == TextPointerContext.Text)
                {
                    var next = current.GetNextContextPosition(direction);
                    if (next == null) break;
                    var range = new TextRange(current, next);
                    var bg = range.GetPropertyValue(TextElement.BackgroundProperty);
                    if (bg is SolidColorBrush brush && brush.Color == targetColor)
                        current = next;
                    else
                        break;
                }
                else
                {
                    current = current.GetNextContextPosition(direction);
                }
            }
            return current;
        }

        private void AddError(object _)
        {
            if (string.IsNullOrWhiteSpace(NewErrorText)) return;
            var engHighlights = ExtractHighlightsFromDocument(EnglishRichTextBox.Document, TemporaryHighlightBrush.Color);
            var rusHighlights = ExtractHighlightsFromDocument(RussianRichTextBox.Document, TemporaryHighlightBrush.Color);
            var errorItem = new ErrorItem
            {
                Text = NewErrorText.Trim(),
                EnglishHighlights = engHighlights,
                RussianHighlights = rusHighlights
            };
            Errors.Add(errorItem);
            NewErrorText = string.Empty;
            SelectedError = errorItem;
            IsMarkerActive = false;
            SaveChanges();
        }

        private List<TextRangeInfo> ExtractHighlightsFromDocument(FlowDocument doc, Color targetColor)
        {
            var highlights = new List<TextRangeInfo>();
            int currentPos = 0;
            var navigator = doc.ContentStart;
            while (navigator != null)
            {
                if (navigator.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var next = navigator.GetNextContextPosition(LogicalDirection.Forward);
                    if (next != null)
                    {
                        var textRange = new TextRange(navigator, next);
                        var bg = textRange.GetPropertyValue(TextElement.BackgroundProperty);
                        int length = textRange.Text.Length;
                        if (bg is SolidColorBrush brush && brush.Color == targetColor)
                        {
                            highlights.Add(new TextRangeInfo { StartIndex = currentPos, Length = length });
                        }
                        currentPos += length;
                        navigator = next;
                        continue;
                    }
                }
                navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
            }
            return highlights;
        }

        private void DeleteError(ErrorItem item)
        {
            if (item == null || !Errors.Contains(item)) return;
            int index = Errors.IndexOf(item);
            Errors.Remove(item);
            if (SelectedError == item)
            {
                SelectedError = Errors.Count > 0 ? Errors[Math.Min(index, Errors.Count - 1)] : null;
            }
            SaveChanges();
        }

        private void EditError(ErrorItem item)
        {
            if (item != null)
                item.IsEditing = true;
        }

        public void SaveChanges()
        {
            var errorList = Errors.Where(e => !string.IsNullOrWhiteSpace(e.Text)).ToList();
            _errorStorage.UpdateErrors(_key, errorList);
        }

        public void OnClosing()
        {
            SaveChanges();
        }

        public void SaveDocumentsToSelectedError()
        {
        }
    }
}