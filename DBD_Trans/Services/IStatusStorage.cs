using DBD_Trans.Models;

namespace DBD_Trans.Services
{
    public interface IStatusStorage
    {
        ItemStatus GetStatus(string key);
        void SetStatus(string key, ItemStatus status);
        void Save();
    }
}