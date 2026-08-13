using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using PropRiskManager.Domain;
using PropRiskManager.Execution;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private AspBlock _smartDashboardBlock = null!;
    private TextBlock _smartAccountDashboard = null!;
    private TextBlock _smartSymbolDashboard = null!;
    private TextBlock _smartLastUpdateDashboard = null!;
    private TextBlock _smartAlertCountDashboard = null!;
    private TextBlock _smartPerformanceDashboard = null!;
    private TextBlock _smartAlertLevelsDashboard = null!;
    private TextBlock _smartAlertFeed = null!;
    private Button _smartMonitorPositionsButton = null!;
    private Button _smartShowAlertDetailsButton = null!;
    private Button _smartRemoveAlertsButton = null!;
    private Button _smartManagementButton = null!;
    private Button _smartContextualActionButton = null!;
    private TextBlock _smartDashboardStatus = null!;
    private StackPanel _smartManagementPanel = null!;
    private ComboBox _smartManagementMode = null!;
    private CheckBox _smartStopFinancial = null!;
    private CheckBox _smartPreBeEnabled = null!;
    private CheckBox _smartBeEnabled = null!;
    private CheckBox _smartPostBeEnabled = null!;
    private TextBox _smartBeTrigger = null!;
    private TextBox _smartBeAdjustment = null!;
    private TextBox _smartPreBeAdjustment = null!;
    private TextBox _smartPostBeAdjustment = null!;
    private CheckBox _smartPpFinancial = null!;
    private CheckBox _smartMultiPp = null!;
    private TextBox _smartPpSpacing = null!;
    private TextBox _smartPpClosePercent = null!;
    private ComboBox _smartProfileScope = null!;
    private TextBox _smartAssetClassKey = null!;
    private TextBlock _smartParametersStatus = null!;
    private SmartPerformanceSeriesState _smartPerformanceSeries = new();

    private void BuildSmartDashboardPanel()
    {
        _smartDashboardBlock = Asp.SymbolTab.AddBlock("Smart Position Manager");
        ConfigureAspBlock(_smartDashboardBlock, 350);

        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(6) };

        var topCards = new Grid(1, 5) { Margin = new Thickness(0, 0, 0, 4) };
        _smartAccountDashboard = SmartDashboardCard();
        _smartSymbolDashboard = SmartDashboardCard();
        _smartLastUpdateDashboard = SmartDashboardCard();
        _smartAlertCountDashboard = SmartDashboardCard();
        _smartContextualActionButton = new Button { Height = 56, Margin = new Thickness(1), BackgroundColor = Color.OrangeRed, ForegroundColor = Color.White };
        _smartContextualActionButton.Click += _ => ExecuteContextualSmartClose();
        topCards.AddChild(_smartAccountDashboard, 0, 0);
        topCards.AddChild(_smartSymbolDashboard, 0, 1);
        topCards.AddChild(_smartLastUpdateDashboard, 0, 2);
        topCards.AddChild(_smartAlertCountDashboard, 0, 3);
        topCards.AddChild(_smartContextualActionButton, 0, 4);
        root.AddChild(topCards);

        _smartPerformanceDashboard = new TextBlock { Margin = new Thickness(2, 3, 2, 6) };
        _smartAlertLevelsDashboard = new TextBlock { Margin = new Thickness(2, 2, 2, 6) };
        root.AddChild(_smartPerformanceDashboard);
        root.AddChild(_smartAlertLevelsDashboard);

        _smartMonitorPositionsButton = new Button { Height = 34, Margin = new Thickness(1), Text = "MONITOR POSITIONS", BackgroundColor = Color.SeaGreen, ForegroundColor = Color.White };
        _smartMonitorPositionsButton.Click += _ => MonitorActiveSymbolPositions();
        root.AddChild(_smartMonitorPositionsButton);

        var alertCommandRow = new Grid(1, 2);
        _smartShowAlertDetailsButton = new Button { Height = 32, Margin = new Thickness(1), Text = "SHOW ALERT DETAILS" };
        _smartRemoveAlertsButton = new Button { Height = 32, Margin = new Thickness(1), Text = "REMOVE ALERTS", BackgroundColor = Color.OrangeRed, ForegroundColor = Color.White };
        _smartShowAlertDetailsButton.Click += _ => ToggleSmartAlertDetails();
        _smartRemoveAlertsButton.Click += _ => RemoveActiveSymbolAlerts();
        alertCommandRow.AddChild(_smartShowAlertDetailsButton, 0, 0);
        alertCommandRow.AddChild(_smartRemoveAlertsButton, 0, 1);
        root.AddChild(alertCommandRow);

        _smartManagementButton = new Button { Height = 28, Margin = new Thickness(1, 2, 1, 2), Text = "MANAGEMENT PARAMETERS" };
        _smartManagementButton.Click += _ => ToggleSmartManagementPanel();
        root.AddChild(_smartManagementButton);

        _smartAlertFeed = new TextBlock { Margin = new Thickness(2, 5, 2, 5), IsVisible = false };
        root.AddChild(_smartAlertFeed);

        _smartManagementPanel = BuildSmartManagementPanel();
        _smartManagementPanel.IsVisible = false;
        root.AddChild(_smartManagementPanel);

        _smartDashboardStatus = new TextBlock { Margin = new Thickness(2, 4, 2, 0) };
        root.AddChild(_smartDashboardStatus);
        _smartDashboardBlock.Child = root;

        ApplySmartManagementParameters(SmartManagementProfileResolver.BuiltInDefaults());
    }

    private StackPanel BuildSmartManagementPanel()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(2, 5, 2, 5) };
        panel.AddChild(new TextBlock
        {
            Text = "MANAGEMENT PARAMETERS",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _smartManagementMode = new ComboBox { Height = 24 };
        _smartManagementMode.AddItem("Points");
        _smartManagementMode.AddItem("Percentage");
        _smartManagementMode.SelectedItem = "Points";
        panel.AddChild(SmartFieldRow("Mode", _smartManagementMode));
        panel.AddChild(new TextBlock
        {
            Text = "Points fields are resolved price distances at the Smart Position boundary; no implicit pip/tick conversion.",
            Margin = new Thickness(0, 1, 0, 4)
        });

        _smartStopFinancial = new CheckBox { Text = "SL Trail financial", IsChecked = false };
        _smartPreBeEnabled = new CheckBox { Text = "Pre-BE trail", IsChecked = false };
        _smartBeEnabled = new CheckBox { Text = "Break Even", IsChecked = false };
        _smartPostBeEnabled = new CheckBox { Text = "Post-BE trail", IsChecked = false };
        panel.AddChild(SmartTwoCheckRow(_smartStopFinancial, _smartPreBeEnabled));
        panel.AddChild(SmartTwoCheckRow(_smartBeEnabled, _smartPostBeEnabled));

        _smartBeTrigger = new TextBox { Height = 24 };
        _smartBeAdjustment = new TextBox { Height = 24 };
        _smartPreBeAdjustment = new TextBox { Height = 24 };
        _smartPostBeAdjustment = new TextBox { Height = 24 };
        panel.AddChild(SmartFieldRow("BE trigger", _smartBeTrigger));
        panel.AddChild(SmartFieldRow("BE adjust", _smartBeAdjustment));
        panel.AddChild(SmartFieldRow("Pre-BE adjust", _smartPreBeAdjustment));
        panel.AddChild(SmartFieldRow("Post-BE adjust", _smartPostBeAdjustment));

        _smartPpFinancial = new CheckBox { Text = "1st Partial Profit", IsChecked = false };
        _smartMultiPp = new CheckBox { Text = "Multi-PP", IsChecked = false };
        panel.AddChild(SmartTwoCheckRow(_smartPpFinancial, _smartMultiPp));
        _smartPpSpacing = new TextBox { Height = 24 };
        _smartPpClosePercent = new TextBox { Height = 24 };
        panel.AddChild(SmartFieldRow("PP spacing", _smartPpSpacing));
        panel.AddChild(SmartFieldRow("PP Adj %", _smartPpClosePercent));

        _smartProfileScope = new ComboBox { Height = 24 };
        _smartProfileScope.AddItem("Account");
        _smartProfileScope.AddItem("Asset Class");
        _smartProfileScope.AddItem("Symbol");
        _smartProfileScope.SelectedItem = "Symbol";
        panel.AddChild(SmartFieldRow("Save scope", _smartProfileScope));
        _smartAssetClassKey = new TextBox { Height = 24, Text = string.Empty };
        panel.AddChild(SmartFieldRow("Asset class key", _smartAssetClassKey));

        var profileRow = new Grid(1, 2);
        var save = new Button { Height = 30, Margin = new Thickness(2), Text = "SAVE PARAMETERS" };
        var load = new Button { Height = 30, Margin = new Thickness(2), Text = "LOAD DEFAULTS" };
        save.Click += _ => SaveSmartProfileFromEditor();
        load.Click += _ => LoadResolvedSmartDefaults();
        profileRow.AddChild(save, 0, 0);
        profileRow.AddChild(load, 0, 1);
        panel.AddChild(profileRow);
        _smartParametersStatus = new TextBlock { Margin = new Thickness(0, 3, 0, 0) };
        panel.AddChild(_smartParametersStatus);
        return panel;
    }

    private static TextBlock SmartDashboardCard()
    {
        return new TextBlock
        {
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(3, 6, 3, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Grid SmartFieldRow(string label, ControlBase control)
    {
        var row = new Grid(1, 2) { Margin = new Thickness(0, 1, 0, 1) };
        row.AddChild(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        row.AddChild(control, 0, 1);
        return row;
    }

    private static Grid SmartTwoCheckRow(CheckBox left, CheckBox right)
    {
        var row = new Grid(1, 2) { Margin = new Thickness(0, 1, 0, 1) };
        row.AddChild(left, 0, 0);
        row.AddChild(right, 0, 1);
        return row;
    }

    private void RefreshSmartDashboard()
    {
        var activeSymbol = _symbol?.Name ?? string.Empty;
        var positions = Positions.Select(p => new SmartDashboardPosition(p.Id, p.SymbolName, p.NetProfit)).ToArray();
        var snapshot = SmartDashboardEngine.Build(
            Account.Equity,
            Server.TimeInUtc,
            activeSymbol,
            positions,
            _runtimeState.SmartPositions.Values);

        _smartAccountDashboard.Text = $"{snapshot.Account.OpenPositions}\nOPEN POSITIONS";
        _smartSymbolDashboard.Text = $"{snapshot.Account.Equity:F2}\nACC. EQUITY";
        _smartLastUpdateDashboard.Text = $"{snapshot.Account.LastUpdateUtc:HH:mm:ss}\nLAST UPDATE";
        _smartAlertCountDashboard.Text = $"{snapshot.Account.ArmedSmartAlerts}\nSMART ALERT";

        UpdateSmartPerformanceSeries(activeSymbol);
        RefreshSmartAlertLevels(activeSymbol);
        if (_smartAlertFeed.IsVisible)
            RefreshSmartAlertFeed(activeSymbol);

        var symbolPositionCount = positions.Count(position => string.Equals(position.SymbolName, activeSymbol, StringComparison.Ordinal));
        var managedCount = _runtimeState.SmartPositions.Values.Count(state => string.Equals(state.SymbolName, activeSymbol, StringComparison.Ordinal));
        var eligibleCount = Math.Max(0, symbolPositionCount - managedCount);
        _smartMonitorPositionsButton.Text = $"MONITOR {activeSymbol}\n({eligibleCount} eligible)";
        _smartMonitorPositionsButton.IsEnabled = !string.IsNullOrEmpty(activeSymbol) && eligibleCount > 0;
        _smartRemoveAlertsButton.Text = $"REMOVE ALERTS\n({managedCount} managed)";
        _smartRemoveAlertsButton.IsEnabled = managedCount > 0;

        var card = SmartDashboardEngine.BuildContextualActionCard(activeSymbol, positions);
        var scopeText = card.Scope == SmartCloseScope.ActiveSymbol
            ? (string.IsNullOrEmpty(activeSymbol) ? "SYMBOL" : activeSymbol)
            : "ACCOUNT";
        _smartContextualActionButton.Text = card.Scope == SmartCloseScope.ActiveSymbol
            ? $"{card.NetProfit:+0.00;-0.00;0.00}\n{scopeText} P&L\n{card.ActionText}"
            : $"{card.PositionCount} pos\nCLOSE ALL\n{card.NetProfit:+0.00;-0.00;0.00}";
        _smartContextualActionButton.IsEnabled = card.CanClose;
    }

    private void UpdateSmartPerformanceSeries(string activeSymbol)
    {
        if (_symbol == null || string.IsNullOrEmpty(activeSymbol))
        {
            _smartPerformanceDashboard.Text = "ACTIVE-SYMBOL PERF  unavailable";
            return;
        }

        var mid = (_symbol.Bid + _symbol.Ask) / 2.0;
        _smartPerformanceSeries = SmartPerformanceSeriesEngine.AddSample(
            _smartPerformanceSeries,
            activeSymbol,
            mid,
            Server.TimeInUtc).State;

        if (_smartPerformanceSeries.Samples.Count == 0)
        {
            _smartPerformanceDashboard.Text = $"ACTIVE-SYMBOL PERF {activeSymbol}  empty";
            return;
        }

        var latest = _smartPerformanceSeries.Samples[^1];
        var sparkline = BuildSmartSparkline(_smartPerformanceSeries.Samples.Select(sample => sample.BasisPoints));
        _smartPerformanceDashboard.Text =
            $"{activeSymbol} PERFORMANCE  {latest.BasisPoints:+0.0;-0.0;0.0} bps\n{sparkline}";
    }

    private static string BuildSmartSparkline(IEnumerable<double> values)
    {
        var samples = values.Skip(Math.Max(0, values.Count() - 24)).ToArray();
        if (samples.Length == 0)
            return string.Empty;
        var min = samples.Min();
        var max = samples.Max();
        const string levels = "▁▂▃▄▅▆▇█";
        if (Math.Abs(max - min) < 1e-12)
            return new string(levels[3], samples.Length);
        return new string(samples.Select(value => levels[Math.Clamp((int)Math.Round((value - min) / (max - min) * 7), 0, 7)]).ToArray());
    }

    private void RefreshSmartAlertLevels(string activeSymbol)
    {
        var states = _runtimeState.SmartPositions.Values
            .Where(state => string.Equals(state.SymbolName, activeSymbol, StringComparison.Ordinal))
            .OrderBy(state => state.PositionId)
            .Take(4)
            .ToArray();
        if (states.Length == 0)
        {
            _smartAlertLevelsDashboard.Text = $"ACTIVE-SYMBOL ALERT LEVELS {activeSymbol}  none";
            return;
        }

        var partialProfit = BuildAlertLevelLine(states, SmartAlertType.PartialProfit, "PARTIAL PROFIT");
        var breakEven = BuildAlertLevelLine(states, SmartAlertType.BreakEven, "BREAK EVEN");
        var stopLoss = BuildAlertLevelLine(states, SmartAlertType.StopLoss, "STOP LOSS");
        _smartAlertLevelsDashboard.Text =
            $"POSITION ALERT LEVELS  {activeSymbol}\n{partialProfit}\n{breakEven}\n{stopLoss}";
    }

    private static string BuildAlertLevelLine(IEnumerable<SmartPositionState> states, SmartAlertType alertType, string label)
    {
        var levels = states
            .SelectMany(state => state.AlertDefinitions
                .Where(definition => definition.AlertType == alertType)
                .Select(definition => $"P{state.PositionId} {definition.TriggerPrice:F5} [{definition.State}]"))
            .ToArray();
        return levels.Length == 0 ? $"{label}: none" : $"{label}: {string.Join(" | ", levels)}";
    }

    private void RefreshSmartAlertFeed(string activeSymbol)
    {
        var events = _runtimeState.SmartAlertHistory
            .Where(item => string.Equals(item.SymbolName, activeSymbol, StringComparison.Ordinal))
            .OrderByDescending(item => item.TriggeredAtUtc)
            .Take(6)
            .ToArray();
        if (events.Length == 0)
        {
            _smartAlertFeed.Text = $"ACTIVE-SYMBOL ALERT HISTORY {activeSymbol}\nNo events.";
            return;
        }

        _smartAlertFeed.Text = $"ACTIVE-SYMBOL ALERT HISTORY {activeSymbol}\n" + string.Join(
            "\n",
            events.Select(item =>
                $"{item.TriggeredAtUtc:HH:mm:ss} Pos {item.PositionId} {item.Direction} {SmartAlertLabel(item.AlertType)} " +
                $"T {item.TriggerPrice:F5} / Px {item.ObservedPrice:F5} / {item.ActionResult}" +
                (string.IsNullOrEmpty(item.DiagnosticError) ? string.Empty : $" / {item.DiagnosticError}")));
    }

    private static string SmartAlertLabel(SmartAlertType type) => type switch
    {
        SmartAlertType.PartialProfit => "PP",
        SmartAlertType.BreakEven => "BE",
        _ => "SL"
    };

    private void ToggleSmartAlertDetails()
    {
        _smartAlertFeed.IsVisible = !_smartAlertFeed.IsVisible;
        _smartShowAlertDetailsButton.Text = _smartAlertFeed.IsVisible ? "HIDE ALERT DETAILS" : "SHOW ALERT DETAILS";
        RefreshSmartDashboardHeight();
        if (_smartAlertFeed.IsVisible)
            RefreshSmartAlertFeed(_symbol?.Name ?? string.Empty);
    }

    private void ToggleSmartManagementPanel()
    {
        _smartManagementPanel.IsVisible = !_smartManagementPanel.IsVisible;
        _smartManagementButton.Text = _smartManagementPanel.IsVisible ? "HIDE PARAMETERS" : "MANAGEMENT PARAMETERS";
        RefreshSmartDashboardHeight();
    }

    private void RefreshSmartDashboardHeight()
    {
        _smartDashboardBlock.Height = _smartManagementPanel.IsVisible
            ? 690
            : _smartAlertFeed.IsVisible ? 470 : 350;
        _smartDashboardBlock.DetachedWindow.Height = Math.Max(520, _smartDashboardBlock.Height + 60);
    }

    private void MonitorActiveSymbolPositions()
    {
        var activeSymbol = _symbol?.Name ?? string.Empty;
        if (string.IsNullOrEmpty(activeSymbol))
        {
            _smartDashboardStatus.Text = "Monitor Positions: active symbol unavailable.";
            return;
        }

        if (!TryCaptureSmartManagementParameters(out var parameters, out var parameterError))
        {
            _smartDashboardStatus.Text = "Monitor Positions: " + parameterError;
            return;
        }

        var candidates = Positions
            .Where(position => string.Equals(position.SymbolName, activeSymbol, StringComparison.Ordinal) &&
                               !_runtimeState.SmartPositions.ContainsKey(position.Id))
            .ToArray();
        var enrolled = 0;
        var diagnostics = new List<string>();
        foreach (var position in candidates)
        {
            var snapshot = BuildSmartPositionSnapshot(position, observedPrice: null, isOpen: true);
            var resolution = SmartManagementEnrollmentResolver.Resolve(parameters, snapshot);
            diagnostics.AddRange(resolution.Diagnostics.Select(message => $"Pos {position.Id}: {message}"));
            var evaluation = SmartPositionEngine.Evaluate(snapshot, resolution.Settings, null);
            diagnostics.AddRange(evaluation.Diagnostics.Select(message => $"Pos {position.Id}: {message}"));
            if (evaluation.NextState == null)
                continue;

            _runtimeState.SmartPositions[position.Id] = evaluation.NextState;
            enrolled++;
        }

        SaveAccountState();
        _smartDashboardStatus.Text = $"Monitor {activeSymbol}: {enrolled}/{candidates.Length} enrolled." +
                                     (diagnostics.Count == 0 ? string.Empty : $" {diagnostics[0]}");
        RefreshSmartDashboard();
    }

    private void RemoveActiveSymbolAlerts()
    {
        var activeSymbol = _symbol?.Name ?? string.Empty;
        var states = _runtimeState.SmartPositions.Values
            .Where(state => string.Equals(state.SymbolName, activeSymbol, StringComparison.Ordinal))
            .ToArray();
        var updated = 0;
        foreach (var state in states)
        {
            var brokerPosition = Positions.FirstOrDefault(position => position.Id == state.PositionId);
            if (brokerPosition == null)
                continue;

            var snapshot = BuildSmartPositionSnapshot(brokerPosition, observedPrice: null, isOpen: true);
            var evaluation = SmartPositionEngine.Evaluate(
                snapshot,
                new SmartPositionSettings { RemoveAlertsRequested = true },
                state);
            if (evaluation.NextState == null)
                continue;
            _runtimeState.SmartPositions[state.PositionId] = evaluation.NextState;
            updated++;
        }

        SaveAccountState();
        _smartDashboardStatus.Text = $"Remove Alerts {activeSymbol}: {updated}/{states.Length} managed positions updated; broker positions unchanged.";
        RefreshSmartDashboard();
    }

    private void SaveSmartProfileFromEditor()
    {
        if (!TryCaptureSmartManagementParameters(out var parameters, out var error))
        {
            _smartParametersStatus.Text = error;
            return;
        }

        var scopeText = _smartProfileScope.SelectedItem?.ToString() ?? "Symbol";
        var scope = scopeText switch
        {
            "Account" => SmartProfileScope.Account,
            "Asset Class" => SmartProfileScope.AssetClass,
            _ => SmartProfileScope.Symbol
        };
        var scopeKey = scope switch
        {
            SmartProfileScope.Account => string.Empty,
            SmartProfileScope.AssetClass => _smartAssetClassKey.Text?.Trim() ?? string.Empty,
            _ => _symbol?.Name ?? string.Empty
        };
        var layer = new SmartManagementProfileLayer
        {
            Scope = scope,
            AccountNumber = Account.Number,
            ScopeKey = scopeKey,
            Parameters = parameters
        };
        _smartParametersStatus.Text = SaveSmartManagementProfile(layer, out var diagnostic)
            ? $"Saved {scope} parameters. Existing enrollments unchanged."
            : diagnostic;
    }

    private void LoadResolvedSmartDefaults()
    {
        var assetClass = string.IsNullOrWhiteSpace(_smartAssetClassKey.Text) ? null : _smartAssetClassKey.Text.Trim();
        var parameters = ResolveSmartManagementProfile(assetClass, _symbol?.Name, out var diagnostics);
        ApplySmartManagementParameters(parameters);
        _smartParametersStatus.Text = diagnostics.Count == 0
            ? "Loaded resolved defaults. Existing enrollments unchanged."
            : diagnostics[0];
    }

    private bool TryCaptureSmartManagementParameters(out SmartManagementParameters parameters, out string error)
    {
        parameters = SmartManagementProfileResolver.BuiltInDefaults();
        error = string.Empty;
        if (!TrySmartNumber(_smartBeTrigger, "BE trigger", out var beTrigger) ||
            !TrySmartNumber(_smartBeAdjustment, "BE adjustment", out var beAdjustment) ||
            !TrySmartNumber(_smartPreBeAdjustment, "Pre-BE adjustment", out var preBe) ||
            !TrySmartNumber(_smartPostBeAdjustment, "Post-BE adjustment", out var postBe) ||
            !TrySmartNumber(_smartPpSpacing, "PP spacing", out var ppSpacing) ||
            !TrySmartNumber(_smartPpClosePercent, "PP Adj %", out var ppClose))
        {
            error = "All management values must be finite numbers.";
            return false;
        }

        parameters.Mode = _smartManagementMode.SelectedItem?.ToString() == "Percentage"
            ? SmartManagementMode.Percentage
            : SmartManagementMode.Points;
        parameters.FinancialStopManagementEnabled = _smartStopFinancial.IsChecked == true;
        parameters.PreBreakEvenTrailingEnabled = _smartPreBeEnabled.IsChecked == true;
        parameters.BreakEvenEnabled = _smartBeEnabled.IsChecked == true;
        parameters.PostBreakEvenTrailingEnabled = _smartPostBeEnabled.IsChecked == true;
        parameters.BreakEvenTrigger = beTrigger;
        parameters.BreakEvenAdjustment = beAdjustment;
        parameters.PreBreakEvenTrailingAdjustment = preBe;
        parameters.PostBreakEvenTrailingAdjustment = postBe;
        parameters.FinancialPartialProfitEnabled = _smartPpFinancial.IsChecked == true;
        parameters.MultiPartialProfitEnabled = _smartMultiPp.IsChecked == true;
        parameters.PartialProfitSpacing = ppSpacing;
        parameters.PartialProfitClosePercent = ppClose;

        if (!SmartManagementProfileResolver.ValidateParameters(parameters))
        {
            error = "Management values are outside the validated financial range.";
            return false;
        }
        if (parameters.Mode == SmartManagementMode.Percentage &&
            (parameters.BreakEvenTrigger > 100 || parameters.PartialProfitSpacing > 100))
        {
            error = "Percentage BE trigger and PP spacing must be in (0, 100].";
            return false;
        }
        return true;
    }

    private static bool TrySmartNumber(TextBox box, string name, out double value)
    {
        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !double.IsNaN(value) && !double.IsInfinity(value))
            return true;
        value = 0;
        return false;
    }

    private void ApplySmartManagementParameters(SmartManagementParameters parameters)
    {
        _smartManagementMode.SelectedItem = parameters.Mode == SmartManagementMode.Percentage ? "Percentage" : "Points";
        _smartStopFinancial.IsChecked = parameters.FinancialStopManagementEnabled;
        _smartPreBeEnabled.IsChecked = parameters.PreBreakEvenTrailingEnabled;
        _smartBeEnabled.IsChecked = parameters.BreakEvenEnabled;
        _smartPostBeEnabled.IsChecked = parameters.PostBreakEvenTrailingEnabled;
        _smartBeTrigger.Text = parameters.BreakEvenTrigger.ToString(CultureInfo.InvariantCulture);
        _smartBeAdjustment.Text = parameters.BreakEvenAdjustment.ToString(CultureInfo.InvariantCulture);
        _smartPreBeAdjustment.Text = parameters.PreBreakEvenTrailingAdjustment.ToString(CultureInfo.InvariantCulture);
        _smartPostBeAdjustment.Text = parameters.PostBreakEvenTrailingAdjustment.ToString(CultureInfo.InvariantCulture);
        _smartPpFinancial.IsChecked = parameters.FinancialPartialProfitEnabled;
        _smartMultiPp.IsChecked = parameters.MultiPartialProfitEnabled;
        _smartPpSpacing.Text = parameters.PartialProfitSpacing.ToString(CultureInfo.InvariantCulture);
        _smartPpClosePercent.Text = parameters.PartialProfitClosePercent.ToString(CultureInfo.InvariantCulture);
    }

    private void ExecuteContextualSmartClose()
    {
        var activeSymbol = _symbol?.Name ?? string.Empty;
        var positions = Positions.Select(p => new SmartDashboardPosition(p.Id, p.SymbolName, p.NetProfit)).ToArray();
        var card = SmartDashboardEngine.BuildContextualActionCard(activeSymbol, positions);
        ExecuteSmartClose(card.Scope);
    }

    private void ExecuteSmartClose(SmartCloseScope scope)
    {
        var activeSymbol = _symbol?.Name ?? string.Empty;
        var snapshots = Positions.Select(p => new SmartDashboardPosition(p.Id, p.SymbolName, p.NetProfit)).ToArray();
        var plan = SmartDashboardEngine.PlanClose(scope, activeSymbol, snapshots);
        if (plan.Count == 0)
        {
            _smartDashboardStatus.Text = scope == SmartCloseScope.Account
                ? "Close All: no open positions."
                : $"Close {activeSymbol}: no open positions.";
            RefreshSmartDashboard();
            return;
        }

        var selectedIds = plan.PositionIds.ToHashSet();
        var result = PositionManagementService.Close(Positions.Where(position => selectedIds.Contains(position.Id)).ToArray());
        var scopeText = scope == SmartCloseScope.Account ? "ACCOUNT" : activeSymbol;
        _smartDashboardStatus.Text =
            $"Close {scopeText}: {result.Succeeded}/{result.Attempted} succeeded." +
            (string.IsNullOrEmpty(result.LastError) ? string.Empty : $" {result.LastError}");
        RefreshSmartDashboard();
        RefreshPositionManagement();
    }
}
