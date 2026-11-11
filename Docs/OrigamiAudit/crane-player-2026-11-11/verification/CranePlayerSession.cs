using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.InputSystem;using UnityEngine.InputSystem.LowLevel;using FlyEditor.Flight;
// Screen-coordinate input only. The observer may read state and save screenshots,
// but never constructs a paper, applies a recipe, chooses a face or sets an angle.
public static class CranePlayerSession {
 const string Flag="FlyEditor.CranePlayerSession";static string folder;static Mouse mouse;static Keyboard keyboard;static PaperFlightController game;static Camera cam;static EditorWindow gameView;
 static int lastId=-1,action,eventFrame=-1;static double next;static Command command;static Vector2 pointer;static bool left,right,middle;static int lastFold=-1;
 static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 [Serializable]public class Stroke {public string kind;public float x,y,scroll;public bool down;public string key;public float wait=.18f;}
 [Serializable]public class Command {public int id;public string label;public Stroke[] actions;public bool quit;}
 public static void RunPlay(){Environment.SetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS",Path.Combine(Path.GetTempPath(),"fly-player-"+Guid.NewGuid()+".json"));EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity");SessionState.SetBool(Flag,true);EditorApplication.isPlaying=true;}
 [InitializeOnLoadMethod]static void Start(){if(SessionState.GetBool(Flag,false))EditorApplication.update+=Tick;}
 static object Desk=>game==null?null:typeof(PaperFlightController).GetField("folding",Hidden).GetValue(game);
 static object Read(string name)=>Desk?.GetType().GetField(name,Hidden)?.GetValue(Desk);
 static void Pointer(Vector2 p,float scroll=0){var delta=p-pointer;pointer=p;mouse.MakeCurrent();var state=new MouseState{position=p,delta=delta,scroll=new Vector2(0,scroll)}.WithButton(MouseButton.Left,left).WithButton(MouseButton.Right,right).WithButton(MouseButton.Middle,middle);InputSystem.QueueStateEvent(mouse,state);}
 static void Tick(){try{
  if(!Application.isPlaying||EditorApplication.timeSinceStartup<next)return;
  if(game==null){game=UnityEngine.Object.FindAnyObjectByType<PaperFlightController>();if(!game||game.Model==null)return;
   folder=Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_SESSION");Directory.CreateDirectory(folder);
   InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
   mouse=InputSystem.AddDevice<Mouse>();keyboard=InputSystem.AddDevice<Keyboard>();gameView=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));gameView.Repaint();
   var dump="";for(var t=gameView.GetType();t!=null;t=t.BaseType)foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))dump+=t.Name+"."+f.Name+" : "+f.FieldType+"\\n";File.WriteAllText(Path.Combine(folder,"view-fields.txt"),dump);
   File.WriteAllText(Path.Combine(folder,"ready.txt"),"Ready "+Screen.width+"x"+Screen.height);next=EditorApplication.timeSinceStartup+1;return;
  }
  if(command==null){var path=Path.Combine(folder,"command.json");if(!File.Exists(path))return;Command value;try{using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(input))value=JsonUtility.FromJson<Command>(reader.ReadToEnd());}catch(IOException){return;}if(value.id<=lastId)return;command=value;action=0;File.AppendAllText(Path.Combine(folder,"input.jsonl"),JsonUtility.ToJson(value)+"\n");}
  if(action<command.actions.Length){if(eventFrame==Time.frameCount)return;eventFrame=Time.frameCount;var s=command.actions[action++];
   if(s.kind=="key"){var k=(Key)Enum.Parse(typeof(Key),s.key);InputSystem.QueueStateEvent(keyboard,s.down?new KeyboardState(k):new KeyboardState());}
   else if(s.kind=="ui"){
    // Send the same IMGUI events as a click in the Game View. No button method invocation.
    var viewType=gameView.GetType();var rect=(Rect?)viewType.GetProperty("targetInView",Hidden|BindingFlags.Public)?.GetValue(gameView);
    var p=new Vector2(s.x,s.y);if(rect.HasValue)p=rect.Value.position+Vector2.Scale(p,new Vector2(rect.Value.width/Screen.width,rect.Value.height/Screen.height));
    gameView.SendEvent(new Event{type=s.down?EventType.MouseDown:EventType.MouseUp,button=0,mousePosition=p});gameView.Repaint();
   }else if(s.kind!="wait"){
    Vector2 p;
    if(s.kind=="paper"||s.kind=="orbit"){var viewport=(Rect)Read("viewport");float scale=(float)Read("scale");p=new Vector2((viewport.x+s.x/1280*viewport.width)*scale,Screen.height-(viewport.y+s.y/900*viewport.height)*scale);}
    else p=new Vector2(s.x,Screen.height-s.y);
    if(s.kind=="right"||s.kind=="orbit")right=s.down;else if(s.kind=="middle")middle=s.down;else if(s.kind!="move"&&s.kind!="scroll")left=s.down;
    Pointer(p,s.scroll);
   }
   next=EditorApplication.timeSinceStartup+Math.Max(.08,s.wait);return;
  }
  cam=GameObject.Find("종이 작업대 카메라")?.GetComponent<Camera>();
  ScreenCapture.CaptureScreenshot(Path.Combine(folder,"full-"+command.id.ToString("000")+".png"));
  var sheet=Read("sheet") as PaperSheet;int count=sheet?.Count??-1;
  if(count>=0){File.WriteAllText(Path.Combine(folder,"fold-"+count.ToString("00")+".json"),JsonUtility.ToJson(new ResearchSave{paperFolds=sheet.Recipe()},true));lastFold=count;}
  if(Read("selected") is FoldStep axis)File.WriteAllText(Path.Combine(folder,"selection.json"),JsonUtility.ToJson(axis,true));

  Save(cam?.targetTexture,"command-"+command.id.ToString("000")+".png");
  var target=gameView.GetType().GetField("m_TargetTexture",Hidden)?.GetValue(gameView) as RenderTexture;Save(target,"screen.png");
  string status="id="+command.id+" label="+command.label+" screen="+Screen.width+"x"+Screen.height+" folds="+count+" phase="+Read("phase")+" angle="+Read("angle")+" notice="+Read("notice")+" scope="+Read("scope")+" gesture="+Read("gesture")+" edge="+Read("edgeIndex")+" model="+game.Model.State+" focus="+typeof(PaperFlightController).GetField("hasFocus",Hidden).GetValue(game)+" mouse="+Mouse.current.position.ReadValue()+" current="+Mouse.current.name+" frame="+Time.frameCount;
  File.WriteAllText(Path.Combine(folder,"status.txt"),status);Debug.Log("[CranePlayer] "+status);
  File.WriteAllText(Path.Combine(folder,"done.txt"),command.id.ToString());lastId=command.id;bool quit=command.quit;command=null;
  if(quit){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;var path=Environment.GetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS");if(path!=null)foreach(var ext in new[]{"",".bak",".tmp"})File.Delete(path+ext);EditorApplication.Exit(0);}
 }catch(IOException){next=EditorApplication.timeSinceStartup+.2;}catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());command=null;next=EditorApplication.timeSinceStartup+1;}}
 static void Save(RenderTexture rt,string name){if(rt==null||!rt.IsCreated())return;var old=RenderTexture.active;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);try{RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,name),tex.EncodeToPNG());}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(tex);}}
}


