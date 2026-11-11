using System;using System.IO;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class LilyStudyVerification {
 static void Check(bool v,string text){if(!v)throw new Exception("[LilyStudyVerification] "+text);}
 [UnityEditor.MenuItem("Tools/FlyEditor/Validation/Lily study check (incomplete)")]
 public static void Run(){var root=Environment.GetEnvironmentVariable("FLYEDITOR_LILY") ?? Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/lily-study"));var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(root,"partial.json"))).paperFolds;var s=new PaperSheet();int samples=0,turnSamples=0,blockedDirections=0;
 for(int i=0;i<recipe.Length;i++){var st=recipe[i];var before=s.Faces;
 if(st.cornerPull&&(st.exactSquash||st.partialPetalBacking)){var p=PaperCornerPull.Create(s,st);Check(p!=null,"plan "+i);for(int a=0;a<=180;a+=5){Check(p.Pose(Mathf.Sign(st.degrees)*a),"sweep "+i+"/"+a);ReverseDirectionVerification.Geometry(p.Preview);samples++;}foreach(int a in new[]{90,15,150,0,180}){Check(p.Pose(Mathf.Sign(st.degrees)*a),"scrub "+i);ReverseDirectionVerification.Geometry(p.Preview);}Check(!p.Pose(float.NaN)&&!p.Pose(181),"invalid angle");}
 if(st.neutralContacts && !st.cornerPull){var plan=s.Plan(st);Check(plan!=null,"turn plan "+i);if(Mathf.Abs(plan.Constrain(-st.degrees))<1)blockedDirections++;foreach(float fraction in new[]{.25f,.5f,.75f}){var trial=PaperSheet.Replay(s.Recipe());var part=st;part.degrees*=fraction;Check(trial.ApplyStep(part),"neutral turn "+i);ReverseDirectionVerification.Geometry(trial.Faces);PetalFoldingVerification.Replay(trial);turnSamples++;}}
 Check(s.ApplyStep(st),"commit "+i);ReverseDirectionVerification.Geometry(s.Faces);PetalFoldingVerification.Replay(s);Check(s.Undo()&&ReferenceEquals(before,s.Faces),"undo "+i);Check(s.ApplyStep(st),"recommit "+i);
 }
 Check(s.Count==recipe.Length,"replay length");Check(blockedDirections>0,"stationary layer no longer blocks opposite motion");File.WriteAllText(Path.Combine(root,"verified-partial.json"),JsonUtility.ToJson(new ResearchSave{paperFolds=s.Recipe()},true));Debug.Log("[LilyStudyVerification] PASS "+s.Count+" committed folds, "+samples+" pull poses, "+turnSamples+" rigid turn poses, "+blockedDirections+" blocked opposite directions; area, all seams, replay and undo. Model NOT complete.");

 }
}
