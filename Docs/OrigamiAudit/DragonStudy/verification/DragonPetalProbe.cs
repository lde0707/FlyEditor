using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
public static class DragonPetalProbe {
 static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 public static void Run(){try{var root=Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON");var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(root+"/checkpoint-seven.json")).paperFolds;var s=PaperSheet.Replay(recipe);var step=JsonUtility.FromJson<FoldStep>(File.ReadAllText(root+"/intersection-petal.json"));
 var type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.PaperPetalFold");var obj=type.GetMethod("Create",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{s,step});Debug.Log("[DragonPetal] petal="+(obj!=null));var basis=s.Faces[step.anchor];var constructor=type.GetConstructors(F)[0];var observer=constructor.Invoke(new object[]{s,basis});var locate=type.GetMethod("Locate",F);
 foreach(var name in new[]{"pullTip","pullStop","pullLeft","pullRight"}){var q=(Vector2)typeof(FoldStep).GetField(name).GetValue(step);var m=basis.material.MultiplyPoint3x4(new Vector3(q.x,0,q.y));Debug.Log("[DragonPetal] "+name+" raw="+q+" material="+m+" located="+locate.Invoke(observer,new object[]{m}));}
 var p=PaperCornerPull.Create(s,step);Debug.Log("[DragonPetal] corner="+(p!=null)+" exact="+p?.UsesExactSquash+" petal="+p?.UsesPetalFold);
 if(p!=null)foreach(int sign in new[]{1,-1}){for(int a=0;a<=180;a+=5){if(p.Pose(sign*a))continue;Debug.Log("[DragonPetal] first fail="+(sign*a));if(obj!=null)Debug.Log("[DragonPetal] reason="+type.GetField("Failure",F).GetValue(obj));break;}}
 EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
