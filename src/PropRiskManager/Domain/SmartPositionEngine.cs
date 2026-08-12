using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PropRiskManager.Domain;

public static class SmartPositionEngine
{
    public static SmartPositionEvaluation Evaluate(SmartPositionSnapshot positionSnapshot, SmartPositionSettings settings, SmartPositionState? persistedState)
    {
        if (settings.RemoveMonitoringRequested) return new SmartPositionEvaluation { NextState = null };
        if (persistedState is null)
        {
            if (!settings.EnrollRequested) return new SmartPositionEvaluation { NextState = null };
            var diagnostics = ValidateEnrollment(positionSnapshot);
            if (diagnostics.Count > 0) return new SmartPositionEvaluation { Diagnostics = diagnostics, NextState = null };
            var state = CreateInitialState(positionSnapshot);
            diagnostics.AddRange(CaptureReferenceDiagnostics(positionSnapshot));
            diagnostics.AddRange(ConfigureAlerts(state, positionSnapshot, settings, false));
            if (settings.ConfigureStopManagementRequested) diagnostics.AddRange(ConfigureStopManagement(state, settings));
            if (settings.ConfigurePartialProfitRequested) diagnostics.AddRange(ConfigurePartialProfit(state, settings));
            return new SmartPositionEvaluation { Diagnostics = diagnostics, NextState = state };
        }
        if (persistedState.PositionId != positionSnapshot.PositionId)
            return new SmartPositionEvaluation { Diagnostics = new[] { "Persisted state position identity does not match the supplied snapshot." }, NextState = Clone(persistedState) };
        if (!positionSnapshot.IsOpen) return new SmartPositionEvaluation { NextState = null };
        var nextState = Clone(persistedState);
        var diagnosticsResult = ValidateReconciliationIdentity(positionSnapshot, persistedState);
        if (diagnosticsResult.Count > 0) return new SmartPositionEvaluation { Diagnostics = diagnosticsResult, NextState = nextState };
        ReconcileBrokerState(nextState, positionSnapshot, diagnosticsResult);
        if (settings.ConfigureStopManagementRequested) diagnosticsResult.AddRange(ConfigureStopManagement(nextState, settings));
        if (settings.ConfigurePartialProfitRequested) diagnosticsResult.AddRange(ConfigurePartialProfit(nextState, settings));
        if (settings.RetryRejectedPartialProfitRequested && nextState.PendingPartialProfitAction?.ExecutionStatus is SmartActionExecutionStatus.Rejected or SmartActionExecutionStatus.NoOp)
            nextState.PendingPartialProfitAction = null;
        if (settings.RemoveAlertsRequested)
            foreach (var definition in nextState.AlertDefinitions) if (definition.State == SmartAlertState.Armed) definition.State = SmartAlertState.Disarmed;
        else if (settings.RearmAlertsRequested) diagnosticsResult.AddRange(ConfigureAlerts(nextState, positionSnapshot, settings, true));
        var events = EvaluateAlerts(nextState, positionSnapshot);
        var actions = EvaluateFinancialStopManagement(nextState, positionSnapshot, diagnosticsResult).Concat(EvaluateFirstPartialProfit(nextState, positionSnapshot, diagnosticsResult)).ToArray();
        return new SmartPositionEvaluation { Actions = actions, Alerts = events, Diagnostics = diagnosticsResult, NextState = nextState };
    }

    public static SmartPositionState RecordStopActionResult(SmartPositionState persistedState, string actionId, SmartActionExecutionStatus status, double? normalizedRequestedStopPrice = null, string? diagnosticError = null)
    {
        var nextState = Clone(persistedState); var pending = nextState.PendingStopAction;
        if (pending == null || !string.Equals(pending.ActionId, actionId, StringComparison.Ordinal)) return nextState;
        if (normalizedRequestedStopPrice.HasValue && IsFinitePositive(normalizedRequestedStopPrice.Value)) pending.RequestedStopPrice = normalizedRequestedStopPrice.Value;
        pending.ExecutionStatus = status; pending.DiagnosticError = diagnosticError ?? string.Empty; return nextState;
    }

    public static SmartPositionState RecordPartialProfitActionResult(SmartPositionState persistedState, string actionId, SmartActionExecutionStatus status, string? diagnosticError = null)
    {
        var nextState = Clone(persistedState); var pending = nextState.PendingPartialProfitAction;
        if (pending == null || !string.Equals(pending.ActionId, actionId, StringComparison.Ordinal)) return nextState;
        pending.ExecutionStatus = status; pending.DiagnosticError = diagnosticError ?? string.Empty; return nextState;
    }

