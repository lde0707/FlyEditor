using System;
using System.IO;
using System.Reflection;
using FlyEditor.Flight;
using UnityEditor;
using UnityEngine;
namespace FlyEditor.Editor
{
    // The sample is kept separate from the player's aircraft until Apply.
    public static class JetStudyReview
    {
        private const string Menu="Tools/FlyEditor/Validation/Open jet study - INCOMPLETE (Play Mode)";
        [MenuItem(Menu,true)] private static bool Available()=>EditorApplication.isPlaying;
        [MenuItem(Menu)] public static void Open()
        {
            string folder=Environment.GetEnvironmentVariable("FLYEDITOR_JET");
            if(string.IsNullOrEmpty(folder))folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/jet-study"));
            var save=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(folder,"partial.json")));
            var sample=PaperSheet.Replay(save.paperFolds);
            if(sample.Count!=save.paperFolds.Length)throw new InvalidOperationException("Jet study recipe could not replay completely.");
            var game=UnityEngine.Object.FindAnyObjectByType<PaperFlightController>();
            if(!game || (!game.FoldingOpen && !game.OpenFolding())){Debug.LogWarning("Open the sample while the flight is ready.");return;}
            var desk=typeof(PaperFlightController).GetField("folding",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(game);
            desk.GetType().GetMethod("Open",BindingFlags.Instance|BindingFlags.Public).Invoke(desk,new object[]{sample.Recipe()});
            Debug.Log("[JetStudy] Replayed "+sample.Count+" actions. INCOMPLETE study: main wings and tips unfinished; saved aircraft unchanged.");
        }
    }
}
