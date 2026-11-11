using System.Collections.Generic;
using UnityEngine;
namespace FlyEditor.Flight
{
    // Four existing handles describe an edge and its target line. Their shared
    // corner determines the bisector without a preparatory crease or a new dot.
    internal sealed class PaperEdgeMatchFold
    {
        private readonly PaperSheet sheet;
        private readonly IReadOnlyList<PaperSheet.Face> source;
        private readonly PaperSheet.Turn turn;
        private readonly PaperSheet.Face basis;
        private readonly Vector2 handle;
        private float lastAngle=float.NaN;
        private readonly List<PaperSheet.Face> preview=new List<PaperSheet.Face>(),trial=new List<PaperSheet.Face>();
        public IReadOnlyList<PaperSheet.Face> Preview=>preview;
        public Vector3 Tip {get;private set;}
        public Vector3 Stop {get;private set;}
        private PaperEdgeMatchFold(PaperSheet sheet,PaperSheet.Turn turn,PaperSheet.Face basis,Vector2 handle)
        {this.sheet=sheet;source=sheet.Faces;this.turn=turn;this.basis=basis;this.handle=handle;}
        private static bool Locate(PaperSheet sheet,PaperSheet.Face basis,Vector2 original,bool continuous,out Vector2 result)
        {
            if(!float.IsFinite(original.x)||!float.IsFinite(original.y)){result=default;return false;}
            var material=basis.Original(original);
            foreach(var f in sheet.Faces){var v=f.material.inverse.MultiplyPoint3x4(material);var p=new Vector2(v.x,v.z);
                if(!f.Contains(p))continue;
                if(!PaperSheet.SameFrame(f,basis)){if(continuous)continue;break;}result=p;return true;}
            result=default;return false;
        }
        public static PaperEdgeMatchFold Create(PaperSheet sheet,FoldStep step)
        {
            if(!step.adaptiveCreases||!step.customFlanks||(!step.regionalPull&&!(step.continuousHinge&&step.flap==0)))return null;
            var basis=sheet.Faces[step.anchor];
            if(!Locate(sheet,basis,step.pullTip,step.continuousHinge,out var a)||!Locate(sheet,basis,step.pullStop,step.continuousHinge,out var b)||
               !Locate(sheet,basis,step.pullLeft,step.continuousHinge,out var c)||!Locate(sheet,basis,step.pullRight,step.continuousHinge,out var d))return null;
            var target=(d-c).normalized;
            if((d-c).sqrMagnitude<.01f||Mathf.Abs(target.x*(a-c).y-target.y*(a-c).x)>.001f)return null;
            if(Vector2.Dot(target,(c+d)*.5f-a)<0)target=-target;
            var edge=(b-a).normalized;float dot=Vector2.Dot(edge,target);
            if(dot>.999f||dot<-.99f)return null;
            var axis=(edge+target).normalized;var n=new Vector2(-axis.y,axis.x);
            var line=new FoldStep{anchor=step.anchor,flap=step.continuousHinge?step.flap:step.anchor+1,continuousHinge=step.continuousHinge,neutralContacts=step.neutralContacts,compactLayers=step.compactLayers,connectedPanel=step.continuousHinge,creaseNormal=n,creaseOffset=Vector2.Dot(n,a),side=Vector2.Dot(n,b-a)>0?1:-1};
            var turn=sheet.Plan(line);if(turn==null)return null;
            var plan=new PaperEdgeMatchFold(sheet,turn,basis,b){Stop=basis.World(a,step.continuousHinge?0:(basis.layer+.5f)*PaperSheet.Thickness)};
            return plan;
        }
        public void MarkGuides(List<PaperSheet.Guide> guides)
        {
            guides.Clear();foreach(var f in turn.movingFaces)
                if(PaperSheet.Intersection(f,turn.step,out var a,out var b))
                    guides.Add(new PaperSheet.Guide{face=f,layer=f.layer,line=turn.step,a=a,b=b});
        }
        public bool Pose(float angle)
        {
            if(sheet.Faces!=source||!float.IsFinite(angle)||Mathf.Abs(angle)>180||Mathf.Abs(turn.Constrain(angle)-angle)>.001f)return false;
            if(angle==lastAngle)return true;
            var rotation=turn.Rotation(angle);
            if(turn.step.continuousHinge){
                if(!sheet.TryPreviewTurn(turn,angle,trial))return false;
                preview.Clear();preview.AddRange(trial);
                lastAngle=angle;Tip=rotation.MultiplyPoint3x4(basis.World(handle,0));return true;
            }
            preview.Clear();preview.AddRange(turn.fixedFaces);
            foreach(var f in turn.movingFaces){
                if(Mathf.Abs(angle)==180 && PaperSheet.SameFrame(f,basis))
                    preview.Add(PaperSheet.ReflectFlat(f,turn.step,angle>0?2*turn.top+1-f.layer:2*turn.bottom-1-f.layer));
                else preview.Add(new PaperSheet.Face(f.points,f.layer,f.back,rotation*f.frame,f.material));
            }
            lastAngle=angle;Tip=rotation.MultiplyPoint3x4(basis.World(handle,(basis.layer+.5f)*PaperSheet.Thickness));return true;
        }
    }
}

