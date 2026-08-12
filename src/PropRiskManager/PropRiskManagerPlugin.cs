using System;
using cAlgo.API;
using cAlgo.API.Internals;
using PropRiskManager.State;

namespace PropRiskManager;

[Plugin(AccessRights = AccessRights.None)]
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

    protected override void OnStart()
    {
        BuildTradeExecutionPanel();
        BuildAdvancedProtectionPanel();
        BuildPositionManagementPanel();

        LoadAccountState();
        ApplySettingsToUi();

        ChartManager.ActiveFrameChanged += OnActiveFrameChanged;
        Account.Switched += OnAccountSwitched;

        BindToActiveChart();
        RefreshPositionManagement();
        Timer.Start(TimeSpan.FromMilliseconds(250));
    }

    protected override void OnStop()
    {
        SaveAccountState();
        ChartManager.ActiveFrameChanged -= OnActiveFrameChanged;
        Account.Switched -= OnAccountSwitched;
        UnbindChart();
    }

    protected override void OnTimer()
    {
        RefreshMarketInfo();
        UpdateLineVisibility();
        UpdateRuntimeState();
        RecalculatePreview();
        RunAdvancedProtection();
        RefreshPositionManagement();

        if (Server.Time >= _lastPersistTime.AddSeconds(2))
        {
            SaveAccountState();
            _lastPersistTime = Server.Time;
        }
    }
}
