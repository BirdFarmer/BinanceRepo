# StrategyUtils Design (Phase 2)

Audience: internal design for Strategy Suite cleanup. This document specifies the shared helpers we will introduce in Phase 2. No functional changes planned—only consolidation and safety guards.

Goals
- Remove duplicate utility code across strategies (HTTP requests, klines parsing, candle selection, MA helpers).
- Improve null-safety and invariants in a single place.
- Make strategies shorter, clearer, and easier to test.

Scope (helpers only; no strategy logic changes)
- HTTP request factory
- Klines JSON parsing (futures klines format)
- Quotes conversion
- Candle selection helpers (last closed, previous closed)
- Data sufficiency checks and safe indexing
- Moving average helpers (EHMA for now; HMA decision deferred to Phase 3/4)
- Optional: order book bucketing

Namespace
- Namespace: `BinanceTestnet.Strategies.Helpers`
- Class: `StrategyUtils` (static)

API Proposal

1) HTTP
- RestRequest CreateGet(string resource, IDictionary<string, string>? query = null)
  - Adds standard headers: Content-Type: application/json, Accept: application/json
  - Applies query parameters if provided
  - Returns RestRequest configured for GET

2) Parsing
- List<Kline> ParseKlines(string content)
  - Input: Binance futures klines JSON (array of arrays)
  - Behavior: Returns empty list on bad/missing content; never throws
  - Parsing: invariant culture for decimals; fields: OpenTime, Open, High, Low, Close, CloseTime, NumberOfTrades; Volume if present
- bool TryParseKlines(string? content, out List<Kline> result)
  - Returns false if content is null/invalid; result = empty list

3) Quotes conversion
- List<BinanceTestnet.Models.Quote> ToQuotes(IReadOnlyList<Kline> klines, bool includeOpen = true, bool includeVolume = true)
  - Converts Klines to Skender.Stock.Indicators Quote list (Date, Open, High, Low, Close, Volume)

4) Candle selection & guards
- bool HasEnough<T>(IReadOnlyList<T> list, int required)
- (Kline? lastClosed, Kline? prevClosed) GetLastClosedPair(IReadOnlyList<Kline> klines)
  - Returns (klines[^2], klines[^3]) if available; otherwise (null, null)
- T? LastOrDefaultSafe<T>(IReadOnlyList<T> list)
- T? ElementAtOrDefaultSafe<T>(IReadOnlyList<T> list, int index)

5) Safe parsing helpers
- decimal SafeDecimal(object? value)
  - Invariant-culture parse; returns 0 if null/invalid
- bool TryParseDecimal(object? value, out decimal result)

6) Moving average helpers
- List<(DateTime Date, decimal EHMA, decimal EHMAPrev)> CalculateEHMA(IReadOnlyList<BinanceTestnet.Models.Quote> quotes, int length)
  - Definition: 2 * EMA(n/2) - EMA(n)
  - Alignment: Returns a list with same count as quotes (pre-length items return EHMA=0, EHMAPrev=0)
  - Note: HMA vs EHMA decision deferred (Phase 3/4). For now we consolidate the existing EHMA behavior.

7) Order book helpers (optional, used by FVG)
- Dictionary<decimal, decimal> BucketOrders(List<List<decimal>> orders, int significantDigits = 4)
- decimal RoundToSignificantDigits(decimal value, int significantDigits)

Design Choices & Contracts
- Exceptions: Helpers do not throw for bad input; they return empty collections or zero values. Strategies handle absence of data.
- Nullability: Public methods avoid returning null collections. `Try*` methods use out parameters.
- Culture: Always use `CultureInfo.InvariantCulture` for parsing and formatting.
- Consistency: ParseKlines returns the same fields across strategies; quote conversion includes Open/Volume by default.
- Alignment: Indicator lists should match input count when feasible to simplify indexing inside strategies.

Usage Examples (pseudocode)
- Fetch and parse klines
  var request = StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>{{"symbol", symbol},{"interval", interval},{"limit","401"}});
  var response = await client.ExecuteGetAsync(request);
  var klines = StrategyUtils.TryParseKlines(response.Content, out var parsed) ? parsed : new List<Kline>();
  if (!StrategyUtils.HasEnough(klines, 2)) return;

- Get last closed candle pair
  var (lastClosed, prevClosed) = StrategyUtils.GetLastClosedPair(klines);
  if (lastClosed is null || prevClosed is null) return;

- Compute EHMA once
  var quotes = StrategyUtils.ToQuotes(klines);
  var ehma = StrategyUtils.CalculateEHMA(quotes, 70);
  var last = StrategyUtils.LastOrDefaultSafe(ehma);

Migration Plan (no behavior change)
1) Introduce `StrategyUtils` with methods above.
2) Update AroonStrategy, HullSMAStrategy, EnhancedMACDStrategy to:
   - Use CreateGet/ParseKlines
   - Use GetLastClosedPair for candle selection (read-only in Phase 2; enforcement moved to Phase 3)
   - Use CalculateEHMA for their current EHMA logic
3) Run build & minimal backtest smoke to confirm identical behavior (signal counts should not change in Phase 2).
4) Later phases:
   - Phase 3: Enforce closed-candle usage and fix Aroon down-cross logic
   - Phase 4: Move parameters to config and inject

Test Plan (helpers)
- ParseKlines
  - Happy path with complete JSON
  - Missing fields/short arrays
  - Null/empty content returns empty list
- EHMA
  - Known-sequence verification (unit test with small synthetic data)
  - Alignment length equals quotes length; first `length` items zeroed
- Candle selection
  - Lists with <2 items -> (null, null)
  - Exactly 2/3 items -> correct pair returned

Non-Goals (Phase 2)
- No changes to signal rules or thresholds
- No behavioral diffs in live vs historical logic
- No configuration rewiring

Open Questions
- Do we want a global logger inside helpers, or keep helpers pure and let strategies log? (Proposal: keep helpers pure.)
- Do we need a shared Kline/Quote cache now, or defer to Phase 7 performance work?
