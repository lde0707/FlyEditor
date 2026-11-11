using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using FlyEditor.Flight;
public static class PetalScopePlay
{
    const string Flag="FlyEditor.PetalScope";
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
                folder=Environment.GetEnvironmentVariable("FLYEDITOR_PETAL_INPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/input-petal"));
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
    static void MakeSteps(){
        Do(()=>Check(game.OpenFolding(),"Open desk"));
        Do(()=>deskCam=GameObject.Find("종이 작업대 카메라").GetComponent<Camera>());
        Do(()=>{
            var paper=FlyEditor.Editor.PetalFoldingVerification.Square();
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var desk=game.GetType().GetField("folding",flags).GetValue(game);
            desk.GetType().GetField("sheet",flags).SetValue(desk,paper);desk.GetType().GetMethod("Browse",flags).Invoke(desk,null);
        });
        Four(0,0,5.5f,5.5f,0,5.5f,5.5f,0);
        Do(()=>Save("four-selected"));
        Do(()=>ClickPaper(1,1.3f,true));
        Do(()=>{Phase("AdjustAngle");Check(((PaperCornerPull)Field("pull")).UsesPetalFold,"Petal solver not selected");Button(pointer+Vector2.up*(90/.65f)*Mathf.Min(Screen.width/1440f,Screen.height/900f),true);});
        Do(()=>Save("petal-open"));Do(()=>Button(pointer,false));
        Do(()=>Check(((PaperSheet)Field("sheet")).Count==5,"Partial petal not committed"));
        KeyTap(Key.E);Do(()=>Check(((PaperCornerPull)Field("pull")).UsesPetalFold,"Resume changed solver"));
        KeyTap(Key.Escape);Do(()=>Check(((PaperSheet)Field("sheet")).Recipe()[4].degrees==90,"Cancel lost partial petal"));
        KeyTap(Key.E);KeyTap(Key.C);
        Do(()=>{Phase("FirstPoint");var paper=(PaperSheet)Field("sheet");Check(paper.Count==5 && Mathf.Abs(paper.Recipe()[4].degrees-180)<.001f,"Petal did not close");FlyEditor.Editor.FourPointFoldingVerification.Metrics(paper);Save("petal-flat");});
        // Deselect in empty space, then start the next ordinary fold on the result.
        Click(7.7781746f,7.7781746f);Do(()=>Phase("SecondPoint"));
        Click(0,7);Do(()=>Phase("FirstPoint"));
        KeyTap(Key.Z);Do(()=>{Check(((PaperSheet)Field("sheet")).Count==4,"Undo failed");});
        Do(()=>{
            var paper=FlyEditor.Editor.PetalFoldingVerification.Square();
            var h=Vector2.one*5.5f;
            Check(paper.FinishPull(FlyEditor.Editor.PetalFoldingVerification.Plan(paper,new Vector2(0,11),h,new Vector2(0,5.5f),new Vector2(5.5f,11),1,true),180),"Seed front petal");
            Check(paper.FinishPull(FlyEditor.Editor.PetalFoldingVerification.Plan(paper,new Vector2(11,0),h,new Vector2(5.5f,0),new Vector2(11,5.5f),-1,true),-180),"Seed back petal");
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var desk=game.GetType().GetField("folding",flags).GetValue(game);
            desk.GetType().GetField("sheet",flags).SetValue(desk,paper);desk.GetType().GetMethod("Browse",flags).Invoke(desk,null);
        });
        Four(0,0,2.2781746f,5.5f,3.8890873f,3.8890873f,7.7781746f,7.7781746f);
        Do(()=>Check(PaperCornerPull.Create((PaperSheet)Field("sheet"),(FoldStep)Field("selected")).UsesEdgeMatch,"Edge match not selected"));
        DragFace(1,2,180,7);Do(()=>{Save("next-edge-fold");FlyEditor.Editor.PetalFoldingVerification.Replay((PaperSheet)Field("sheet"));
        Debug.Log("[PetalScopePlay] ALL PASS input, partial commit, edit, cancel, contact closure, next selection, undo and four-point edge alignment");Finish(0);});
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










