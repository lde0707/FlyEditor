using System;using System.Collections.Generic;using System.Reflection;using System.IO;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class WholeSheetFoldVerification
{
    const BindingFlags H=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object desk;static Type type;
    static object Get(string name)=>type.GetField(name,H).GetValue(desk);
    static object Call(string name,params object[] args){foreach(var m in type.GetMethods(H)){if(m.Name!=name||m.GetParameters().Length!=args.Length)continue;var p=m.GetParameters();bool match=true;for(int i=0;i<args.Length;i++)if(args[i]!=null&&!p[i].ParameterType.IsInstanceOfType(args[i]))match=false;if(match)return m.Invoke(desk,args);}throw new Exception(name);}
    static void Check(bool ok,string text){if(!ok)throw new Exception("[WholeSheetFold] "+text);}
    static Vector2 R(Vector2 p,int rotation){while(rotation-->0)p=new Vector2(11-p.y,p.x);return p;}
    static Vector2 Screen(PaperSheet sheet,Vector2 p){PaperSheet.Face visible=null;foreach(var f in sheet.Faces)if(f.Contains(p)&&(visible==null||f.layer>visible.layer))visible=f;Check(visible!=null,"test point outside paper");return (Vector2)Call("SurfaceScreen",visible,p);}
    public static void Verify(){var go=new GameObject("whole-sheet-fold-check");var owner=go.AddComponent<PaperFlightController>();var mat=new Material(Shader.Find("Unlit/Color"));type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");desk=Activator.CreateInstance(type,H,null,new object[]{owner,mat},null);int cases=0,poses=0;
        try{foreach(bool narrow in new[]{false,true})for(int rotation=0;rotation<4;rotation++){
            var seed=WingSideVerification.Seed(narrow,rotation,out _);Check(seed.Undo(),"prepare whole paper before body fold");var recipe=seed.Recipe();Call("Open",recipe);Call("Layout");Call("SnapView",7,false);var sheet=(PaperSheet)Get("sheet");
            Call("PickSurfacePoint",Screen(sheet,R(new Vector2(0,5.5f),rotation)));Call("PickSurfacePoint",Screen(sheet,R(new Vector2(11,5.5f),rotation)));Check((int)Get("second")>=0,"line endpoints absent");
            Check(Get("scope").ToString()=="Top","default selection changed");Call("ChoosePly",Enum.Parse(type.GetNestedType("Ply",BindingFlags.NonPublic),"Stack"));Check(Get("scope").ToString()=="Stack","whole scope could not be selected");
            Check((bool)Call("ChooseSide",Screen(sheet,R(new Vector2(8,6),rotation)),false),"whole side click rejected");var turn=Get("turn") as PaperSheet.Turn;Check(turn!=null&&turn.step.flap==0&&!turn.step.singlePanel,"whole fold retained single-leaf scope");
            float expected=0,actual=0;var clip=typeof(PaperSheet).GetMethod("Clip",H);foreach(var f in sheet.Faces){var part=(PaperSheet.Face)clip.Invoke(null,new object[]{f,turn.step,turn.step.side});if(part!=null)expected+=PaperSheet.Area(part.points);}foreach(var f in turn.movingFaces)actual+=PaperSheet.Area(f.points);Check(Mathf.Abs(expected-actual)<.001f,"some crease-side plies omitted");
            var trial=new List<PaperSheet.Face>();var preview=typeof(PaperSheet).GetMethod("TryPreviewTurn",H);foreach(float a in new[]{0f,3f,30f,90f,150f,180f}){Check((bool)preview.Invoke(sheet,new object[]{turn,a,trial}),"whole fold blocked at "+a);ReverseDirectionVerification.Geometry(trial);poses++;}
            var original=sheet.Faces;Check(sheet.Finish(turn,180),"whole fold could not commit");PetalFoldingVerification.Replay(sheet);Check(sheet.Undo()&&ReferenceEquals(original,sheet.Faces),"undo failed");
            Call("ChoosePly",Enum.Parse(type.GetNestedType("Ply",BindingFlags.NonPublic),"Top"));Check(Get("scope").ToString()=="Top","single-leaf mode not restored");cases++;
        }
        Debug.Log("[WholeSheetFold] PASS "+cases+" basic/narrow and rotated planes; "+poses+" whole-stack poses; API scope, all crease-side plies, area/seams, replay/undo and return to visible leaf. Real keyboard/mouse proof is separate.");
        }finally{Call("Dispose");UnityEngine.Object.DestroyImmediate(mat);UnityEngine.Object.DestroyImmediate(go);}
    }
    public static void Prepare(){try{var sheet=WingSideVerification.Seed(true,0,out _);Check(sheet.Undo(),"seed before half fold");File.WriteAllText(Environment.GetEnvironmentVariable("FLYEDITOR_WHOLE_ROOT")+"/narrow-seed.json",JsonUtility.ToJson(new ResearchSave{paperFolds=sheet.Recipe()},true));EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    public static void VerifyPlayer(){
        var root=Environment.GetEnvironmentVariable("FLYEDITOR_WHOLE_PLAYER_ROOT");Check(!string.IsNullOrEmpty(root),"player evidence root absent");
        string At(string session,string file)=>Path.Combine(root,"session-"+session,file);
        Check(File.ReadAllText(At("a","status-002.txt")).Contains("scope=Top"),"baseline input not preserved");Check(File.ReadAllText(At("a","status-003.txt")).Contains("angle=0"),"baseline failure changed");
        Check(File.ReadAllText(At("b","status-002.txt")).Contains("scope=Stack"),"real key 2 did not enter whole scope");Check(File.ReadAllText(At("b","status-004.txt")).Contains("scope=Top"),"completion did not return to safe default");
        var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(At("b","recipe-004.json"))).paperFolds;var last=recipe[recipe.Length-1];Check(recipe.Length==5&&last.flap==0&&last.degrees==180&&!last.singlePanel,"real whole fold not recorded");
        typeof(FragmentBranchVerification).GetMethod("PlayerTransition",H).Invoke(null,new object[]{root,"b",3,4});
        for(int i=1;i<=6;i++){var r=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(At("b","recipe-"+i.ToString("000")+".json"))).paperFolds;var s=PaperSheet.Replay(r);Check(s.Count==r.Length,"input replay reset");ReverseDirectionVerification.Geometry(s.Faces);}
        var saved=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(At("b","progress-005.json"))).paperFolds;Check(saved.Length==5&&saved[4].flap==0,"isolated disk save lost whole fold");
        Debug.Log("[WholeSheetPlayer] PASS same real mouse/key trace: baseline Top rejected, corrected Stack folded 180; independent moving/fixed material, 6 saved states, disk save, reset to visible leaf. User screenshot recipe was not obtained; this is a similar-shape reproduction.");
    }
    public static void Run(){try{Verify();VerifyPlayer();WingSideVerification.Run();WingSideVerification.RunFlat();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