    public static int CountArmedAlerts(IEnumerable<SmartPositionState> states) => states.Sum(s => s.AlertDefinitions.Count(d => d.State == SmartAlertState.Armed));

    private static List<string> ValidateEnrollment(SmartPositionSnapshot s)
    {
        var d = new List<string>(); if (!s.IsOpen) d.Add("Only open positions can be enrolled."); if (s.PositionId <= 0) d.Add("Position identity is required for enrollment.");
        if (string.IsNullOrWhiteSpace(s.SymbolName)) d.Add("Symbol name is required for enrollment."); if (!IsFinitePositive(s.EntryPrice)) d.Add("Entry price must be a finite positive value.");
        if (!IsFinitePositive(s.VolumeInUnits)) d.Add("Position volume must be a finite positive value."); if (s.ObservedAtUtc == default) d.Add("Observation time is required for enrollment."); return d;
    }

    private static List<string> ValidateReconciliationIdentity(SmartPositionSnapshot s, SmartPositionState st)
    {
        var d = new List<string>(); if (string.IsNullOrWhiteSpace(s.SymbolName) || !string.Equals(s.SymbolName, st.SymbolName, StringComparison.Ordinal)) d.Add("Broker symbol is missing or contradicts the enrolled position identity; reconciliation was skipped.");
        if (s.Direction != st.Direction) d.Add("Broker direction contradicts the enrolled position identity; reconciliation was skipped.");
        if (!IsFinitePositive(s.EntryPrice) || Math.Abs(s.EntryPrice - st.EntryPrice) > 1e-12) d.Add("Broker entry price is missing or contradicts the captured position identity; reconciliation was skipped.");
        if (s.ObservedAtUtc == default) d.Add("Broker observation time is missing; reconciliation was skipped."); return d;
    }

    private static IReadOnlyList<string> CaptureReferenceDiagnostics(SmartPositionSnapshot s)
    {
        var d = new List<string>(); if (s.StopLoss.HasValue && !IsValidInitialStopLoss(s.Direction, s.EntryPrice, s.StopLoss.Value)) d.Add("Initial stop-loss reference is invalid and was captured as unavailable.");
        if (s.TakeProfit.HasValue && !IsValidFavorableTrigger(s.Direction, s.EntryPrice, s.TakeProfit.Value)) d.Add("Initial take-profit reference is invalid and was captured as unavailable."); return d;
    }

    private static SmartPositionState CreateInitialState(SmartPositionSnapshot s)
    {
        var sl = s.StopLoss.HasValue && IsValidInitialStopLoss(s.Direction, s.EntryPrice, s.StopLoss.Value); var tp = s.TakeProfit.HasValue && IsValidFavorableTrigger(s.Direction, s.EntryPrice, s.TakeProfit.Value);
        return new SmartPositionState { PositionId=s.PositionId, SymbolName=s.SymbolName, Direction=s.Direction, EntryPrice=s.EntryPrice, OriginalVolumeInUnits=s.VolumeInUnits, CurrentVolumeInUnits=s.VolumeInUnits,
            InitialStopLoss=sl?s.StopLoss:null, InitialTakeProfit=tp?s.TakeProfit:null, CurrentStopLoss=IsFiniteNullablePositive(s.StopLoss)?s.StopLoss:null, CurrentTakeProfit=IsFiniteNullablePositive(s.TakeProfit)?s.TakeProfit:null,
            LastReconciledAtUtc=s.ObservedAtUtc, MonitoringStartedAtUtc=s.ObservedAtUtc, Phase=SmartPositionPhase.MonitoringPreBreakEven, StopManagement=new SmartStopManagementPlan(), PartialProfit=new SmartPartialProfitPlan(), AlertDefinitions=new List<SmartAlertDefinition>() };
    }

    private static void ReconcileBrokerState(SmartPositionState st, SmartPositionSnapshot s, ICollection<string> d)
    {
        if (!IsFinitePositive(s.VolumeInUnits)) d.Add("Broker reports an invalid open-position volume; prior volume was retained and no financial action was emitted."); else st.CurrentVolumeInUnits=s.VolumeInUnits;
        st.CurrentStopLoss=ReconcileOptionalPrice(s.StopLoss,"stop loss",d); st.CurrentTakeProfit=ReconcileOptionalPrice(s.TakeProfit,"take profit",d); st.LastReconciledAtUtc=s.ObservedAtUtc;
        var a=st.AlertDefinitions.FirstOrDefault(x=>x.AlertType==SmartAlertType.StopLoss&&!x.IsVirtualStopLoss); if(a==null)return; if(!st.CurrentStopLoss.HasValue){if(a.State==SmartAlertState.Armed)a.State=SmartAlertState.Disarmed;return;} if(a.State==SmartAlertState.Armed)a.TriggerPrice=st.CurrentStopLoss.Value;
    }
    private static double? ReconcileOptionalPrice(double? p,string n,ICollection<string>d){if(!p.HasValue)return null;if(IsFinitePositive(p.Value))return p.Value;d.Add($"Broker {n} is invalid; the reconciled reference was cleared.");return null;}

