using System;
namespace PropRiskManager.Domain;
public enum SmartManagementMode { Points, Percentage }
public static class SmartManagementSemantics
{
    public const int MaximumMultiPartialProfitStages=5;
    public static double ProfitReferenceDistance(double e,double t){Positive(e,nameof(e));Positive(t,nameof(t));var d=Math.Abs(t-e);if(d<=0)throw new ArgumentOutOfRangeException(nameof(t));return d;}
    public static double RiskReferenceDistance(double e,double s){Positive(e,nameof(e));Positive(s,nameof(s));var d=Math.Abs(e-s);if(d<=0)throw new ArgumentOutOfRangeException(nameof(s));return d;}
    public static double FavorablePercentageTrigger(SmartPositionDirection d,double e,double t,double p){Pct(p,nameof(p));return e+DirectionSign(d)*ProfitReferenceDistance(e,t)*p/100.0;}
    public static double PercentageStopDistance(double e,double s,double p){Pct(p,nameof(p));return RiskReferenceDistance(e,s)*p/100.0;}
    public static double PercentageTrailingStop(SmartPositionDirection d,double q,double e,double s,double p){Positive(q,nameof(q));return q-DirectionSign(d)*PercentageStopDistance(e,s,p);}
    public static int PercentageMultiPartialStageCount(double p){Pct(p,nameof(p));if(p>100)return 0;return Math.Min(MaximumMultiPartialProfitStages,(int)Math.Floor(100.0/p+1e-12));}
    public static double DirectionSign(SmartPositionDirection d)=>d==SmartPositionDirection.Long?1.0:-1.0;
    private static void Pct(double v,string n){if(!(v>0)||double.IsNaN(v)||double.IsInfinity(v))throw new ArgumentOutOfRangeException(n);}private static void Positive(double v,string n){if(!(v>0)||double.IsNaN(v)||double.IsInfinity(v))throw new ArgumentOutOfRangeException(n);}
}
