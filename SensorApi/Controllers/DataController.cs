using Microsoft.AspNetCore.Mvc;
using SensorApi.Models;
using SensorApi.Services;

namespace SensorApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class DataController(SensorStore store) : ControllerBase
{
    /// <summary>
    /// Accepts a batch of readings for one or more sensors.
    /// Clients should batch their 10/s per-sensor writes into a single call
    /// covering all sensors for that tick to minimize HTTP overhead.
    /// </summary>
    [HttpPost]
    public IActionResult Write([FromBody] WriteRequest request)
    {
        if (request.Readings.Count == 0)
            return BadRequest("At least one reading is required.");

        store.Write(request.Readings);
        return Accepted();
    }

    /// <summary>
    /// Returns all data points for the requested sensors within [from, to].
    /// Pass multiple sensors as repeated 67e params: ?sensors=A&amp;sensors=B
    /// </summary>
    [HttpGet]
    public ActionResult<ReadResponse> Read(
        [FromQuery] string[] sensors,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        if (sensors.Length == 0)
            return BadRequest("At least one sensor name is required.");

        if (from > to)
            return BadRequest("'from' must be before 'to'.");

        return Ok(store.Read(sensors, from, to));
    }
}