    private static IReadOnlyList<string> ConfigurePartialProfit(SmartPositionState st, SmartPositionSettings s)
    {
        var d=new List<string>(); var p=new SmartPartialProfitPlan{Mode=s.ManagementMode,Enabled=s.FinancialPartialProfitEnabled,MultiEnabled=s.MultiPartialProfitEnabled};
        var cv=s.PartialProfitClosePercent.HasValue&&s.PartialProfitClosePercent.Value>0&&s.PartialProfitClosePercent.Value<=100&&!double.IsNaN(s.PartialProfitClosePercent.Value)&&!double.IsInfinity(s.PartialProfitClosePercent.Value); if(cv)p.ClosePercent=s.PartialProfitClosePercent!.Value;
        var sv=false;
        if(s.ManagementMode==SmartManagementMode.Percentage){var x=s.PartialProfitSpacingPercentage;if(st.InitialTakeProfit.HasValue&&x.HasValue&&x.Value>0&&x.Value<=100&&!double.IsNaN(x.Value)&&!double.IsInfinity(x.Value)){p.StageSpacingPriceDistance=SmartManagementSemantics.ProfitReferenceDistance(st.EntryPrice,st.InitialTakeProfit.Value)*x.Value/100.0;p.MaximumStages=p.MultiEnabled?SmartManagementSemantics.PercentageMultiPartialStageCount(x.Value):1;sv=p.MaximumStages>0;}}
        else {double? x=s.PartialProfitSpacingPriceDistance;if(!x.HasValue&&s.FirstPartialProfitTriggerPrice.HasValue&&IsValidFavorableTrigger(st.Direction,st.EntryPrice,s.FirstPartialProfitTriggerPrice.Value))x=Math.Abs(s.FirstPartialProfitTriggerPrice.Value-st.EntryPrice);if(x.HasValue&&IsFinitePositive(x.Value)){p.StageSpacingPriceDistance=x.Value;p.MaximumStages=p.MultiEnabled?SmartManagementSemantics.MaximumMultiPartialProfitStages:1;sv=true;}}
        if(p.Enabled&&(!sv||!cv)){p.Enabled=false;d.Add("Partial-profit spacing/close percentage is invalid for the frozen management mode; financial partial profit was disabled.");}
        st.PartialProfit=p;st.PendingPartialProfitAction=null;st.FirstPartialProfitCompleted=false;st.FirstPartialProfitCompletedAtUtc=null;st.CompletedPartialProfitStageCount=0;return d;
    }

