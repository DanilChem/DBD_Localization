using System.Collections.Generic;

namespace DBD_Trans.Services
{
    public interface IMergeStorage
    {
        List<int> GetMerges(string key, bool isEnglish);
        void SetMerges(string key, bool isEnglish, List<int> mergedStartIndices);
        void Save();
    }
}