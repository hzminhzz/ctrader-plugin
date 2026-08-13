using System;
using cAlgo.API;
using cAlgo.API.Internals;
using PropRiskManager.State;

namespace PropRiskManager;

[Plugin(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
public sealed partial class PropRiskManagerPlugin : Plugin
{
    private Chart? _chart;
    private Symbol? _symbol;

    private PluginSettings _settings = new();
    private AccountRuntimeState _runtimeState = new();
    private int _stateAccountNumber;
    private DateTime _lastPersistTime;
    private bool _runtimeFaulted;
    private string _runtimeFaultReason = string.Empty;
    private int _runtimeExceptionCount;

    protected override void OnStart()
    {
        BuildAdvancedProtectionPanel();
        BuildSmartDashboardPanel();
        BuildPositionManagementPanel();
        BuildPartialTakeProfitPanel();
        BuildPartialStopLossPanel();
        BuildPropFirmProtectionPanel();

        LoadAccountState();
        ApplySettingsToUi();

        ChartManager.ActiveFrameChanged += OnActiveFrameChanged;
        Account.Switched += OnAccountSwitched;
        Positions.Opened += OnSmartPositionOpened;
        Positions.Modified += OnSmartPositionModified;
        Positions.Closed += OnSmartPositionClosed;

        BindToActiveChart();
        RefreshPositionManagement();
        Timer.Start(TimeSpan.FromMilliseconds(250));
    }

    protected override void OnException(Exception exception)
    {
        _runtimeExceptionCount++;
        _runtimeFaulted = true;
        _runtimeFaultReason = $"Unhandled runtime exception #{_runtimeExceptionCount}: {exception.GetType().Name}: {exception.Message}";
        Print($"PropRiskManager {_runtimeFaultReason}\n{exception.StackTrace}");

    }

    protected override void OnError(Error error)
    {
        Print($"PropRiskManager trade operation error: {error}");
    }

    protected override void OnStop()
    {
        SaveAccountState();
        ChartManager.ActiveFrameChanged -= OnActiveFrameChanged;
        Account.Switched -= OnAccountSwitched;
        Positions.Opened -= OnSmartPositionOpened;
        Positions.Modified -= OnSmartPositionModified;
        Positions.Closed -= OnSmartPositionClosed;
        UnbindChart();
    }

    protected override void OnTimer()
    {
        if (_chart == null)
            BindToActiveChart();

        CapturePropFirmSettingsFromUi();
        UpdateRuntimeState();
        RunPropFirmGuardian();
        RunAdvancedProtection();
        RunPartialExitAutomation();
        RunSmartPositionMonitoring();
        RunSmartPositionSafetyReconciliation();
        RefreshSmartDashboard();
        RefreshPositionManagement();

        if (Server.Time >= _lastPersistTime.AddSeconds(2))
        {
            SaveAccountState();
            _lastPersistTime = Server.Time;
        }
    }
}
