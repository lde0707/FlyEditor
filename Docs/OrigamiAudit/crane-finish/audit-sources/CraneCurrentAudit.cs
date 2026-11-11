using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FlyEditor.Flight;
namespace FlyEditor.Editor
{
    // Diagram coordinates belong to this audit only. Production fold solvers do
    // not know this model, its stage number, or any of these material positions.
    public static class CraneCurrentAudit
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
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("[CraneCurrent] "+message);}
        private static void Capture(string name,PaperCornerPull preview=null)
        {
            Render(paper,name,preview);
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
            foreach(int sign in new[]{1,-1})for(int i=0;i<paper.Faces.Count;i++){
                var f=paper.Faces[i];if(!f.Contains(a)||!f.Contains(b))continue;
                Vector3 cm=default,dm=default;bool hasC=false,hasD=false;
                foreach(var other in paper.Faces){if(other.Contains(c)){cm=Material(other,c);hasC=true;}if(other.Contains(d)){dm=Material(other,d);hasD=true;}}
                if(!hasC||!hasD)continue;
                var step=new FoldStep{cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,regionalPull=true,coupledHinges=true,adaptiveCreases=true,coordinatedCreases=true,compactLayers=true,anchor=i,flap=i+1,pullTip=a,pullStop=b,pullLeft=Local(f,cm),pullRight=Local(f,dm)};
                var fold=PaperCornerPull.Create(paper,step);if(fold==null||!fold.UsesEdgeMatch)continue;
                fold.TryMove(0,sign*180,out float reached);if(Mathf.Abs(reached-sign*180)>.001f)continue;
                if(paper.FinishPull(fold,sign*180))return true;
            }return false;
        }
        private static void ProbeNext()
        {
            foreach(int book in new[]{0,1,-1}){
                var trial=PaperSheet.Replay(paper.Recipe());
                if(book!=0&&!trial.Finish(trial.Plan(new FoldStep{creaseNormal=new Vector2(1,-1),side=1,compactLayers=true}),book*180)){result.attempts.Add("Book fold "+book+": blocked");continue;}
                foreach(var tip in new[]{Vector2.zero,new Vector2(11,11)}){
                    int candidates=0;float best=0;var vertices=new List<PaperSheet.FoldVertex>();trial.CollectVertices(vertices);
                    var stops=new List<Vector3>();for(int sample=1;sample<64;sample++){float k=5.5f*sample/64f;float m=tip==Vector2.zero?k:11-k;stops.Add(new Vector3(m,0,m));}foreach(var v in vertices){var m=Material(trial.Faces[v.anchor],v.point);
                        if(Mathf.Abs(m.x-m.z)>.001f||Vector2.Distance(new Vector2(m.x,m.z),tip)<.1f||Mathf.Abs(m.x-5.5f)<.1f)continue;
                        if((tip==Vector2.zero&&m.x>=5.5f)||(tip!=Vector2.zero&&m.x<=5.5f))continue;
                        if(!stops.Contains(m))stops.Add(m);
                    }
                    foreach(var stop in stops)for(int i=0;i<trial.Faces.Count;i++){
                        var f=trial.Faces[i];var step=new FoldStep{insideReverse=true,cornerPull=true,anchor=i,flap=i+1,pullTip=Local(f,new Vector3(tip.x,0,tip.y)),pullStop=Local(f,stop)};
                        var fold=PaperInsideReverse.Create(trial,step);if(fold==null)continue;candidates++;
                        for(float angle=2;angle<=180;angle+=2){if(!fold.Pose(angle))break;best=Mathf.Max(best,angle);}
                    }
                    result.attempts.Add("Neck/tail material tip "+tip+", book="+book+", free and existing stops="+stops.Count+", candidates="+candidates+", maximum progress="+best+"/180");
                }
            }
        }
        private static PaperCornerPull CurrentPlan(PaperSheet s,Vector2 t,Vector2 h,Vector2 l,Vector2 r,int sign,bool petal){
            for(int i=0;i<s.Faces.Count;i++){
                var f=s.Faces[i];var a=Local(f,new Vector3(t.x,0,t.y));if(!f.Contains(a))continue;
                var step=new FoldStep{cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,regionalPull=true,coupledHinges=true,adaptiveCreases=true,coordinatedCreases=true,compactLayers=true,anchor=i,flap=i+1,pullTip=a,pullStop=Local(f,new Vector3(h.x,0,h.y)),pullLeft=Local(f,new Vector3(l.x,0,l.y)),pullRight=Local(f,new Vector3(r.x,0,r.y))};
                var p=PaperCornerPull.Create(s,step);if(p==null||p.UsesPetalFold!=petal)continue;p.TryMove(0,sign*180,out var reached);if(Mathf.Abs(reached-sign*180)<.001f)return p;
            }return null;
        }
        private static PaperSheet CurrentSquare(){
            var s=new PaperSheet();Require(s.ApplyStep(new FoldStep{creaseNormal=Vector2.one,creaseOffset=11,side=1,compactLayers=true}),"first diagonal");Require(s.ApplyStep(new FoldStep{creaseNormal=new Vector2(1,-1),side=-1,compactLayers=true}),"second diagonal");
            var h=Vector2.one*5.5f;
            Require(s.FinishPull(CurrentPlan(s,new Vector2(0,11),h,new Vector2(0,5.5f),new Vector2(5.5f,11),1,false),180),"front squash");
            Require(s.FinishPull(CurrentPlan(s,new Vector2(11,0),h,new Vector2(5.5f,0),new Vector2(11,5.5f),-1,false),-180),"back squash");return s;
        }
    static void Render(PaperSheet paper,string name,PaperCornerPull pull=null)
    {
        var scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("Audit paper");var cameraGo=new GameObject("Audit camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
        var mesh=new Mesh();var type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldMesh");
        var method=type.GetMethod("Build");var info=method.GetParameters();var args=new object[info.Length];
        for(int i=0;i<args.Length;i++)args[i]=info[i].HasDefaultValue?info[i].DefaultValue:null;
        args[0]=mesh;args[1]=paper;args[2]=null;args[3]=0f;
        var points=new List<PaperSheet.FoldVertex>();args[4]=points;args[10]=pull;
        args[18]=Vector3.zero;
        method.Invoke(Activator.CreateInstance(type,true),args);mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var material=new Material(Shader.Find("FlyEditor/PaperFlight"));go.AddComponent<MeshRenderer>().sharedMaterial=material;
        var cam=cameraGo.AddComponent<Camera>();cam.cameraType=CameraType.Preview;cam.orthographic=true;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.91f,.94f,.92f);
        var center=mesh.bounds.center;cam.transform.position=center+new Vector3(2,16,-12);cam.transform.LookAt(center);cam.orthographicSize=Mathf.Max(4f,mesh.bounds.size.magnitude*.58f);
        cam.nearClipPlane=.1f;cam.farClipPlane=100;cam.scene=scene;
        var rt=new RenderTexture(1000,760,24);var tex=new Texture2D(1000,760,TextureFormat.RGB24,false);var old=RenderTexture.active;
        try {cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1000,760),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),tex.EncodeToPNG());}
        finally {RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);EditorSceneManager.ClosePreviewScene(scene);}
    }
        [MenuItem("Tools/FlyEditor/Validation/Current crane check")]
        public static void Run()
        {
            folder=Environment.GetEnvironmentVariable("FLYEDITOR_CRANE_CURRENT");
            if(string.IsNullOrEmpty(folder))folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/results-crane-current"));
            Directory.CreateDirectory(folder);result=new Result();paper=CurrentSquare();Capture("04-square-base");
            result.actions.Add("PDF 1-6: two shape folds and two four-point pocket squashes. No precreases.");
            var h=Vector2.one*5.5f;
            var front=CurrentPlan(paper,new Vector2(0,11),h,new Vector2(0,5.5f),new Vector2(5.5f,11),1,true);
            Require(front!=null,"front petal plan");Require(front.Pose(90),"front midway");Capture("05-petal-midway",front);Require(paper.FinishPull(front,180),"front close");Geometry();Capture("05-front-petal");
            var back=CurrentPlan(paper,new Vector2(11,0),h,new Vector2(5.5f,0),new Vector2(11,5.5f),-1,true);
            Require(back!=null&&paper.FinishPull(back,-180),"back close");Geometry();Capture("06-bird-base");result.actions.Add("PDF 9-12: exact front and back petals; marking-only PDF 7-8/11 folds omitted.");
            for(int i=0;i<4;i++){Require(Narrow(i%2==0),"narrow edge "+i);Geometry();Capture((7+i).ToString("00")+"-narrow");}
            result.actions.Add("PDF 13-15: four edge-to-line folds using freely placed surface handles; exact bisectors calculated by the common solver.");
            ProbeNext();result.committedFolds=paper.Count;result.lastVerifiedStage="PDF 15 / narrowed bird base";result.nextStage="PDF 16-17 / raise and reverse the multi-layer neck and tail; not completed";
            File.WriteAllText(Path.Combine(folder,"result.json"),JsonUtility.ToJson(result,true));
            Debug.Log("[CraneCurrent] Verified "+paper.Count+" folds without precreases. Crane NOT complete; neck/tail remains blocked. "+folder);
            foreach(var attempt in result.attempts)Debug.Log("[CraneCurrent] "+attempt);
        }
    }
}
