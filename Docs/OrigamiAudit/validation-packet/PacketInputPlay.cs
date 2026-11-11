using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using FlyEditor.Flight;
public static class PacketInputPlay
{
    const string Flag="FlyEditor.PacketInput";
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
                folder=Environment.GetEnvironmentVariable("FLYEDITOR_PACKET_INPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/input-surface"));
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
    static Vector2 pullOrigin,pullDelta;
    static FoldStep[] packetRecipe;
    static void MakeSteps(){
        Do(()=>Check(game.OpenFolding(),"Open desk"));Do(()=>{deskCam=GameObject.Find("종이 작업대 카메라").GetComponent<Camera>();var p=new PaperSheet();Check(p.Finish(p.Plan(new FoldStep{creaseNormal=Vector2.right,creaseOffset=5.5f,side=-1,compactLayers=true}),180),"Half setup");Reset(p);});
        KeyTap(Key.Digit4);Click(5.52f,.02f);Do(()=>Phase("SecondPoint"));Click(5.52f,2.75f);
        Do(()=>{Phase("PickSide");var reverse=(PaperInsideReverse)Field("reverse");Check(reverse!=null&&reverse.UsesPacket&&reverse.PacketPanels>=2,"Packet preview missing");Check(((FoldStep)Field("selected")).packetReverse,"Scope flag lost");Save("packet-selected");});
        KeyTap(Key.Tab);Do(()=>Check(Field("scope").ToString()=="Packet"&&((FoldStep)Field("selected")).packetReverse,"Changing ply lost packet mode"));
        KeyTap(Key.Digit1);Do(()=>Check(Field("scope").ToString()=="Top"&&!((FoldStep)Field("selected")).packetReverse,"Top scope retained packet flag"));
        KeyTap(Key.Digit4);Do(()=>Check(((PaperInsideReverse)Field("reverse")).UsesPacket,"Return to packet scope"));
        Do(()=>ClickPaper(7,1,true));Do(()=>{Phase("PullCorner");pullOrigin=pointer;var delta=(Vector2)Field("pullVector");float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);pullDelta=new Vector2(delta.x,-delta.y)*scale;Button(pullOrigin+pullDelta*.3f,true);});
        Do(()=>{Check((float)Field("angle")>45,"No forward motion");Button(pullOrigin+pullDelta*.1f,true);});
        Do(()=>{Check((float)Field("angle")<25,"No backward scrub");Save("packet-opening");Button(pullOrigin+pullDelta,true);});
        Do(()=>Button(pointer,false));
        Do(()=>{Phase("FirstPoint");var p=(PaperSheet)Field("sheet");Check(p.Count==2&&p.Recipe()[1].packetReverse&&p.Recipe()[1].packetMaterialHandles,"Packet save flags");Check(p.Recipe()[1].degrees>179,"Not fully closed");FlyEditor.Editor.PetalFoldingVerification.Replay(p);packetRecipe=p.Recipe();Save("packet-closed");});
        KeyTap(Key.E);Do(()=>{Phase("PullCorner");Check(((PaperInsideReverse)Field("reverse")).UsesPacket,"Edit lost packet");});KeyTap(Key.Escape);
        Do(()=>Check(((PaperSheet)Field("sheet")).Count==2,"Edit cancellation changed history"));KeyTap(Key.Z);
        Do(()=>{Check(((PaperSheet)Field("sheet")).Count==1,"Undo");Check(Markers()==0,"Grid returned");});
        KeyTap(Key.Digit4);Click(5.52f,.02f);Click(5.52f,2.75f);Click(4,5.5f);Do(()=>{Phase("FirstPoint");Check(Field("reverse")==null&&Markers()==0,"Empty click did not clear packet selection");});
        KeyTap(Key.Digit1);Click(7.2f,3.2f);Click(7.25f,8.2f);Do(()=>{Phase("PickSide");Check(Field("scope").ToString()=="Top","Single ply scope not restored");Check(!((FoldStep)Field("selected")).packetReverse,"Single ply promoted to packet");});
        Click(4,5.5f);Do(()=>Reset(new PaperSheet()));KeyTap(Key.Digit4);
        Click(10.98f,5.5f);Click(5.5f,5.5f);AddPointsButton();Click(10.98f,.02f);Click(10.98f,10.98f);
        Do(()=>{Phase("PickSide");Check(((FoldStep)Field("selected")).customFlanks,"Four point fallback unavailable");});
        DragFace(7,6,90,1);Do(()=>FlyEditor.Editor.PetalFoldingVerification.Replay((PaperSheet)Field("sheet")));
        Do(()=>Reset(PaperSheet.Replay(packetRecipe)));KeyTap(Key.Enter);Do(()=>{Check(!game.FoldingOpen,"Packet aircraft apply failed");SaveAircraft();});
        Do(()=>{Debug.Log("[PacketInputPlay] ALL PASS scope, highlighted preview, plain point clicks, forward/backward drag, close/save/replay, edit/cancel, undo, empty deselection, return to one ply, four-point fallback, aircraft apply.");Finish(0);});
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










