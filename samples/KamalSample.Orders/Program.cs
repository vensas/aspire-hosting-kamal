var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapGet("/orders", () => new[]
{
    new Order(1, "Keyboard", 2),
    new Order(2, "Monitor", 1)
});

await app.RunAsync();

record Order(int Id, string Product, int Quantity);
