using System;using UnityEditor;using UnityEngine;using FlyEditor.Editor;
public static class PlyPartitionRegression {
 public static void Run(){try{PlyPartitionVerification.Verify();OrigamiTechniqueBaseline.Run();}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
