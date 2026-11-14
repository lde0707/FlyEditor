using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FlyEditor.Flight;

public static class SequenceSelectionVerification
{
    const BindingFlags H=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static Type type;static object desk;
    static object Get(string name)=>type.GetField(name,H).GetValue(desk);
    static object Call(string name,params object[] args){foreach(var m in type.GetMethods(H)){
        if(m.Name!=name||m.GetParameters().Length!=args.Length)continue;var ps=m.GetParameters();bool valid=true;for(int i=0;i<args.Length;i++)if(args[i]!=null&&!ps[i].ParameterType.IsInstanceOfType(args[i]))valid=false;
        if(valid)return m.Invoke(desk,args);
    }throw new Exception(name);}
    static void Check(bool ok,string message){if(!ok)throw new Exception("[SequenceSelection] "+message);}
    static Vector2 Screen(Vector2 material){var s=(PaperSheet)Get("sheet");foreach(var f in s.Faces){var p=f.material.inverse.MultiplyPoint3x4(new Vector3(material.x,0,material.y));var q=new Vector2(p.x,p.z);if(f.Contains(q))return (Vector2)Call("SurfaceScreen",f,q);}throw new Exception("point not on sheet");}
    public static void Verify(){
        var go=new GameObject("sequence-selection-check");var owner=go.AddComponent<PaperFlightController>();var material=new Material(Shader.Find("Unlit/Color"));
        type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");desk=Activator.CreateInstance(type,H,null,new object[]{owner,material},null);
        try{
            var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("FLYEDITOR_SEQUENCE_OUT"),"unfolded-book.json"))).paperFolds;int count=0;
            foreach(bool back in new[]{false,true})foreach(bool reverse in new[]{false,true})foreach(float offset in new[]{2.75f,8.25f}){
                Call("Open",recipe);Call("Layout");Call("SnapView",7,back);
                var a=Screen(new Vector2(offset,0));var b=Screen(new Vector2(offset,11));
                Call("PickSurfacePoint",reverse?b:a);Call("PickSurfacePoint",reverse?a:b);Check((int)Get("second")>=0,"second point missing");
                var at=Screen(new Vector2(offset<5.5f?8.25f:2.75f,4.4f));
                Check((bool)Call("ChooseSide",at,false),"click rejected");
                Check(Get("sideChoiceState").ToString()=="Ready"&&Get("turn") is PaperSheet.Turn,"valid connected surface blocked as "+Get("sideChoiceState"));
                var turn=(PaperSheet.Turn)Get("turn");float area=0;foreach(var f in turn.movingFaces)area+=PaperSheet.Area(f.points);Check(Mathf.Abs(area-90.75f)<.005f,"selected connected area mismatch");
                typeof(SelectionRegionVerification).GetMethod("MeshMatches",H).Invoke(null,new object[]{(PaperSheet)Get("sheet"),turn,turn.step.anchor,null});count++;
            }
            Debug.Log("[SequenceSelection] PASS "+count+" front/back and reversed-endpoint selections across an unfolded material seam; exact moving highlight.");
        }finally{Call("Dispose");UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(go);}
    }
    public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
