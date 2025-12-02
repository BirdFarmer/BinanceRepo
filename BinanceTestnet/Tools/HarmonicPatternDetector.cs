using System;
using System.Collections.Generic;
using System.Linq;
using BinanceTestnet.Models;

namespace BinanceTestnet.Tools
{
    public enum HarmonicPattern
    {
        None,
        Gartley,
        Butterfly,
        Bat,
        Cypher
        ,Crab
        ,Shark
    }

    public class HarmonicDetectionResult
    {
        public HarmonicPattern Pattern { get; set; } = HarmonicPattern.None;
        public bool IsBullish { get; set; }
        public bool IsBearish { get; set; }
        public List<(DateTime time, decimal price)> Points { get; set; } = new List<(DateTime, decimal)>();
        public string Notes { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public double AbXa { get; set; }
        public double BcAb { get; set; }
        public double CdXa { get; set; }
        // extra ratios useful to match Pine script outputs
        public double CdBc { get; set; }
        public double AdXa { get; set; }
        // indexes of the points in the original quotes list (if available)
        public List<int> PointIndices { get; set; } = new List<int>();
        // D validation (requires validationBars passed to Detect)
        public bool IsValidatedD { get; set; } = true;
    }

    public static class HarmonicPatternDetector
    {
        // Counters for diagnostics
        private static int _rawGartley = 0;
        private static int _rawButterfly = 0;
        private static int _rawBat = 0;
        private static int _rawCypher = 0;
        private static int _rawCrab = 0;
        private static int _rawShark = 0;
        private static int _validGartley = 0;
        private static int _validButterfly = 0;
        private static int _validBat = 0;
        private static int _validCypher = 0;
        private static int _validCrab = 0;
        private static int _validShark = 0;
        private static int _filteredGartley = 0;
        private static int _filteredButterfly = 0;
        private static int _filteredBat = 0;
        private static int _filteredCypher = 0;
        private static int _filteredCrab = 0;
        private static int _filteredShark = 0;

        public static (int rawGartley, int rawButterfly, int rawBat, int rawCypher, int validGartley, int validButterfly, int validBat, int validCypher, int filteredGartley, int filteredButterfly, int filteredBat, int filteredCypher) GetCounters()
        {
            return (_rawGartley, _rawButterfly, _rawBat, _rawCypher, _validGartley, _validButterfly, _validBat, _validCypher, _filteredGartley, _filteredButterfly, _filteredBat, _filteredCypher);
        }

