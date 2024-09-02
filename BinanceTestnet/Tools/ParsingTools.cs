using System;

namespace BinanceTestnet.Tools
{
    public static class ParsingTools
    {
        public static decimal ParseDecimal(string value)
        {
            if (decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }
            return 0; // Default value or appropriate error handling
        }

        // Add other parsing methods as needed
    }
}
