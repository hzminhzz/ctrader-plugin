using cAlgo.API;
using PropRiskManager.State;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private void OnAccountSwitched(AccountSwitchedEventArgs args)
    {
        SaveAccountState();
        LoadAccountState();
        ApplySettingsToUi();
        BindToActiveChart();
        RefreshPositionManagement();
    }

    private void LoadAccountState()
    {
        _stateAccountNumber = Account.Number;
        _settings = LocalStorage.GetObject<PluginSettings>(SettingsKey(_stateAccountNumber), LocalStorageScope.Type) ?? new PluginSettings();
        _runtimeState = LocalStorage.GetObject<AccountRuntimeState>(RuntimeKey(_stateAccountNumber), LocalStorageScope.Type)
            ?? AccountStateManager.Create(_stateAccountNumber, Server.Time.Date, Account.Balance, Account.Equity);

        if (_runtimeState.AccountNumber != _stateAccountNumber)
            _runtimeState = AccountStateManager.Create(_stateAccountNumber, Server.Time.Date, Account.Balance, Account.Equity);
    }

    private void SaveAccountState()
    {
        if (_stateAccountNumber == 0)
            return;

        CaptureSettingsFromUi();
        LocalStorage.SetObject(SettingsKey(_stateAccountNumber), _settings, LocalStorageScope.Type);
        LocalStorage.SetObject(RuntimeKey(_stateAccountNumber), _runtimeState, LocalStorageScope.Type);
        LocalStorage.Flush(LocalStorageScope.Type);
    }

    private void UpdateRuntimeState()
    {
        AccountStateManager.Update(_runtimeState, Server.Time.Date, Account.Balance, Account.Equity);
    }

    private void CaptureSettingsFromUi()
    {
        CaptureTradeSettingsFromUi();
        CaptureManagementSettingsFromUi();
    }

    private void ApplySettingsToUi()
    {
        ApplyTradeSettingsToUi();
        ApplyManagementSettingsToUi();
    }

    private static string SettingsKey(int accountNumber) => $"PRM Settings {accountNumber}";
    private static string RuntimeKey(int accountNumber) => $"PRM Runtime {accountNumber}";
}
