using System.IO;
using System.Reflection;
using FlyEditor.Flight;
using UnityEditor;
using UnityEngine;
namespace FlyEditor.Editor
{
    // Replays a documented recipe into the workbench. The player's saved paper
    // is only replaced if they explicitly apply the sample in the workbench.
    public static class LilyStudyReview
    {
        private const string Menu="Tools/FlyEditor/Validation/Open lily study - INCOMPLETE (Play Mode)";
        [MenuItem(Menu,true)] private static bool Available()=>EditorApplication.isPlaying;
        [MenuItem(Menu)] public static void Open()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/lily-study/partial.json"));
            var save=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(path));
            var sample=PaperSheet.Replay(save.paperFolds);
            if(sample.Count!=save.paperFolds.Length)throw new System.InvalidOperationException("Lily study recipe could not replay completely.");
            var game=Object.FindAnyObjectByType<PaperFlightController>();
            if(!game || (!game.FoldingOpen && !game.OpenFolding())){Debug.LogWarning("Open the sample while the flight is ready.");return;}
            var desk=typeof(PaperFlightController).GetField("folding",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(game);
            desk.GetType().GetMethod("Open",BindingFlags.Instance|BindingFlags.Public).Invoke(desk,new object[]{sample.Recipe()});
            Debug.Log("[LilyStudy] Replayed "+sample.Count+" folds in workbench. INCOMPLETE: two petals folded; remaining shaping unfinished; saved aircraft unchanged.");
        }
    }
}
