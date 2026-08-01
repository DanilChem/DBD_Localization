using DBD_Trans.Models;
using System.Collections.Generic;

namespace DBD_Trans.Services
{
    public interface IChangeHistoryStorage
    {
        /// <summary>
        /// Сравнивает текущее содержимое Dbd-En.json / Dbd-Ru.json со снимком,
        /// сохранённым при предыдущей проверке, и обновляет снимок текущим состоянием.
        /// Если это самый первый запуск с данной функцией (снимка ещё не существует),
        /// изменения не вычисляются — просто фиксируется точка отсчёта, и метод
        /// возвращает null (чтобы не показывать уведомление о "тысячах добавленных строк").
        /// Если реальных изменений нет — тоже возвращает null.
        /// Если изменения найдены — сохраняет их в историю и возвращает новый ChangeSet.
        /// </summary>
        ChangeSet DetectAndRecordChanges(Dictionary<string, string> currentEnglish, Dictionary<string, string> currentRussian);

        /// <summary>Вся история изменений, от новых к старым.</summary>
        List<ChangeSet> GetHistory();

        /// <summary>Сколько отдельных строк изменилось в ещё не просмотренных наборах.</summary>
        int GetUnviewedChangeItemCount();

        /// <summary>Отметить всю историю как просмотренную (сбрасывает счётчик уведомлений).</summary>
        void MarkAllAsViewed();

        /// <summary>Полностью очистить историю изменений (текущий снимок не затрагивается).</summary>
        void ClearHistory();

        void Save();
    }
}
