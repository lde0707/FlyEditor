using System;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
public static class PaperContainmentVerification
{
    const BindingFlags H=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static void Check(bool ok,string message){if(!ok)throw new Exception("[PaperContainment] "+message);}
    static PaperSheet.Face Face(Vector2[] points){var ctor=typeof(PaperSheet.Face).GetConstructor(H,null,new[]{typeof(Vector2[]),typeof(int),typeof(bool),typeof(Matrix4x4),typeof(Matrix4x4),typeof(PaperSheet.Face),typeof(bool)},null);return (PaperSheet.Face)ctor.Invoke(new object[]{points,0,false,Matrix4x4.identity,Matrix4x4.identity,null,false});}
    public static void Verify(){int cases=0;foreach(float width in new[]{.001f,.003f,.03f,1f,11f})foreach(int rotate in new[]{0,1,2,3})foreach(bool reverse in new[]{false,true}){
        Vector2 Rotate(Vector2 p){for(int i=0;i<rotate;i++)p=new Vector2(-p.y,p.x);return p+new Vector2(5.5f,5.5f);}
        var points=new[]{Rotate(Vector2.zero),Rotate(new Vector2(width,0)),Rotate(new Vector2(width,1)),Rotate(new Vector2(0,1))};if(reverse)Array.Reverse(points);var f=Face(points);
        Check(f.Contains(Rotate(new Vector2(width*.5f,.5f))),"interior rejected");Check(f.Contains(Rotate(new Vector2(width*.5f,0))),"edge rejected");
        Check(!f.Contains(Rotate(new Vector2(width*.5f,-.002f))),"short edge admitted material outside by 0.002");Check(!f.Contains(Rotate(new Vector2(width+.002f,.5f))),"side admitted outside material");cases++;
    }Debug.Log("[PaperContainment] PASS "+cases+" short/long edge, rotated and reversed polygons; physical-distance boundary and outside guards.");}
    public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
