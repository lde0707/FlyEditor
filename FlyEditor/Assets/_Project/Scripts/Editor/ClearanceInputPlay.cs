using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using FlyEditor.Flight;
public static class ClearanceInputPlay
{
    const string Flag="FlyEditor.ClearanceInput";
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
                folder=Environment.GetEnvironmentVariable("FLYEDITOR_CLEARANCE_INPUT") ?? Path.Combine(ClearanceCommonVerification.Folder,"input");
            }
            if(steps.Count==0){Directory.CreateDirectory(folder);MakeSteps();}
            steps[stage++]();next=EditorApplication.timeSinceStartup+.12;
        }catch(Exception ex){if(deskCam && folder!=null)Save("failure");Debug.LogError("[ClearanceInputPlay] action "+stage);Debug.LogException(ex);Finish(1);}
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

    static void Assist(){var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var desk=game.GetType().GetField("folding",flags).GetValue(game);desk.GetType().GetMethod("SuggestFlanks",flags).Invoke(desk,null);}
    static readonly System.Reflection.BindingFlags H=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    static object Desk=>game.GetType().GetField("folding",H).GetValue(game);
    static object Invoke(string name,params object[] args)=>Desk.GetType().GetMethod(name,H).Invoke(Desk,args);
    static void Set(string name,object value)=>Desk.GetType().GetField(name,H).SetValue(Desk,value);
    static void SeedPair(){
        var paper=ClearanceCommonVerification.Load();Set("sheet",paper);Invoke("Browse");var step=ClearanceCommonVerification.Request(paper);
        Set("first",(int)Invoke("AssistPoint",new PaperSheet.FoldVertex(step.anchor,step.pullTip)));
        Set("second",(int)Invoke("AssistPoint",new PaperSheet.FoldVertex(step.anchor,step.pullStop)));
        Invoke("SelectPair");Phase("PickSide");
    }
    static void WaitReady(){if(Field("phase").ToString()=="PreparingClearance"){stage--;return;}Phase("PickSide");var st=(FoldStep)Field("selected");Check(st.clearance!=null&&st.clearance.Length>0,"No prepared path");Check((int)Field("leftFlank")>=0&&(int)Field("rightFlank")>=0,"Missing auto flanks");Check(((PaperSheet)Field("sheet")).Count==21,"Planning committed paper");}
    static void MakeSteps(){
        Do(()=>Check(game.OpenFolding(),"Open desk"));Do(()=>deskCam=GameObject.Find("종이 작업대 카메라").GetComponent<Camera>());
        Do(SeedPair);Do(Assist);Do(()=>Phase("PreparingClearance"));KeyTap(Key.Escape);
        Do(()=>{Phase("FirstPoint");Check(Field("clearanceTask")==null&&((PaperSheet)Field("sheet")).Count==21,"Cancel changed paper or retained task");});
        Do(SeedPair);Do(Assist);Do(WaitReady);Do(()=>Save("ready"));
        Do(()=>{float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);var pos=new Vector2(400,400);Button(new Vector2(pos.x*scale,Screen.height-pos.y*scale),true);Invoke("BeginFourDrag",pos);});
        Do(()=>{Phase("AdjustAngle");Check(((PaperCornerPull)Field("pull")).UsesClearanceSequence,"Not a sequence");Button(pointer+Vector2.up*(90/.65f)*Mathf.Min(Screen.width/1440f,Screen.height/900f),true);});
        Do(()=>Save("clearing"));Do(()=>Button(pointer,false));
        Do(()=>{Phase("FirstPoint");Check(((PaperSheet)Field("sheet")).Count==22,"Partial drag did not commit once");});
        KeyTap(Key.E);Do(()=>Check(((PaperCornerPull)Field("pull")).UsesClearanceSequence,"Edit lost sequence"));KeyTap(Key.Escape);
        Do(()=>Check(((PaperSheet)Field("sheet")).Count==22,"Cancel edit changed count"));
        KeyTap(Key.E);KeyTap(Key.C);
        Do(()=>{Phase("FirstPoint");var sheet=(PaperSheet)Field("sheet");Check(sheet.Count==22&&Mathf.Abs(sheet.Recipe()[21].degrees)==180,"Close did not finish");FlyEditor.Editor.PetalFoldingVerification.Replay(sheet);FlyEditor.Editor.ReverseDirectionVerification.Geometry(sheet.Faces);Save("next-petal");});
        Do(()=>{Set("pitch",35f);Set("yaw",135f);Invoke("PositionCamera");});Do(()=>Save("next-petal-back"));
        KeyTap(Key.Z);Do(()=>{Phase("FirstPoint");Check(((PaperSheet)Field("sheet")).Count==21,"One undo did not restore all preparations");});
        Do(SeedPair);Do(Assist);Do(()=>{Phase("PreparingClearance");game.GetType().GetMethod("CloseFolding",H).Invoke(game,null);Check(Field("clearanceTask")==null,"Close retained search");});
        Do(()=>{Debug.Log("[ClearanceInputPlay] PASS async assist callback, cancel, stale result disposal, actual pointer drag, partial commit, edit, Escape, C closure, one undo, close cancellation; no OS IMGUI click asserted.");Finish(0);});
    }

    static void Finish(int code){
        SessionState.SetBool(Flag,false);EditorApplication.update-=Step;
        string path=Environment.GetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS");if(path!=null)foreach(string ext in new[]{"",".bak",".tmp"})File.Delete(path+ext);
        EditorApplication.Exit(code);
    }
}

