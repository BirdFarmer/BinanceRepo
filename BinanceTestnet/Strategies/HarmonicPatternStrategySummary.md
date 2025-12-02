**Harmonic Pattern Strategy — Summary**

- **Purpose:** Implement a Harmonic Pattern trading strategy detecting Gartley, Butterfly and Bat patterns and acting via the existing `OrderManager` for backtesting and live trading.

**Status:** Initial scaffold and detection logic added.

**Files Added / Edited:**
- `BinanceTestnet/Strategies/HarmonicPatternStrategy.cs` — Strategy scaffold following `StrategyBase` patterns (`RunAsync`, `RunOnHistoricalDataAsync`).
- `BinanceTestnet/Tools/HarmonicPatternDetector.cs` — Detector implementation (pivot detection + ratio checks).

**Detection Approach**
- **Input type:** `List<Quote>` (expects `Date`, `High`, `Low`, `Close`).
- **Pivot detection:** Local extrema over a neighbor window (`pivotStrength` default = 3).
- **Pattern test:** Slide windows of 5 pivots (X-A-B-C-D) and evaluate Fibonacci ratio relationships with tolerances.
  - Gartley: AB ≈ 0.618 * XA, BC in [0.382, 0.886]*AB, CD ≈ 0.786 * XA
  - Butterfly: AB ≈ 0.786 * XA, BC in [0.382, 0.886]*AB, CD ≈ 1.27 or 1.618 * XA
  - Bat: AB in [0.382,0.50]*XA, BC in [0.382,0.886]*AB, CD ≈ 0.886 * XA
- **Output:** `HarmonicDetectionResult` containing `Pattern`, `IsBullish`/`IsBearish`, and the 5 pivot points.

**Strategy Behavior**
- On detection, `HarmonicPatternStrategy` places a market entry via `OrderManager.PlaceLongOrderAsync` or `PlaceShortOrderAsync` depending on pattern direction.
- Historical backtest hook iterates through historical bars and calls the detector per-step, placing orders and calling `OrderManager.CheckAndCloseTrades`.

**Limitations (current implementation)**
- Pivot detector is a simple neighbor-based extrema finder — may miss or misclassify pivots in noisy data.
- Detector returns the first found pattern (no confidence scoring or best-match selection).
- Tolerances are intentionally wide; may produce false positives.
- No pattern-specific entry/exit (SL/TP) logic implemented yet.

**Planned Next Steps**
- Add unit tests with synthetic datasets that contain clear Gartley/Butterfly/Bat patterns.
- Tighten and/or parameterize ratio tolerances; add confidence scoring (distance from ideal ratios).
- Improve pivot detection (peak/trough smoothing, multi-scale pivots, ATR-based filters).
- Add pattern-specific configuration (min/max lookback, allowed tolerances, prefer pattern types).
- Integrate logging and UI warnings for high lookback/performance impact.
- Add patterns-to-trade selector and risk-management hooks (TP/SL, trailing stops) in the strategy.

**How to run tests (when added)**
- From repository root: `dotnet test BinanceTestnet.UnitTests`

**Notes for reviewers**
- The detector is intentionally conservative as a scaffolding step; it is ready for unit tests and tuning.
- If you want I can generate synthetic patterns and immediate unit tests next — that will make iterative tuning much faster.

---

Last updated: 2025-11-30

**TradingView / Pine Script (v5) — Visual helper**

Below is a small Pine Script that plots pivots (left/right) and labels 5-point sequences so you can visually compare patterns in TradingView. This is not a full automatic harmonic detector, but it helps you mark the X-A-B-C-D points and visually inspect candidate patterns.

