using System;

namespace BinanceLive.Tools
{
    public static class TimeTools
    {
        public static TimeSpan GetTimeSpanFromInterval(string interval)
        {
            if (interval.EndsWith("m"))
            {
                // Minutes
                if (int.TryParse(interval.Substring(0, interval.Length - 1), out int minutes))
                {
                    return TimeSpan.FromMinutes(minutes);
                }
            }
            else if (interval.EndsWith("h"))
            {
                // Hours
                if (int.TryParse(interval.Substring(0, interval.Length - 1), out int hours))
                {
                    return TimeSpan.FromHours(hours);
                }
            }
            else if (interval.EndsWith("d"))
            {
                // Days
                if (int.TryParse(interval.Substring(0, interval.Length - 1), out int days))
                {
                    return TimeSpan.FromDays(days);
                }
            }

            // Default to 1 minute if parsing fails
            return TimeSpan.FromMinutes(1);
        }

        // Add more time-related utility methods here as needed
    }
}
