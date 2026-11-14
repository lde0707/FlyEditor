using System;using System.IO;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
[InitializeOnLoad] internal static class EdgeAlignmentSaveInspection {
 static EdgeAlignmentSaveInspection(){EditorApplication.delayCall+=Capture;}
 static void Capture(){try{var store=ResearchFileStore.OpenCurrent();var root=Path.Combine(Application.dataPath,"../Logs");Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"EdgeAlignmentSavedPaper.json"),JsonUtility.ToJson(store.Load(),true));File.WriteAllText(Path.Combine(root,"EdgeAlignmentSavedPaperPath.txt"),store.Path);}catch(Exception e){Debug.LogException(e);}}
}
