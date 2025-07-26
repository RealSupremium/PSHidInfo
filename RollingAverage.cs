namespace PSHidInfo
{
    class RollingAverage
    {
        public Queue<double> _values;
        private int _maxSize;
        public double _sum;

        public RollingAverage(int maxSize)
        {
            _maxSize = maxSize;
            _values = new Queue<double>(maxSize);
            _sum = 0;
        }

        public void Add(double value)
        {
            if (_values.Count == _maxSize)
            {
                double removedValue = _values.Dequeue();
                _sum -= removedValue;
            }

            _values.Enqueue(value);
            _sum += value;
        }

        /// <summary>
        /// Resets the rolling average, clearing all values.
        /// </summary>
        public void Reset()
        {
            _values.Clear();
            _sum = 0;
        }

        public double Average => _values.Count == 0 ? 0 : _sum / _values.Count;
    }
}
