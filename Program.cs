using BinanceTestnet.Trading;
using BinanceTestnet.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestSharp;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register dependencies needed for OrderManager
builder.Services.AddSingleton<ExcelWriter>(); // Add any necessary initialization or options
builder.Services.AddSingleton<RestClient>(sp => new RestClient("http://api.example.com")); // Adjust as needed
builder.Services.AddSingleton<OrderManager>(sp =>
{
    var wallet = new Wallet(1000m); // Initialize wallet with an initial balance
    var excelWriter = sp.GetRequiredService<ExcelWriter>(); // Resolve ExcelWriter
    var operationMode = OperationMode.Backtest; // Set as needed
    var interval = "1m";
    var fileName = "trades.xlsx"; // Set a default or configuration-based value
    var takeProfit = 0.6m;
    var tradeDirection = SelectedTradeDirection.Both; // Set as needed
    var tradingStrategy = SelectedTradingStrategy.Aroon; // Set as needed
    var client = sp.GetRequiredService<RestClient>(); // Resolve RestClient

    return new OrderManager(wallet, 1.0m, excelWriter, operationMode, interval, fileName, takeProfit, tradeDirection, tradingStrategy, client);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "My API v1");
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
