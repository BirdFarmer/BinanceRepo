using Microsoft.AspNetCore.Mvc;
using BinanceTestnet.Enums;
using BinanceLive.Services;
using System.Threading.Tasks;
using TradingAPI.Services;

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

        [HttpPost("start")]
        public async Task<IActionResult> StartTrading([FromQuery] OperationMode operationMode)
        {
            // Start the trading process with the specified operation mode
            await _tradingService.RunTradingAsync(operationMode);

            return Ok("Trading started with mode: " + operationMode);
        }
    }
}
