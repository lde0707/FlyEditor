using System;using System.IO;using System.Reflection;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class WingSearch {
 static readonly BindingFlags H=BindingFlags.Static|BindingFlags.NonPublic;
 static Vector2 Local(PaperSheet.Face f,Vector2 p){var v=f.material.inverse.MultiplyPoint3x4(new Vector3(p.x,0,p.y));return new Vector2(v.x,v.z);}
 static Vector3 At(PaperSheet s,Vector2 p){foreach(var f in s.Faces){var q=Local(f,p);if(f.Contains(q))return f.World(q,0);}throw new Exception("missing");}
 public static bool Wing(PaperSheet s,Vector2 tip,float h,out PaperSheet result,float force=0){result=null;var other=tip.x==0?new Vector2(11,0):new Vector2(0,11);var oldOther=At(s,other);var oldHead=At(s,Vector2.zero);var oldTail=At(s,Vector2.one*11);
 for(int i=0;i<s.Faces.Count;i++){var f=s.Faces[i];if(!f.Contains(Local(f,tip)))continue;var step=new FoldStep{singlePanel=true,connectedPanel=true,continuousHinge=true,compactLayers=true,anchor=i,flap=i+1,creaseNormal=Vector2.one.normalized,creaseOffset=h*Mathf.Sqrt(2),side=1};
 var t=s.Plan(step);if(t==null)continue;foreach(float angle in force==0?new[]{90f,-90f}:new[]{force}){if(Mathf.Abs(t.Constrain(angle)-angle)>.001f)continue;step.degrees=angle;var trial=PaperSheet.Replay(s.Recipe());if(!trial.ApplyStep(step))continue;if(Vector3.Distance(At(trial,other),oldOther)>.002f||Vector3.Distance(At(trial,Vector2.zero),oldHead)>.002f||Vector3.Distance(At(trial,Vector2.one*11),oldTail)>.002f)continue;result=trial;return true;}}
 return false;}
 public static void Run(){var root=Environment.GetEnvironmentVariable("FLYEDITOR_CRANE_FINISH");var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(root,"../.crane-current/results/10-narrow.json"))).paperFolds;
 foreach(int side in new[]{1,-1})foreach(int sign in new[]{1,-1}){var s=PaperSheet.Replay(recipe);if(!s.ApplyStep(new FoldStep{creaseNormal=new Vector2(1,-1),side=side,degrees=sign*180,compactLayers=true})){Debug.Log("[Search] book blocked");continue;}
 bool ok=true;foreach(var k in new[]{0,1,2}){var tip=k==1?Vector2.one*11:Vector2.zero;var stop=Vector2.one*(k==0?2.75f:k==1?8.25f:.75f);var p=PacketFoldingVerification.Plan(s,tip,stop);if(p==null||!s.FinishReverse(p,180)){ok=false;break;}}if(!ok){Debug.Log("[Search] returns blocked side="+side+" sign="+sign);continue;}
 for(int n=0;n<=32;n++){float h=3.9f+n*.05f;if(!Wing(s,new Vector2(0,11),h,out var one)||!Wing(one,new Vector2(11,0),h,out var both))continue;PacketFoldingVerification.Metrics(both);PetalFoldingVerification.Replay(both);string name="candidate-"+side+"-"+sign;File.WriteAllText(Path.Combine(root,name+".json"),JsonUtility.ToJson(new ResearchSave{paperFolds=both.Recipe()},true));typeof(CraneCurrentAudit).GetField("folder",H).SetValue(null,root);typeof(CraneCurrentAudit).GetMethod("Render",H).Invoke(null,new object[]{both,name,null});Debug.Log("[Search] lowest independent hinge="+h+" side="+side+" sign="+sign);break;}
 }
 }
}
