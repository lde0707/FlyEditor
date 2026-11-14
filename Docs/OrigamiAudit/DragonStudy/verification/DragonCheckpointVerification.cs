using System;using System.IO;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class DragonCheckpointVerification {
 public static void Run(){try{var a=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_RECIPE"))).paperFolds;var s=new PaperSheet();int poses=0;
 for(int i=0;i<a.Length;i++){var st=a[i];var before=s.Faces;if(st.cornerPull){var p=PaperCornerPull.Create(s,st);if(p==null)throw new Exception("plan "+i);for(int angle=0;angle<=180;angle+=5){if(!p.Pose(Mathf.Sign(st.degrees)*angle))throw new Exception("pose "+i+"/"+angle);ReverseDirectionVerification.Geometry(p.Preview);poses++;}}
 if(!s.ApplyStep(st))throw new Exception("commit "+i);ReverseDirectionVerification.Geometry(s.Faces);PetalFoldingVerification.Replay(s);if(!s.Undo()||!ReferenceEquals(before,s.Faces)||!s.ApplyStep(st))throw new Exception("undo/redo "+i);
 }Debug.Log("[DragonCheckpoint] PASS "+a.Length+" recorded folds, "+poses+" pull poses, material area/seams, replay and undo/redo. Dragon remains incomplete.");EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
