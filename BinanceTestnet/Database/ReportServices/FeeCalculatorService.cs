namespace BinanceTestnet.Services.Reporting
{
    public class FeeCalculatorService
    {
        public (decimal totalFees, decimal netPnL) CalculateFeeImpact(List<Trade> trades, decimal entrySize)
        {
            const decimal FEE_RATE = 0.0004m;
            decimal totalFees = trades.Count * entrySize * FEE_RATE * 2;
            decimal totalPnL = trades.Sum(t => t.Profit ?? 0);
            return (totalFees, totalPnL - totalFees);
        }
    }
}