using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FlyEditor.Flight;
namespace FlyEditor.Editor
{
    // Diagram coordinates belong to this audit only. Production fold solvers do
    // not know this model, its stage number, or any of these material positions.
    public static class CranePetalAudit
    {
        [Serializable] private sealed class Result
        {
            public string reference="https://californiamuseum.org/wp-content/uploads/CAM_Uprooted_CraneInsturctions.pdf";
            public bool craneCompleted;
            public int precreases,committedFolds;
            public string lastVerifiedStage,nextStage;
            public List<string> actions=new List<string>(),attempts=new List<string>();
        }
        private static PaperSheet paper;
        private static Result result;
        private static string folder;
        private static readonly BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;
        private static Vector3 Material(PaperSheet.Face f,Vector2 p)=>f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
        private static Vector2 Local(PaperSheet.Face f,Vector3 m){var p=f.material.inverse.MultiplyPoint3x4(m);return new Vector2(p.x,p.z);}
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("[CranePetal] "+message);}
        private static void Capture(string name,PaperCornerPull preview=null)
        {
            typeof(OrigamiFeasibilityAudit).GetField("folder",Hidden).SetValue(null,folder);
            typeof(OrigamiFeasibilityAudit).GetMethod("Render",Hidden).Invoke(null,new object[]{paper,name,preview});
            if(preview==null)File.WriteAllText(Path.Combine(folder,name+".json"),JsonUtility.ToJson(new ResearchSave{paperFolds=paper.Recipe()},true));
        }
        private static void Geometry()
        {
            float area=0;var seams=new Dictionary<Vector2Int,Vector3>();
            foreach(var f in paper.Faces){area+=PaperSheet.Area(f.points);foreach(var p in f.points){
                var m=Material(f,p);var key=new Vector2Int(Mathf.RoundToInt(m.x*10000),Mathf.RoundToInt(m.z*10000));
                // Completed flat folds are represented by a common midsurface
                // plus integer shell layers. Do not confuse thickness with tears.
                var world=f.World(p,0);Require(float.IsFinite(world.x)&&float.IsFinite(world.y)&&float.IsFinite(world.z),"nonfinite vertex");
                if(seams.TryGetValue(key,out var old))Require(Vector3.Distance(world,old)<.002f,"material seam separated");else seams.Add(key,world);
            }}
            Require(Mathf.Abs(area-paper.Width*paper.Height)<.01f,"material area changed");PetalFoldingVerification.Replay(paper);
        }
        private static bool Narrow(bool left)
        {
            var a=Vector2.zero;var b=left?new Vector2(5.5f*(Mathf.Sqrt(2)-1),5.5f):new Vector2(5.5f,5.5f*(Mathf.Sqrt(2)-1));
            var c=Vector2.one*(5.5f/Mathf.Sqrt(2));var d=Vector2.one*(5.5f*Mathf.Sqrt(2));
            var vertices=new List<PaperSheet.FoldVertex>();paper.CollectVertices(vertices);
            foreach(var target in new[]{a,b,c,d}){bool found=false;foreach(var v in vertices)found|=(v.point-target).sqrMagnitude<.00001f;Require(found,"alignment requires a nonexistent marker");}
            foreach(int sign in new[]{1,-1})for(int i=0;i<paper.Faces.Count;i++){
                var f=paper.Faces[i];if(!f.Contains(a)||!f.Contains(b))continue;
                Vector3 cm=default,dm=default;bool hasC=false,hasD=false;
                foreach(var other in paper.Faces){if(other.Contains(c)){cm=Material(other,c);hasC=true;}if(other.Contains(d)){dm=Material(other,d);hasD=true;}}
                if(!hasC||!hasD)continue;
                var step=new FoldStep{cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,regionalPull=true,coupledHinges=true,adaptiveCreases=true,anchor=i,flap=i+1,pullTip=a,pullStop=b,pullLeft=Local(f,cm),pullRight=Local(f,dm)};
                var fold=PaperCornerPull.Create(paper,step);if(fold==null||!fold.UsesEdgeMatch)continue;
                fold.TryMove(0,sign*180,out float reached);if(Mathf.Abs(reached-sign*180)>.001f)continue;
                if(paper.FinishPull(fold,sign*180))return true;
            }return false;
        }
        private static void ProbeNext()
        {
            foreach(int book in new[]{0,1,-1}){
                var trial=PaperSheet.Replay(paper.Recipe());
                if(book!=0&&!trial.Finish(trial.Plan(new FoldStep{creaseNormal=new Vector2(1,-1),side=1}),book*180)){result.attempts.Add("Book fold "+book+": blocked");continue;}
                foreach(var tip in new[]{Vector2.zero,new Vector2(11,11)}){
                    int candidates=0;float best=0;var vertices=new List<PaperSheet.FoldVertex>();trial.CollectVertices(vertices);
                    var stops=new List<Vector3>();foreach(var v in vertices){var m=Material(trial.Faces[v.anchor],v.point);
                        if(Mathf.Abs(m.x-m.z)>.001f||Vector2.Distance(new Vector2(m.x,m.z),tip)<.1f||Mathf.Abs(m.x-5.5f)<.1f)continue;
                        if((tip==Vector2.zero&&m.x>=5.5f)||(tip!=Vector2.zero&&m.x<=5.5f))continue;
                        if(!stops.Contains(m))stops.Add(m);
                    }
                    foreach(var stop in stops)for(int i=0;i<trial.Faces.Count;i++){
                        var f=trial.Faces[i];var step=new FoldStep{insideReverse=true,cornerPull=true,anchor=i,flap=i+1,pullTip=Local(f,new Vector3(tip.x,0,tip.y)),pullStop=Local(f,stop)};
                        var fold=PaperInsideReverse.Create(trial,step);if(fold==null)continue;candidates++;
                        for(float angle=2;angle<=180;angle+=2){if(!fold.Pose(angle))break;best=Mathf.Max(best,angle);}
                    }
                    result.attempts.Add("Neck/tail material tip "+tip+", book="+book+", existing stops="+stops.Count+", candidates="+candidates+", maximum progress="+best+"/180");
                }
            }
        }
        [MenuItem("Tools/FlyEditor/Validation/Crane petal audit")]
        public static void Run()
        {
            folder=Environment.GetEnvironmentVariable("FLYEDITOR_PETAL_AUDIT");
            if(string.IsNullOrEmpty(folder))folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/results-petal"));
            Directory.CreateDirectory(folder);result=new Result();paper=PetalFoldingVerification.Square();Capture("04-square-base");
            result.actions.Add("PDF 1-6: two shape folds and two four-point pocket squashes. No precreases.");
            var h=Vector2.one*5.5f;
            var front=PetalFoldingVerification.Plan(paper,new Vector2(0,11),h,new Vector2(0,5.5f),new Vector2(5.5f,11),1,true);
            Require(front!=null,"front petal plan");Require(front.Pose(90),"front midway");Capture("05-petal-midway",front);Require(paper.FinishPull(front,180),"front close");Geometry();Capture("05-front-petal");
            var back=PetalFoldingVerification.Plan(paper,new Vector2(11,0),h,new Vector2(5.5f,0),new Vector2(11,5.5f),-1,true);
            Require(back!=null&&paper.FinishPull(back,-180),"back close");Geometry();Capture("06-bird-base");result.actions.Add("PDF 9-12: exact front and back petals; marking-only PDF 7-8/11 folds omitted.");
            for(int i=0;i<4;i++){Require(Narrow(i%2==0),"narrow edge "+i);Geometry();Capture((7+i).ToString("00")+"-narrow");}
            result.actions.Add("PDF 13-15: four edge-to-line folds using existing handles; exact bisectors calculated by the common solver.");
            ProbeNext();result.committedFolds=paper.Count;result.lastVerifiedStage="PDF 15 / narrowed bird base";result.nextStage="PDF 16-17 / raise and reverse the multi-layer neck and tail; not completed";
            File.WriteAllText(Path.Combine(folder,"result.json"),JsonUtility.ToJson(result,true));
            Debug.Log("[CranePetal] Verified "+paper.Count+" folds without precreases. Crane NOT complete; neck/tail remains blocked. "+folder);
            foreach(var attempt in result.attempts)Debug.Log("[CranePetal] "+attempt);
        }
    }
}
