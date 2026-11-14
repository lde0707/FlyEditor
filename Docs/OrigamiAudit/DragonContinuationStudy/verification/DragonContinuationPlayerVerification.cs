using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class DragonContinuationPlayerVerification
{
    [Serializable]class Sample {public Vector3 material,world;}
    [Serializable]class Samples {public Sample[] fixedFaces,rendered;}
    static string root;static readonly BindingFlags H=BindingFlags.Static|BindingFlags.NonPublic;
    static void Check(bool ok,string m){if(!ok)throw new Exception("[DragonPlayer] "+m);}
    static string FileAt(string s,string file)=>Path.Combine(root,"session-"+s,file);
    static FoldStep[] Recipe(string s,int id)=>JsonUtility.FromJson<ResearchSave>(File.ReadAllText(FileAt(s,"recipe-"+id.ToString("000")+".json"))).paperFolds;
    static ThreePlayerFlowVerification.Geometry Geometry(string s,int id)=>JsonUtility.FromJson<ThreePlayerFlowVerification.Geometry>(File.ReadAllText(FileAt(s,"geometry-"+id.ToString("000")+".json")));
    static void Equal(string a,int ai,string b,int bi)=>typeof(ThreePlayerFlowVerification).GetMethod("EqualMaterial",H).Invoke(null,new object[]{Geometry(a,ai).sheet,Geometry(b,bi).sheet,Matrix4x4.identity});
    static void SamplesEqual(Sample[] samples,ThreePlayerFlowVerification.Face[] faces){
        foreach(var sample in samples){bool found=false;foreach(var f in faces){var q=f.material.inverse.MultiplyPoint3x4(sample.material);var p=new Vector2(q.x,q.z);float sign=0;bool inside=true;
            for(int i=0;i<f.points.Length;i++){var a=f.points[i];var b=f.points[(i+1)%f.points.Length];float c=(b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);if(Mathf.Abs(c)<.0005f)continue;if(sign*c<0){inside=false;break;}sign=c;}
            if(!inside)continue;found=true;Check(Vector3.Distance(f.frame.MultiplyPoint3x4(new Vector3(p.x,0,p.y)),sample.world)<.003f,"fixed/preview material mismatch");}Check(found,"sample material missing");}
    }
    public static void Verify(){root=Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON_PLAYER_ROOT");
        typeof(ThreePlayerFlowVerification).GetField("root",H).SetValue(null,root);
        foreach(var t in new[]{new[]{"a","3","4"},new[]{"b","9","10"},new[]{"c","3","5"},new[]{"c","6","7"}})typeof(ThreePlayerFlowVerification).GetMethod("Transition",H).Invoke(null,new object[]{t[0],int.Parse(t[1]),int.Parse(t[2])});
        Equal("a",6,"a",10);Equal("c",1,"c",7);Equal("c",7,"d",1);
        var initial=JsonUtility.FromJson<Samples>(File.ReadAllText(FileAt("b","material-004.json")));var final=JsonUtility.FromJson<Samples>(File.ReadAllText(FileAt("b","material-006.json")));var actual=Geometry("b",7).sheet;
        SamplesEqual(initial.fixedFaces,actual);SamplesEqual(final.rendered,actual);
        Check(Recipe("a",1).Length==8&&Recipe("a",6).Length==10&&Recipe("b",7).Length==11&&Recipe("b",10).Length==12&&Recipe("c",5).Length==13&&Recipe("c",7).Length==14,"player fold sequence incomplete");
        Check(Recipe("c",5)[12].contactPacket,"packet selection was not recorded");
        var saved=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(FileAt("c","progress-008.json"))).paperFolds;var loaded=Recipe("d",1);Check(saved.Length==14&&loaded.Length==14,"actual disk reload count");for(int i=0;i<14;i++)Check(JsonUtility.ToJson(saved[i])==JsonUtility.ToJson(loaded[i]),"disk reload operation differs");
        int replays=0;foreach(var session in new[]{new[]{"a","12"},new[]{"b","15"},new[]{"c","9"},new[]{"d","2"}})for(int i=1;i<=int.Parse(session[1]);i++){
            var recipe=Recipe(session[0],i);var sheet=PaperSheet.Replay(recipe);Check(sheet.Count==recipe.Length,"actual state silently reset");ReverseDirectionVerification.Geometry(sheet.Faces);replays++;
        }
        Debug.Log("[DragonPlayer] PASS actual 8-to-14 history; 4 rigid transitions, 120 fixed and 144 preview samples, supported opposite petal, contact packet reopen, disk reload; "+replays+" observed saved states replayed. Complete model remains unverified.");
    }
    public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
