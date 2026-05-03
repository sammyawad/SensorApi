using System.Diagnostics;
using System.Net.Http.Json;
using SensorApi.Models;

Console.WriteLine("--- Canary Labs Sensor API Client Simulator ---");
Console.WriteLine("Make sure the SensorApi is running before starting.");
var baseUrl = "http://localhost:5258";

var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };

// 1. Start background writers
Console.WriteLine("\nStarting Background Writers...");
const int numClients = 5; // The prompt requires: "Multiple clients should be used"

for (int i = 0; i < numClients; i++)
{
    int clientId = i;
    _ = Task.Run(() => RunWriteClientAsync(httpClient, clientId));
}

Console.WriteLine($"Started {numClients} background writers (Each sending a batch of 10,000 time-series points/sec).");

// 2. Interactive Reader
Console.WriteLine("\n--- Interactive Read Client ---");
Console.WriteLine("Format: <NumSensors> <SecondsBack>");
Console.WriteLine("Example: 5 10 (Queries 5 sensors for the last 10 seconds)");
Console.WriteLine("Type 'exit' to quit.");

while (true)
{
    Console.Write("\nQuery> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2 || !int.TryParse(parts[0], out int numSensorsToQuery) || !int.TryParse(parts[1], out int secondsBack))
    {
        Console.WriteLine("Invalid format. Example: 5 10");
        continue;
    }

    if (numSensorsToQuery > 1000 * numClients)
    {
        Console.WriteLine($"Max sensors available is {1000 * numClients}. Limiting query.");
        numSensorsToQuery = 1000 * numClients;
    }

    // Pick sensors evenly across clients to query
    var sensorsToQuery = new List<string>();
    for(int i = 0; i < numSensorsToQuery; i++)
    {
        int cId = i % numClients;
        int sId = i / numClients;
        sensorsToQuery.Add($"Client_{cId}_Sensor_{sId}");
    }

    var to = DateTime.UtcNow;
    var from = to.AddSeconds(-secondsBack);

    // Formulate the query string for all requested sensors
    var sensorParams = string.Join("&", sensorsToQuery.Select(s => $"sensors={Uri.EscapeDataString(s)}"));
    var queryUrl = $"/api/data?{sensorParams}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

    try
    {
        var sw = Stopwatch.StartNew();
        var response = await httpClient.GetFromJsonAsync<ReadResponse>(queryUrl);
        sw.Stop();

        if (response != null)
        {
            int totalPoints = response.Results.Values.Sum(v => v.Count);
            Console.WriteLine($"[Success] Retrieved {totalPoints:N0} points across {numSensorsToQuery} sensors in {sw.ElapsedMilliseconds}ms");
            
            if (totalPoints > 0)
            {
                Console.WriteLine("\n          --- Data Breakdown ---");
                var sampleSensors = response.Results.Where(kvp => kvp.Value.Count > 0).Take(3);
                foreach (var kvp in sampleSensors)
                {
                    var sensorName = kvp.Key;
                    var points = kvp.Value;
                    var firstTime = points.First().Timestamp;
                    var lastTime = points.Last().Timestamp;
                    Console.WriteLine($"          [{sensorName}] : {points.Count} points (From: {firstTime:T} To: {lastTime:T})");
                }
                
                int remainingSensors = response.Results.Count - 3;
                if (remainingSensors > 0)
                {
                    Console.WriteLine($"          ... and {remainingSensors} more sensors.");
                }
                Console.WriteLine("          ----------------------\n");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}");
    }
}

static async Task RunWriteClientAsync(HttpClient client, int clientId)
{
    var sensors = Enumerable.Range(0, 1000)
        .Select(i => $"Client_{clientId}_Sensor_{i}")
        .ToArray();

    while (true)
    {
        try
        {
            var now = DateTime.UtcNow;
            var readings = new List<SensorReading>(10000);

            // Generate 10 chronological points for all 1000 sensors (10,000 points total)
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
        catch 
        {
            // Suppress background connection errors
        }

        await Task.Delay(1000); // Wait 1 full second before sending the next batch
    }
}