    private static IReadOnlyList<SmartPositionAction> EvaluateFirstPartialProfit(SmartPositionState st,SmartPositionSnapshot s,ICollection<string>d)
    {
        st.PartialProfit??=new SmartPartialProfitPlan();ConfirmPendingPartialProfit(st,s);if(!st.PartialProfit.Enabled||st.PendingPartialProfitAction!=null)return Array.Empty<SmartPositionAction>();var k=st.CompletedPartialProfitStageCount+1;if(k>st.PartialProfit.MaximumStages)return Array.Empty<SmartPositionAction>();
        if(!s.ObservedPrice.HasValue||!IsFinitePositive(s.ObservedPrice.Value))return Array.Empty<SmartPositionAction>();var trigger=st.EntryPrice+DirectionSign(st.Direction)*k*st.PartialProfit.StageSpacingPriceDistance;if(!IsFavorableTriggerReached(st.Direction,s.ObservedPrice.Value,trigger))return Array.Empty<SmartPositionAction>();
        if(!IsFinitePositive(st.CurrentVolumeInUnits)){d.Add("Partial profit requires a finite positive reconciled broker volume.");return Array.Empty<SmartPositionAction>();}var close=st.CurrentVolumeInUnits*st.PartialProfit.ClosePercent/100.0;if(!IsFinitePositive(close)||close>st.CurrentVolumeInUnits){d.Add("Partial-profit close volume is invalid; no financial action was emitted.");return Array.Empty<SmartPositionAction>();}
        var a=new SmartPositionAction{ActionId=BuildPartialProfitActionId(st.PositionId,k,close,s.ObservedAtUtc),ActionType=SmartPositionActionType.PartialClose,PositionId=st.PositionId,SymbolName=st.SymbolName,Direction=st.Direction,RequestedCloseVolumeInUnits=close,PartialProfitStageIndex=k,RequestedAtUtc=s.ObservedAtUtc};st.PendingPartialProfitAction=new SmartPendingPartialProfitAction{ActionId=a.ActionId,StageIndex=k,BrokerVolumeAtRequest=st.CurrentVolumeInUnits,RequestedCloseVolumeInUnits=close,RequestedAtUtc=s.ObservedAtUtc,ExecutionStatus=SmartActionExecutionStatus.Pending};return new[]{a};
    }
    private static void ConfirmPendingPartialProfit(SmartPositionState st,SmartPositionSnapshot s){var p=st.PendingPartialProfitAction;if(p==null)return;var t=Math.Max(1e-9,Math.Abs(p.BrokerVolumeAtRequest)*1e-12);if(st.CurrentVolumeInUnits<p.BrokerVolumeAtRequest-t){st.CompletedPartialProfitStageCount=Math.Max(st.CompletedPartialProfitStageCount,p.StageIndex);st.FirstPartialProfitCompleted=st.CompletedPartialProfitStageCount>=1;if(st.FirstPartialProfitCompletedAtUtc==null&&st.FirstPartialProfitCompleted)st.FirstPartialProfitCompletedAtUtc=s.ObservedAtUtc;st.PendingPartialProfitAction=null;}}

    private static IReadOnlyList<string> ConfigureStopManagement(SmartPositionState st,SmartPositionSettings s)
    {
        var d=new List<string>();var p=new SmartStopManagementPlan{Mode=s.ManagementMode,Enabled=s.FinancialStopManagementEnabled,StopImprovementEpsilon=IsFiniteNonNegative(s.StopImprovementEpsilon)?s.StopImprovementEpsilon:0};if(!IsFiniteNonNegative(s.StopImprovementEpsilon))d.Add("Stop-improvement epsilon is invalid and was reset to zero.");
        if(s.ManagementMode==SmartManagementMode.Percentage){if(!st.InitialStopLoss.HasValue||!st.InitialTakeProfit.HasValue){p.Enabled=false;d.Add("Percentage stop management requires captured initial SL and TP references; financial stop management was disabled.");}else{var r=SmartManagementSemantics.RiskReferenceDistance(st.EntryPrice,st.InitialStopLoss.Value);if(s.PreBreakEvenTrailingEnabled&&s.PreBreakEvenTrailingPercentage.HasValue&&IsFinitePositive(s.PreBreakEvenTrailingPercentage.Value)){p.PreBreakEvenTrailingEnabled=true;p.PreBreakEvenTrailingPriceDistance=r*s.PreBreakEvenTrailingPercentage.Value/100.0;}if(s.BreakEvenFinancialEnabled&&s.BreakEvenTriggerPercentage.HasValue&&IsFinitePositive(s.BreakEvenTriggerPercentage.Value)&&s.BreakEvenAdjustmentPercentage.HasValue&&IsFiniteNonNegative(s.BreakEvenAdjustmentPercentage.Value)){p.BreakEvenEnabled=true;p.BreakEvenTriggerPrice=SmartManagementSemantics.FavorablePercentageTrigger(st.Direction,st.EntryPrice,st.InitialTakeProfit.Value,s.BreakEvenTriggerPercentage.Value);p.BreakEvenAdjustmentPriceDistance=r*s.BreakEvenAdjustmentPercentage.Value/100.0;}if(s.PostBreakEvenTrailingEnabled&&s.PostBreakEvenTrailingPercentage.HasValue&&IsFinitePositive(s.PostBreakEvenTrailingPercentage.Value)){p.PostBreakEvenTrailingEnabled=true;p.PostBreakEvenTrailingPriceDistance=r*s.PostBreakEvenTrailingPercentage.Value/100.0;}}}
        else{if(s.PreBreakEvenTrailingEnabled&&IsFinitePositive(s.PreBreakEvenTrailingPriceDistance??double.NaN)){p.PreBreakEvenTrailingEnabled=true;p.PreBreakEvenTrailingPriceDistance=s.PreBreakEvenTrailingPriceDistance!.Value;}if(s.BreakEvenFinancialEnabled){var tv=s.BreakEvenFinancialTriggerPrice.HasValue&&IsFinitePositive(s.BreakEvenFinancialTriggerPrice.Value)&&IsValidFavorableTrigger(st.Direction,st.EntryPrice,s.BreakEvenFinancialTriggerPrice.Value);var av=s.BreakEvenAdjustmentPriceDistance.HasValue&&IsFiniteNonNegative(s.BreakEvenAdjustmentPriceDistance.Value);if(tv&&av){p.BreakEvenEnabled=true;p.BreakEvenTriggerPrice=s.BreakEvenFinancialTriggerPrice;p.BreakEvenAdjustmentPriceDistance=s.BreakEvenAdjustmentPriceDistance!.Value;}}if(s.PostBreakEvenTrailingEnabled&&IsFinitePositive(s.PostBreakEvenTrailingPriceDistance??double.NaN)){p.PostBreakEvenTrailingEnabled=true;p.PostBreakEvenTrailingPriceDistance=s.PostBreakEvenTrailingPriceDistance!.Value;}}
        if(p.Enabled&&!p.PreBreakEvenTrailingEnabled&&!p.BreakEvenEnabled&&!p.PostBreakEvenTrailingEnabled){p.Enabled=false;d.Add("Financial stop management had no valid enabled behavior and was disabled.");}st.StopManagement=p;st.PendingStopAction=null;st.Phase=SmartPositionPhase.MonitoringPreBreakEven;st.PhaseChangedAtUtc=null;return d;
    }

