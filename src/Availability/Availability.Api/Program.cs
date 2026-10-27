var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { service = "Availability", status = "ok" }));
app.Run();