        public static void ResetCounters()
        {
            _rawGartley = _rawButterfly = _rawBat = _rawCypher = 0;
            _validGartley = _validButterfly = _validBat = _validCypher = 0;
            _filteredGartley = _filteredButterfly = _filteredBat = _filteredCypher = 0;
        }
        // Public API: accept typed Quote list for reliability
        // validationBars: if >0, will only accept a detected D if the next validationBars do not invalidate the pivot (like Pine's t_b)
        // verbose: when true, logs raw detections to console for debugging
        public static HarmonicDetectionResult Detect(List<Quote> quotes, int pivotStrength = 3, int validationBars = 0, bool verbose = false)
        {
            if (quotes == null || quotes.Count < 100)
            {
                return new HarmonicDetectionResult { Pattern = HarmonicPattern.None, Notes = "Insufficient data" };
            }

            var swings = GetSwings(quotes, pivotStrength);
            if (swings.Count < 5)
                return new HarmonicDetectionResult { Pattern = HarmonicPattern.None, Notes = "Not enough swings" };

            // Slide a window of 5 swings and test patterns
            for (int i = 0; i <= swings.Count - 5; i++)
            {
                var w = swings.Skip(i).Take(5).ToList();

                // Require alternating highs/lows
                if (!IsAlternating(w)) continue;

                decimal pX = w[0].price;
                decimal pA = w[1].price;
                decimal pB = w[2].price;
                decimal pC = w[3].price;
                decimal pD = w[4].price;

                // locate point indices in the original quotes array so we can perform validation similar to Pine's t_b
                var pointIndices = new List<int>();
                foreach (var pt in w)
                {
                    int idx = quotes.FindIndex(q => q.Date == pt.time && (q.High == pt.price || q.Low == pt.price));
                    pointIndices.Add(idx);
                }

                decimal xa = Math.Abs(pA - pX);
                decimal ab = Math.Abs(pB - pA);
                decimal bc = Math.Abs(pC - pB);
                decimal cd = Math.Abs(pD - pC);

                if (xa == 0 || ab == 0) continue;

                double ab_xa = (double)(ab / xa);
                double bc_ab = (double)(bc / ab);
                double cd_xa = (double)(cd / xa);
                double cd_bc = bc == 0 ? double.NaN : (double)(cd / bc);
                decimal ad = Math.Abs(pA - pD);
                double ad_xa = xa == 0 ? double.NaN : (double)(ad / xa);

                // Determine direction: if A < X then XA was a down move -> bullish pattern
                bool isBullish = pA < pX;

                // Gartley: AB ≈ 0.618 XA, BC 0.382-0.886 AB, CD ≈ 0.786 XA
                if (IsApproximately(ab_xa, 0.618, 0.12) && InRange(bc_ab, 0.382, 0.886) && IsApproximately(cd_xa, 0.786, 0.12))
                {
                    bool validated = validationBars <= 0 || ValidateD(quotes, pointIndices[4], !w[4].isHigh, validationBars);
                    var res = BuildResult(HarmonicPattern.Gartley, isBullish, w, ab_xa, bc_ab, cd_xa, cd_bc, ad_xa, pointIndices, validated);
                    // counters
                    _rawGartley++;
                    if (validated) _validGartley++; else _filteredGartley++;
                    if (verbose && validated) Console.WriteLine($"[HarmonicDetector] Gartley detected ab_xa={ab_xa:F3} bc_ab={bc_ab:F3} cd_xa={cd_xa:F3} conf={res.Confidence:F3} validated={validated}");
                    return res;
                }

                // Bat: AB 0.382-0.50 XA, BC 0.382-0.886 AB, CD ≈ 0.886 XA
                if (InRange(ab_xa, 0.382, 0.50) && InRange(bc_ab, 0.382, 0.886) && IsApproximately(cd_xa, 0.886, 0.10))
                {
                    bool validated = validationBars <= 0 || ValidateD(quotes, pointIndices[4], !w[4].isHigh, validationBars);
                    var res = BuildResult(HarmonicPattern.Bat, isBullish, w, ab_xa, bc_ab, cd_xa, cd_bc, ad_xa, pointIndices, validated);
                    // counters
                    _rawBat++;
                    if (validated) _validBat++; else _filteredBat++;
                    if (verbose && validated) Console.WriteLine($"[HarmonicDetector] Bat detected ab_xa={ab_xa:F3} bc_ab={bc_ab:F3} cd_xa={cd_xa:F3} conf={res.Confidence:F3} validated={validated}");
                    return res;
                }

                // Butterfly: AB ≈ 0.786 XA, BC 0.382-0.886 AB, CD ≈ 1.27 or 1.618 XA
                if (IsApproximately(ab_xa, 0.786, 0.12) && InRange(bc_ab, 0.382, 0.886) && (IsApproximately(cd_xa, 1.27, 0.12) || IsApproximately(cd_xa, 1.618, 0.12)))
                {
                    bool validated = validationBars <= 0 || ValidateD(quotes, pointIndices[4], !w[4].isHigh, validationBars);
                    var res = BuildResult(HarmonicPattern.Butterfly, isBullish, w, ab_xa, bc_ab, cd_xa, cd_bc, ad_xa, pointIndices, validated);
                    // counters
                    _rawButterfly++;
                    if (validated) _validButterfly++; else _filteredButterfly++;
                    if (verbose && validated) Console.WriteLine($"[HarmonicDetector] Butterfly detected ab_xa={ab_xa:F3} bc_ab={bc_ab:F3} cd_xa={cd_xa:F3} conf={res.Confidence:F3} validated={validated}");
                    return res;
                }

                // Cypher pattern:
                // AB = 0.382 - 0.618 of XA
                // BC = 1.13 - 2.00 of AB (extension)
                // CD is approx 0.786 of XC (i.e., CD/XC ~= 0.786)
                decimal xc = Math.Abs(pC - pX);
                double cd_xc = double.NaN;
                if (xc != 0)
                {
                    cd_xc = (double)(cd / xc);
                }
                if (InRange(ab_xa, 0.382, 0.618) && InRange(bc_ab, 1.13, 2.00) && !double.IsNaN(cd_xc) && IsApproximately(cd_xc, 0.786, 0.06))
                {
                    bool validated = validationBars <= 0 || ValidateD(quotes, pointIndices[4], !w[4].isHigh, validationBars);
                    // use cd_xc in the BuildResult call (BuildResult will treat the provided cd value according to the pattern targets)
                    var res = BuildResult(HarmonicPattern.Cypher, isBullish, w, ab_xa, bc_ab, cd_xc, cd_bc, ad_xa, pointIndices, validated);
                    _rawCypher++;
                    if (validated) _validCypher++; else _filteredCypher++;
                    if (verbose && validated) Console.WriteLine($"[HarmonicDetector] Cypher detected ab_xa={ab_xa:F3} bc_ab={bc_ab:F3} cd_xc={cd_xc:F3} conf={res.Confidence:F3} validated={validated}");
                    return res;
                }

                // Crab pattern: deep reversal with a large CD extension (~2.618 XA)
                // AB = 0.382 - 0.618 XA, BC = 0.382 - 0.886 AB, CD ≈ 2.618 XA
                if (InRange(ab_xa, 0.382, 0.618) && InRange(bc_ab, 0.382, 0.886) && IsApproximately(cd_xa, 2.618, 0.12))
                {
                    bool validated = validationBars <= 0 || ValidateD(quotes, pointIndices[4], !w[4].isHigh, validationBars);
                    var res = BuildResult(HarmonicPattern.Crab, isBullish, w, ab_xa, bc_ab, cd_xa, cd_bc, ad_xa, pointIndices, validated);
                    _rawCrab++;
                    if (validated) _validCrab++; else _filteredCrab++;
                    if (verbose && validated) Console.WriteLine($"[HarmonicDetector] Crab detected ab_xa={ab_xa:F3} bc_ab={bc_ab:F3} cd_xa={cd_xa:F3} conf={res.Confidence:F3} validated={validated}");
                    return res;
                }

                // Shark pattern (approximate): AB small retrace, BC extension, CD modest extension relative to XA
                // AB = 0.382 - 0.886 XA, BC = 1.13 - 1.618 AB (extension), CD ≈ 1.13 XA (approx)
                if (InRange(ab_xa, 0.382, 0.886) && InRange(bc_ab, 1.13, 1.618) && IsApproximately(cd_xa, 1.13, 0.15))
                {
                    bool validated = validationBars <= 0 || ValidateD(quotes, pointIndices[4], !w[4].isHigh, validationBars);
                    var res = BuildResult(HarmonicPattern.Shark, isBullish, w, ab_xa, bc_ab, cd_xa, cd_bc, ad_xa, pointIndices, validated);
                    _rawShark++;
                    if (validated) _validShark++; else _filteredShark++;
                    if (verbose && validated) Console.WriteLine($"[HarmonicDetector] Shark detected ab_xa={ab_xa:F3} bc_ab={bc_ab:F3} cd_xa={cd_xa:F3} conf={res.Confidence:F3} validated={validated}");
                    return res;
                }
            }

            return new HarmonicDetectionResult { Pattern = HarmonicPattern.None };
        }

