using System.Collections.Generic;
using cAlgo.API;
using PropRiskManager.Domain;
using PropRiskManager.State;
namespace PropRiskManager;
public sealed partial class PropRiskManagerPlugin
{
    private void OnAccountSwitched(AccountSwitchedEventArgs args){SaveAccountState();LoadAccountState();ApplySettingsToUi();BindToActiveChart();RefreshPositionManagement();}
    private void LoadAccountState()
    {
        _stateAccountNumber=Account.Number;_settings=LocalStorage.GetObject<PluginSettings>(SettingsKey(_stateAccountNumber),LocalStorageScope.Type)??new PluginSettings();_settings.EnsureDefaults(Account.Balance);
        var tradingDay=GetGuardianTradingDay(Server.TimeInUtc);_runtimeState=LocalStorage.GetObject<AccountRuntimeState>(RuntimeKey(_stateAccountNumber),LocalStorageScope.Type)??AccountStateManager.Create(_stateAccountNumber,tradingDay,Account.Balance,Account.Equity);
        if(_runtimeState.AccountNumber!=_stateAccountNumber)_runtimeState=AccountStateManager.Create(_stateAccountNumber,tradingDay,Account.Balance,Account.Equity);
        _runtimeState.PositionAutomation??=new Dictionary<int,PositionAutomationState>();_runtimeState.SmartPositions??=new Dictionary<int,SmartPositionState>();_runtimeState.SmartAlertHistory??=new List<SmartAlertEvent>();_runtimeState.SmartManagementProfiles??=new Dictionary<string,SmartManagementProfileLayer>();
        if(_runtimeState.DailyEquityPeak<=0)_runtimeState.DailyEquityPeak=Math.Max(_runtimeState.DayStartEquity,Account.Equity);
    }
    private void SaveAccountState(){if(_stateAccountNumber==0)return;CaptureSettingsFromUi();LocalStorage.SetObject(SettingsKey(_stateAccountNumber),_settings,LocalStorageScope.Type);LocalStorage.SetObject(RuntimeKey(_stateAccountNumber),_runtimeState,LocalStorageScope.Type);LocalStorage.Flush(LocalStorageScope.Type);}
    private void UpdateRuntimeState()=>AccountStateManager.Update(_runtimeState,GetGuardianTradingDay(Server.TimeInUtc),Account.Balance,Account.Equity);
    private void CaptureSettingsFromUi(){CaptureTradeSettingsFromUi();CaptureManagementSettingsFromUi();CapturePartialExitSettingsFromUi();CapturePropFirmSettingsFromUi();CapturePropPreTradeSettingsFromUi();}
    private void ApplySettingsToUi(){ApplyTradeSettingsToUi();ApplyManagementSettingsToUi();ApplyPartialExitSettingsToUi();ApplyPropFirmSettingsToUi();ApplyPropPreTradeSettingsToUi();}
    private static string SettingsKey(int accountNumber)=>$"PRM Settings {accountNumber}";private static string RuntimeKey(int accountNumber)=>$"PRM Runtime {accountNumber}";
}
