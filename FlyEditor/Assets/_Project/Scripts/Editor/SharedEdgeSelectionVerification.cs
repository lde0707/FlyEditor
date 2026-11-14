using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
public static class SharedEdgeSelectionVerification {
 static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 public static void Run(){Verify(true);}
 public static void Verify(bool exit){try{var type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");var go=new GameObject("shared-edge-selection");var owner=go.AddComponent<PaperFlightController>();var material=new Material(Shader.Find("Unlit/Color"));var desk=Activator.CreateInstance(type,F,null,new object[]{owner,material},null);
 var seed=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_RECIPE"))).paperFolds;
 type.GetMethod("Open",F).Invoke(desk,new object[]{seed});type.GetMethod("Layout",F).Invoke(desk,null);
 type.GetField("pitch",F).SetValue(desk,90f);type.GetField("yaw",F).SetValue(desk,315f);type.GetField("zoom",F).SetValue(desk,.86206896f);type.GetMethod("PositionCamera",F).Invoke(desk,null);
 var rect=(Rect)type.GetField("viewport",F).GetValue(desk);int checks=0;
 foreach(float x in new[]{635f,637f,643f,645f})foreach(float y in new[]{620f,650f,680f}){
 var pos=rect.position+Vector2.Scale(new Vector2(x,y),new Vector2(rect.width/1280,rect.height/900));var uv=new Vector2((pos.x-rect.x)/rect.width,1-(pos.y-rect.y)/rect.height);
 object[] hit={uv,Vector2.zero,0};var face=type.GetMethod("SelectedFace",F).Invoke(desk,hit);if(face==null)throw new Exception("missing front surface");int expected=(int)hit[2];object[] args={pos,null};if(!(bool)type.GetMethod("NearestAlignmentEdge",F).Invoke(desk,args))throw new Exception("missing shared edge");var picked=(int)args[1].GetType().GetField("face").GetValue(args[1]);if(picked!=expected)throw new Exception($"[SharedEdge] x={x} y={y} expected visible face={expected}, selected edge face={picked}");checks++;
 }
 Debug.Log("[SharedEdge] PASS "+checks+" shared-edge clicks retain the visible side under the pointer");type.GetMethod("Dispose",F).Invoke(desk,null);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);if(exit)EditorApplication.Exit(0);
 }catch(Exception e){if(exit){Debug.LogException(e);EditorApplication.Exit(1);}else throw;}}
}
