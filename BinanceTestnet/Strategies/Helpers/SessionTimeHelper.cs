using System;

namespace BinanceTestnet.Strategies.Helpers
{
    public static class SessionTimeHelper
    {
        // Given a reference time (usually latest kline close) and session start/end times (UTC TimeSpan),
        // returns the session date (midnight UTC), sessionStart DateTimeUtc, and sessionEnd DateTimeUtc.
        // Handles the case where the reference time is before the session start for that UTC day (then returns previous day's session).
        public static (DateTime sessionDate, DateTime sessionStartUtc, DateTime sessionEndUtc) GetSessionWindow(DateTime referenceUtc, TimeSpan sessionStart, TimeSpan sessionEnd)
        {
            var sessionDate = new DateTime(referenceUtc.Year, referenceUtc.Month, referenceUtc.Day, 0, 0, 0, DateTimeKind.Utc);

            // If sessionEnd > sessionStart it's a normal same-day session (e.g., 08:00-14:30).
            // If sessionEnd <= sessionStart it's an overnight session that spans midnight (e.g., 21:00-09:00).
            bool overnight = sessionEnd <= sessionStart;

            if (!overnight)
            {
                var sessionStartUtc = sessionDate.Add(sessionStart);
                var sessionEndUtc = sessionDate.Add(sessionEnd);

                // If reference is before today's session start, use previous day's session
                if (referenceUtc < sessionStartUtc)
                {
                    sessionDate = sessionDate.AddDays(-1);
                    sessionStartUtc = sessionDate.Add(sessionStart);
                    sessionEndUtc = sessionDate.Add(sessionEnd);
                }

                // Cap session length to a maximum span (counting back from session end).
                // This will not fail if the original session is longer; it simply uses the
                // last `maxSpan` hours ending at `sessionEndUtc` for calculations.
                var maxSpan = TimeSpan.FromHours(8);
                if (sessionEndUtc - sessionStartUtc > maxSpan)
                {
                    sessionStartUtc = sessionEndUtc - maxSpan;
                }

                return (sessionDate, sessionStartUtc, sessionEndUtc);
            }

            // Overnight session: end is on the next day
            var candidateStart = sessionDate.Add(sessionStart);
            var candidateEnd = sessionDate.AddDays(1).Add(sessionEnd);

            // If reference falls within today's overnight session (start today -> end tomorrow), use it
            if (referenceUtc >= candidateStart && referenceUtc <= candidateEnd)
            {
                // Cap overnight session length if it's longer than maxSpan
                var maxSpan = TimeSpan.FromHours(8);
                var sStart = candidateStart;
                var sEnd = candidateEnd;
                if (sEnd - sStart > maxSpan)
                {
                    sStart = sEnd - maxSpan;
                }

                return (sessionDate, sStart, sEnd);
            }

            // Otherwise use the previous day's overnight session (start yesterday -> end today)
            sessionDate = sessionDate.AddDays(-1);
            var sessionStartUtcPrev = sessionDate.Add(sessionStart);
            var sessionEndUtcPrev = sessionDate.AddDays(1).Add(sessionEnd);

            // Cap previous overnight session length
            var maxSpanPrev = TimeSpan.FromHours(8);
            if (sessionEndUtcPrev - sessionStartUtcPrev > maxSpanPrev)
            {
                sessionStartUtcPrev = sessionEndUtcPrev - maxSpanPrev;
            }

            return (sessionDate, sessionStartUtcPrev, sessionEndUtcPrev);
        }
    }
}
