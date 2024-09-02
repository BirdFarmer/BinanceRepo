using TradingAPI.Services; // Ensure you have the correct namespace for TradingService
using RestSharp;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSwaggerGen(); // Swagger setup

// Register the TradingService and RestClient as singletons
builder.Services.AddSingleton<TradingService>();
builder.Services.AddSingleton(new RestClient("https://api.binance.com"));

// Build the app
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TradingAPI V1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at the app's root
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

app.Run();
