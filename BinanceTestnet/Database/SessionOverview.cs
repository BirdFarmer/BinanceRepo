using System;

namespace BinanceTestnet.Database;

public class SessionOverview
{
        public string SessionId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string TotalDuration { get; set; }
}
