using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using FlyEditor.Flight;
public static class EdgePocketInputPlay
{
    const string Flag="FlyEditor.EdgePocketInput";
    static int stage;static double next,deadline;static Mouse mouse;static Keyboard keyboard;
    static readonly System.Collections.Generic.List<Action> steps=new System.Collections.Generic.List<Action>();
    static System.Collections.Generic.IReadOnlyList<PaperSheet.Face> beforeWing;
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
                folder=Environment.GetEnvironmentVariable("FLYEDITOR_EDGE_INPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/crane-edge/input"));
            }
            if(steps.Count==0){Directory.CreateDirectory(folder);MakeSteps();}
            steps[stage++]();next=EditorApplication.timeSinceStartup+.12;
        }catch(Exception ex){if(deskCam && folder!=null)Save("failure");Debug.LogError("[EdgePocketInputPlay] action "+stage);Debug.LogException(ex);Finish(1);}
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

    static Vector2 edgeOrigin;
    static void EdgeDrag(float amount){
        var vector=(Vector2)Field("pullVector");float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
        Button(edgeOrigin+new Vector2(vector.x,-vector.y)*amount*scale,true);
    }
    static void MakeSteps(){
        Do(()=>Check(game.OpenFolding(),"Open desk"));Do(()=>deskCam=GameObject.Find("종이 작업대 카메라").GetComponent<Camera>());
        Do(()=>{
            var paper=new PaperSheet();Check(paper.ApplyStep(new FoldStep{creaseNormal=Vector2.one,creaseOffset=11,side=1}),"diagonal");Check(paper.ApplyStep(new FoldStep{creaseNormal=new Vector2(1,-1),side=-1}),"half triangle");
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var desk=game.GetType().GetField("folding",flags).GetValue(game);
            desk.GetType().GetField("sheet",flags).SetValue(desk,paper);desk.GetType().GetMethod("Browse",flags).Invoke(desk,null);
            beforeWing=paper.Faces;
        });
        Do(()=>ClickPaper(4,.04f,false));Do(()=>{Check(Field("edgeCandidate")!=null,"Visible mouth edge not found");Save("edge-hover");});
        Do(()=>{ClickPaper(4,.04f,true);edgeOrigin=pointer;});Do(()=>{Phase("PullCorner");Check((int)Field("edgeMotionSign")==1,"wrong opening direction");Button(pointer+Vector2.up*8,true);});
        Do(()=>EdgeDrag(.5f));Do(()=>Save("pocket-open"));Do(()=>EdgeDrag(.9f));Do(()=>EdgeDrag(.25f));
        Do(()=>{Check((float)Field("angle")<60,"backwards scrub did not reduce fold");EdgeDrag(1.97f);});
        Do(()=>Save("pocket-flat-preview"));Do(()=>Button(pointer,false));
        Do(()=>{var p=(PaperSheet)Field("sheet");Check(p.Count==3,"edge fold commit");Check(Mathf.Abs(p.Recipe()[2].degrees-180)<.01f,"end snap");
            FlyEditor.Editor.ReverseDirectionVerification.Geometry(p.Faces);FlyEditor.Editor.PetalFoldingVerification.Replay(p);Save("pocket-committed");
            foreach(var fixedFace in new[]{beforeWing[0],beforeWing[1]})foreach(var q in fixedFace.points){var m=fixedFace.material.MultiplyPoint3x4(new Vector3(q.x,0,q.y));bool found=false;
                foreach(var f in p.Faces){var a=f.material.inverse.MultiplyPoint3x4(m);var local=new Vector2(a.x,a.z);if(!f.Contains(local))continue;Check(Vector3.Distance(fixedFace.World(q,0),f.World(local,0))<.002f,"unselected packet moved");found=true;}Check(found,"lost fixed material");}
        });
        KeyTap(Key.Z);Do(()=>Check(((PaperSheet)Field("sheet")).Count==2,"undo"));
        Do(()=>ClickPaper(4,.04f,true));Do(()=>Button(pointer,false));Do(()=>Phase("PullCorner"));
        KeyTap(Key.Escape);Do(()=>{Phase("FirstPoint");Check(((PaperSheet)Field("sheet")).Count==2,"cancel changed sheet");Debug.Log("[EdgePocketInput] PASS visible edge hover, click/drag, forward/back scrub, snap 180, stationary other packet, geometry/replay, undo, click-select/cancel.");Finish(0);});
    }
    static void Finish(int code){
        SessionState.SetBool(Flag,false);EditorApplication.update-=Step;
        string path=Environment.GetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS");if(path!=null)foreach(string ext in new[]{"",".bak",".tmp"})File.Delete(path+ext);
        EditorApplication.Exit(code);
    }
}