    private static IReadOnlyList<SmartPositionAction> EvaluateFinancialStopManagement(SmartPositionState st,SmartPositionSnapshot s,ICollection<string>d){var p=st.StopManagement??new SmartStopManagementPlan();st.StopManagement=p;if(!p.Enabled)return Array.Empty<SmartPositionAction>();ConfirmPendingStopAction(st,s);if(!s.ObservedPrice.HasValue||!IsFinitePositive(s.ObservedPrice.Value))return Array.Empty<SmartPositionAction>();if(st.Phase==SmartPositionPhase.BreakEven&&st.PendingStopAction==null&&st.PhaseChangedAtUtc.HasValue&&s.ObservedAtUtc>st.PhaseChangedAtUtc.Value){st.Phase=SmartPositionPhase.PostBreakEvenTrailing;st.PhaseChangedAtUtc=s.ObservedAtUtc;}if(st.PendingStopAction!=null){var q=st.PendingStopAction;if(q.ExecutionStatus is SmartActionExecutionStatus.Pending or SmartActionExecutionStatus.AcceptedAwaitingReconciliation)return Array.Empty<SmartPositionAction>();if(q.Reason==SmartStopActionReason.BreakEven)return Array.Empty<SmartPositionAction>();}return st.Phase switch{SmartPositionPhase.MonitoringPreBreakEven=>EvaluatePreBreakEven(st,s,d),SmartPositionPhase.PostBreakEvenTrailing=>EvaluatePostBreakEven(st,s,d),_=>Array.Empty<SmartPositionAction>()};}
    private static IReadOnlyList<SmartPositionAction> EvaluatePreBreakEven(SmartPositionState st,SmartPositionSnapshot s,ICollection<string>d){var p=st.StopManagement;var q=s.ObservedPrice!.Value;if(p.BreakEvenEnabled&&p.BreakEvenTriggerPrice.HasValue&&IsFavorableTriggerReached(st.Direction,q,p.BreakEvenTriggerPrice.Value)){var x=st.EntryPrice+DirectionSign(st.Direction)*p.BreakEvenAdjustmentPriceDistance;if(!IsExecutableStopCandidate(st.Direction,x,q,p.StopImprovementEpsilon)){d.Add("Break-even stop candidate is not executable relative to the observed close-side price; no action was emitted.");return Array.Empty<SmartPositionAction>();}if(IsAtOrBetterProtection(st.Direction,st.CurrentStopLoss,x,p.StopImprovementEpsilon)){st.Phase=SmartPositionPhase.BreakEven;st.PhaseChangedAtUtc=s.ObservedAtUtc;st.PendingStopAction=null;return Array.Empty<SmartPositionAction>();}return EmitStopAction(st,s,x,SmartStopActionReason.BreakEven,SmartPositionPhase.BreakEven);}if(!p.PreBreakEvenTrailingEnabled)return Array.Empty<SmartPositionAction>();return EmitTrailingActionIfImproving(st,s,q-DirectionSign(st.Direction)*p.PreBreakEvenTrailingPriceDistance,SmartStopActionReason.PreBreakEvenTrailing,SmartPositionPhase.MonitoringPreBreakEven,d);}
    private static IReadOnlyList<SmartPositionAction> EvaluatePostBreakEven(SmartPositionState st,SmartPositionSnapshot s,ICollection<string>d){var p=st.StopManagement;if(!p.PostBreakEvenTrailingEnabled)return Array.Empty<SmartPositionAction>();var q=s.ObservedPrice!.Value;return EmitTrailingActionIfImproving(st,s,q-DirectionSign(st.Direction)*p.PostBreakEvenTrailingPriceDistance,SmartStopActionReason.PostBreakEvenTrailing,SmartPositionPhase.PostBreakEvenTrailing,d);}
    private static IReadOnlyList<SmartPositionAction> EmitTrailingActionIfImproving(SmartPositionState st,SmartPositionSnapshot s,double x,SmartStopActionReason r,SmartPositionPhase phase,ICollection<string>d){var e=st.StopManagement.StopImprovementEpsilon;if(!IsFinitePositive(x)||!IsExecutableStopCandidate(st.Direction,x,s.ObservedPrice!.Value,e)){d.Add($"{r} stop candidate is invalid; no action was emitted.");return Array.Empty<SmartPositionAction>();}if(!ImprovesProtection(st.Direction,st.CurrentStopLoss,x,e))return Array.Empty<SmartPositionAction>();if(st.PendingStopAction!=null&&!ImprovesProtection(st.Direction,st.PendingStopAction.RequestedStopPrice,x,e))return Array.Empty<SmartPositionAction>();return EmitStopAction(st,s,x,r,phase);}
    private static IReadOnlyList<SmartPositionAction> EmitStopAction(SmartPositionState st,SmartPositionSnapshot s,double x,SmartStopActionReason r,SmartPositionPhase phase){var a=new SmartPositionAction{ActionId=BuildStopActionId(st.PositionId,r,x,s.ObservedAtUtc),ActionType=SmartPositionActionType.ImproveStopLoss,Reason=r,PositionId=st.PositionId,SymbolName=st.SymbolName,Direction=st.Direction,RequestedStopPrice=x,TargetPhase=phase,RequestedAtUtc=s.ObservedAtUtc};st.PendingStopAction=new SmartPendingStopAction{ActionId=a.ActionId,Reason=r,RequestedStopPrice=x,TargetPhase=phase,RequestedAtUtc=s.ObservedAtUtc,ExecutionStatus=SmartActionExecutionStatus.Pending};return new[]{a};}
    private static void ConfirmPendingStopAction(SmartPositionState st,SmartPositionSnapshot s){var p=st.PendingStopAction;if(p==null||!IsAtOrBetterProtection(st.Direction,st.CurrentStopLoss,p.RequestedStopPrice,st.StopManagement.StopImprovementEpsilon))return;if(p.TargetPhase!=st.Phase){st.Phase=p.TargetPhase;st.PhaseChangedAtUtc=s.ObservedAtUtc;}st.PendingStopAction=null;}