        private static HarmonicDetectionResult BuildResult(HarmonicPattern pattern, bool isBullish, List<(DateTime time, decimal price, bool isHigh)> w, double ab_xa, double bc_ab, double cd_xa, double cd_bc, double ad_xa, List<int> pointIndices, bool validated)
        {
            // Confidence: 1 - average relative error to target ratios (clamped 0..1)
            // Use pattern-specific target values
            var targets = GetTargetsForPattern(pattern, cd_xa);
            double errAb = Math.Abs(ab_xa - targets.AbTarget) / targets.AbTarget;
            double errBc = 0;
            if (targets.BcMin > 0)
            {
                // For BC range, measure distance to nearest bound if outside, or 0 if inside
                if (bc_ab < targets.BcMin) errBc = (targets.BcMin - bc_ab) / targets.BcMin;
                else if (bc_ab > targets.BcMax) errBc = (bc_ab - targets.BcMax) / targets.BcMax;
                else errBc = 0;
            }
            double errCd = Math.Abs(cd_xa - targets.CdTarget) / Math.Max(0.0001, targets.CdTarget);

            double avgErr = (errAb + errBc + errCd) / 3.0;
            double confidence = 1.0 - avgErr;
            if (confidence < 0) confidence = 0;
            if (confidence > 1) confidence = 1;

            var res = new HarmonicDetectionResult
            {
                Pattern = pattern,
                IsBullish = isBullish,
                IsBearish = !isBullish,
                Points = w.Select(x => (x.time, x.price)).ToList(),
                Confidence = confidence,
                AbXa = ab_xa,
                BcAb = bc_ab,
                CdXa = cd_xa
            };
            res.CdBc = cd_bc;
            res.AdXa = ad_xa;
            res.PointIndices = pointIndices ?? new List<int>();
            res.IsValidatedD = validated;
            res.Notes = $"errAb={errAb:F3},errBc={errBc:F3},errCd={errCd:F3}";
            return res;
        }

