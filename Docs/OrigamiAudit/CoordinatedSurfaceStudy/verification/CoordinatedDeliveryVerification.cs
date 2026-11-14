using System;using UnityEditor;using UnityEngine;using FlyEditor.Editor;
public static class CoordinatedDeliveryVerification {
 public static void Run(){try{CoordinatedPlayerReplayVerification.Verify();CoordinatedSurfaceVerification.RejectedHints();PlyPartitionVerification.Verify();OrigamiTechniqueBaseline.Run();}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
