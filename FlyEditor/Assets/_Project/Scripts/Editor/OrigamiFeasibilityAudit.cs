using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FlyEditor.Flight;

// Calls the shipped folding engine. Coordinates are diagram-derived diagnostics,
// not mouse input, and no final-model meshes or replacement faces are injected.
public static class OrigamiFeasibilityAudit
{
    [Serializable] public sealed class Event
    {
        public string model, sourceStep, action, outcome, detail;
        public int commits, panels;
        public float requestedAngle, permittedAngle, materialArea, milliseconds;
        public FoldStep request;
    }
    [Serializable] public sealed class Result
    {
        public string id, source, status="blocked", nextOperation, note;
        public int commits, panels;
        public bool completed;
        public FoldStep[] recipe;
        public List<Event> events=new List<Event>();
    }
    [Serializable] public sealed class Bundle
    {
        public string utc, unity, execution="Coupled pocket hinges with continuous opening and pressing; no marking-only precreases. Engine routes plus separate actual input validation; completion requires shape verification.";
        public int gridPointsPerEdge, foldLimit;
        public List<Result> models=new List<Result>();
    }
    static string folder;
    static Bundle bundle;
    static Result result;
    static PaperSheet sheet;

    [MenuItem("Tools/FlyEditor/Validation/Origami feasibility audit")]
    public static void Run()
    {
        folder=Environment.GetEnvironmentVariable("FLYEDITOR_ORIGAMI_AUDIT");
        if(string.IsNullOrEmpty(folder)) folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/results-pocket"));
        Directory.CreateDirectory(folder);
        bundle=new Bundle{utc=DateTime.UtcNow.ToString("o"),unity=Application.unityVersion,
            gridPointsPerEdge=PaperSheet.GridIntervals+1,foldLimit=PaperSheet.MaxFolds};
        Case("waterbomb","https://origami.me/waterbomb/",Waterbomb);
        Case("frog","https://www.japansociety.org.uk/usercontent/3bc1ed5a28641e243974254376c82e51/Jumping-Frog-Origami-Instructions.pdf",Frog);
        Case("crane","https://californiamuseum.org/wp-content/uploads/CAM_Uprooted_CraneInsturctions.pdf",Crane);
        Case("lily","https://www.origami-fun.com/support-files/origami-lily-print.pdf",Lily);
        Case("dragon","https://origami.me/dragon-advanced/",Dragon);
        File.WriteAllText(Path.Combine(folder,"results.json"),JsonUtility.ToJson(bundle,true));
        Debug.Log("[OrigamiAudit] Recorded five attempts at "+folder);
    }
    static void Case(string id,string source,Action run)
    {
        result=new Result{id=id,source=source};sheet=new PaperSheet();bundle.models.Add(result);
        try { run(); }
        catch(Exception e) { result.note="Harness error: "+e;result.status="harness-error";Debug.LogException(e); }
        result.commits=sheet.Count;result.panels=sheet.Faces.Count;result.recipe=sheet.Recipe();
        var replay=PaperSheet.Replay(result.recipe);
        Log("verification","recipe replay",replay.Count==sheet.Count?"pass":"fail", "replayed commits="+replay.Count);
        DumpGeometry();Render(sheet,result.id+"-stopped");
        Debug.Log("[OrigamiAudit] "+id+" "+result.status+" commits="+sheet.Count+" next="+result.nextOperation);
    }
    static Event Log(string stage,string action,string outcome,string detail,FoldStep step=default,float angle=0,float limit=0,float ms=0)
    {
        var item=new Event{model=result.id,sourceStep=stage,action=action,outcome=outcome,detail=detail,
            commits=sheet.Count,panels=sheet.Faces.Count,materialArea=Area(sheet),request=step,
            requestedAngle=angle,permittedAngle=limit,milliseconds=ms};
        result.events.Add(item);return item;
    }
    static float Area(PaperSheet p)
    {
        float total=0;foreach(var f in p.Faces){float area=0;for(int i=0;i<f.points.Length;i++){
            var a=f.points[i];var b=f.points[(i+1)%f.points.Length];area+=a.x*b.y-a.y*b.x;
        }total+=Mathf.Abs(area)*.5f;}return total;
    }
    static FoldStep Axis(Vector2 a,Vector2 b,int side=1,int flap=0)
    {
        var n=new Vector2(-(b-a).y,(b-a).x).normalized;
        return new FoldStep(CreaseFamily.Vertical,0,side){creaseNormal=n,creaseOffset=Vector2.Dot(n,a),
            flap=flap,anchor=flap>0?flap-1:0,singlePanel=flap>0};
    }
    static bool Fold(string stage,Vector2 a,Vector2 b,int side=1,int flap=0,float angle=180)
    {
        var request=Axis(a,b,side,flap);
        var points=new List<PaperSheet.FoldVertex>();sheet.CollectVertices(points);
        bool hasA=points.Exists(p=>(p.point-a).sqrMagnitude<.00001f), hasB=points.Exists(p=>(p.point-b).sqrMagnitude<.00001f);
        var watch=System.Diagnostics.Stopwatch.StartNew();var turn=sheet.Plan(request);
        float allowed=turn==null?0:turn.Constrain(angle);bool ok=sheet.Finish(turn,angle);watch.Stop();
        string detail="endpoint markers="+hasA+","+hasB+"; "+(turn==null?"Plan returned null":ok?"committed":"angle rejected by contact/commit guard");
        Log(stage,"straight fold",ok?"pass":"blocked",detail,request,angle,allowed,(float)watch.Elapsed.TotalMilliseconds);
        if(!ok){result.nextOperation=stage+": straight fold";ScopeProbe(stage,request,angle);}
        if(ok)Render(sheet,result.id+"-"+result.events.Count.ToString("D2"));
        return ok;
    }
    static void ScopeProbe(string stage,FoldStep original,float angle)
    {
        int planned=0,clear=0,isolated=0;
        for(int i=0;i<sheet.Faces.Count;i++)foreach(bool strict in new[]{true,false}){
            var step=original;step.anchor=i;step.flap=i+1;step.singlePanel=strict;
            var turn=sheet.Plan(step);if(turn==null)continue;planned++;
            if(Mathf.Abs(turn.Constrain(angle)-angle)>.001f)continue;clear++;
            Vector2 contact=Vector2.zero;foreach(var p in sheet.Faces[i].points)contact+=p;contact/=sheet.Faces[i].points.Length;
            if(!turn.MovesOtherPlyAt(contact))isolated++;
        }
        Log(stage,"alternative anchor/scope probe","diagnostic","planned="+planned+"; full requested angle="+clear+"; no other ply at face centroid="+isolated+"; not committed or certified as correct front layer");
    }
    // Compare material locations, so moving the wrong half or undoing a different
    // flap cannot be reported as an unfolded precrease.
    static float ShapeError(PaperSheet actual,PaperSheet expected)
    {
        float error=0;
        foreach(var f in actual.Faces)foreach(var p in f.points){
            Vector3 original=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
            var world=f.World(p,0);bool found=false;float best=float.PositiveInfinity;
            foreach(var target in expected.Faces){
                var q=target.material.inverse.MultiplyPoint3x4(original);var local=new Vector2(q.x,q.z);
                if(!target.Contains(local))continue;
                found=true;best=Mathf.Min(best,Vector3.Distance(world,target.World(local,0)));
            }
            if(!found)return float.PositiveInfinity;error=Mathf.Max(error,best);
        }return error;
    }
    static bool Unfold(string stage,PaperSheet expected)
    {
        var points=new List<PaperSheet.FoldVertex>();sheet.CollectVertices(points);
        var candidates=new List<PaperSheet.Turn>();var tried=new HashSet<string>();
        int plans=0;float best=float.PositiveInfinity;FoldStep chosen=default;bool found=false;
        foreach(bool wholeStack in new[]{false,true}){
          foreach(var point in points){
            sheet.OpeningTurns(point,candidates,wholeStack);
            foreach(var turn in candidates){
                string key=wholeStack+"/"+turn.step.anchor+"/"+turn.step.creaseNormal+"/"+turn.step.creaseOffset+"/"+turn.step.side;
                if(!tried.Add(key))continue;plans++;
                foreach(float angle in new[]{-180f,180f}){
                    if(Mathf.Abs(turn.Constrain(angle)-angle)>.001f)continue;
                    var copy=PaperSheet.Replay(sheet.Recipe());var step=turn.step;step.degrees=angle;
                    if(!copy.ApplyStep(step))continue;
                    float error=ShapeError(copy,expected);best=Mathf.Min(best,error);
                    if(error>.05f)continue;
                    chosen=step;found=true;break;
                }if(found)break;
            }if(found)break;
          }if(found)break;
        }
        bool ok=found && sheet.ApplyStep(chosen);
        Log(stage,"unfold existing seam",ok?"pass":"blocked","candidate hinges="+plans+"; minimum material-position error="+best+"; Undo not used",chosen,chosen.degrees);
        if(!ok)result.nextOperation=stage+": unfold existing seam";
        return ok;
    }
    static bool Precrease(string stage,Vector2 a,Vector2 b,int side=1,int flap=0,float angle=180)
    {
        var before=PaperSheet.Replay(sheet.Recipe());
        return Fold(stage+" fold",a,b,side,flap,angle) && Unfold(stage+" unfold",before);
    }
    static bool FourPrecreases(string stage)
    {
        return Precrease(stage+" horizontal",new Vector2(0,5.5f),new Vector2(11,5.5f)) &&
            Precrease(stage+" vertical",new Vector2(5.5f,0),new Vector2(5.5f,11)) &&
            Precrease(stage+" diagonal /",Vector2.zero,new Vector2(11,11)) &&
            Precrease(stage+" diagonal ",new Vector2(0,11),new Vector2(11,0));
    }
    static void Waterbomb()
    {
        Log("web 1-2","marking-only precreases","skipped","Specify handles directly.");
        if(!FourMaterial("web 3 / first side",new Vector2(0,5.5f),new Vector2(5.5f,5.5f),Vector2.zero,new Vector2(0,11),135))return;
        if(!FourMaterial("web 3 / opposite side",new Vector2(11,5.5f),new Vector2(5.5f,5.5f),new Vector2(11,0),new Vector2(11,11),135))return;
        Uncertified("web 3 base shape","Both handle operations committed; compare the resulting base with the diagram before advancing.");
    }
    static void Lily()
    {
        Log("PDF 1-2","marking-only precreases","skipped","Specify handles directly.");
        if(!FourMaterial("PDF 3 / first side",new Vector2(11,5.5f),new Vector2(5.5f,5.5f),new Vector2(11,0),new Vector2(11,11),135))return;
        if(!FourMaterial("PDF 3 / opposite side",new Vector2(0,5.5f),new Vector2(5.5f,5.5f),Vector2.zero,new Vector2(0,11),135))return;
        Uncertified("PDF 3 base shape","Base geometry must be certified before squash/petal folds; later steps not attempted.");
    }
    static void Frog()
    {
        Log("PDF 1,3-4","marking-only precreases","skipped","Use existing midpoint and quarter markers.");
        if(!Fold("PDF 2",new Vector2(5.5f,0),new Vector2(5.5f,11),-1))return;
        if(!FourMaterial("PDF 5 / head pocket",new Vector2(5.5f,11),new Vector2(5.5f,8.25f),new Vector2(2.75f,11),new Vector2(8.25f,11),180))return;
        Uncertified("PDF 5 head shape","Check the actual triangular head and unchanged lower body; motion success alone is not completion.");
    }
    static void Crane()
    {
        if(!Fold("PDF 1",new Vector2(0,11),new Vector2(11,0)))return;
        if(!Fold("PDF 2",Vector2.zero,new Vector2(5.5f,5.5f)))return;
        if(!FourMaterial("PDF 3-4 / front squash",new Vector2(0,11),new Vector2(5.5f,5.5f),new Vector2(0,5.5f),new Vector2(5.5f,11),180))return;
        if(!FourMaterial("PDF 5-6 / back squash",new Vector2(11,0),new Vector2(5.5f,5.5f),new Vector2(5.5f,0),new Vector2(11,5.5f),-180))return;
        Log("PDF 7-8","marking-only precreases","skipped","Square base formed. Petal attempt uses available grid markers; exact angle bisectors are not grid markers.");
        if(!FourMaterial("PDF 9-10 / approximate petal attempt",new Vector2(0,11),new Vector2(5.5f,5.5f),new Vector2(0,8.25f),new Vector2(2.75f,11),180))return;
        Uncertified("PDF 9-10 petal shape","Both square pockets are formed. The subsequent available-marker petal attempt is not exact diagram geometry; later neck/tail folds not executed.");
    }
    static void Dragon()
    {
        Log("web 1,4","marking-only precreases","skipped","Specify reference lines directly.");
        if(!Fold("web 3",new Vector2(0,11),new Vector2(11,0)))return;
        int top=0;for(int i=1;i<sheet.Faces.Count;i++)if(sheet.Faces[i].layer>sheet.Faces[top].layer)top=i;
        if(!Fold("web 5 top layer",new Vector2(0,2.75f),new Vector2(2.75f,0),-1,top+1))return;
        if(!Fold("web 6",new Vector2(0,8.25f),new Vector2(8.25f,0)))return;
        if(!FourMaterial("web 7 / lift near flap attempt",new Vector2(5.5f,11),new Vector2(5.5f,5.5f),new Vector2(0,11),new Vector2(11,11),90))return;
        Uncertified("web 7 flap shape","The exact near-flap arrangement has not been certified; later swivel and crimp folds not executed.");
    }
    static void Uncertified(string stage,string note)
    {
        result.status="shape-not-certified";result.nextOperation=stage;result.note=note;
        Log(stage,"target shape comparison","not certified",note);
    }
    static Vector2 InChart(PaperSheet.Face face,Vector2 original)
    {
        var p=face.material.inverse.MultiplyPoint3x4(new Vector3(original.x,0,original.y));return new Vector2(p.x,p.z);
    }
    static bool FourMaterial(string stage,Vector2 tip,Vector2 stop,Vector2 left,Vector2 right,float requested,bool wholeStack=false)
    {
        var vertices=new List<PaperSheet.FoldVertex>();sheet.CollectVertices(vertices);
        bool selectable=true;
        foreach(var original in new[]{tip,stop,left,right}){
            bool found=false;foreach(var vertex in vertices){
                var material=sheet.Faces[vertex.anchor].material.MultiplyPoint3x4(new Vector3(vertex.point.x,0,vertex.point.y));
                if((new Vector2(material.x,material.z)-original).sqrMagnitude<.000001f){found=true;break;}
            }selectable&=found;
        }
        PaperCornerPull best=null;float bestAngle=0;int candidates=0;
        var seen=new HashSet<string>();
        for(int i=0;i<sheet.Faces.Count;i++){
            var face=sheet.Faces[i];if(!face.Contains(InChart(face,tip)))continue;
            string key=face.material.ToString("F4")+face.frame.ToString("F4")+face.layer;
            if(!seen.Add(key))continue;
            var step=new FoldStep{cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,
                regionalPull=!wholeStack,coupledHinges=true,anchor=i,flap=wholeStack?0:i+1,
                pullTip=InChart(face,tip),pullStop=InChart(face,stop),pullLeft=InChart(face,left),pullRight=InChart(face,right)};
            var watch=System.Diagnostics.Stopwatch.StartNew();var pull=PaperCornerPull.Create(sheet,step);candidates++;
            if(pull==null){Log(stage,"four-point create","blocked","material anchor="+i+"; markers="+selectable,step,requested);continue;}
            if(!pull.TryMove(0,requested,out float reached)){Log(stage,"four-point drag","blocked","Could not establish the starting pose",step,requested);continue;}
            bool safe=true;
            if(Mathf.Abs(reached)>=1 && sheet.FinishPull(pull,reached)){
                try{FlyEditor.Editor.FourPointFoldingVerification.Metrics(sheet);}
                catch(Exception e){safe=false;Log(stage,"material geometry","blocked",e.Message,step,requested,reached);}
                finally{sheet.Undo();}
            }
            watch.Stop();
            Log(stage,"four-point continuous candidate",safe?"diagnostic":"rejected geometry",
                "coupled="+pull.UsesCoupledHinges+"; material anchor="+i+"; markers="+selectable+"; scope="+(wholeStack?"whole stack":"authored material region"),step,requested,reached,(float)watch.Elapsed.TotalMilliseconds);
            if(safe && Mathf.Abs(reached)>Mathf.Abs(bestAngle)){best=pull;bestAngle=reached;}
            if(safe && Mathf.Abs(reached-requested)<.1f)break;
        }
        if(best==null || Mathf.Abs(bestAngle)<1 || !sheet.FinishPull(best,bestAngle)){
            result.nextOperation=stage;result.note="No continuous, geometry-checked four-point commit. Candidates="+candidates;
            Log(stage,"four-point commit","blocked",result.note);return false;
        }
        bool reachedTarget=Mathf.Abs(bestAngle-requested)<.1f;
        Log(stage,"four-point commit",reachedTarget?"motion reached":"partial contact","Target shape is not certified by angle alone.",best.Step,requested,bestAngle);
        Render(sheet,result.id+"-four-"+sheet.Count);
        if(!reachedTarget){result.status="partial-contact";result.nextOperation=stage;result.note="Stopped at allowed contact/constraint angle "+bestAngle+" before request "+requested;}
        return reachedTarget;
    }
    static void CollapseProbe(string stage,Vector2 tip,Vector2 stop,Vector2 left,Vector2 right)
    {
        // Probe the UI's four-point mechanism, preserving real layer anchors.
        // Acceptance alone is not a successful waterbomb/squash; its crease
        // layout and end shape must also match the diagram.
        int attempts=0,created=0,posed=0;PaperCornerPull sample=null;float sampleAngle=0;
        for(int i=0;i<sheet.Faces.Count;i++){
            if(!sheet.Faces[i].Contains(tip))continue;
            foreach(bool single in new[]{true,false}){
                var step=new FoldStep{cornerPull=true,flexible=true,closeFlat=true,customFlanks=true,
                    anchor=i,flap=single?i+1:0,singlePanel=single,pullTip=tip,pullStop=stop,pullLeft=left,pullRight=right,degrees=90};
                attempts++;var pull=PaperCornerPull.Create(sheet,step);if(pull==null)continue;created++;
                foreach(float angle in new[]{-180f,-135f,-90f,-45f,45f,90f,135f,180f})if(Mathf.Abs(angle)<=pull.AngleLimit && pull.Pose(angle)){
                    posed++;if(sample==null){sample=pull;sampleAngle=angle;}
                }
            }
        }
        Log(stage,"four-point collapse probe","not certified","anchors/scopes="+attempts+"; created="+created+"; valid sampled poses="+posed+"; arbitrary fan acceptance does not certify the diagram's collapse");
        if(sample!=null){sample.Pose(sampleAngle);Render(sheet,result.id+"-fan-preview",sample);}
        result.nextOperation=stage+": multi-crease collapse / squash";
        result.note="Four-point fan probe only; required target geometry has not been reproduced. Later source steps were not attempted.";
    }
    static void DumpGeometry()
    {
        var lines=new List<string>{"panel,layer,back,x,z,originalX,originalZ"};
        for(int i=0;i<sheet.Faces.Count;i++)foreach(var p in sheet.Faces[i].points){
            var o=sheet.Faces[i].material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
            lines.Add(FormattableString.Invariant($"{i},{sheet.Faces[i].layer},{sheet.Faces[i].back},{p.x},{p.y},{o.x},{o.z}"));
        }File.WriteAllLines(Path.Combine(folder,result.id+"-panels.csv"),lines);
    }
    static void Render(PaperSheet paper,string name,PaperCornerPull pull=null)
    {
        var scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("Audit paper");var cameraGo=new GameObject("Audit camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
        var mesh=new Mesh();var type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldMesh");
        var method=type.GetMethod("Build");var info=method.GetParameters();var args=new object[info.Length];
        for(int i=0;i<args.Length;i++)args[i]=info[i].HasDefaultValue?info[i].DefaultValue:null;
        args[0]=mesh;args[1]=paper;args[2]=null;args[3]=0f;
        var points=new List<PaperSheet.FoldVertex>();paper.CollectVertices(points);args[4]=points;args[10]=pull;
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
}
