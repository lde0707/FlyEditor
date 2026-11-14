using System;using System.IO;using System.Collections.Generic;using System.Reflection;using UnityEngine;using FlyEditor.Flight;
// Read-only observer for the screen-input harness. Never applies a fold or sets a selection.
public static class PlayerMaterialObservation {
 [Serializable]public class Sample {public int face;public long region;public Vector3 material,world;}
 [Serializable]public class Snapshot {public int folds;public float angle;public Sample[] sheet,moving,fixedFaces;}
 static Sample[] Samples(IReadOnlyList<PaperSheet.Face> faces){var result=new List<Sample>();if(faces==null)return result.ToArray();
 for(int i=0;i<faces.Count;i++){var f=faces[i];Vector2 centre=Vector2.zero;foreach(var p in f.points)centre+=p;centre/=f.points.Length;
 var points=new List<Vector2>{centre};foreach(var p in f.points)points.Add(Vector2.Lerp(p,centre,.01f));foreach(var p in points)result.Add(new Sample{face=i,region=f.MaterialRegionId,material=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y)),world=f.World(p,0)});}
 return result.ToArray();}
 public static void Write(string folder,int id,object desk){if(desk==null)return;var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=desk.GetType();var sheet=(PaperSheet)type.GetField("sheet",flags).GetValue(desk);if(sheet==null)return;
 var turn=type.GetField("turn",flags).GetValue(desk) as PaperSheet.Turn;
 var value=new Snapshot{folds=sheet.Count,angle=(float)type.GetField("angle",flags).GetValue(desk),sheet=Samples(sheet.Faces),moving=Samples(turn?.movingFaces),fixedFaces=Samples(turn?.fixedFaces)};
 File.WriteAllText(Path.Combine(folder,"material-"+id.ToString("000")+".json"),JsonUtility.ToJson(value,true));
 }
}
