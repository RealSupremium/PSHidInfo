namespace PSHidInfo
{
    class RollingMedian
    {
        private List<double> _values;
        private int _maxSize;

        public RollingMedian(int maxSize)
        {
            _maxSize = maxSize;
            _values = new List<double>(maxSize);
        }

        public void Add(double value)
        {
            if (_values.Count == _maxSize)
            {
                _values.RemoveAt(0);
            }

            _values.Add(value);
        }

        /// <summary>
        /// Resets the rolling median, clearing all values.
        /// </summary>
        public void Reset()
        {
            _values.Clear();
        }

        public double Median
        {
            get
            {
                if (_values.Count == 0) return 0;

                var sortedValues = _values.OrderBy(x => x).ToList();
                int midIndex = sortedValues.Count / 2;

                if (sortedValues.Count % 2 == 0)
                {
                    return (sortedValues[midIndex - 1] + sortedValues[midIndex]) / 2.0;
                }
                else
                {
                    return sortedValues[midIndex];
                }
            }
        }

        public double Deviation
        {
            get
            {
                if (_values.Count < 2) return 0;

                double mean = _values.Average();
                double sumOfSquares = _values.Sum(x => Math.Pow(x - mean, 2));
                return Math.Sqrt(sumOfSquares / (_values.Count - 1));
            }
        }
    }
}
