using System;using UnityEditor;using UnityEngine;using FlyEditor.Editor;
public static class DragonContinuationRegression
{
    public static void Run(){try{
        MaterialChartContinuationVerification.Verify();ContactPacketVerification.Verify();DragonContinuationPlayerVerification.Verify();
        SequenceConsistencyVerification.Verify();SequenceSelectionVerification.Verify();ThreePlayerFlowVerification.Verify();
        PlyPartitionVerification.Verify();CoordinatedSurfaceVerification.Verify();CoordinatedPlayerReplayVerification.Verify();OrigamiTechniqueBaseline.Run();
    }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
