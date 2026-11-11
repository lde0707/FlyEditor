using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // One drag and one undo entry. Preparations are ordinary swept hinge turns,
    // followed by the ordinary petal solver. Replay uses the recorded path.
    internal sealed class PaperClearanceSequence
    {
        private readonly PaperSheet source;
        private readonly IReadOnlyList<PaperSheet.Face> original;
        private readonly List<PaperSheet.Turn> turns=new List<PaperSheet.Turn>();
        private readonly List<IReadOnlyList<PaperSheet.Face>> resting=new List<IReadOnlyList<PaperSheet.Face>>();
        private readonly List<PaperSheet.Face> preview=new List<PaperSheet.Face>();
        private readonly Vector3[] handles;
        private readonly int direction;
        private readonly Matrix4x4 reference;
        private PaperCornerPull petal;
        private float travel=180;
        internal IReadOnlyList<PaperSheet.Face> Preview=>preview;
        internal Vector3 Tip {get;private set;}
        internal Vector3 Stop {get;private set;}
        private PaperClearanceSequence(PaperSheet source,FoldStep step)
        {this.source=source;original=source.Faces;handles=PaperFoldClearance.Handles(source,step);direction=step.clearanceDirection;reference=source.Faces[step.anchor].frame;}
        internal static PaperClearanceSequence Create(PaperSheet source,FoldStep step)
        {
            if(step.clearance==null||step.clearance.Length==0||step.clearance.Length>3||Mathf.Abs(step.clearanceDirection)!=1)return null;
            var plan=new PaperClearanceSequence(source,step);var copy=source.LocalPlanningCopy(plan.reference);
            plan.resting.Add(copy.Faces);
            foreach(var preparation in step.clearance){
                var st=preparation.Step;if(!float.IsFinite(st.degrees)||Mathf.Abs(st.degrees)!=90&&Mathf.Abs(st.degrees)!=180)return null;
                var turn=copy.Plan(st);if(turn==null||Mathf.Abs(turn.Constrain(st.degrees)-st.degrees)>.001f)return null;
                plan.turns.Add(turn);plan.travel+=Mathf.Abs(st.degrees);
                // Separate stage snapshots keep each Turn.source current.
                var next=copy.PlanningCopy();if(!next.ApplyStep(st))return null;copy=next;plan.resting.Add(copy.Faces);
            }
            if(!PaperFoldClearance.Resolve(copy,step,plan.handles,plan.direction,out plan.petal))return null;
            return plan.Pose(0)?plan:null;
        }
        internal bool Pose(float angle)
        {
            if(source.Faces!=original||!float.IsFinite(angle)||Mathf.Abs(angle)>180||angle*direction<0)return false;
            preview.Clear();
            if(angle==0){preview.AddRange(original);UpdateHandles();return true;}
            float remaining=Mathf.Abs(angle)/180*travel;
            for(int i=0;i<turns.Count;i++){
                var turn=turns[i];float length=Mathf.Abs(turn.step.degrees);
                if(remaining==0){preview.AddRange(resting[i]);ToWorld();return true;}
                if(remaining<length){
                    float degrees=Mathf.Sign(turn.step.degrees)*remaining;
                    if(Mathf.Abs(turn.Constrain(degrees)-degrees)>.001f)return false;
                    preview.AddRange(turn.fixedFaces);var rotation=turn.Rotation(degrees);
                    foreach(var f in turn.movingFaces)preview.Add(new PaperSheet.Face(f.points,f.layer,f.back,rotation*f.frame,f.material));
                    ToWorld();return true;
                }
                remaining-=length;
            }
            if(!petal.Pose(direction*Mathf.Min(180,remaining)))return false;
            preview.AddRange(petal.Preview);ToWorld();return true;
        }
        private void ToWorld()
        {
            for(int i=0;i<preview.Count;i++){
                var f=preview[i];var world=f.frame==Matrix4x4.identity?reference:reference*f.frame;
                preview[i]=new PaperSheet.Face(f.points,f.layer,f.back,world,f.material);
            }UpdateHandles();
        }
        private Vector3 Locate(Vector3 material)
        {
            foreach(var f in preview){var p=PaperFoldTopology.Local(f,material);if(f.Contains(p))return f.World(p,(f.layer+.5f)*PaperSheet.Thickness);}
            return Vector3.zero;
        }
        private void UpdateHandles(){Tip=Locate(handles[0]);Stop=Locate(handles[1]);}
    }
}