    private static IReadOnlyList<SmartAlertEvent> EvaluateAlerts(SmartPositionState st,SmartPositionSnapshot s){if(!s.ObservedPrice.HasValue||!IsFinitePositive(s.ObservedPrice.Value))return Array.Empty<SmartAlertEvent>();var ev=new List<SmartAlertEvent>();foreach(var x in st.AlertDefinitions.Where(x=>x.State==SmartAlertState.Armed)){if(!IsTriggered(x,s.ObservedPrice.Value))continue;x.State=SmartAlertState.Triggered;x.TriggeredAtUtc=s.ObservedAtUtc;ev.Add(CreateAlertEvent(x,s.ObservedPrice.Value,s.ObservedAtUtc));}return ev;}
    private static IReadOnlyList<string> ConfigureAlerts(SmartPositionState st,SmartPositionSnapshot s,SmartPositionSettings set,bool rearm){var d=new List<string>();ConfigureAlert(st,SmartAlertType.PartialProfit,set.PartialProfitTriggerPrice,false,rearm,d);ConfigureAlert(st,SmartAlertType.BreakEven,set.BreakEvenTriggerPrice,false,rearm,d);double? stop=set.StopLossTriggerPrice;var virtualSl=false;if(!stop.HasValue&&s.StopLoss.HasValue)stop=s.StopLoss;else if(!stop.HasValue&&!s.StopLoss.HasValue&&set.VirtualStopLossPrice.HasValue){stop=set.VirtualStopLossPrice;virtualSl=true;}ConfigureAlert(st,SmartAlertType.StopLoss,stop,virtualSl,rearm,d);return d;}
    private static void ConfigureAlert(SmartPositionState st,SmartAlertType type,double? trigger,bool virtualSl,bool rearm,ICollection<string>d){var x=st.AlertDefinitions.FirstOrDefault(a=>a.AlertType==type);if(!trigger.HasValue){if(rearm&&x!=null){x.State=SmartAlertState.Armed;x.TriggeredAtUtc=null;}return;}if(!IsFinitePositive(trigger.Value)||!IsValidTrigger(type,st.Direction,st.EntryPrice,trigger.Value)){d.Add($"{type} alert trigger is invalid for the position and was not armed.");return;}if(x==null){st.AlertDefinitions.Add(new SmartAlertDefinition{AlertId=BuildAlertId(st.PositionId,type,st.MonitoringStartedAtUtc),PositionId=st.PositionId,SymbolName=st.SymbolName,Direction=st.Direction,AlertType=type,TriggerPrice=trigger.Value,IsVirtualStopLoss=type==SmartAlertType.StopLoss&&virtualSl,State=SmartAlertState.Armed,CreatedAtUtc=st.MonitoringStartedAtUtc});return;}if(!rearm)return;x.TriggerPrice=trigger.Value;x.IsVirtualStopLoss=type==SmartAlertType.StopLoss&&virtualSl;x.State=SmartAlertState.Armed;x.TriggeredAtUtc=null;}
    private static bool IsTriggered(SmartAlertDefinition d,double q)=>d.AlertType is SmartAlertType.PartialProfit or SmartAlertType.BreakEven?IsFavorableTriggerReached(d.Direction,q,d.TriggerPrice):(d.Direction==SmartPositionDirection.Long?q<=d.TriggerPrice:q>=d.TriggerPrice);
    private static SmartAlertEvent CreateAlertEvent(SmartAlertDefinition d,double q,DateTime t)=>new(){EventId=$"{d.AlertId}:{t.Ticks}",AlertId=d.AlertId,PositionId=d.PositionId,SymbolName=d.SymbolName,Direction=d.Direction,AlertType=d.AlertType,TriggerPrice=d.TriggerPrice,ObservedPrice=q,TriggeredAtUtc=t,RequestedAction="None",ActionResult="MonitoringOnly"};
    private static string BuildAlertId(int id,SmartAlertType t,DateTime c)=>$"{id}:{t}:{c.Ticks}";
    private static string BuildStopActionId(int id,SmartStopActionReason r,double x,DateTime t)=>$"{id}:{r}:{x.ToString("R",CultureInfo.InvariantCulture)}:{t.Ticks}";
    private static string BuildPartialProfitActionId(int id,int k,double x,DateTime t)=>$"{id}:PP:{k}:{x.ToString("R",CultureInfo.InvariantCulture)}:{t.Ticks}";
    private static bool IsValidTrigger(SmartAlertType t,SmartPositionDirection d,double e,double x)=>t==SmartAlertType.StopLoss||IsValidFavorableTrigger(d,e,x);
    private static bool IsValidInitialStopLoss(SmartPositionDirection d,double e,double x)=>IsFinitePositive(x)&&(d==SmartPositionDirection.Long?x<e:x>e);
    private static bool IsValidFavorableTrigger(SmartPositionDirection d,double e,double x)=>d==SmartPositionDirection.Long?x>e:x<e;
    private static bool IsFavorableTriggerReached(SmartPositionDirection d,double q,double x)=>d==SmartPositionDirection.Long?q>=x:q<=x;
    private static bool ImprovesProtection(SmartPositionDirection d,double? c,double x,double e)=>!c.HasValue||(d==SmartPositionDirection.Long?x>c.Value+e:x<c.Value-e);
    private static bool IsAtOrBetterProtection(SmartPositionDirection d,double? c,double x,double e)=>c.HasValue&&(d==SmartPositionDirection.Long?c.Value>=x-e:c.Value<=x+e);
    private static bool IsExecutableStopCandidate(SmartPositionDirection d,double x,double q,double e)=>d==SmartPositionDirection.Long?x<q-e:x>q+e;
    private static double DirectionSign(SmartPositionDirection d)=>SmartManagementSemantics.DirectionSign(d);
    private static bool IsFiniteNullablePositive(double? v)=>!v.HasValue||IsFinitePositive(v.Value);private static bool IsFiniteNonNegative(double v)=>v>=0&&!double.IsNaN(v)&&!double.IsInfinity(v);private static bool IsFinitePositive(double v)=>v>0&&!double.IsNaN(v)&&!double.IsInfinity(v);