```pinescript
//@version=5
indicator("Harmonic Pattern Classifier (visual helper)", overlay=true)

left = input.int(3, "Pivot Left", minval=1)
right = input.int(3, "Pivot Right", minval=1)
minConfidence = input.float(0.45, "Min confidence to label", step=0.01)

pH = ta.pivothigh(left, right)
pL = ta.pivotlow(left, right)

plotshape(not na(pH) ? high[right] : na, style=shape.triangleup, location=location.absolute, color=color.red, size=size.tiny, title="PivotHigh")
plotshape(not na(pL) ? low[right] : na, style=shape.triangledown, location=location.absolute, color=color.green, size=size.tiny, title="PivotLow")

// buffers for recent pivots
var float[] times = array.new_float()
var float[] prices = array.new_float()
var int[] types = array.new_int()

// Inline pivot buffering (avoid multi-line function issues in Pine)
if not na(pH)
    array.unshift(times, time[right])
    array.unshift(prices, high[right])
    array.unshift(types, 1)
    if array.size(prices) > 40
        array.pop(times)
        array.pop(prices)
        array.pop(types)

if not na(pL)
    array.unshift(times, time[right])
    array.unshift(prices, low[right])
    array.unshift(types, 0)
    if array.size(prices) > 40
        array.pop(times)
        array.pop(prices)
        array.pop(types)

// find last alternating 5 pivots and return an array [tX,pX,tA,pA,tB,pB,tC,pC,tD,pD]
find_last_5_alt() =>
  res = array.new_float()
  cnt = array.size(prices)
  if cnt < 5
    res
  else
    // scan from newest (index 0) toward older
    found = false
    for i = 0 to cnt - 5
      valid = true
      for j = 1 to 4
        if array.get(types, i + j) == array.get(types, i + j - 1)
          valid := false
      if valid
        // oldest is at i+4, newest at i
        tX = array.get(times, i + 4)
        pX = array.get(prices, i + 4)
        tA = array.get(times, i + 3)
        pA = array.get(prices, i + 3)
        tB = array.get(times, i + 2)
        pB = array.get(prices, i + 2)
        tC = array.get(times, i + 1)
        pC = array.get(prices, i + 1)
        tD = array.get(times, i + 0)
        pD = array.get(prices, i + 0)
        array.push(res, tX)
        array.push(res, pX)
        array.push(res, tA)
        array.push(res, pA)
        array.push(res, tB)
        array.push(res, pB)
        array.push(res, tC)
        array.push(res, pC)
        array.push(res, tD)
        array.push(res, pD)
        found := true
        break
    res

res = find_last_5_alt()
if array.size(res) == 10
  tX = array.get(res, 0)
  pX = array.get(res, 1)
  tA = array.get(res, 2)
  pA = array.get(res, 3)
  tB = array.get(res, 4)
  pB = array.get(res, 5)
  tC = array.get(res, 6)
  pC = array.get(res, 7)
  tD = array.get(res, 8)
  pD = array.get(res, 9)

  xa = math.abs(pA - pX)
  ab = math.abs(pB - pA)
  bc = math.abs(pC - pB)
  cd = math.abs(pD - pC)

  safe_div(a, b) => b == 0.0 ? na : a / b

  ab_xa = safe_div(ab, xa)
  bc_ab = safe_div(bc, ab)
  cd_xa = safe_div(cd, xa)

  isGartley = ab_xa != na and bc_ab != na and cd_xa != na and
        math.abs(ab_xa - 0.618) <= 0.618 * 0.12 and
        bc_ab >= 0.382 and bc_ab <= 0.886 and
        math.abs(cd_xa - 0.786) <= 0.786 * 0.12

  isButterfly = ab_xa != na and bc_ab != na and cd_xa != na and
        math.abs(ab_xa - 0.786) <= 0.786 * 0.12 and
        bc_ab >= 0.382 and bc_ab <= 0.886 and
        (math.abs(cd_xa - 1.27) <= 1.27 * 0.12 or math.abs(cd_xa - 1.618) <= 1.618 * 0.12)

  isBat = ab_xa != na and bc_ab != na and cd_xa != na and
        ab_xa >= 0.382 and ab_xa <= 0.50 and
        bc_ab >= 0.382 and bc_ab <= 0.886 and
        math.abs(cd_xa - 0.886) <= 0.886 * 0.10

  get_confidence(_pattern) =>
    AbT = _pattern == 1 ? 0.618 : _pattern == 2 ? 0.786 : 0.45
    CdT = _pattern == 1 ? 0.786 : _pattern == 2 ? (math.abs(cd_xa - 1.27) < math.abs(cd_xa - 1.618) ? 1.27 : 1.618) : 0.886
    errAb = math.abs(ab_xa - AbT) / AbT
    errBc = 0.0
    if not (bc_ab >= 0.382 and bc_ab <= 0.886)
      errBc := bc_ab < 0.382 ? (0.382 - bc_ab) / 0.382 : (bc_ab - 0.886) / 0.886
    errCd = math.abs(cd_xa - CdT) / math.max(0.0001, CdT)
    avgErr = (errAb + errBc + errCd) / 3
    **Harmonic Pattern Strategy — User-Focused Summary**

    This document explains what the `HarmonicPatternStrategy` actually does when it runs (live or historical). It is written for someone operating or evaluating the strategy — not for the developer who wrote it.

    **What it does (high level)**
    - Detects harmonic patterns (Gartley, Butterfly, Bat — and also Cypher, Shark, Crab) from recent price candles using an internal pivot detector. The detector recognizes these additional patterns; the strategy applies per-pattern confidence rules (see below) and falls back to a default confidence requirement for patterns without an explicit threshold.
    - Applies safety filters (trend bias, confidence, age and price proximity) and places a single market entry per detected pattern via the shared `OrderManager`.
    - Logs every detection and decision to `harmonic_detections.csv` (in the application's base directory) for later review.

    **Key behavior details**
    - Detection window and pivots: the detector looks for 5 alternating pivots (X-A-B-C-D) using local extrema (pivotStrength=3).
    - Immediate D validation: the detector uses `validationBars = 3` to confirm the D pivot is stable (this is a very short, detector-internal check).
    -- Confidence threshold (per-pattern): the strategy requires a minimum confidence before trading: Gartley 0.40, Butterfly 0.60, Bat 0.40. Cypher, Shark and Crab are detected as well; they use the default required confidence (0.45) unless you add explicit thresholds in code. Lower-confidence detections are recorded but skipped.
    - Trend filter: the strategy computes a simple SMA(50). It will avoid opening shorts when the market is up (price > SMA50) and avoid longs when market is down.
    - Price proximity: before entering, the strategy requires the current signal price to be within 5% of the D price (`MaxEntryPricePct = 0.05`). If the price is farther, the detection is skipped.
    - Age/expiry rule: the strategy allows entries for some time after D. It computes:
      - patternLengthBars = index(D) - index(X)
      - allowedBars = patternLengthBars * PatternExpiryMultiplier (default 1.2)
      - If barsSinceD > allowedBars the pattern is considered expired and skipped. Example: X->D = 10 bars, multiplier 1.2 → allowed = 12 bars after D.
    - One-pattern → one-trade: once the strategy places an entry tied to a specific pattern (identified by symbol, pattern type, D timestamp and D price), that exact pattern will be skipped on subsequent detections for the lifetime of the process. This prevents repeatedly trading the same pattern multiple times. Note: this flag is kept in-memory and is not persisted across restarts.
    - Order placement: the strategy delegates actual order creation and exits to `OrderManager`. It calls `PlaceLongOrderAsync` or `PlaceShortOrderAsync` with a signal string that includes the pattern and confidence.

    **Historical / backtest behavior**
    - The historical path (`RunOnHistoricalDataAsync`) runs the same detection and filters on a growing candle window, calls `OrderManager.Place*` to simulate entries and calls `OrderManager.CheckAndCloseTrades` to evaluate exits per-step. The same one-pattern→one-trade rule is enforced during the backtest run.

    **Outputs and logs**
    - `harmonic_detections.csv` (base directory): appended lines for every detection/decision with timestamps, pattern, direction, confidence, pivot prices/times, signal price, price distance, and a short result code such as `skipped_low_confidence`, `skipped_trend`, `skipped_expired`, `skipped_price_distance`, `skipped_already_traded`, `placed_long`, `placed_short`.
    - Console: brief human-readable detection summaries are printed when a pattern is found (pattern, ratios, pivot times/prices) so you can quickly inspect a run in real time.

    **Important default parameters (where to change them)**
    - `MinLookback = 100` (minimum bars before running detector)
    - `validationBars = 3` (detector-level D validation)
    - `pivotStrength = 3` (pivot neighbor window)
    - `PatternExpiryMultiplier = 1.2` (age/expiry multiplier)
    - `MaxEntryPricePct = 0.05` (maximum allowed distance from D to signal price)
    -- Confidence thresholds: Gartley=0.40, Butterfly=0.60, Bat=0.40; other patterns default to 0.45 unless configured.

    These values live in `BinanceTestnet/Strategies/HarmonicPatternStrategy.cs` and can be adjusted there. Changing `PatternExpiryMultiplier` is the simplest way to make the strategy accept later or only earlier entries (reduce it to 1.0 or 0.8 to forbid late entries).

    **What it does not do**
    - It does not persist the "pattern already traded" flag across restarts; a restart clears that memory and the same pattern can be traded again.
    - It does not implement pattern-specific stop-loss / take-profit logic inside the strategy — exits and TP/SL behavior are handled by the shared `OrderManager` (which uses ATR-based defaults or UI-configured TP/SL).
    - It does not implement per-symbol cooldown windows (the strategy relies on `OrderManager` to prevent simultaneous duplicate active trades). If you want a cooldown or a per-candle de-duplication, that can be added in the strategy layer.

    **Known limitations & suggestions (practical)**
    - You may see late entries when the allowed expiry window is large relative to pattern size; reduce `PatternExpiryMultiplier` to tighten this.
    - If a symbol is volatile, the 5% price proximity rule may let through entries that are no longer meaningful — consider lowering `MaxEntryPricePct` or switching to an ATR-based proximity check.
    - For less noise, consider smoothing pivots (higher `pivotStrength`) or increasing `MinLookback` when running live on small timeframes.

    **Quick checks you can run**
    - Inspect recent detections: open `harmonic_detections.csv` and filter lines with `placed_` to see what was traded and why.
    - Adjust expiry quickly: edit `PatternExpiryMultiplier` in `HarmonicPatternStrategy.cs`, rebuild (`dotnet build .\BinanceAPI.sln`), and re-run.

    **If you want changes I can make now**
    - Persist the "pattern traded" set to disk or DB so restarts don't allow re-trading the same pattern.
    - Add per-pattern expiry multipliers (e.g., Shark shorter than Butterfly).
    - Add a per-symbol cooldown (ignore new patterns on a symbol for N bars after a trade).
    - Replace percent proximity with ATR-based proximity.

    ---

    Last updated: 2025-12-02

- On detection, `HarmonicPatternStrategy` places a market entry via `OrderManager.PlaceLongOrderAsync` or `PlaceShortOrderAsync` depending on pattern direction.
- Historical backtest hook iterates through historical bars and calls the detector per-step, placing orders and calling `OrderManager.CheckAndCloseTrades`.

**Limitations (current implementation)**
- Pivot detector is a simple neighbor-based extrema finder — may miss or misclassify pivots in noisy data.
- Detector returns the first found pattern (no confidence scoring or best-match selection).
- Tolerances are intentionally wide; may produce false positives.
- No pattern-specific entry/exit (SL/TP) logic implemented yet.

**Planned Next Steps**
- Add unit tests with synthetic datasets that contain clear Gartley/Butterfly/Bat patterns.
- Tighten and/or parameterize ratio tolerances; add confidence scoring (distance from ideal ratios).
- Improve pivot detection (peak/trough smoothing, multi-scale pivots, ATR-based filters).
- Add pattern-specific configuration (min/max lookback, allowed tolerances, prefer pattern types).
- Integrate logging and UI warnings for high lookback/performance impact.
- Add patterns-to-trade selector and risk-management hooks (TP/SL, trailing stops) in the strategy.

**How to run tests (when added)**
- From repository root: `dotnet test BinanceTestnet.UnitTests`

**Notes for reviewers**
- The detector is intentionally conservative as a scaffolding step; it is ready for unit tests and tuning.
- If you want I can generate synthetic patterns and immediate unit tests next — that will make iterative tuning much faster.

---

Last updated: 2025-11-30
