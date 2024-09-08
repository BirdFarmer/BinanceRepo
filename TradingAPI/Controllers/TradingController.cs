using Microsoft.AspNetCore.Mvc;
using BinanceTestnet.Enums;
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

        // Start trading with specified operation mode
        [HttpPost("start")]
        public async Task<IActionResult> StartTrading([FromQuery] OperationMode operationMode = OperationMode.LivePaperTrading)
        {
            try
            {
                await _tradingService.RunTradingAsync(operationMode);
                return Ok("Trading started successfully in mode: " + operationMode);
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
            
                Console.WriteLine($"Stopped trading, through API.");
            return Ok("Trading has been stopped.");
        }
    }
}
