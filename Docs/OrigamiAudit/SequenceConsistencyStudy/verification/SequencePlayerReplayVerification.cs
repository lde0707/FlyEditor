using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using FlyEditor.Flight;
using FlyEditor.Editor;
public static class SequencePlayerReplayVerification
{
    static void Check(bool ok,string message){if(!ok)throw new Exception("[SequencePlayer] "+message);}
    public static void Verify(){
        var folder=Environment.GetEnvironmentVariable("FLYEDITOR_SEQUENCE_PLAYER_DIR");
        FoldStep[] Load(int id)=>JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(folder,"recipe-"+id.ToString("000")+".json"))).paperFolds;
        var original=Load(1);Check(original.Length==2,"expected reopened-book checkpoint");
        foreach(int id in new[]{5,8}){var r=Load(id);Check(r.Length==2,"cancel/undo retained a preview fold");for(int i=0;i<2;i++)Check(JsonUtility.ToJson(r[i])==JsonUtility.ToJson(original[i]),"cancel/undo changed recipe");}
        foreach(int id in new[]{7,9}){var r=Load(id);Check(r.Length==3&&r[2].materialPartition&&Mathf.Abs(r[2].degrees-(id==7?78:-78))<.001f,"actual commit missing");
            var sheet=PaperSheet.Replay(r);Check(sheet.Count==3,"actual input replay reset");ReverseDirectionVerification.Geometry(sheet.Faces);PetalFoldingVerification.Replay(sheet);
            Check(sheet.Undo()&&sheet.Count==2&&sheet.ApplyStep(r[2]),"actual input undo/redo");ReverseDirectionVerification.Geometry(sheet.Faces);
        }
        Debug.Log("[SequencePlayer] PASS actual input: same previously blocked two endpoints and face; 78-degree cancel, commit, undo and opposite -78-degree commit; saved replay and seams.");
    }
    public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
