using System;using System.IO;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class CoordinatedPlayerReplayVerification {
 public static void Verify(){var path=Environment.GetEnvironmentVariable("FLYEDITOR_COMPLEX_PLAYER_RECIPE");var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(path)).paperFolds;
  if(recipe.Length!=8||!recipe[7].coordinatedSurface||recipe[7].degrees!=180)throw new Exception("[CoordinatedPlayerReplay] expected actual 8-fold coordinated input recipe");
  var sheet=PaperSheet.Replay(recipe);if(sheet.Count!=8)throw new Exception("[CoordinatedPlayerReplay] replay silently reset paper");
  ReverseDirectionVerification.Geometry(sheet.Faces);PetalFoldingVerification.Replay(sheet);
  if(!sheet.Undo()||sheet.Count!=7||!sheet.ApplyStep(recipe[7]))throw new Exception("[CoordinatedPlayerReplay] undo/redo failed");ReverseDirectionVerification.Geometry(sheet.Faces);
  Debug.Log("[CoordinatedPlayerReplay] PASS actual screen-input recipe: 8 recorded folds, 180 degree support fold, material seams/area, serialized replay and undo/redo.");
 }
 public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
