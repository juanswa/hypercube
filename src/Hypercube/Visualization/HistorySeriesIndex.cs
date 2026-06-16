namespace Hypercube.Visualization;

/// <summary>
/// Pre-indexed per-cell history buffers for low-allocation terminal rendering.
/// </summary>
public sealed class HistorySeriesIndex
{
    private readonly Dictionary<CellKey, double[]> _seriesByCell;

    internal HistorySeriesIndex(IReadOnlyList<SummarySnapshot> history)
    {
        _seriesByCell = new Dictionary<CellKey, double[]>(CellKeyComparer.Instance);
        for (var i = 0; i < history.Count; i++)
        {
            var snapshot = history[i];
            foreach (var row in snapshot.Rows)
            {
                var cellKey = new CellKey(row.Dimension, row.Key);
                if (!_seriesByCell.TryGetValue(cellKey, out var series))
                {
                    series = new double[history.Count];
                    _seriesByCell[cellKey] = series;
                }

                series[i] = snapshot.PrimaryValue(row);
            }
        }
    }

    /// <summary>
    /// Returns a pre-built series for the given cell when present in history.
    /// </summary>
    public bool TryGetSeries(string dimension, string key, out ReadOnlySpan<double> series)
    {
        if (_seriesByCell.TryGetValue(new CellKey(dimension, key), out var values))
        {
            series = values;
            return true;
        }

        series = ReadOnlySpan<double>.Empty;
        return false;
    }

    private readonly record struct CellKey(string Dimension, string Key);

    private sealed class CellKeyComparer : IEqualityComparer<CellKey>
    {
        public static CellKeyComparer Instance { get; } = new();

        public bool Equals(CellKey x, CellKey y) =>
            x.Dimension.Equals(y.Dimension, StringComparison.OrdinalIgnoreCase) &&
            x.Key.Equals(y.Key, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(CellKey obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Dimension),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key));
    }
}
