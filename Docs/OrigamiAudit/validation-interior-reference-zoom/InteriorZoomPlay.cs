using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using FlyEditor.Flight;
public static class InteriorZoomPlay
{
    const string Flag="FlyEditor.InteriorZoom";
    static int stage;static double next,deadline;static Mouse mouse;static Keyboard keyboard;
    static readonly System.Collections.Generic.List<Action> steps=new System.Collections.Generic.List<Action>();
    static PaperFlightController game;static Camera deskCam;static string folder;
    public static void Run(){
        // Engine regression is run separately; this harness drives actual input.
        Environment.SetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS",Path.Combine(Path.GetTempPath(),"fly-fold-"+Guid.NewGuid()+".json"));
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity");SessionState.SetBool(Flag,true);EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod]static void Start(){if(!SessionState.GetBool(Flag,false))return; deadline=EditorApplication.timeSinceStartup+150;EditorApplication.update+=Step;}
    static void Check(bool a,string b){if(!a)throw new Exception(b);}
    static Vector2 pointer; static void Button(Vector2 pos,bool held){pointer=pos;InputSystem.QueueStateEvent(mouse,new MouseState{position=pos}.WithButton(MouseButton.Left,held));}
    static void SendKey(Key key){InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));}
    static void ClickPaper(float x,float y,bool down){
        float s=Mathf.Min(Screen.width/1440f,Screen.height/900f);
        var area=new Rect(36,105,Screen.width/s-414,Screen.height/s-185);float aspect=1280f/900f;float w=Mathf.Min(area.width,area.height*aspect);Vector2 sz=new Vector2(w,w/aspect);var viewport=new Rect(area.center-sz*.5f,sz);
        var v=deskCam.WorldToViewportPoint(deskCam.transform.parent.position+new Vector3(x,.015f,y));
        Button(new Vector2((viewport.x+v.x*viewport.width)*s,Screen.height-(viewport.y+(1-v.y)*viewport.height)*s),down);
    }
    static void Save(string name){
        deskCam.Render();
        var rt=deskCam.targetTexture;var active=RenderTexture.active;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),tex.EncodeToPNG());}
        finally{RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(tex);}
    }
    static void SaveAircraft(){
        var cam=game.FlightCamera;var old=cam.targetTexture;var active=RenderTexture.active;var pos=cam.transform.position;float zoom=cam.orthographicSize;
        var rt=new RenderTexture(720,440,24);var tex=new Texture2D(720,440,TextureFormat.RGB24,false);
        try{
            cam.transform.position=new Vector3(-.6f,5.0f,-18);cam.orthographicSize=3.7f;cam.targetTexture=rt;cam.Render();
            RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,"equipped.png"),tex.EncodeToPNG());
        }finally{cam.transform.position=pos;cam.orthographicSize=zoom;cam.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
    }
    static void Step(){
        try{
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Timed out at "+stage);
            if(!Application.isPlaying || EditorApplication.timeSinceStartup<next)return;
            if(game==null){
                game=UnityEngine.Object.FindAnyObjectByType<PaperFlightController>();if(!game||game.Model==null)return;
                InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                mouse=InputSystem.AddDevice<Mouse>();keyboard=InputSystem.AddDevice<Keyboard>();
                folder=Environment.GetEnvironmentVariable("FLYEDITOR_REFERENCE_INPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/input-surface"));
            }
            if(steps.Count==0){Directory.CreateDirectory(folder);MakeSteps();}
            steps[stage++]();next=EditorApplication.timeSinceStartup+.12;
        }catch(Exception ex){Save("failure"); Debug.LogError("Vertex proof action "+stage+" notice="+Field("notice")+" pinned="+Field("pinnedFace")); var sh=(PaperSheet)Field("sheet"); for(int i=0;i<sh.Faces.Count;i++){var f=sh.Faces[i];foreach(var p in f.points)Debug.Log("FACE "+i+" layer "+f.layer+" point "+p+" world "+f.World(p,0)+" material "+f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y)));}Debug.LogException(ex);Finish(1);}
    }
    static void Do(Action action)=>steps.Add(action);
    static void KeyTap(Key k){Do(()=>SendKey(k));Do(()=>InputSystem.QueueStateEvent(keyboard,new KeyboardState()));}
    static void Click(float x,float y){Do(()=>ClickPaper(x,y,true));Do(()=>ClickPaper(x,y,false));}
    static object Field(string name){
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var desk=game.GetType().GetField("folding",flags).GetValue(game);
        return desk.GetType().GetField(name,flags).GetValue(desk);
    }
    static void Phase(string expected){Check(Field("phase").ToString()==expected,"Expected "+expected+" got "+Field("phase"));}
    static void Fold(float ax,float ay,float bx,float by,float sideX,float sideY,float deg,int count){
        Click(ax,ay);Do(()=>Phase("SecondPoint"));
        Click(bx,by);Do(()=>Phase("PickSide"));
        Do(()=>ClickPaper(sideX,sideY,true));Do(()=>{
            Phase("AdjustAngle");
            float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
            Button(pointer+Vector2.up*(deg/.65f)*scale,true);
        });
        Do(()=>Button(pointer,false));
        Do(()=>Check(Mathf.Abs((float)Field("angle")-deg)<1.1f,"Drag did not set "+deg+" stage "+count));
        Do(()=>{Phase("FirstPoint");Check(((PaperSheet)Field("sheet")).Count==count,"Fold count");});
    }
    static void CaptureWorkbench(){
        var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var win=EditorWindow.GetWindow(type);win.Repaint();
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        var tex=type.GetField("m_TargetTexture",flags)?.GetValue(win) as RenderTexture;
        if(!tex || !tex.IsCreated()){Debug.Log("[ShapePlay] No active Game View render target");return;}
        var old=RenderTexture.active;var pixels=new Texture2D(tex.width,tex.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=tex;pixels.ReadPixels(new Rect(0,0,tex.width,tex.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,"workbench.png"),pixels.EncodeToPNG());}
        finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(pixels);}
    }
    static void CheckSelectedGuide(){
        var marks=(System.Collections.IList)Field("hinge");
        var sheet=(PaperSheet)Field("sheet");var selected=(FoldStep)Field("selected");
        Check(marks.Count==1,"Line drawn on "+marks.Count+" panels");
        var face=marks[0].GetType().GetField("face").GetValue(marks[0]);
        Check(ReferenceEquals(face,sheet.Faces[selected.anchor]),"Guide and selected panel differ");
        Check(selected.singlePanel && selected.flap==selected.anchor+1,"Unbound selection");
    }
    static void Points(Vector2 first,Vector2 second){
        Phase("PickSide");
        var points=(System.Collections.Generic.List<PaperSheet.FoldVertex>)Field("vertices");
        Check((points[(int)Field("first")].point-first).sqrMagnitude<.001f,"First point was not replaced");
        Check((points[(int)Field("second")].point-second).sqrMagnitude<.001f,"Second point was not replaced");
        Check(((PaperSheet)Field("sheet")).Count==0,"Click folded paper");
        CheckSelectedGuide();
    }
    static void Four(float tx,float ty,float cx,float cy,float lx,float ly,float rx,float ry){
        Click(tx,ty);Do(()=>Phase("SecondPoint"));
        Click(cx,cy);Do(()=>{Phase("PickSide");Check((int)Field("leftFlank")==-1,"Two points entered four-point mode");});
        Click(lx,ly);Do(()=>Phase("RightFlank"));
        Click(rx,ry);Do(()=>{Phase("PickSide");Check(((FoldStep)Field("selected")).customFlanks,"Four handles missing");});
    }
    static void DragFace(float x,float y,float degrees,int count){
        Do(()=>ClickPaper(x,y,true));
        Do(()=>{Phase("AdjustAngle");Button(pointer+Vector2.up*(degrees/.65f)*Mathf.Min(Screen.width/1440f,Screen.height/900f),true);});
        Do(()=>Button(pointer,false));
        Do(()=>{Phase("FirstPoint");var paper=(PaperSheet)Field("sheet");Check(paper.Count==count,"Fold failed");
            Check(Mathf.Abs(paper.Recipe()[count-1].degrees-degrees)<1.1f,"Angle limited to "+paper.Recipe()[count-1].degrees);});
    }
    static PaperSheet.Face untouched;
    static Vector2 dragOrigin,dragVector;
    static void CheckScope(){
        var plan=(PaperCornerPull)Field("pull");
        Check(plan!=null && plan.Step.regionalPull && plan.Step.flap>0 && !plan.Step.singlePanel,"Four-point entry discarded picked region");
    }
    static void FixedPly(){
        bool found=false;foreach(var face in ((PaperSheet)Field("sheet")).Faces)found|=ReferenceEquals(face,untouched);
        Check(found,"The unselected ply moved");
    }
    static void AltClick(float x,float y){Do(()=>SendKey(Key.LeftAlt));Click(x,y);Do(()=>InputSystem.QueueStateEvent(keyboard,new KeyboardState()));}
    static void Reset(PaperSheet paper){var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var desk=game.GetType().GetField("folding",flags).GetValue(game);desk.GetType().GetField("sheet",flags).SetValue(desk,paper);desk.GetType().GetMethod("Browse",flags).Invoke(desk,null);}
    static int Markers()=>((System.Collections.Generic.List<PaperSheet.FoldVertex>)Field("vertices")).Count;
    static PaperSheet.FoldVertex Selected(string slot)=>((System.Collections.Generic.List<PaperSheet.FoldVertex>)Field("vertices"))[(int)Field(slot)];
    static void AddPointsButton(){KeyTap(Key.F);Do(()=>Phase("LeftFlank"));}
    static PaperSheet Half(){var p=new PaperSheet();Check(p.ApplyStep(new FoldStep(CreaseFamily.Vertical,0,1){creaseNormal=Vector2.right,creaseOffset=5.5f,degrees=180}),"Half fold");return p;}
    static void Inspect(string label){
        Phase("PickSide");var paper=(PaperSheet)Field("sheet");var line=(FoldStep?)Field("selected");
        Debug.Log("[InteriorZoomPlay] "+label+" anchor="+line?.anchor+" single="+line?.singlePanel+" reverse="+line?.insideReverse+" hinge="+((System.Collections.IList)Field("hinge")).Count+" pinned="+Field("pinnedFace"));
        Check(line.HasValue && line.Value.singlePanel && !line.Value.insideReverse,"Ordinary selection expanded into multiple plies");
        CheckSelectedGuide();Save(label);
    }
    static PaperSheet Corner(){var p=new PaperSheet();Check(p.ApplyStep(new FoldStep(CreaseFamily.Falling,0,1){creaseNormal=Vector2.one.normalized,creaseOffset=16.5f/Mathf.Sqrt(2),degrees=180}),"Corner setup");return p;}
    static PaperSheet.FoldVertex endpointA,endpointB;
    static void CheckAxis(bool all){
        Phase("PickSide");var step=(FoldStep?)Field("selected");Check(step.HasValue,"No crease from two visible endpoints");
        Check(step.Value.singlePanel!=all,"Wrong fold scope");
        var guides=(System.Collections.IList)Field("hinge");Check(guides.Count==(all?2:1),"Expected guides "+(all?2:1)+" got "+guides.Count);
    }
    static void KeepEndpoints(){endpointA=Selected("first");endpointB=Selected("second");Check(endpointA.anchor!=endpointB.anchor,"Did not select distinct physical panels");}
    static void EndpointsUnchanged(){var a=Selected("first");var b=Selected("second");Check(a.anchor==endpointA.anchor && b.anchor==endpointB.anchor && (a.point-endpointA.point).sqrMagnitude<.000001f && (b.point-endpointB.point).sqrMagnitude<.000001f,"Scope change moved authored endpoints");}
    static void ClickFace(int index,Vector2 p){Do(()=>FaceButton(index,p,true));Do(()=>FaceButton(index,p,false));}
    static void FaceButton(int index,Vector2 p,bool down){var paper=(PaperSheet)Field("sheet");var f=paper.Faces[index];var w=deskCam.transform.parent.position+f.World(p,(f.layer+1)*PaperSheet.Thickness+.001f);var uv=deskCam.WorldToViewportPoint(w);float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);var viewport=(Rect)Field("viewport");Button(new Vector2((viewport.x+uv.x*viewport.width)*scale,Screen.height-(viewport.y+(1-uv.y)*viewport.height)*scale),down);}
    static PaperSheet Angled(){var p=new PaperSheet();Check(p.ApplyStep(new FoldStep(CreaseFamily.Vertical,0,1){creaseNormal=Vector2.right,creaseOffset=5.5f,degrees=65}),"Angled fold");return p;}
    static void CheckCounts(params int[] expected){
        var groups=(System.Collections.IList)Field("referenceMembers");var counts=new int[expected.Length];
        for(int i=0;i<groups.Count;i++)foreach(PaperSheet.FoldVertex member in (System.Collections.IEnumerable)groups[i])counts[member.anchor]++;
        for(int f=0;f<counts.Length;f++)Check(counts[f]==expected[f],"Face "+f+" has "+counts[f]+" references, expected "+expected[f]);
    }
    static void CheckEdges(){
        var paper=(PaperSheet)Field("sheet");var groups=(System.Collections.IList)Field("referenceMembers");
        for(int faceIndex=0;faceIndex<paper.Faces.Count;faceIndex++){
            var face=paper.Faces[faceIndex];
            for(int edge=0;edge<face.points.Length;edge++){
                var a=face.points[edge];var b=face.points[(edge+1)%face.points.Length];var delta=b-a;int count=0;
                int expected=delta.magnitude<=2.75001f?3:5;
                foreach(System.Collections.IEnumerable group in groups)foreach(PaperSheet.FoldVertex member in group){
                    if(member.anchor!=faceIndex)continue;var offset=member.point-a;float along=Vector2.Dot(offset,delta)/delta.sqrMagnitude;
                    if(along<-.00001f||along>1.00001f || Mathf.Abs(delta.x*offset.y-delta.y*offset.x)>.0001f)continue;
                    count++;Check(Mathf.Abs(along*(expected-1)-Mathf.Round(along*(expected-1)))<.0001f,"Uneven edge spacing");
                }
                Check(count==expected,"Face "+faceIndex+" edge "+edge+" expected "+expected+" points, got "+count);
            }
        }
    }
    static int VisibleCount(){int count=0;foreach(bool value in (System.Collections.IEnumerable)Field("visiblePoints"))if(value)count++;return count;}
    static int Joint(){var groups=(System.Collections.IList)Field("referenceMembers");var sheet=(PaperSheet)Field("sheet");
        for(int i=0;i<groups.Count;i++){var group=(System.Collections.IList)groups[i];if(group.Count<2)continue;var v=(PaperSheet.FoldVertex)group[0];var m=sheet.Faces[v.anchor].material.MultiplyPoint3x4(new Vector3(v.point.x,0,v.point.y));if((m-new Vector3(5.5f,0,0)).sqrMagnitude<.001f)return i;}throw new Exception("Common seam endpoint not merged");}
    static void JointClick(){Do(()=>{var v=((System.Collections.Generic.List<PaperSheet.FoldVertex>)Field("displayVertices"))[Joint()];FaceButton(v.anchor,v.point,true);});Do(()=>Button(pointer,false));}
    static void Zoom(float value){var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var desk=game.GetType().GetField("folding",flags).GetValue(game);desk.GetType().GetMethod("SetZoom",flags).Invoke(desk,new object[]{value,new Vector2(.5f,.5f)});}
    static float Fill(int index){var f=((PaperSheet)Field("sheet")).Faces[index];Vector2 min=new Vector2(float.PositiveInfinity,float.PositiveInfinity),max=-min;foreach(var p in f.points){var v=deskCam.WorldToViewportPoint(deskCam.transform.parent.position+f.World(p,(f.layer+.5f)*PaperSheet.Thickness));min=Vector2.Min(min,new Vector2(v.x,v.y));max=Vector2.Max(max,new Vector2(v.x,v.y));}return Mathf.Max(max.x-min.x,max.y-min.y);}
    static void Fit(float ratio)=>Zoom((float)Field("zoom")*ratio/Fill(0));
    static int VisibleInteriors(){var tags=(System.Collections.Generic.List<bool>)Field("interiorReferences");var shown=(System.Collections.Generic.List<bool>)Field("visiblePoints");int count=0;for(int i=0;i<tags.Count;i++)if(tags[i]&&shown[i])count++;return count;}
    static int NearestAt(Vector2 p){var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var desk=game.GetType().GetField("folding",flags).GetValue(game);var face=((PaperSheet)Field("sheet")).Faces[0];var pos=desk.GetType().GetMethod("SurfaceScreen",flags).Invoke(desk,new object[]{face,p});return (int)desk.GetType().GetMethod("NearestVertex",flags).Invoke(desk,new object[]{pos,13f});}
    static void Wheel(float value){Do(()=>{float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);var rect=(Rect)Field("viewport");var pos=new Vector2(rect.center.x*scale,Screen.height-rect.center.y*scale);InputSystem.QueueStateEvent(mouse,new MouseState{position=pos,scroll=new Vector2(0,value)});});Do(()=>InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer}));}
    static void MakeSteps(){
        Do(()=>Check(game.OpenFolding(),"Open desk"));Do(()=>{deskCam=GameObject.Find("종이 작업대 카메라").GetComponent<Camera>();Zoom(.6f);Check(Markers()==25,"Expected 16 edge plus 9 interior references");CheckEdges();Check(VisibleInteriors()==0 && VisibleCount()==16,"Far view leaked interior markers");Check(NearestAt(new Vector2(2.75f,2.75f))==-1,"Hidden interior point still captures clicks");Save("far-edges-only");});
        Do(()=>{Zoom(1.4f);Check(VisibleInteriors()==9 && VisibleCount()==25,"Close view missing 3x3 interior references");Save("near-interior-points");Fit(.77f);Check(VisibleInteriors()==9,"Hide hysteresis lost");Fit(.70f);Check(VisibleInteriors()==0,"Far threshold did not hide");Fit(.77f);Check(VisibleInteriors()==0,"Show hysteresis lost");Fit(.82f);Check(VisibleInteriors()==9,"80 percent reveal threshold failed");Fit(.70f);});
        Wheel(120);Do(()=>Check(VisibleInteriors()==9,"Mouse wheel zoom-in did not show interiors"));Wheel(-120);Do(()=>Check(VisibleInteriors()==0,"Mouse wheel zoom-out did not hide interiors"));
        Do(()=>Zoom(1.4f));Click(5.5f,5.5f);Do(()=>{Phase("SecondPoint");Check(((System.Collections.Generic.List<bool>)Field("interiorReferences"))[(int)Field("first")],"Centre was not an interior reference");Zoom(.6f);Check(VisibleInteriors()==1,"Selected interior handle disappeared on zoom-out");Check(NearestAt(new Vector2(5.5f,5.5f))==(int)Field("first"),"Selected hidden-distance handle is not clickable");});
        Click(5.5f,5.5f);Do(()=>{Phase("FirstPoint");Check(VisibleInteriors()==0,"Deselected interior remained at far zoom");Zoom(1.4f);});
        Click(2.75f,2.75f);Click(2.75f,8.25f);DragFace(1.1f,5,60,1);Do(()=>FlyEditor.Editor.PetalFoldingVerification.Replay((PaperSheet)Field("sheet")));KeyTap(Key.Z);Do(()=>{Check(((PaperSheet)Field("sheet")).Count==0 && Markers()==25,"Undo did not restore references");});
        Do(()=>{var p=new PaperSheet();Check(p.ApplyStep(new FoldStep(CreaseFamily.Rising,0,1){degrees=180}),"Triangle setup");Reset(p);Zoom(1.4f);Check(VisibleInteriors()==9,"Stacked interior markers duplicated or missing");Save("triangle-near");Zoom(.4f);Check(VisibleInteriors()==0 && VisibleCount()==12,"Triangle far edge count");});
        Do(()=>{var p=new PaperSheet();Check(p.ApplyStep(new FoldStep(CreaseFamily.Vertical,0,1){creaseNormal=Vector2.right,creaseOffset=8,degrees=180}),"Mixed-size one");Check(p.ApplyStep(new FoldStep(CreaseFamily.Horizontal,0,1){creaseNormal=Vector2.up,creaseOffset=8,degrees=180}),"Mixed-size two");Reset(p);Fit(.85f);var flags=(System.Collections.Generic.List<bool>)Field("interiorVisibleByFace");Check(flags[0]&&!flags[3],"Interior threshold is not per face");Save("mixed-size-far");Zoom(4);Check(((System.Collections.Generic.List<bool>)Field("interiorVisibleByFace"))[3],"Small face did not reveal when close");Save("small-face-near");});
        Do(()=>{Debug.Log("[InteriorZoomPlay] ALL PASS edge preservation, 3x3 interiors, 80-percent fit threshold, hysteresis, real mouse wheel, hidden point no hit, selected handle retention/toggle, interior crease fold, undo/replay, hidden stacked interiors, per-face size threshold.");Finish(0);});
    }
    static void KeepOtherPly(){var p=(PaperSheet)Field("sheet");int selected=((FoldStep)Field("selected")).anchor;untouched=p.Faces[1-selected];}
    static void CheckAdaptive(PaperSheet paper,int expectedTotal=50){
        var points=(System.Collections.Generic.List<PaperSheet.FoldVertex>)Field("vertices");
        for(int f=0;f<paper.Faces.Count;f++){
            var face=paper.Faces[f];var unique=new System.Collections.Generic.List<Vector2>();
            foreach(var v in points){
                Check(paper.Faces[v.anchor].Contains(v.point),"Point outside its own face");
                if(v.anchor!=f)continue;
                foreach(var old in unique)Check((old-v.point).sqrMagnitude>.00000001f,"Duplicate point");
                unique.Add(v.point);
            }
            Check(unique.Count==25 || unique.Count==3,"Unexpected face count "+unique.Count);
            if(unique.Count==3){var ab=unique[1]-unique[0];var ac=unique[2]-unique[0];Check(Mathf.Abs(ab.x*ac.y-ab.y*ac.x)>.00001f,"Narrow handles are collinear");}

        }
        Check(points.Count==expectedTotal,"Expected "+expectedTotal+" points got "+points.Count);
        Debug.Log("[InteriorZoomPlay] Checked "+paper.Faces.Count+" faces, "+points.Count+" adaptive points");
    }
    static Vector3[] handles;
    static Vector3[] SelectedMaterial(){
        var vertices=(System.Collections.Generic.List<PaperSheet.FoldVertex>)Field("vertices");var paper=(PaperSheet)Field("sheet");
        var output=new Vector3[3];int n=0;
        foreach(string name in new[]{"first","second","leftFlank"}){var v=vertices[(int)Field(name)];output[n++]=paper.Faces[v.anchor].material.MultiplyPoint3x4(new Vector3(v.point.x,0,v.point.y));}
        return output;
    }
    static void Finish(int code){
        SessionState.SetBool(Flag,false);EditorApplication.update-=Step;
        string path=Environment.GetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS");if(path!=null)foreach(string ext in new[]{"",".bak",".tmp"})File.Delete(path+ext);
        EditorApplication.Exit(code);
    }
}










