using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
public static class CoordinatedFoldProbe {
 [Serializable]public class Face {public int index,layer;public long region;public Vector3[] world,material;}
 [Serializable]public class Data {public Face[] faces;public Vector3 tip,head,left,right;}
 static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 public static void Run(){try{
 var root=Environment.GetEnvironmentVariable("FLYEDITOR_COMPLEX_ROOT");var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(root+"/checkpoint-seven.json")).paperFolds;
 var s=PaperSheet.Replay(recipe);if(s.Count!=7)throw new Exception("checkpoint replay failed");
 var step=JsonUtility.FromJson<FoldStep>(File.ReadAllText(root+"/intersection-petal.json"));step.materialPartition=step.coordinatedSurface=true;
 if(Environment.GetEnvironmentVariable("FLYEDITOR_COMPLEX_HANDLES")=="boundary"){
     step.pullLeft=new Vector2(7.7781744f,0);step.pullRight=new Vector2(11,3.2218256f);
     var json=JsonUtility.ToJson(step);step=JsonUtility.FromJson<FoldStep>(json.Contains("\"coordinatedSurface\"")?json.Replace("\"coordinatedSurface\":false","\"coordinatedSurface\":true"):json.TrimEnd('}')+",\"coordinatedSurface\":true}");
 }
 var basis=s.Faces[step.anchor];var list=new List<Face>();for(int i=0;i<s.Faces.Count;i++){var f=s.Faces[i];var w=new List<Vector3>();var m=new List<Vector3>();foreach(var v in f.points){w.Add(basis.frame.inverse.MultiplyPoint3x4(f.World(v,0)));m.Add(f.material.MultiplyPoint3x4(new Vector3(v.x,0,v.y)));}list.Add(new Face{index=i,layer=f.layer,region=f.MaterialRegionId,world=w.ToArray(),material=m.ToArray()});}
 Func<Vector2,Vector3> locate=q=>{var m=basis.material.MultiplyPoint3x4(new Vector3(q.x,0,q.y));foreach(var f in s.Faces){var p=f.material.inverse.MultiplyPoint3x4(m);if(f.Contains(new Vector2(p.x,p.z)))return basis.frame.inverse.MultiplyPoint3x4(f.World(new Vector2(p.x,p.z),0));}throw new Exception("missing handle");};
 File.WriteAllText(root+"/geometry.json",JsonUtility.ToJson(new Data{faces=list.ToArray(),tip=locate(step.pullTip),head=locate(step.pullStop),left=locate(step.pullLeft),right=locate(step.pullRight)},true));
 var p=PaperCornerPull.Create(s,step);Debug.Log("[Coordinated] plan="+(p!=null)+" petal="+p?.UsesPetalFold+" coupled="+p?.UsesCoupledHinges);
 if(p==null||!p.UsesPetalFold||!p.Pose(5))throw new Exception("[Coordinated] small folded pocket cannot lift 5 degrees while keeping all material seams and lower plies");
 for(float a=0;a<=180;a+=5){if(!p.Pose(a))throw new Exception("[Coordinated] blocked at "+a);FlyEditor.Editor.ReverseDirectionVerification.Geometry(p.Preview);}
 Debug.Log("[Coordinated] PASS initial lift");EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
