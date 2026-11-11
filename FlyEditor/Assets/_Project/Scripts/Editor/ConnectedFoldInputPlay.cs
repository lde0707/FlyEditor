using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using FlyEditor.Flight;
public static class ConnectedFoldInputPlay
{
    const string Flag="FlyEditor.ConnectedFoldInput";
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
        var world=new Vector3(x,.015f,y);float nearest=float.PositiveInfinity;
        var paper=(PaperSheet)Field("sheet");var point=new Vector2(x,y);
        foreach(var face in paper.Faces){if(!face.Contains(point))continue;bool front=Vector3.Dot(deskCam.transform.forward,face.frame.MultiplyVector(Vector3.up))<0;
            var candidate=face.World(point,(face.layer+(front?1:0))*PaperSheet.Thickness+(front?.001f:-.001f));
            var projected=deskCam.WorldToViewportPoint(deskCam.transform.parent.position+candidate);if(projected.z<nearest){nearest=projected.z;world=candidate;}}
        var v=deskCam.WorldToViewportPoint(deskCam.transform.parent.position+world);
        var mousePoint=new Vector2((viewport.x+v.x*viewport.width)*s,Screen.height-(viewport.y+(1-v.y)*viewport.height)*s);
        Button(mousePoint,down);
    }
    static void Save(string name){
        var rt=deskCam.targetTexture;var active=RenderTexture.active;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),tex.EncodeToPNG());}
        finally{RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(tex);}
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
        }catch(Exception ex){if(deskCam && folder!=null)Save("failure");Debug.LogError("[ConnectedFoldInputPlay] action "+stage);Debug.LogException(ex);Finish(1);}
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

    static void Four(float tx,float ty,float cx,float cy,float lx,float ly,float rx,float ry){
        Click(tx,ty);Do(()=>Phase("SecondPoint"));
        Click(cx,cy);Do(()=>{Phase("PickSide");Check(((FoldStep)Field("selected")).neutralContacts,"Neutral contact not enabled for two-point input");Check((int)Field("leftFlank")==-1,"Two points entered four-point mode");});
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
        Do(()=>{Phase("AdjustAngle");Check(((PaperCornerPull)Field("pull")).UsesPetalFold,"Petal solver not selected");Check(((PaperCornerPull)Field("pull")).Step.connectedPetalBacking,"Connected backing not enabled in actual input");Button(pointer+Vector2.up*(90/.65f)*Mathf.Min(Screen.width/1440f,Screen.height/900f),true);});
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
        Click(7.7781746f,7.7781746f);Do(()=>Phase("RightFlank"));
        Click(7.7781746f,7.7781746f);Do(()=>Phase("PickSide"));
        Do(()=>Check(PaperCornerPull.Create((PaperSheet)Field("sheet"),(FoldStep)Field("selected")).UsesEdgeMatch,"Edge match not selected"));
        DragFace(1,2,180,7);Do(()=>{Save("next-edge-fold");FlyEditor.Editor.PetalFoldingVerification.Replay((PaperSheet)Field("sheet"));
        Debug.Log("[ConnectedFoldInputPlay] ALL PASS input, partial commit, edit, cancel, contact closure, next selection, undo, fourth-point deselect/reselect and four-point edge alignment");Finish(0);});
    }

    static void Finish(int code){
        SessionState.SetBool(Flag,false);EditorApplication.update-=Step;
        string path=Environment.GetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS");if(path!=null)foreach(string ext in new[]{"",".bak",".tmp"})File.Delete(path+ext);
        EditorApplication.Exit(code);
    }
}

