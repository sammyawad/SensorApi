namespace SensorApi.Models;

public sealed class WriteRequest
{
    public required IReadOnlyList<SensorReading> Readings { get; init; }
}

public sealed class SensorReading
{
    public required string Sensor { get; init; }
    public required DateTime Timestamp { get; init; }
    public required double Value { get; init; }
}
