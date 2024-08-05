namespace BinanceLive.Models
{
    
    public interface IQuote
    {
        string Symbol { get; }
        decimal Open { get; }
        decimal High { get; }
        decimal Low { get; }
        decimal Close { get; }
        decimal Volume { get; } // Ensure this is decimal
    }
}