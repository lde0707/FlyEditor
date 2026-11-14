using System;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class SliverClippingVerification
{
    public static void Verify(){
        const BindingFlags H=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;var clip=typeof(PaperSheet).GetMethod("Clip",H);int cases=0;
        foreach(float size in new[]{.001f,.003f,.009f})foreach(bool reverse in new[]{false,true}){
            var sheet=new PaperSheet();var line=new FoldStep{anchor=0,flap=1,side=1,degrees=180,creaseNormal=Vector2.one,creaseOffset=22-size,continuousHinge=true,neutralContacts=true,materialPartition=true,vertexJoints=true,preserveSlivers=true};
            if(reverse){line.creaseNormal=-line.creaseNormal;line.creaseOffset=-line.creaseOffset;line.side=-line.side;}
            var cap=(PaperSheet.Face)clip.Invoke(null,new object[]{sheet.Faces[0],line,line.side});if(cap==null)throw new Exception("[SliverClipping] a valid small material cap disappeared");
            var legacy=line;legacy.preserveSlivers=false;if(clip.Invoke(null,new object[]{sheet.Faces[0],legacy,legacy.side})!=null)throw new Exception("[SliverClipping] legacy polygon indexing changed");
            var plan=sheet.Plan(line);if(plan==null||!sheet.Finish(plan,180))throw new Exception("[SliverClipping] small cap could not turn");ReverseDirectionVerification.Geometry(sheet.Faces);PetalFoldingVerification.Replay(sheet);if(!sheet.Undo())throw new Exception("[SliverClipping] undo failed");cases++;
        }
        Debug.Log("[SliverClipping] PASS "+cases+" tiny cap and reversed-axis folds; retained material, seams, serialization, undo; legacy topology preserved.");
    }
    public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
