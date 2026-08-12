using System;
using cAlgo.API;
using cAlgo.API.Internals;
using PropRiskManager.State;

namespace PropRiskManager;

[Plugin(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
public sealed partial class PropRiskManagerPlugin : Plugin
{
    private const string EntryLineName = "PRM_ENTRY";
    private const string StopLineName = "PRM_STOP";
    private const string TargetLineName = "PRM_TARGET";
    private const string Label = "PropRiskManager";

    private Chart? _chart;
    private Symbol? _symbol;
    private ChartHorizontalLine? _entryLine;
    private ChartHorizontalLine? _stopLine;
    private ChartHorizontalLine? _targetLine;

    private PluginSettings _settings = new();
    private AccountRuntimeState _runtimeState = new();
    private int _stateAccountNumber;
    private DateTime _lastPersistTime;
    private bool _runtimeFaulted;
    private string _runtimeFaultReason = string.Empty;
    private int _runtimeExceptionCount;

    protected override void OnStart()
    {
        BuildTradeExecutionPanel();
        BuildAdvancedProtectionPanel();
        BuildPositionManagementPanel();
        BuildPartialTakeProfitPanel();
        BuildPartialStopLossPanel();
        BuildPropFirmProtectionPanel();
        BuildPropPreTradePanel();
        BuildTradingStatsPanel();

        LoadAccountState();
        ApplySettingsToUi();

        ChartManager.ActiveFrameChanged += OnActiveFrameChanged;
        Account.Switched += OnAccountSwitched;
        Positions.Opened += OnSmartPositionOpened;
        Positions.Modified += OnSmartPositionModified;
        Positions.Closed += OnSmartPositionClosed;

        BindToActiveChart();
        InitializeTradeHotkeys();
        RefreshPositionManagement();
        RefreshTradingStats(true);
        Timer.Start(TimeSpan.FromMilliseconds(250));
    }

    protected override void OnException(Exception exception)
    {
        _runtimeExceptionCount++;
        _runtimeFaulted = true;
        _runtimeFaultReason = $"Unhandled runtime exception #{_runtimeExceptionCount}: {exception.GetType().Name}: {exception.Message}";
        Print($"PropRiskManager {_runtimeFaultReason}\n{exception.StackTrace}");
        if (_status != null) _status.Text = "TRADING BLOCKED: " + _runtimeFaultReason + " Restart the plugin after resolving the error.";
    }

    protected override void OnError(Error error)
    {
        Print($"PropRiskManager trade operation error: {error}");
        if (_status != null) _status.Text = $"Trade operation error: {error}";
    }

    protected override void OnStop()
    {
        SaveAccountState();
        DisposeTradeHotkeys();
        ChartManager.ActiveFrameChanged -= OnActiveFrameChanged;
        Account.Switched -= OnAccountSwitched;
        Positions.Opened -= OnSmartPositionOpened;
        Positions.Modified -= OnSmartPositionModified;
        Positions.Closed -= OnSmartPositionClosed;
        UnbindChart();
    }

    protected override void OnTimer()
    {
        if (_chart == null) BindToActiveChart();
        RefreshMarketInfo();
        UpdateLineVisibility();
        CapturePropFirmSettingsFromUi();
        CapturePropPreTradeSettingsFromUi();
        UpdateRuntimeState();
        RunPropFirmGuardian();
        RecalculatePreview();
        RunAdvancedProtection();
        RunPartialExitAutomation();
        RunSmartPositionMonitoring();
        RunSmartPositionSafetyReconciliation();
        RefreshPositionManagement();
        RefreshTradingStats();

        if (Server.Time >= _lastPersistTime.AddSeconds(2))
        {
            SaveAccountState();
            _lastPersistTime = Server.Time;
        }
    }
}
