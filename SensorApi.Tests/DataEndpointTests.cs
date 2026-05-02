using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SensorApi.Models;

namespace SensorApi.Tests;

public sealed class DataEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Write_ValidBatch_Returns202()
    {
        var request = BuildWriteRequest("Temp 01", DateTime.UtcNow, count: 5);
        var response = await _client.PostAsJsonAsync("/api/data", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Read_AfterWrite_ReturnsCorrectPoints()
    {
        var sensor = $"Sensor_{Guid.NewGuid():N}";
        var baseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const int count = 100;

        await _client.PostAsJsonAsync("/api/data", BuildWriteRequest(sensor, baseTime, count));

        var url = BuildReadUrl([sensor], baseTime, baseTime.AddSeconds(count));
        var response = await _client.GetFromJsonAsync<ReadResponse>(url);

        Assert.NotNull(response);
        Assert.Equal(count, response.Results[sensor].Count);
    }

    [Fact]
    public async Task Read_TimeRangeFilter_ReturnsOnlyPointsInRange()
    {
        var sensor = $"Sensor_{Guid.NewGuid():N}";
        var baseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Write 200 points, one per second starting at baseTime.
        await _client.PostAsJsonAsync("/api/data", BuildWriteRequest(sensor, baseTime, 200));

        // Read only the middle 100.
        var from = baseTime.AddSeconds(50);
        var to   = baseTime.AddSeconds(149);
        var url  = BuildReadUrl([sensor], from, to);
        var response = await _client.GetFromJsonAsync<ReadResponse>(url);

        Assert.NotNull(response);
        Assert.Equal(100, response.Results[sensor].Count);
        Assert.All(response.Results[sensor], p =>
        {
            Assert.True(p.Timestamp >= from && p.Timestamp <= to);
        });
    }

    [Fact]
    public async Task Read_MultipleSensors_ReturnsDataForEach()
    {
        var sensors = Enumerable.Range(1, 5)
            .Select(i => $"Multi_{Guid.NewGuid():N}_{i}")
            .ToArray();

        var baseTime = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        var readings = sensors.SelectMany(s =>
            Enumerable.Range(0, 20).Select(i => new SensorReading
            {
                Sensor    = s,
                Timestamp = baseTime.AddSeconds(i),
                Value     = i
            })).ToList();

        await _client.PostAsJsonAsync("/api/data", new WriteRequest { Readings = readings });

        var url = BuildReadUrl(sensors, baseTime, baseTime.AddSeconds(19));
        var response = await _client.GetFromJsonAsync<ReadResponse>(url);

        Assert.NotNull(response);
        foreach (var s in sensors)
            Assert.Equal(20, response.Results[s].Count);
    }

    [Fact]
    public async Task Read_TenThousandPoints_DoesNotCrashOrBlock()
    {
        var sensor = $"Sensor_{Guid.NewGuid():N}";
        var baseTime = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        const int totalPoints = 10_000;

        // Write in two batches to keep request size reasonable.
        const int batchSize = 5_000;
        for (int b = 0; b < totalPoints / batchSize; b++)
        {
            var offset = b * batchSize;
            await _client.PostAsJsonAsync("/api/data",
                BuildWriteRequest(sensor, baseTime.AddMilliseconds(offset), batchSize, stepMs: 1));
        }

        var url = BuildReadUrl([sensor], baseTime, baseTime.AddMilliseconds(totalPoints));
        var response = await _client.GetFromJsonAsync<ReadResponse>(url);

        Assert.NotNull(response);
        Assert.Equal(totalPoints, response.Results[sensor].Count);
    }

    [Fact]
    public async Task Write_ConcurrentClients_AllPointsStored()
    {
        var sensor = $"Concurrent_{Guid.NewGuid():N}";
        var baseTime = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        const int clientCount = 10;
        const int pointsPerClient = 100;

        var tasks = Enumerable.Range(0, clientCount).Select(c =>
            _client.PostAsJsonAsync("/api/data",
                BuildWriteRequest(sensor, baseTime.AddSeconds(c * pointsPerClient), pointsPerClient)));

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));

        var url = BuildReadUrl([sensor], baseTime, baseTime.AddSeconds(clientCount * pointsPerClient));
        var response = await _client.GetFromJsonAsync<ReadResponse>(url);

        Assert.NotNull(response);
        Assert.Equal(clientCount * pointsPerClient, response.Results[sensor].Count);
    }

    // --- helpers ---

    private static WriteRequest BuildWriteRequest(
        string sensor, DateTime start, int count, int stepMs = 1000) =>
        new()
        {
            Readings = Enumerable.Range(0, count)
                .Select(i => new SensorReading
                {
                    Sensor    = sensor,
                    Timestamp = start.AddMilliseconds(i * stepMs),
                    Value     = i * 0.1
                })
                .ToList()
        };

    private static string BuildReadUrl(string[] sensors, DateTime from, DateTime to)
    {
        var sensorParams = string.Join("&", sensors.Select(s => $"sensors={Uri.EscapeDataString(s)}"));
        return $"/api/data?{sensorParams}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
    }
}
