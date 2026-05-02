using SensorApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<SensorStore>();

var app = builder.Build();

app.MapControllers();
app.Run();

// Exposed so WebApplicationFactory<Program> can reference this assembly.
public partial class Program { }
