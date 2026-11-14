using System;using UnityEditor;using UnityEngine;using FlyEditor.Editor;
public static class SequenceConsistencyRegression
{
    public static void Run(){try{
        SequenceConsistencyVerification.Verify();SequenceSelectionVerification.Verify();SequencePlayerReplayVerification.Verify();
        PlyPartitionVerification.Verify();CoordinatedSurfaceVerification.Verify();CoordinatedPlayerReplayVerification.Verify();
        OrigamiTechniqueBaseline.Run();
    }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
