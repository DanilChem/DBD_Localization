using DBD_Trans.Base;
using System.Collections.Generic;

namespace DBD_Trans.Models
{
    public class ErrorItem : ObservableObject
    {
        private string _text;
        public string Text
        {
            get => _text;
            set => Set(ref _text, value);
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set => Set(ref _isEditing, value);
        }

        public List<TextRangeInfo> EnglishHighlights { get; set; } = new List<TextRangeInfo>();
        public List<TextRangeInfo> RussianHighlights { get; set; } = new List<TextRangeInfo>();
    }

    public class TextRangeInfo
    {
        public int StartIndex { get; set; }
        public int Length { get; set; }
    }
}