namespace SensorApi.Models;

public sealed class ReadResponse
{
    public required IReadOnlyDictionary<string, IReadOnlyList<DataPoint>> Results { get; init; }
}
