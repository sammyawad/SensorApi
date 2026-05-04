using System.Diagnostics;
using System.Net.Http.Json;
using SensorApi.Models;

Console.WriteLine("--- Canary Labs Sensor API Client Simulator ---");
Console.WriteLine("Make sure the SensorApi is running before starting.");

var baseUrl = args.FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("SENSORAPI_URL")
    ?? "http://localhost:5258";

var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };

Console.WriteLine($"\nTargeting API at {baseUrl}");
Console.WriteLine("Starting Background Writers...");
const int numClients = 5;

for (int i = 0; i < numClients; i++)
{
    int clientId = i;
    _ = Task.Run(() => RunWriteClientAsync(httpClient, clientId));
}

Console.WriteLine($"Started {numClients} writers — each pushing 1,000 sensors × 10 points/sec (10,000 pts/sec/client).");

Console.WriteLine($"\nSensors are named Client_<0..{numClients - 1}>_Sensor_<0..999>.");
Console.WriteLine("Type: <sensor> <seconds>   (or 'exit')");

while (true)
{
    Console.Write("\nQuery> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2 || !int.TryParse(parts[1], out int seconds))
    {
        Console.WriteLine("[err] expected: <sensor> <seconds>");
        continue;
    }

    var sensor = parts[0];
    var to = DateTime.UtcNow;
    var from = to.AddSeconds(-seconds);
    var url = $"/api/data?sensors={Uri.EscapeDataString(sensor)}"
            + $"&from={Uri.EscapeDataString(from.ToString("O"))}"
            + $"&to={Uri.EscapeDataString(to.ToString("O"))}";

    try
    {
        var sw = Stopwatch.StartNew();
        var response = await httpClient.GetFromJsonAsync<ReadResponse>(url);
        sw.Stop();

        var count = response?.Results.GetValueOrDefault(sensor)?.Count ?? 0;
        Console.WriteLine($"[ok] {count} points in {sw.ElapsedMilliseconds}ms");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[err] {ex.Message}");
    }
}

static async Task RunWriteClientAsync(HttpClient client, int clientId)
{
    var sensors = Enumerable.Range(0, 1000)
        .Select(i => $"Client_{clientId}_Sensor_{i}")
        .ToArray();

    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

    while (await timer.WaitForNextTickAsync())
    {
        try
        {
            var now = DateTime.UtcNow;
            var readings = new List<SensorReading>(10000);

            for (int offset = 0; offset < 10; offset++)
            {
                var timestamp = now.AddMilliseconds(offset * 100);
                foreach (var s in sensors)
                {
                    readings.Add(new SensorReading { Sensor = s, Timestamp = timestamp, Value = Random.Shared.NextDouble() * 100 });
                }
            }

            var request = new WriteRequest { Readings = readings };
            await client.PostAsJsonAsync("/api/data", request);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Writer {clientId}] {ex.Message}");
        }
    }
}
