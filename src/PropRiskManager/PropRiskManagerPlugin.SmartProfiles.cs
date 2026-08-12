using System.Collections.Generic;using PropRiskManager.Domain;
namespace PropRiskManager;
public sealed partial class PropRiskManagerPlugin
{
 private bool SaveSmartManagementProfile(SmartManagementProfileLayer layer,out string diagnostic){var m=new List<string>();if(!SmartManagementProfileResolver.ValidateLayer(layer,Account.Number,m)){diagnostic=m.Count==0?"Smart management profile is invalid.":m[0];return false;}_runtimeState.SmartManagementProfiles[SmartManagementProfileResolver.StorageKey(layer)]=layer;SaveAccountState();diagnostic=string.Empty;return true;}
 private SmartManagementParameters ResolveSmartManagementProfile(string? assetClass,string? symbolName,out IReadOnlyList<string> diagnostics)=>SmartManagementProfileResolver.Resolve(Account.Number,assetClass,symbolName,_runtimeState.SmartManagementProfiles.Values,out diagnostics);
 private static SmartManagementParameters LoadSmartManagementDefaults()=>SmartManagementProfileResolver.BuiltInDefaults();
}
