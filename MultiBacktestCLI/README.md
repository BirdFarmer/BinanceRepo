# MultiBacktestCLI

Simple console entry point for running the multi strategy backtest harness.

## Build & Run

```powershell
# From repo root
cd c:\Repo\BinanceAPI

# Build
dotnet build .\MultiBacktestCLI\MultiBacktestCLI.csproj

# Run with sample config and database path
# (Database will be created if it does not exist)
$cfg = "BinanceTestnet/Tools/multi_backtest.sample.json"
$db  = "MultiBacktestData.db"

dotnet run --project .\MultiBacktestCLI\MultiBacktestCLI.csproj -- $cfg $db
```

Results written to `results/multi/multi_results.csv`.

## Config
Uses the JSON schema in `BinanceTestnet/Tools/multi_backtest.sample.json`. Adjust symbol sets, timeframes, and exit modes as needed.

## Notes / Next Steps
- Add YAML support via YamlDotNet if desired.
- Implement historical pagination for >200 candles per symbol.
- Add caching layer so symbols aren't refetched for each exit mode.
- Add regime week sampling batch or per-run metadata tagging.
