var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// Aspire injects services__{name}__http__0 for referenced projects; on Kamal these
// resolve to the apps' network aliases (e.g. http://api-web:8080).
builder.Services.AddHttpClient("api", c =>
    c.BaseAddress = new Uri(builder.Configuration["services:api:http:0"] ?? "http://localhost:5000"));
builder.Services.AddHttpClient("orders", c =>
    c.BaseAddress = new Uri(builder.Configuration["services:orders:http:0"] ?? "http://localhost:5001"));

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapGet("/", async (IHttpClientFactory factory) =>
{
    using var api = factory.CreateClient("api");
    using var orders = factory.CreateClient("orders");

    var weather = await api.GetStringAsync("/weatherforecast");
    var orderList = await orders.GetStringAsync("/orders");

    return Results.Json(new { weather, orders = orderList });
});

await app.RunAsync();
