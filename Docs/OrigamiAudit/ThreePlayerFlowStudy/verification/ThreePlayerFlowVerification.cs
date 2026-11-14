using System;using System.IO;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class ThreePlayerFlowVerification
{
    [Serializable]public class Face {public Vector2[] points;public int layer;public bool back;public Matrix4x4 frame,material;public long region;}
    [Serializable]public class Geometry {public string phase;public float angle;public Face[] sheet,moving,stationary;public Matrix4x4 rotation;public Vector3[] meshVertices;public Color[] meshColors;public int[] meshIndices;}
    static string root;static int transitions,materialSamples,greenChecks,replayed;
    static void Check(bool ok,string m){if(!ok)throw new Exception("[ThreePlayerFlows] "+m);}
    static string PathFor(string s,string file)=>Path.Combine(root,"session-"+s,file);
    static Geometry Read(string s,int id)=>JsonUtility.FromJson<Geometry>(File.ReadAllText(PathFor(s,"geometry-"+id.ToString("000")+".json")));
    static FoldStep[] Recipe(string s,int id)=>JsonUtility.FromJson<ResearchSave>(File.ReadAllText(PathFor(s,"recipe-"+id.ToString("000")+".json"))).paperFolds;
    static Vector3 Material(Face f,Vector2 p)=>f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
    static Vector3 World(Face f,Vector2 p)=>f.frame.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
    static Vector2 Local(Face f,Vector3 material){var p=f.material.inverse.MultiplyPoint3x4(material);return new Vector2(p.x,p.z);}
    static bool Inside(Face f,Vector2 p){float sign=0;for(int i=0;i<f.points.Length;i++){var a=f.points[i];var b=f.points[(i+1)%f.points.Length];float c=(b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);if(Mathf.Abs(c)<.0005f)continue;if(sign*c<0)return false;sign=c;}return true;}
    static List<Vector2> Samples(Face f){var list=new List<Vector2>(f.points);var c=Vector2.zero;foreach(var p in f.points)c+=p;c/=f.points.Length;list.Add(c);foreach(var p in f.points)list.Add(Vector2.Lerp(p,c,.01f));return list;}
    static void EqualMaterial(Face[] before,Face[] after,Matrix4x4 transform){
        foreach(var f in before)foreach(var p in Samples(f)){var m=Material(f,p);bool found=false;var expected=transform.MultiplyPoint3x4(World(f,p));
            foreach(var g in after){var q=Local(g,m);if(!Inside(g,q))continue;found=true;Check(Vector3.Distance(expected,World(g,q))<.003f,"material moved or seam split at "+m);}
            Check(found,"material missing at "+m);materialSamples++;
        }
    }
    static void Unchanged(string a,int ai,string b,int bi)=>EqualMaterial(Read(a,ai).sheet,Read(b,bi).sheet,Matrix4x4.identity);
    static void Transition(string s,int selected,int committed){
        var before=Read(s,selected);var after=Read(s,committed);var recipe=Recipe(s,committed);var step=recipe[recipe.Length-1];
        Check(before.moving.Length>0&&before.stationary.Length>0,"missing selected/fixed regions");
        // Independent axis-angle oracle from the actually saved user operation.
        var basis=before.sheet[step.anchor];var n=step.Normal;var at=n*(step.Position/n.sqrMagnitude);
        var pivot=basis.frame.MultiplyPoint3x4(new Vector3(at.x,0,at.y));var axis=basis.frame.MultiplyVector(new Vector3(-n.y,0,n.x)*step.side).normalized;
        var rotate=Matrix4x4.Translate(pivot)*Matrix4x4.Rotate(Quaternion.AngleAxis(step.degrees,axis))*Matrix4x4.Translate(-pivot);
        EqualMaterial(before.moving,after.sheet,rotate);EqualMaterial(before.stationary,after.sheet,Matrix4x4.identity);transitions++;
    }
    static void Green(string s,int id){
        var g=Read(s,id);var normals=new List<Vector3>();float expected=0;
        foreach(var f in g.moving){expected+=2*PaperSheet.Area(f.points);normals.Add(g.rotation.MultiplyVector(f.frame.MultiplyVector(Vector3.up)).normalized);}
        float actual=0;for(int i=0;i<g.meshIndices.Length;i+=3){int a=g.meshIndices[i],b=g.meshIndices[i+1],c=g.meshIndices[i+2];var col=g.meshColors[a];if(Mathf.Abs(col.r-.62f)>.001f||Mathf.Abs(col.g-.83f)>.001f||Mathf.Abs(col.b-.73f)>.001f)continue;
            var cross=Vector3.Cross(g.meshVertices[b]-g.meshVertices[a],g.meshVertices[c]-g.meshVertices[a]);if(cross.sqrMagnitude<1e-12f)continue;
            bool surface=false;foreach(var normal in normals)surface|=Mathf.Abs(Vector3.Dot(cross.normalized,normal))>.9999f;
            if(surface)actual+=cross.magnitude*.5f; // Exclude the physical thickness walls.
        }
        Check(expected>0&&Mathf.Abs(expected-actual)<.015f,"actual highlighted mesh differs from moving material "+s+"/"+id+" "+actual+" vs "+expected);greenChecks++;
    }
    static void SameRecipe(FoldStep[] a,FoldStep[] b){Check(a.Length==b.Length,"saved count differs");for(int i=0;i<a.Length;i++)Check(JsonUtility.ToJson(a[i])==JsonUtility.ToJson(b[i]),"saved operation differs");}
    static void Disk(string s,int saved,int recorded)=>SameRecipe(JsonUtility.FromJson<ResearchSave>(File.ReadAllText(PathFor(s,"progress-"+saved.ToString("000")+".json"))).paperFolds,Recipe(s,recorded));
    static void Replays(string s,int end){for(int id=1;id<=end;id++){var r=Recipe(s,id);var sheet=PaperSheet.Replay(r);Check(sheet.Count==r.Length,"saved input silently reset "+s+"/"+id);ReverseDirectionVerification.Geometry(sheet.Faces);if(r.Length>0)PetalFoldingVerification.Replay(sheet);replayed++;}}
    public static void Verify(){root=Environment.GetEnvironmentVariable("FLYEDITOR_THREE_PLAYER_ROOT");transitions=materialSamples=greenChecks=replayed=0;
        Check(Recipe("b",1).Length==0&&Recipe("c",1).Length==0,"flows must start from fresh paper input");
        foreach(var x in new[]{new[]{"b","3","4"},new[]{"b","11","12"},new[]{"b","13","15"},new[]{"b","17","19"},new[]{"c","3","4"},new[]{"c","5","6"},new[]{"c","8","10"},new[]{"c","13","15"},new[]{"d","6","11"},new[]{"d","6","13"},new[]{"e","7","8"}})Transition(x[0],int.Parse(x[1]),int.Parse(x[2]));
        foreach(int id in new[]{7,8,9,10})Unchanged("b",4,"b",id);
        foreach(int id in new[]{7,8,9,10,12})Unchanged("d",4,"d",id);
        Unchanged("c",6,"c",11);foreach(int id in new[]{4,5,6})Unchanged("e",1,"e",id);
        Unchanged("b",19,"b",21);Unchanged("b",19,"d",1);Unchanged("d",13,"e",1);Unchanged("e",8,"e",10);
        Check(Read("b",7).angle==0&&Read("d",7).angle==0&&Read("e",5).angle==0,"blocked direction passed contact");
        foreach(var f in Read("b",13).moving)foreach(var p in f.points)Check(Material(f,p).x>=8.2498f,"upper opening captured lower material");
        foreach(var f in Read("b",17).moving)foreach(var p in f.points)Check(Material(f,p).x<=5.5002f,"underside opening captured upper material");
        foreach(var pair in new[]{new[]{"b","3"},new[]{"b","6"},new[]{"b","9"},new[]{"b","11"},new[]{"b","13"},new[]{"b","17"},new[]{"c","8"},new[]{"c","13"},new[]{"d","6"},new[]{"d","9"},new[]{"e","2"},new[]{"e","3"},new[]{"e","5"},new[]{"e","7"}})Green(pair[0],int.Parse(pair[1]));
        Disk("b",20,19);Disk("c",16,15);Disk("d",14,13);Disk("e",9,8);
        SameRecipe(JsonUtility.FromJson<ResearchSave>(File.ReadAllText(PathFor("b","progress-020.json"))).paperFolds,Recipe("d",1));
        SameRecipe(JsonUtility.FromJson<ResearchSave>(File.ReadAllText(PathFor("d","progress-014.json"))).paperFolds,Recipe("e",1));
        Replays("b",21);Replays("c",17);Replays("d",15);Replays("e",11);
        Debug.Log("[ThreePlayerFlows] PASS flows=3 transitions="+transitions+" neutralMaterialSamples="+materialSamples+" highlightedMeshes="+greenChecks+" savedInputReplays="+replayed+" actual cold restarts=2; source/fixed ply, contact, cancel, undo and actual save preserved.");
    }
    public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