        // Validate D pivot similarly to Pine's t_b confirmation: for a low D (bullish) ensure following bars do not make a lower low;
        // for a high D (bearish) ensure following bars do not make a higher high. Returns false if unable to validate (not enough bars)
        private static bool ValidateD(List<Quote> quotes, int idxD, bool expectLow, int validationBars)
        {
            if (idxD < 0 || idxD >= quotes.Count) return false;
            // need enough bars to validate
            if (idxD + validationBars >= quotes.Count) return false;
            var pDLow = quotes[idxD].Low;
            var pDHigh = quotes[idxD].High;
            for (int k = 1; k <= validationBars; k++)
            {
                int j = idxD + k;
                if (j >= quotes.Count) return false;
                if (expectLow)
                {
                    if (quotes[j].Low < pDLow) return false;
                }
                else
                {
                    if (quotes[j].High > pDHigh) return false;
                }
            }
            return true;
        }

        private static (double AbTarget, double BcMin, double BcMax, double CdTarget) GetTargetsForPattern(HarmonicPattern pattern, double observedCd)
        {
            switch (pattern)
            {
                case HarmonicPattern.Gartley:
                    return (0.618, 0.382, 0.886, 0.786);
                case HarmonicPattern.Butterfly:
                    // For butterfly CD can be 1.27 or 1.618; pick closest
                    double targetCd = Math.Abs(observedCd - 1.27) < Math.Abs(observedCd - 1.618) ? 1.27 : 1.618;
                    return (0.786, 0.382, 0.886, targetCd);
                case HarmonicPattern.Bat:
                    return (0.45, 0.382, 0.886, 0.886);
                case HarmonicPattern.Cypher:
                    // Cypher scoring: use AB target midpoint ~0.50, BC extension 1.13-2.00, CD target relative to XC = 0.786
                    return (0.50, 1.13, 2.00, 0.786);
                case HarmonicPattern.Crab:
                    // Crab scoring: AB ~0.50, BC 0.382-0.886, CD target ~2.618 XA
                    return (0.50, 0.382, 0.886, 2.618);
                case HarmonicPattern.Shark:
                    // Shark scoring (approx): AB ~0.50, BC extension 1.13-1.618, CD target ~1.13 XA
                    return (0.50, 1.13, 1.618, 1.13);
                default:
                    return (0.618, 0.382, 0.886, 0.786);
            }
        }

        private static bool IsApproximately(double value, double target, double tolFraction)
        {
            double tol = Math.Abs(target) * tolFraction;
            return Math.Abs(value - target) <= tol;
        }

        private static bool InRange(double value, double min, double max)
        {
            return value >= min && value <= max;
        }

        private static bool IsAlternating(List<(DateTime time, decimal price, bool isHigh)> w)
        {
            for (int i = 1; i < w.Count; i++)
            {
                if (w[i].isHigh == w[i - 1].isHigh) return false;
            }
            return true;
        }

        // Pivot detection: simple local extrema over neighbor window (strength)
        private static List<(DateTime time, decimal price, bool isHigh)> GetSwings(List<Quote> quotes, int strength)
        {
            var swings = new List<(DateTime time, decimal price, bool isHigh)>();
            int n = quotes.Count;
            for (int i = strength; i < n - strength; i++)
            {
                bool isHigh = true;
                bool isLow = true;
                decimal priceHigh = quotes[i].High;
                decimal priceLow = quotes[i].Low;
                for (int j = i - strength; j <= i + strength; j++)
                {
                    if (j == i) continue;
                    if (quotes[j].High >= priceHigh) isHigh = false;
                    if (quotes[j].Low <= priceLow) isLow = false;
                    if (!isHigh && !isLow) break;
                }

                if (isHigh)
                {
                    swings.Add((quotes[i].Date, quotes[i].High, true));
                }
                else if (isLow)
                {
                    swings.Add((quotes[i].Date, quotes[i].Low, false));
                }
            }

            // Remove consecutive duplicates (same high/low in a row) by keeping the most extreme
            var filtered = new List<(DateTime time, decimal price, bool isHigh)>();
            foreach (var s in swings)
            {
                if (filtered.Count == 0) { filtered.Add(s); continue; }
                var last = filtered.Last();
                if (last.isHigh == s.isHigh)
                {
                    if (s.isHigh && s.price > last.price)
                    {
                        filtered[filtered.Count - 1] = s;
                    }
                    else if (!s.isHigh && s.price < last.price)
                    {
                        filtered[filtered.Count - 1] = s;
                    }
                }
                else
                {
                    filtered.Add(s);
                }
            }

            // Helper to access tuple elements by name (since tuples use Item2 etc.)
            return filtered;
        }
    }
}

