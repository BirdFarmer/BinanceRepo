public class ExchangeInfo
{
    public List<SymbolInfo> Symbols { get; set; }
}

public class SymbolInfo
{
    public string Symbol { get; set; }
    public string Pair { get; set; }
    public string ContractType { get; set; }
    public long DeliveryDate { get; set; }
    public long OnboardDate { get; set; }
    public string Status { get; set; }
    public string MaintMarginPercent { get; set; }
    public string RequiredMarginPercent { get; set; }
    public string BaseAsset { get; set; }
    public string QuoteAsset { get; set; }
    public string MarginAsset { get; set; }
    public int PricePrecision { get; set; }
    public int QuantityPrecision { get; set; }
    public int BaseAssetPrecision { get; set; }
    public int QuotePrecision { get; set; }
    public string UnderlyingType { get; set; }
    public List<string> UnderlyingSubType { get; set; }
    public string TriggerProtect { get; set; }
    public string LiquidationFee { get; set; }
    public string MarketTakeBound { get; set; }
    public int MaxMoveOrderLimit { get; set; }
    public List<Filter> Filters { get; set; }
    public List<string> OrderTypes { get; set; }
    public List<string> TimeInForce { get; set; }

    // Method to format quantity based on LOT_SIZE filter
    public decimal FormatQuantity(decimal quantity)
    {
        var lotSizeFilter = Filters.FirstOrDefault(f => f.FilterType == "LOT_SIZE");
        if (lotSizeFilter != null)
        {
            decimal stepSize = decimal.Parse(lotSizeFilter.StepSize);
            // Ensure quantity is a multiple of step size
            quantity = Math.Round(quantity / stepSize) * stepSize;
            // Round to the defined quantity precision
            return Math.Round(quantity, QuantityPrecision);
        }

        throw new Exception($"LOT_SIZE filter not found for symbol {Symbol}");
    }
}

public class Filter
{
    public string FilterType { get; set; }
    public string MaxPrice { get; set; }
    public string MinPrice { get; set; }
    public string TickSize { get; set; }
    public string StepSize { get; set; }
    public string MaxQty { get; set; }
    public string MinQty { get; set; }
    public string Notional { get; set; }
    public int Limit { get; set; }
    public string MultiplierUp { get; set; }
    public string MultiplierDown { get; set; }
    public string MultiplierDecimal { get; set; }
}
