using System;
using UnityEditor;
using UnityEngine;
namespace FlyEditor.Editor
{
    // Deterministic shared technique baseline. Full model/input cases are separate.
    public static class OrigamiTechniqueBaseline
    {
        public static void Run()
        {
            try {
                PetalFoldingVerification.Regression();
                ReverseDirectionVerification.Run();
                WingSideVerification.Run();
                WingSideVerification.RunFlat();
                JetEdgeVerification.Run();
                Debug.Log("[OrigamiTechniqueBaseline] PASS");
                EditorApplication.Exit(0);
            } catch(Exception error) {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }
    }
}
