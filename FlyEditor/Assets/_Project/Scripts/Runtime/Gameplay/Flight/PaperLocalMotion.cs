using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // Material constraints and contact ordering are solved near the picked
    // sheet's origin. A rigid scene transform cannot change the folding result.
    internal sealed class PaperLocalMotion
    {
        private readonly PaperSheet sheet;
        private readonly IReadOnlyList<PaperSheet.Face> source;
        private readonly PaperCornerPull plan;
        private readonly Matrix4x4 reference;
        private readonly List<PaperSheet.Face> preview=new List<PaperSheet.Face>();
        internal FoldStep Step=>plan.Step;
        internal float AngleLimit=>plan.AngleLimit;
        internal IReadOnlyList<PaperSheet.Face> Preview=>preview;
        internal Vector3 Tip=>reference.MultiplyPoint3x4(plan.Tip);
        internal Vector3 Stop=>reference.MultiplyPoint3x4(plan.Stop);
        internal string Failure=>plan.LastFailure;
        internal bool HingeHints=>plan.ResolvedHingeHints;
        private PaperLocalMotion(PaperSheet sheet,PaperCornerPull plan,Matrix4x4 reference)
        {this.sheet=sheet;source=sheet.Faces;this.plan=plan;this.reference=reference;}
        internal static PaperLocalMotion Create(PaperSheet sheet,FoldStep step)
        {
            var reference=sheet.Faces[step.anchor].frame;
            var local=sheet.LocalPlanningCopy(reference);
            var plan=PaperCornerPull.CreateCanonical(local,step);
            // Only this support solver is accepted here. It derives joints from
            // current material geometry, independent of historical world frames.
            return plan!=null&&plan.UsesCoordinatedSurface?new PaperLocalMotion(sheet,plan,reference):null;
        }
        internal bool Pose(float angle)
        {
            if(sheet.Faces!=source||!plan.Pose(angle))return false;
            preview.Clear();foreach(var f in plan.Preview)preview.Add(new PaperSheet.Face(f.points,f.layer,f.back,reference*f.frame,f.material,f));
            return true;
        }
        internal int MotionRole(PaperSheet.Face face)=>plan.MotionRole(face);
        internal void MarkGuides(List<PaperSheet.Guide> guides)
        {
            plan.MarkGuides(guides);
            for(int i=0;i<guides.Count;i++){var g=guides[i];var f=g.face;g.face=new PaperSheet.Face(f.points,f.layer,f.back,reference*f.frame,f.material,f);guides[i]=g;}
        }
    }
}
