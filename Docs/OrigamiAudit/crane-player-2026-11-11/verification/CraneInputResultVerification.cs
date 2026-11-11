using System;using System.IO;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class CraneInputResultVerification {
 static void Check(bool v,string m){if(!v)throw new Exception(m);}
 static Vector3 At(PaperSheet s,Vector2 material){foreach(var f in s.Faces){var p=f.material.inverse.MultiplyPoint3x4(new Vector3(material.x,0,material.y));var q=new Vector2(p.x,p.z);if(f.Contains(q))return f.World(q,0);}throw new Exception("missing material sample "+material);}
 public static void Run(){try{
 var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_RECIPE"))).paperFolds;
 Check(recipe.Length==16,"expected 16 player folds");var s=new PaperSheet();
 var samples=new[]{new Vector2(0,0),new Vector2(11,11),new Vector2(.5f,.5f),new Vector2(10.5f,10.5f)};
 for(int i=0;i<recipe.Length;i++){
 var before=new Vector3[samples.Length];for(int j=0;j<samples.Length;j++)before[j]=At(s,samples[j]);
 Check(s.ApplyStep(recipe[i]),"input recipe rejected at "+(i+1));ReverseDirectionVerification.Geometry(s.Faces);
 if(i>=14){for(int j=0;j<samples.Length;j++){float d=Vector3.Distance(before[j],At(s,samples[j]));Debug.Log($"[InputResult] fold={i+1} material={samples[j]} movement={d:R}");bool moving=(i==14)==(j%2==0);Check(moving?d>2f:d<.0001f,"wrong wing moved at fold "+(i+1));}}
 }
 PetalFoldingVerification.Replay(s);Check(s.Undo()&&s.Count==15,"wing undo");Check(s.ApplyStep(recipe[15]),"wing redo");
 WingSideVerification.Run();WingSideVerification.RunFlat();
 Debug.Log("[InputResult] PASS 16 input-produced folds: area, seams, replay, undo; existing wing regression suites.");EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
