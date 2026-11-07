using UnityEngine;

namespace FlyEditor.Flight
{
    public sealed class PaperFlightProfile
    {
        public static readonly PaperFlightProfile Standard=new PaperFlightProfile(.5f,.5f,.5f,.5f,.5f);
        public readonly float launch,momentum,descent,climb,impact;

        internal PaperFlightProfile(float launch,float momentum,float descent,float climb,float impact)
        {
            this.launch=Factor(launch);this.momentum=Factor(momentum);this.descent=Factor(descent);
            this.climb=Factor(climb);this.impact=Factor(impact);
        }
        // The default plane is the midpoint; best / worst = (7/6) / (5/6) = 1.4.
        private static float Factor(float score)=>1f+(Mathf.Clamp01(score)-.5f)/3f;

        public static PaperFlightProfile Measure(PaperSheet sheet)=>sheet.Count==0?Standard:
            AircraftHold.Measure(sheet).MeasurePerformance(sheet);

        internal void Apply(FlightTuning tuning)
        {
            tuning.paperProfile=this;
            if(this==Standard)return;
            tuning.launchSpeed*=launch;
            tuning.speedCap=Mathf.Max(tuning.speedCap,tuning.launchSpeed);
            tuning.drag/=momentum;
            tuning.sink*=descent;
            tuning.divePitch=VerticalPitch(tuning.divePitch,descent);
            tuning.climbPitch=VerticalPitch(tuning.climbPitch,climb);
        }
        private static float VerticalPitch(float degrees,float factor)=>
            Mathf.Asin(Mathf.Clamp(Mathf.Sin(degrees*Mathf.Deg2Rad)*factor,-1f,1f))*Mathf.Rad2Deg;
    }
}
