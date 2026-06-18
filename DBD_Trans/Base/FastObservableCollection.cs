using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace DBD_Trans.Base
{
    public class FastObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotification = false;

        // Метод для массового добавления
        public void AddRange(IEnumerable<T> list)
        {
            if (list == null) return;

            _suppressNotification = true;
            foreach (T item in list)
            {
                Items.Add(item); // Добавляем напрямую во внутреннюю коллекцию, не вызывая событий
            }
            _suppressNotification = false;

            // Вызываем событие обновления ОДИН РАЗ для всей пачки
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification)
            {
                base.OnCollectionChanged(e);
            }
        }
    }
}