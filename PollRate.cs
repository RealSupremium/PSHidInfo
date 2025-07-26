using System.Collections.ObjectModel;

namespace PSHidInfo
{
    public enum Rate
    {
        Poll20Hz = 80,
        Poll33Hz = 48,
        Poll40Hz = 40,
        Poll50Hz = 32,
        Poll66Hz = 24,
        Poll80Hz = 20,
        Poll100Hz = 16,
        Poll133Hz = 12,
        Poll160Hz = 10,
        Poll200Hz = 8,
        Poll266Hz = 6,
        PollDefault = 0
    }

    class PollRate : ObservableCollection<Rate>
    {
        public PollRate()
        {
            foreach (var item in Enum.GetValues<Rate>())
            {
                Add(item);
            }
        }
    }
}
