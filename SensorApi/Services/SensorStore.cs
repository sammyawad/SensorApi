using SensorApi.Models;

namespace SensorApi.Services;

/// <summary>
/// Thread-safe, in-memory store for sensor time-series data.
///
/// Per-sensor design: each sensor gets its own ReaderWriterLockSlim so reads
/// and writes to different sensors never contend with each other.
///
/// Data is kept in a List<DataPoint> sorted ascending by Timestamp. Because
/// real sensor clients emit data in chronological order we can append in O(1)
/// and binary-search for range queries in O(log n + k).
/// </summary>
public sealed class SensorStore
{
    private readonly ConcurrentSensorDictionary _sensors = new();

    public void Write(IEnumerable<SensorReading> readings)
    {
        foreach (var reading in readings)
        {
            var series = _sensors.GetOrAdd(reading.Sensor);
            series.Append(new DataPoint(reading.Timestamp, reading.Value));
        }
    }

    public ReadResponse Read(IEnumerable<string> sensorNames, DateTime from, DateTime to)
    {
        var results = new Dictionary<string, IReadOnlyList<DataPoint>>();

        foreach (var name in sensorNames)
        {
            if (_sensors.TryGet(name, out var series))
                results[name] = series.QueryRange(from, to);
            else
                results[name] = Array.Empty<DataPoint>();
        }

        return new ReadResponse { Results = results };
    }
}

/// <summary>
/// Wraps ConcurrentDictionary to give typed, lock-free get-or-add semantics.
/// </summary>
internal sealed class ConcurrentSensorDictionary
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SensorTimeSeries> _dict = new();

    public SensorTimeSeries GetOrAdd(string sensor) =>
        _dict.GetOrAdd(sensor, static _ => new SensorTimeSeries());

    public bool TryGet(string sensor, out SensorTimeSeries series) =>
        _dict.TryGetValue(sensor, out series!);
}

/// <summary>
/// Stores one sensor's data points in a time-sorted list.
/// Uses ReaderWriterLockSlim so concurrent reads never block each other.
/// </summary>
internal sealed class SensorTimeSeries
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly List<DataPoint> _points = new();

    public void Append(DataPoint point)
    {
        _lock.EnterWriteLock();
        try
        {
            _points.Add(point);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IReadOnlyList<DataPoint> QueryRange(DateTime from, DateTime to)
    {
        _lock.EnterReadLock();
        try
        {
            if (_points.Count == 0)
                return Array.Empty<DataPoint>();

            int lo = LowerBound(_points, from);
            int hi = UpperBound(_points, to);

            if (lo > hi)
                return Array.Empty<DataPoint>();

            // Copy the slice so callers hold no reference into the locked list.
            return _points.GetRange(lo, hi - lo + 1);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    // Returns index of first element with Timestamp >= target.
    private static int LowerBound(List<DataPoint> points, DateTime target)
    {
        int lo = 0, hi = points.Count - 1, result = points.Count;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (points[mid].Timestamp >= target) { result = mid; hi = mid - 1; }
            else lo = mid + 1;
        }
        return result;
    }

    // Returns index of last element with Timestamp <= target.
    private static int UpperBound(List<DataPoint> points, DateTime target)
    {
        int lo = 0, hi = points.Count - 1, result = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (points[mid].Timestamp <= target) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result;
    }
}
