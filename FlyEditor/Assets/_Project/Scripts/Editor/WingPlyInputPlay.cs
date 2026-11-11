using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using FlyEditor.Flight;
public static class WingPlyInputPlay
{
    const string Flag="FlyEditor.WingPlyInput";
    static int stage;static double next,deadline;static Mouse mouse;static Keyboard keyboard;
    static readonly System.Collections.Generic.List<Action> steps=new System.Collections.Generic.List<Action>();
    static System.Collections.Generic.IReadOnlyList<PaperSheet.Face> beforeWing;
    static PaperFlightController game;static Camera deskCam;static string folder;
    public static void RunTapered(){SessionState.SetBool(Flag+".Tapered",true);Run();}
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
                folder=Environment.GetEnvironmentVariable("FLYEDITOR_WING_INPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/independent-wings/input"));
            }
            if(steps.Count==0){Directory.CreateDirectory(folder);MakeSteps();}
            steps[stage++]();next=EditorApplication.timeSinceStartup+.12;
        }catch(Exception ex){if(deskCam && folder!=null)Save("failure");Debug.LogError("[WingPlyInputPlay] action "+stage);Debug.LogException(ex);Finish(1);}
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
    static void MakeSteps(){
        bool tapered=SessionState.GetBool(Flag+".Tapered",false);
        float contactY=tapered?2.5f:3.5f;
        var wingStart=new Vector2(.1f,tapered?3.5f:4.5f);
        var wingEnd=tapered?new Vector2(11,5.5f):new Vector2(7,4.5f);
        Do(()=>Check(game.OpenFolding(),"Open desk"));
        Do(()=>deskCam=GameObject.Find("종이 작업대 카메라").GetComponent<Camera>());
        Do(()=>{
            var paper=new PaperSheet();
            foreach(var st in new[]{new FoldStep{creaseNormal=Vector2.one,creaseOffset=16.5f,side=1},new FoldStep{creaseNormal=new Vector2(1,-1),creaseOffset=5.5f,side=1}}){var step=st;step.continuousHinge=step.neutralContacts=step.compactLayers=true;Check(paper.Finish(paper.Plan(step),180),"nose fold");}
            var n=new Vector2(Mathf.Sin((tapered?Mathf.Atan(.5f):22.5f*Mathf.Deg2Rad)),Mathf.Cos((tapered?Mathf.Atan(.5f):22.5f*Mathf.Deg2Rad)));foreach(int sign in new[]{1,-1}){n.y=Mathf.Abs(n.y)*sign;Check(paper.Finish(paper.Plan(new FoldStep{creaseNormal=n,creaseOffset=Vector2.Dot(n,new Vector2(11,5.5f)),side=1,continuousHinge=true,neutralContacts=true,compactLayers=true}),180),"narrow");}
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var desk=game.GetType().GetField("folding",flags).GetValue(game);
            desk.GetType().GetField("sheet",flags).SetValue(desk,paper);desk.GetType().GetMethod("Browse",flags).Invoke(desk,null);
        });
        Click(0,5.5f);Click(11,5.5f);KeyTap(Key.Digit1);DragFace(2.5f,7.5f,180,5);
        Do(()=>Save("body-folded"));
        Click(wingStart.x,wingStart.y);Do(()=>Phase("SecondPoint"));Click(wingEnd.x,wingEnd.y);Do(()=>Phase("PickSide"));KeyTap(Key.Digit2);Do(()=>Check(Field("scope").ToString()=="Top","Individual scope not selected"));Click(2.5f,contactY);
        Do(()=>ClickPaper(2.5f,contactY,true));Do(()=>Button(pointer,false));Do(()=>Phase("AdjustAngle"));
        KeyTap(Key.Digit1);Do(()=>Check(Field("scope").ToString()=="Stack","Zero-angle whole scope switch"));
        KeyTap(Key.Digit2);Do(()=>Check(Field("scope").ToString()=="Top","Zero-angle individual scope switch"));Click(2.5f,contactY);
        Do(()=>{beforeWing=((PaperSheet)Field("sheet")).Faces;Save("top-picked");});
        Do(()=>ClickPaper(2.5f,contactY,true));
        Do(()=>{Phase("AdjustAngle");var t=(PaperSheet.Turn)Field("turn");Check(t.step.separateFoldSides,"Missing wing scope");Button(pointer+Vector2.up*(76/.65f)*Mathf.Min(Screen.width/1440f,Screen.height/900f),true);});
        Do(()=>Save("top-opening"));Do(()=>Button(pointer,false));
        Do(()=>{var p=(PaperSheet)Field("sheet");Check(p.Count==6,"top wing commit");FixedOther(p,false);FlyEditor.Editor.ReverseDirectionVerification.Geometry(p.Faces);FlyEditor.Editor.PetalFoldingVerification.Replay(p);});
        Do(()=>{
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var desk=game.GetType().GetField("folding",flags).GetValue(game);
            desk.GetType().GetField("pitch",flags).SetValue(desk,303f);desk.GetType().GetMethod("PositionCamera",flags).Invoke(desk,null);
        });
        Click(wingStart.x,wingStart.y);Do(()=>Phase("SecondPoint"));Click(wingEnd.x,wingEnd.y);Do(()=>Phase("PickSide"));KeyTap(Key.Digit2);Click(2.5f,contactY);
        Do(()=>{beforeWing=((PaperSheet)Field("sheet")).Faces;Save("bottom-picked");});
        Do(()=>ClickPaper(2.5f,contactY,true));
        Do(()=>{Phase("AdjustAngle");Check(((PaperSheet.Turn)Field("turn")).step.separateFoldSides,"Missing lower scope");Button(pointer+Vector2.up*(82/.65f)*Mathf.Min(Screen.width/1440f,Screen.height/900f),true);});
        Do(()=>Save("bottom-opening"));Do(()=>Button(pointer,false));
        Do(()=>{var p=(PaperSheet)Field("sheet");Check(p.Count==7,"bottom wing commit");FixedOther(p,true);FlyEditor.Editor.ReverseDirectionVerification.Geometry(p.Faces);FlyEditor.Editor.PetalFoldingVerification.Replay(p);Save("both-wings");});
        KeyTap(Key.Z);Do(()=>{Check(((PaperSheet)Field("sheet")).Count==6,"Lower wing undo");});
        KeyTap(Key.E);KeyTap(Key.Escape);Do(()=>{Check(((PaperSheet)Field("sheet")).Count==6,"Edit cancel changed recipe");Debug.Log("[WingInput] "+(tapered?"TAPERED ":"PARALLEL ")+"PASS narrowed nose, central fold, whole/individual switching at zero-angle preview, face pick, both wing drags, opposite wing material unchanged, replay, lower undo, upper edit/cancel. Camera set below for lower-side visibility; paper gestures use actual Input System.");Finish(0);});
    }
    static void FixedOther(PaperSheet after,bool upper){
        int count=0;foreach(var f in beforeWing){Vector2 centre=Vector2.zero;foreach(var q in f.points)centre+=q;centre/=f.points.Length;var m=f.material.MultiplyPoint3x4(new Vector3(centre.x,0,centre.y));if((m.z>5.5f)!=upper)continue;
            foreach(var q in f.points){var original=f.material.MultiplyPoint3x4(new Vector3(q.x,0,q.y));bool found=false;foreach(var g in after.Faces){var a=g.material.inverse.MultiplyPoint3x4(original);var p=new Vector2(a.x,a.z);if(!g.Contains(p))continue;Check(Vector3.Distance(f.World(q,0),g.World(p,0))<.002f,"Other wing moved in actual input");found=true;}Check(found,"Missing opposite wing");count++;}}
        Check(count>0,"No fixed wing samples");
    }

    static void Finish(int code){
        SessionState.SetBool(Flag,false);SessionState.SetBool(Flag+".Tapered",false);EditorApplication.update-=Step;
        string path=Environment.GetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS");if(path!=null)foreach(string ext in new[]{"",".bak",".tmp"})File.Delete(path+ext);
        EditorApplication.Exit(code);
    }
}
