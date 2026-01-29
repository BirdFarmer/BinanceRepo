using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TradingAppDesktop.Services;
using TradingAppDesktop.ViewModels;
using System.Globalization;
using BinanceTestnet.Strategies;

namespace TradingAppDesktop.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        public SettingsWindow()
        {
            InitializeComponent();

            // Settings service + viewmodel
            var settingsService = new SettingsService();
            _settingsService = settingsService;
            var viewModel = new SettingsViewModel(settingsService);
            DataContext = viewModel;

            // Populate left navigation
            NavList.ItemsSource = viewModel.Sections;
            NavList.SelectedItem = viewModel.SelectedSection;
            UpdateSectionVisibility(viewModel.SelectedSection);
            NavList.SelectionChanged += (s, ev) =>
            {
                if (NavList.SelectedItem is string sec)
                {
                    viewModel.SelectedSection = sec;
                    UpdateSectionVisibility(sec);
                }
            };

            void UpdateSectionVisibility(string sec)
            {
                // Show only the selected section in the right content area
                if (sec == "Strategies")
                {
                    // Hide theme, show strategies full-width
                    ThemePanel.Visibility = Visibility.Collapsed;
                    StrategiesContent.Visibility = Visibility.Visible;
                    Grid.SetColumn(StrategiesContent, 0);
                    Grid.SetColumnSpan(StrategiesContent, 2);
                    PlaceholderText.Visibility = Visibility.Collapsed;
                }
                else if (sec == "General")
                {
                    // Show theme full-width, hide strategies
                    ThemePanel.Visibility = Visibility.Visible;
                    Grid.SetColumn(ThemePanel, 0);
                    Grid.SetColumnSpan(ThemePanel, 2);
                    StrategiesContent.Visibility = Visibility.Collapsed;
                    PlaceholderText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Other sections: show placeholder
                    ThemePanel.Visibility = Visibility.Collapsed;
                    StrategiesContent.Visibility = Visibility.Collapsed;
                    PlaceholderText.Visibility = Visibility.Visible;
                    PlaceholderText.Text = $"{sec} settings coming soon. This is a placeholder for future settings sections.";
                }
            }

            // Populate available themes
            var themes = ThemeManager.GetAvailableThemes().Select(t => new { Key = t.Key, Source = t.Source }).ToList();
            ThemeList.ItemsSource = themes;
            ThemeList.DisplayMemberPath = "Key";

            // Select current theme
            var current = ThemeManager.GetCurrentThemeKey();
            if (!string.IsNullOrEmpty(current))
            {
                var sel = themes.FirstOrDefault(t => t.Key == current);
                if (sel != null) ThemeList.SelectedItem = sel;
            }

            ThemeList.SelectionChanged += ThemeList_SelectionChanged;

            // Load persisted user settings (if any) and wire strategy controls
            var settings = settingsService.Settings;

            // Strategy selector
            if (!string.IsNullOrEmpty(settings.SelectedStrategy))
            {
                foreach (var item in StrategySelector.Items)
                {
                    if (item is ComboBoxItem cbi && cbi.Content as string == settings.SelectedStrategy)
                    {
                        StrategySelector.SelectedItem = item; break;
                    }
                }
            }
            StrategySelector.SelectionChanged += StrategySelector_SelectionChanged;

            // Harmonic toggles
            ChkGartley.IsChecked = settings.HarmonicEnableGartley;
            ChkButterfly.IsChecked = settings.HarmonicEnableButterfly;
            ChkBat.IsChecked = settings.HarmonicEnableBat;
            ChkCrab.IsChecked = settings.HarmonicEnableCrab;
            ChkCypher.IsChecked = settings.HarmonicEnableCypher;
            ChkShark.IsChecked = settings.HarmonicEnableShark;

            // Validation bars and trend filter
            TxtValidationBars.Text = settings.HarmonicValidationBars.ToString();
            ChkUseTrendFilter.IsChecked = settings.HarmonicUseTrendFilter;

            TxtValidationBars.LostFocus += (s, ev) => {
                if (int.TryParse(TxtValidationBars.Text, out var v) && v >= 1 && v <= 30)
                {
                    settings.HarmonicValidationBars = v;
                    settingsService.Save();
                }
                else
                {
                    TxtValidationBars.Text = settings.HarmonicValidationBars.ToString();
                }
            };

            ChkUseTrendFilter.Checked += (s, ev) => { settings.HarmonicUseTrendFilter = true; settingsService.Save(); };
            ChkUseTrendFilter.Unchecked += (s, ev) => { settings.HarmonicUseTrendFilter = false; settingsService.Save(); };

            ChkGartley.Checked += HarmonicToggle_Changed; ChkGartley.Unchecked += HarmonicToggle_Changed;
            ChkButterfly.Checked += HarmonicToggle_Changed; ChkButterfly.Unchecked += HarmonicToggle_Changed;
            ChkBat.Checked += HarmonicToggle_Changed; ChkBat.Unchecked += HarmonicToggle_Changed;
            ChkCrab.Checked += HarmonicToggle_Changed; ChkCrab.Unchecked += HarmonicToggle_Changed;
            ChkCypher.Checked += HarmonicToggle_Changed; ChkCypher.Unchecked += HarmonicToggle_Changed;
            ChkShark.Checked += HarmonicToggle_Changed; ChkShark.Unchecked += HarmonicToggle_Changed;

            // Show harmonic panel if HarmonicPattern selected
            var selItem = StrategySelector.SelectedItem as ComboBoxItem;
            if (selItem != null && (selItem.Content as string) == "HarmonicPattern")
            {
                HarmonicPanel.Visibility = Visibility.Visible;
            }

            // Show Bollinger panel if selected
            if (selItem != null && (selItem.Content as string) == "BollingerNoSqueeze")
            {
                BollingerPanel.Visibility = Visibility.Visible;
            }

            // Show EmaCrossover panel if selected and initialize controls
            if (selItem != null && (selItem.Content as string) == "EmaCrossoverVolume")
            {
                EmaCrossoverPanel.Visibility = Visibility.Visible;
            }

            // Show Candle Pattern panel if selected
            if (selItem != null && (selItem.Content as string) == "CandlePatternAnalysis")
            {
                CandlePatternPanel.Visibility = Visibility.Visible;
            }

            // Initialize London panel controls from persisted settings
            try
            {
                // Break check
                var breakValue = settings.LondonBreakCheck ?? "Close";
                foreach (var it in CboLondonBreakCheck.Items)
                {
                    if (it is ComboBoxItem cbi && (cbi.Content as string) == breakValue)
                    {
                        CboLondonBreakCheck.SelectedItem = it; break;
                    }
                }

                // Session start/end
                TxtLondonSessionStart.Text = settings.LondonSessionStart ?? "08:00";
                TxtLondonSessionEnd.Text = settings.LondonSessionEnd ?? "14:30";

                TxtLondonScanDuration.Text = settings.LondonScanDurationHours.ToString(CultureInfo.InvariantCulture);
                TxtLondonValueArea.Text = settings.LondonValueAreaPercent.ToString(CultureInfo.InvariantCulture);
                ChkLondonUseOrderBook.IsChecked = settings.LondonUseOrderBookVap;
                ChkLondonAllowBothSides.IsChecked = settings.LondonAllowBothSides;
                ChkLondonEnableDebug.IsChecked = settings.LondonEnableDebug;
                TxtLondonLimitExpiry.Text = settings.LondonLimitExpiryMinutes.ToString();
                TxtLondonMaxEntries.Text = settings.LondonMaxEntriesPerSidePerSession.ToString();
                ChkLondonAllowEntriesAfterScanWindow.IsChecked = settings.LondonAllowEntriesAfterScanWindow;
                // New POC stop settings
                try
                {
                    ChkLondonUsePocStop.IsChecked = settings.LondonUsePocAsStop;
                    SldLondonPocRiskRatio.Value = (double)settings.LondonPocRiskRatio;
                    TxtLondonPocRiskRatioValue.Text = settings.LondonPocRiskRatio.ToString(CultureInfo.InvariantCulture);
                    SldLondonPocRiskRatio.IsEnabled = settings.LondonUsePocAsStop;
                }
                catch { }
            }
            catch { }

            // Wire London controls to persist
            CboLondonBreakCheck.SelectionChanged += (s, ev) => {
                if (CboLondonBreakCheck.SelectedItem is ComboBoxItem cbi)
                {
                    settings.LondonBreakCheck = cbi.Content as string ?? settings.LondonBreakCheck;
                    _settingsService.Save();
                }
            };

            TxtLondonScanDuration.LostFocus += (s, ev) => {
                if (decimal.TryParse(TxtLondonScanDuration.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) && dec >= 0.5m && dec <= 12m)
                {
                    settings.LondonScanDurationHours = dec; _settingsService.Save();
                }
                else TxtLondonScanDuration.Text = settings.LondonScanDurationHours.ToString(CultureInfo.InvariantCulture);
            };

            TxtLondonSessionStart.LostFocus += (s, ev) => {
                if (TimeSpan.TryParse(TxtLondonSessionStart.Text, CultureInfo.InvariantCulture, out var t))
                {
                    settings.LondonSessionStart = t.ToString("hh\\:mm"); _settingsService.Save();
                }
                else TxtLondonSessionStart.Text = settings.LondonSessionStart;
            };

            TxtLondonSessionEnd.LostFocus += (s, ev) => {
                if (TimeSpan.TryParse(TxtLondonSessionEnd.Text, CultureInfo.InvariantCulture, out var t))
                {
                    settings.LondonSessionEnd = t.ToString("hh\\:mm"); _settingsService.Save();
                }
                else TxtLondonSessionEnd.Text = settings.LondonSessionEnd;
            };

            TxtLondonValueArea.LostFocus += (s, ev) => {
                if (decimal.TryParse(TxtLondonValueArea.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) && dec >= 50m && dec <= 90m)
                {
                    settings.LondonValueAreaPercent = dec; _settingsService.Save();
                }
                else TxtLondonValueArea.Text = settings.LondonValueAreaPercent.ToString(CultureInfo.InvariantCulture);
            };

            ChkLondonUseOrderBook.Checked += (s, ev) => { settings.LondonUseOrderBookVap = true; _settingsService.Save(); };
            ChkLondonUseOrderBook.Unchecked += (s, ev) => { settings.LondonUseOrderBookVap = false; _settingsService.Save(); };

            ChkLondonAllowBothSides.Checked += (s, ev) => { settings.LondonAllowBothSides = true; _settingsService.Save(); };
            ChkLondonAllowBothSides.Unchecked += (s, ev) => { settings.LondonAllowBothSides = false; _settingsService.Save(); };

            ChkLondonEnableDebug.Checked += (s, ev) => { settings.LondonEnableDebug = true; _settingsService.Save(); };
            ChkLondonEnableDebug.Unchecked += (s, ev) => { settings.LondonEnableDebug = false; _settingsService.Save(); };

            ChkLondonAllowEntriesAfterScanWindow.Checked += (s, ev) => { settings.LondonAllowEntriesAfterScanWindow = true; _settingsService.Save(); };
            ChkLondonAllowEntriesAfterScanWindow.Unchecked += (s, ev) => { settings.LondonAllowEntriesAfterScanWindow = false; _settingsService.Save(); };

            TxtLondonLimitExpiry.LostFocus += (s, ev) => {
                if (int.TryParse(TxtLondonLimitExpiry.Text, out var v) && v >= 0 && v <= 1440)
                {
                    settings.LondonLimitExpiryMinutes = v; _settingsService.Save();
                }
                else TxtLondonLimitExpiry.Text = settings.LondonLimitExpiryMinutes.ToString();
            };

            TxtLondonMaxEntries.LostFocus += (s, ev) => {
                if (int.TryParse(TxtLondonMaxEntries.Text, out var v) && v >= 1 && v <= 10)
                {
                    settings.LondonMaxEntriesPerSidePerSession = v; _settingsService.Save();
                }
                else TxtLondonMaxEntries.Text = settings.LondonMaxEntriesPerSidePerSession.ToString();
            };

            // Wire POC stop checkbox + slider
            ChkLondonUsePocStop.Checked += (s, ev) => { settings.LondonUsePocAsStop = true; SldLondonPocRiskRatio.IsEnabled = true; _settingsService.Save(); BinanceTestnet.Strategies.Helpers.StrategyRuntimeConfig.LondonUsePocAsStop = true; };
            ChkLondonUsePocStop.Unchecked += (s, ev) => { settings.LondonUsePocAsStop = false; SldLondonPocRiskRatio.IsEnabled = false; _settingsService.Save(); BinanceTestnet.Strategies.Helpers.StrategyRuntimeConfig.LondonUsePocAsStop = false; };

            SldLondonPocRiskRatio.ValueChanged += (s, ev) => {
                try
                {
                    var v = (decimal)SldLondonPocRiskRatio.Value;
                    TxtLondonPocRiskRatioValue.Text = v.ToString(CultureInfo.InvariantCulture);
                    settings.LondonPocRiskRatio = v; _settingsService.Save();
                }
                catch { }
            };

            TxtLondonPocRiskRatioValue.LostFocus += (s, ev) => {
                if (decimal.TryParse(TxtLondonPocRiskRatioValue.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) && dec >= 0.5m && dec <= 10m)
                {
                    settings.LondonPocRiskRatio = dec; SldLondonPocRiskRatio.Value = (double)dec; _settingsService.Save();
                }
                else TxtLondonPocRiskRatioValue.Text = settings.LondonPocRiskRatio.ToString(CultureInfo.InvariantCulture);
            };

            try
            {
                TxtEmaFastLen.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.FastEmaLength.ToString();
                TxtEmaSlowLen.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.SlowEmaLength.ToString();
                TxtEmaVolMaLen.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.VolumeMaLength.ToString();
                TxtEmaVolMultiplier.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.VolumeMultiplier.ToString(CultureInfo.InvariantCulture);
            }
            catch { }

            // Initialize Candle Pattern controls from static settings
            try
            {
                TxtCandleIndecThresh.Text = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.IndecisiveThreshold.ToString(CultureInfo.InvariantCulture);
                TxtCandleVolMaLen.Text = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.VolumeMALength.ToString();
                TxtCandleEmaLen.Text = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.EmaLength.ToString();
                ChkCandleUseVolumeFilter.IsChecked = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.UseVolumeFilter;
                ChkCandleUseEmaFilter.IsChecked = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.UseEmaFilter;
                ChkCandleDebugMode.IsChecked = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.DebugMode;
            }
            catch { }

            // Initialize Bollinger controls from static settings
            try
            {
                TxtBBPeriod.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.BBPeriod.ToString();
                TxtBBStdDev.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.BBStdDev.ToString(CultureInfo.InvariantCulture);
                TxtSqueezeMin.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.SqueezeMin.ToString(CultureInfo.InvariantCulture);
                TxtVolumeMultiplier.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.VolumeMultiplier.ToString(CultureInfo.InvariantCulture);
                TxtAvgVolumePeriod.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.AvgVolumePeriod.ToString();
                TxtDelayedReentryPrevBars.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.DelayedReentryPrevBars.ToString();
                TxtTrendPeriod.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.TrendPeriod.ToString();
                TxtAdxThreshold.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.AdxThreshold.ToString(CultureInfo.InvariantCulture);
                ChkDebugMode.IsChecked = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.DebugMode;

                // set trend gate selection
                var trend = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.TrendGate.ToString();
                foreach (var it in CboTrendGate.Items)
                {
                    if (it is ComboBoxItem cbi && (cbi.Content as string) == trend)
                    {
                        CboTrendGate.SelectedItem = it; break;
                    }
                }
            }
            catch { /* non-fatal */ }

            // Wire control events to persist changes into the static settings at runtime
            TxtBBPeriod.LostFocus += (s, ev) => {
                if (int.TryParse(TxtBBPeriod.Text, out var v) && v >= 1 && v <= 5000)
                {
                    BollingerNoSqueezeStrategy.BollingerSqueezeSettings.BBPeriod = v;
                }
                else
                {
                    TxtBBPeriod.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.BBPeriod.ToString();
                }
            };

            TxtBBStdDev.LostFocus += (s, ev) => {
                if (double.TryParse(TxtBBStdDev.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d > 0)
                {
                    BollingerNoSqueezeStrategy.BollingerSqueezeSettings.BBStdDev = d;
                }
                else
                {
                    TxtBBStdDev.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.BBStdDev.ToString(CultureInfo.InvariantCulture);
                }
            };

            TxtSqueezeMin.LostFocus += (s, ev) => {
                if (decimal.TryParse(TxtSqueezeMin.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) && dec > 0)
                {
                    BollingerNoSqueezeStrategy.BollingerSqueezeSettings.SqueezeMin = dec;
                }
                else
                {
                    TxtSqueezeMin.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.SqueezeMin.ToString(CultureInfo.InvariantCulture);
                }
            };

            TxtVolumeMultiplier.LostFocus += (s, ev) => {
                if (decimal.TryParse(TxtVolumeMultiplier.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) && dec > 0)
                {
                    BollingerNoSqueezeStrategy.BollingerSqueezeSettings.VolumeMultiplier = dec;
                }
                else
                {
                    TxtVolumeMultiplier.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.VolumeMultiplier.ToString(CultureInfo.InvariantCulture);
                }
            };

            TxtAvgVolumePeriod.LostFocus += (s, ev) => {
                if (int.TryParse(TxtAvgVolumePeriod.Text, out var v) && v >= 1 && v <= 1000)
                {
                    BollingerNoSqueezeStrategy.BollingerSqueezeSettings.AvgVolumePeriod = v;
                }
                else
                {
                    TxtAvgVolumePeriod.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.AvgVolumePeriod.ToString();
                }
            };

            TxtDelayedReentryPrevBars.LostFocus += (s, ev) => {
                if (int.TryParse(TxtDelayedReentryPrevBars.Text, out var v) && v >= 0 && v <= 50)
                {
                    BollingerNoSqueezeStrategy.BollingerSqueezeSettings.DelayedReentryPrevBars = v;
                }
                else
                {
                    TxtDelayedReentryPrevBars.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.DelayedReentryPrevBars.ToString();
                }
            };

            CboTrendGate.SelectionChanged += (s, ev) => {
                if (CboTrendGate.SelectedItem is ComboBoxItem cbi)
                {
                    var name = (cbi.Content as string) ?? "EMA";
                    switch (name)
                    {
                        case "EMA": BollingerNoSqueezeStrategy.BollingerSqueezeSettings.TrendGate = BollingerNoSqueezeStrategy.TrendFilter.EMA; break;
                        case "ADX": BollingerNoSqueezeStrategy.BollingerSqueezeSettings.TrendGate = BollingerNoSqueezeStrategy.TrendFilter.ADX; break;
                        case "RSI": BollingerNoSqueezeStrategy.BollingerSqueezeSettings.TrendGate = BollingerNoSqueezeStrategy.TrendFilter.RSI; break;
                        default: BollingerNoSqueezeStrategy.BollingerSqueezeSettings.TrendGate = BollingerNoSqueezeStrategy.TrendFilter.None; break;
                    }
                }
            };

            TxtTrendPeriod.LostFocus += (s, ev) => {
                if (int.TryParse(TxtTrendPeriod.Text, out var v) && v >= 1 && v <= 1000)
                {
                    BollingerNoSqueezeStrategy.BollingerSqueezeSettings.TrendPeriod = v;
                }
                else
                {
                    TxtTrendPeriod.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.TrendPeriod.ToString();
                }
            };

            TxtAdxThreshold.LostFocus += (s, ev) => {
                if (decimal.TryParse(TxtAdxThreshold.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) && dec >= 0)
                {
                    BollingerNoSqueezeStrategy.BollingerSqueezeSettings.AdxThreshold = dec;
                }
                else
                {
                    TxtAdxThreshold.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.AdxThreshold.ToString(CultureInfo.InvariantCulture);
                }
            };

            ChkDebugMode.Checked += (s, ev) => { BollingerNoSqueezeStrategy.BollingerSqueezeSettings.DebugMode = true; };
            ChkDebugMode.Unchecked += (s, ev) => { BollingerNoSqueezeStrategy.BollingerSqueezeSettings.DebugMode = false; };

            // Wire EMA crossover controls to persist into strategy static settings
            TxtEmaFastLen.LostFocus += (s, ev) => {
                if (int.TryParse(TxtEmaFastLen.Text, out var v) && v >= 1 && v <= 5000)
                {
                    BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.FastEmaLength = v;
                }
                else
                {
                    TxtEmaFastLen.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.FastEmaLength.ToString();
                }
            };

            TxtEmaSlowLen.LostFocus += (s, ev) => {
                if (int.TryParse(TxtEmaSlowLen.Text, out var v) && v >= 1 && v <= 5000)
                {
                    BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.SlowEmaLength = v;
                }
                else
                {
                    TxtEmaSlowLen.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.SlowEmaLength.ToString();
                }
            };

            TxtEmaVolMaLen.LostFocus += (s, ev) => {
                if (int.TryParse(TxtEmaVolMaLen.Text, out var v) && v >= 1 && v <= 1000)
                {
                    BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.VolumeMaLength = v;
                }
                else
                {
                    TxtEmaVolMaLen.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.VolumeMaLength.ToString();
                }
            };

            TxtEmaVolMultiplier.LostFocus += (s, ev) => {
                if (decimal.TryParse(TxtEmaVolMultiplier.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) && dec > 0)
                {
                    BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.VolumeMultiplier = dec;
                }
                else
                {
                    TxtEmaVolMultiplier.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.VolumeMultiplier.ToString(CultureInfo.InvariantCulture);
                }
            };

            // Wire Candle Pattern controls to static settings
            TxtCandleIndecThresh.LostFocus += (s, ev) => {
                if (decimal.TryParse(TxtCandleIndecThresh.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) && dec > 0 && dec < 1)
                {
                    BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.IndecisiveThreshold = dec;
                }
                else
                {
                    TxtCandleIndecThresh.Text = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.IndecisiveThreshold.ToString(CultureInfo.InvariantCulture);
                }
            };

            TxtCandleVolMaLen.LostFocus += (s, ev) => {
                if (int.TryParse(TxtCandleVolMaLen.Text, out var v) && v >= 1 && v <= 1000)
                {
                    BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.VolumeMALength = v;
                }
                else
                {
                    TxtCandleVolMaLen.Text = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.VolumeMALength.ToString();
                }
            };

            TxtCandleEmaLen.LostFocus += (s, ev) => {
                if (int.TryParse(TxtCandleEmaLen.Text, out var v) && v >= 1 && v <= 1000)
                {
                    BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.EmaLength = v;
                }
                else
                {
                    TxtCandleEmaLen.Text = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.EmaLength.ToString();
                }
            };

            ChkCandleUseVolumeFilter.Checked += (s, ev) => { BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.UseVolumeFilter = true; };
            ChkCandleUseVolumeFilter.Unchecked += (s, ev) => { BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.UseVolumeFilter = false; };

            ChkCandleUseEmaFilter.Checked += (s, ev) => { BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.UseEmaFilter = true; };
            ChkCandleUseEmaFilter.Unchecked += (s, ev) => { BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.UseEmaFilter = false; };

            ChkCandleDebugMode.Checked += (s, ev) => { BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.DebugMode = true; };
            ChkCandleDebugMode.Unchecked += (s, ev) => { BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.DebugMode = false; };
        }

        

        private void HarmonicToggle_Changed(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.Settings;
            settings.HarmonicEnableGartley = ChkGartley.IsChecked ?? true;
            settings.HarmonicEnableButterfly = ChkButterfly.IsChecked ?? true;
            settings.HarmonicEnableBat = ChkBat.IsChecked ?? true;
            settings.HarmonicEnableCrab = ChkCrab.IsChecked ?? true;
            settings.HarmonicEnableCypher = ChkCypher.IsChecked ?? true;
            settings.HarmonicEnableShark = ChkShark.IsChecked ?? true;
            _settingsService.Save();
        }

        // Strategy selector updated: show appropriate strategy panels
        private void StrategySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var settings = _settingsService.Settings;
            var sel = StrategySelector.SelectedItem as ComboBoxItem;
            if (sel != null)
            {
                settings.SelectedStrategy = sel.Content as string ?? settings.SelectedStrategy;
                _settingsService.Save();
                HarmonicPanel.Visibility = (settings.SelectedStrategy == "HarmonicPattern") ? Visibility.Visible : Visibility.Collapsed;
                BollingerPanel.Visibility = (settings.SelectedStrategy == "BollingerNoSqueeze") ? Visibility.Visible : Visibility.Collapsed;
                EmaCrossoverPanel.Visibility = (settings.SelectedStrategy == "EmaCrossoverVolume") ? Visibility.Visible : Visibility.Collapsed;
                CandlePatternPanel.Visibility = (settings.SelectedStrategy == "CandlePatternAnalysis") ? Visibility.Visible : Visibility.Collapsed;
                LondonPanel.Visibility = (settings.SelectedStrategy == "LondonSessionVolumeProfile") ? Visibility.Visible : Visibility.Collapsed;

                // When Bollinger panel becomes visible, (re)load controls
                if (settings.SelectedStrategy == "BollingerNoSqueeze")
                {
                    try
                    {
                        TxtBBPeriod.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.BBPeriod.ToString();
                        TxtBBStdDev.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.BBStdDev.ToString(CultureInfo.InvariantCulture);
                        TxtSqueezeMin.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.SqueezeMin.ToString(CultureInfo.InvariantCulture);
                        TxtVolumeMultiplier.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.VolumeMultiplier.ToString(CultureInfo.InvariantCulture);
                        TxtAvgVolumePeriod.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.AvgVolumePeriod.ToString();
                        TxtDelayedReentryPrevBars.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.DelayedReentryPrevBars.ToString();
                        TxtTrendPeriod.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.TrendPeriod.ToString();
                        TxtAdxThreshold.Text = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.AdxThreshold.ToString(CultureInfo.InvariantCulture);
                        ChkDebugMode.IsChecked = BollingerNoSqueezeStrategy.BollingerSqueezeSettings.DebugMode;
                    }
                    catch { }
                }
                // When EMA Crossover selected, (re)load controls
                if (settings.SelectedStrategy == "EmaCrossoverVolume")
                {
                    try
                    {
                        TxtEmaFastLen.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.FastEmaLength.ToString();
                        TxtEmaSlowLen.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.SlowEmaLength.ToString();
                        TxtEmaVolMaLen.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.VolumeMaLength.ToString();
                        TxtEmaVolMultiplier.Text = BinanceTestnet.Strategies.EmaCrossoverVolumeStrategy.VolumeMultiplier.ToString(CultureInfo.InvariantCulture);
                    }
                    catch { }
                }
                // When CandlePatternAnalysis selected, (re)load controls
                if (settings.SelectedStrategy == "CandlePatternAnalysis")
                {
                    try
                    {
                        TxtCandleIndecThresh.Text = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.IndecisiveThreshold.ToString(CultureInfo.InvariantCulture);
                        TxtCandleVolMaLen.Text = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.VolumeMALength.ToString();
                        TxtCandleEmaLen.Text = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.EmaLength.ToString();
                        ChkCandleUseVolumeFilter.IsChecked = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.UseVolumeFilter;
                        ChkCandleUseEmaFilter.IsChecked = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.UseEmaFilter;
                        ChkCandleDebugMode.IsChecked = BinanceTestnet.Strategies.CandlePatternAnalysisStrategy.DebugMode;
                    }
                    catch { }
                }
            }
        }

        private void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeList.SelectedItem == null) return;
            var item = ThemeList.SelectedItem;
            var keyProp = item.GetType().GetProperty("Key");
            if (keyProp == null) return;
            var key = keyProp.GetValue(item) as string;
            if (string.IsNullOrEmpty(key)) return;

            ThemeManager.ApplyTheme(key);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