    private static SmartPositionState Clone(SmartPositionState st)=>new(){PositionId=st.PositionId,SymbolName=st.SymbolName,Direction=st.Direction,EntryPrice=st.EntryPrice,OriginalVolumeInUnits=st.OriginalVolumeInUnits,CurrentVolumeInUnits=st.CurrentVolumeInUnits,InitialStopLoss=st.InitialStopLoss,InitialTakeProfit=st.InitialTakeProfit,CurrentStopLoss=st.CurrentStopLoss,CurrentTakeProfit=st.CurrentTakeProfit,LastReconciledAtUtc=st.LastReconciledAtUtc,MonitoringStartedAtUtc=st.MonitoringStartedAtUtc,Phase=st.Phase,PhaseChangedAtUtc=st.PhaseChangedAtUtc,StopManagement=CloneStopManagement(st.StopManagement),PendingStopAction=ClonePendingStopAction(st.PendingStopAction),PartialProfit=ClonePartialProfit(st.PartialProfit),PendingPartialProfitAction=ClonePendingPartialProfitAction(st.PendingPartialProfitAction),FirstPartialProfitCompleted=st.FirstPartialProfitCompleted,FirstPartialProfitCompletedAtUtc=st.FirstPartialProfitCompletedAtUtc,CompletedPartialProfitStageCount=st.CompletedPartialProfitStageCount,AlertDefinitions=(st.AlertDefinitions??new List<SmartAlertDefinition>()).Select(CloneAlert).ToList()};
    private static SmartStopManagementPlan CloneStopManagement(SmartStopManagementPlan? p){p??=new SmartStopManagementPlan();return new SmartStopManagementPlan{Mode=p.Mode,Enabled=p.Enabled,PreBreakEvenTrailingEnabled=p.PreBreakEvenTrailingEnabled,BreakEvenEnabled=p.BreakEvenEnabled,PostBreakEvenTrailingEnabled=p.PostBreakEvenTrailingEnabled,BreakEvenTriggerPrice=p.BreakEvenTriggerPrice,BreakEvenAdjustmentPriceDistance=p.BreakEvenAdjustmentPriceDistance,PreBreakEvenTrailingPriceDistance=p.PreBreakEvenTrailingPriceDistance,PostBreakEvenTrailingPriceDistance=p.PostBreakEvenTrailingPriceDistance,StopImprovementEpsilon=p.StopImprovementEpsilon};}
    private static SmartPendingStopAction? ClonePendingStopAction(SmartPendingStopAction? p)=>p==null?null:new SmartPendingStopAction{ActionId=p.ActionId,Reason=p.Reason,RequestedStopPrice=p.RequestedStopPrice,TargetPhase=p.TargetPhase,RequestedAtUtc=p.RequestedAtUtc,ExecutionStatus=p.ExecutionStatus,DiagnosticError=p.DiagnosticError};
    private static SmartPartialProfitPlan ClonePartialProfit(SmartPartialProfitPlan? p){p??=new SmartPartialProfitPlan();return new SmartPartialProfitPlan{Mode=p.Mode,Enabled=p.Enabled,MultiEnabled=p.MultiEnabled,StageSpacingPriceDistance=p.StageSpacingPriceDistance,ClosePercent=p.ClosePercent,MaximumStages=p.MaximumStages};}
    private static SmartPendingPartialProfitAction? ClonePendingPartialProfitAction(SmartPendingPartialProfitAction? p)=>p==null?null:new SmartPendingPartialProfitAction{ActionId=p.ActionId,StageIndex=p.StageIndex,BrokerVolumeAtRequest=p.BrokerVolumeAtRequest,RequestedCloseVolumeInUnits=p.RequestedCloseVolumeInUnits,RequestedAtUtc=p.RequestedAtUtc,ExecutionStatus=p.ExecutionStatus,DiagnosticError=p.DiagnosticError};
    private static SmartAlertDefinition CloneAlert(SmartAlertDefinition d)=>new(){AlertId=d.AlertId,PositionId=d.PositionId,SymbolName=d.SymbolName,Direction=d.Direction,AlertType=d.AlertType,TriggerPrice=d.TriggerPrice,IsVirtualStopLoss=d.IsVirtualStopLoss,State=d.State,CreatedAtUtc=d.CreatedAtUtc,TriggeredAtUtc=d.TriggeredAtUtc};
}
