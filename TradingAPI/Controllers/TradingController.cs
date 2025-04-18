using Microsoft.AspNetCore.Mvc;
using BinanceTestnet.Enums;
using System.Threading.Tasks;
using TradingAPI.Services;
using System.Globalization;
//using TradingAPI.Enums;  // Make sure to include the namespace for your enums

namespace TradingAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TradingController : ControllerBase
    {
        private readonly TradingService _tradingService;

        public TradingController(TradingService tradingService)
        {
            _tradingService = tradingService;
        }
        
        // Start trading with specified operation mode
/// <summary>
/// Start trading based on provided parameters.
/// </summary>
/// <param name="operationMode">1 for LivePaperTrading, 2 for Backtest</param>
/// <param name="direction">0 for Both directions, 1 for Only Long, 2 for Only Short</param>
/// <param name="strategy">0 for SMAExpansion, 1 for MACD, 2 for Hull</param>
/// <param name="takeProfitPercent">Take profit percentage, e.g., 1.5</param>
/// <param name="userName">Optional user name</param>
        [HttpPost("start")]
        public async Task<IActionResult> StartTrading(
            [FromQuery] OperationMode operationMode = OperationMode.LivePaperTrading,
            [FromQuery] SelectedTradeDirection direction = SelectedTradeDirection.Both,
            [FromQuery] SelectedTradingStrategy strategy = SelectedTradingStrategy.SMAExpansion,
            [FromQuery] string takeProfitPercent = "1.5",
            [FromQuery] string userName = "Swagger")
        {
            try
            {
                // Convert takeProfitPercent from string to double and handle comma or dot, default 1%
                double? takeProfit = 1.5;
                if (double.TryParse(takeProfitPercent?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                {
                    takeProfit = result;
                }


                await _tradingService.RunTradingAsync(operationMode, direction, strategy, takeProfit, userName);
                return Ok($"Trading started successfully in mode: {operationMode} with direction: {direction}, strategy: {strategy}, take profit %: {takeProfit}, user: {userName}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Failed to start trading: " + ex.Message);
            }
        }

        // Stop trading
        [HttpPost("stop")]
        public IActionResult StopTrading()
        {
            _tradingService.StopTrading();
            return Ok("Trading has been stopped.");
        }
    }
}
