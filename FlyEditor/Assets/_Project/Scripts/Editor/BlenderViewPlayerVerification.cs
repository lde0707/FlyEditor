using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
public static class BlenderViewPlayerVerification {
 const BindingFlags H=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 [Serializable]public class View {public bool orthographic;public Vector3 cameraForward,cameraUp,cameraPosition,panOffset;public float zoom;public string axisView;}
 static string root;static string At(string f)=>Path.Combine(root,"session-a",f);
 static View Read(int n)=>JsonUtility.FromJson<View>(File.ReadAllText(At("geometry-"+n.ToString("000")+".json")));
 static ThreePlayerFlowVerification.Geometry Geometry(int n)=>JsonUtility.FromJson<ThreePlayerFlowVerification.Geometry>(File.ReadAllText(At("geometry-"+n.ToString("000")+".json")));
 static void Check(bool ok,string text){if(!ok)throw new Exception("[BlenderViewPlayer] "+text);}
 static void Dir(int id,Vector3 forward){Check(Vector3.Dot(Read(id).cameraForward,forward)>.99999f,"actual key direction "+id);}
 public static void Verify(){root=Environment.GetEnvironmentVariable("FLYEDITOR_BLENDER_PLAYER_ROOT");Check(!string.IsNullOrEmpty(root),"input evidence absent");
  Dir(2,Vector3.forward);Dir(3,Vector3.back);Dir(4,Vector3.left);Dir(5,Vector3.right);Dir(6,Vector3.down);Dir(7,Vector3.up);Dir(8,Vector3.down);Dir(10,Vector3.up);Dir(11,Vector3.down);
  for(int i=2;i<=8;i++)Check(Read(i).orthographic,"axis key lost orthographic "+i);
  foreach(int i in new[]{9,10,11,12,19})Check(!Read(i).orthographic,"perspective key state "+i);
  Dir(12,new Vector3(-.258819f,0,.965926f));Check(Vector3.Dot(Read(12).cameraForward,Read(13).cameraForward)>.99999f&&(Read(13).panOffset-Read(12).panOffset).magnitude>.1f,"Ctrl orbit key did not pan");
  Check(Vector3.Dot(Read(13).cameraForward,Read(14).cameraForward)>.99999f&&Mathf.Abs(Vector3.Angle(Read(13).cameraUp,Read(14).cameraUp)-15)<.01f,"Shift orbit key did not roll");
  Check(Read(15).zoom>Read(14).zoom&&Mathf.Abs(Read(16).zoom-Read(14).zoom)<.00001f,"actual keypad zoom");Check(Vector3.Angle(Read(16).cameraForward,Read(17).cameraForward)>20,"held keypad key did not repeat");
  Check(Read(18).orthographic&&Read(18).panOffset.sqrMagnitude<.00001f&&Read(18).zoom==1,"Home frame all");
  var equal=typeof(FragmentBranchVerification).GetMethod("Equal",H);for(int i=2;i<=22;i++)equal.Invoke(null,new object[]{Geometry(1).sheet,Geometry(i).sheet,Matrix4x4.identity});
  typeof(FragmentBranchVerification).GetMethod("PlayerTransition",H).Invoke(null,new object[]{root,"a",21,23});
  var g=Geometry(21);Check(g.moving.Length==1,"perspective box selected bottom ply");float green=0;
  for(int i=0;i<g.meshIndices.Length;i+=3){int a=g.meshIndices[i],b=g.meshIndices[i+1],c=g.meshIndices[i+2];var ink=g.meshColors[a];if(Mathf.Abs(ink.r-.62f)>.001f||Mathf.Abs(ink.g-.83f)>.001f||Mathf.Abs(ink.b-.73f)>.001f)continue;var cross=Vector3.Cross(g.meshVertices[b]-g.meshVertices[a],g.meshVertices[c]-g.meshVertices[a]);if(cross.sqrMagnitude<1e-10f||Mathf.Abs(cross.normalized.y)<.99f)continue;green+=cross.magnitude*.5f;}
  Check(Mathf.Abs(green-60.5f)<.01f,"rendered selected area diverged in perspective");
  var saved=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(At("progress-024.json"))).paperFolds;Check(saved.Length==2&&saved[1].degrees==26&&saved[1].selectedFaces.Length==1,"perspective mouse fold/save failed");var replay=PaperSheet.Replay(saved);Check(replay.Count==2,"perspective saved fold replay");FlyEditor.Editor.ReverseDirectionVerification.Geometry(replay.Faces);
  Debug.Log("[BlenderViewPlayer] PASS 25 actual-input captures: six axes/Ctrl/opposites, perspective retained, orbit/pan/roll/zoom/repeat/Home; paper unchanged during view changes, perspective visible-ply box + 26 degree drag, exact green/moving/fixed material, isolated save/replay.");
 }
 public static void Run(){try{BlenderFoldViewVerification.Verify();BoxSelectionVerification.Verify();Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
