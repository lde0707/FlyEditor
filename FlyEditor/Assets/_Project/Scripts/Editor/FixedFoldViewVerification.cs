using System;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
public static class FixedFoldViewVerification {
 public static void Run(){try{
  var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
  var go=new GameObject("fixed-view-verification");var owner=go.AddComponent<PaperFlightController>();var mat=new Material(Shader.Find("Unlit/Color"));
  var type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");var desk=Activator.CreateInstance(type,flags,null,new object[]{owner,mat},null);
  type.GetMethod("Open",flags).Invoke(desk,new object[]{Array.Empty<FoldStep>()});type.GetMethod("Layout",flags).Invoke(desk,null);
  foreach(int axis in new[]{1,3,7})foreach(bool opposite in new[]{false,true}){
   type.GetMethod("SnapView",flags).Invoke(desk,new object[]{axis,opposite});var camera=(Camera)type.GetField("camera",flags).GetValue(desk);
   var expected=axis==1?Vector3.forward:axis==3?Vector3.left:Vector3.down;if(opposite)expected=-expected;
   if(Vector3.Dot(camera.transform.forward,expected)<.99999f)throw new Exception("axis direction");
   int visible=0;foreach(bool v in (List<bool>)type.GetField("visiblePoints",flags).GetValue(desk))if(v)visible++;
   if(axis==7?visible==0:visible!=0)throw new Exception("edge-on visibility "+axis+" count="+visible);
  }
  type.GetMethod("Dispose",flags).Invoke(desk,null);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mat);
  Debug.Log("[FixedView] PASS six orthographic views, visible front/back points, no invisible face points in edge-on views.");EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
