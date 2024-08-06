using BinanceTestnet.Trading; // Ensure this namespace matches where TradingSettings is located
using Microsoft.AspNetCore.Mvc;

namespace BinanceTestnet.TradingAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TradingController : ControllerBase
    {
        private readonly OrderManager _orderManager;

        public TradingController(OrderManager orderManager)
        {
            _orderManager = orderManager;
        }

        [HttpPost("update-settings")]
        public IActionResult UpdateSettings([FromBody] TradingSettings settings)
        {
            if (settings == null)
                return BadRequest("Invalid settings.");

            _orderManager.UpdateSettings(settings.Leverage, settings.Interval, settings.TakeProfit);
            return Ok("Settings updated.");
        }

        // Add other actions if needed
    }
}
