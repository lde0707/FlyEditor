using UnityEngine;
using FlyEditor.Flight;
public static class LeafProbe {
 public static PaperSheet House(){var p=new PaperSheet();
  if(!p.ApplyStep(new FoldStep(CreaseFamily.Rising,0,1){creaseNormal=new Vector2(-1,1),creaseOffset=5.5f,degrees=180,compactLayers=true}))throw new System.Exception("left");
  if(!p.ApplyStep(new FoldStep(CreaseFamily.Falling,0,1){creaseNormal=Vector2.one,creaseOffset=16.5f,degrees=180,compactLayers=true}))throw new System.Exception("right");
  if(!p.ApplyStep(new FoldStep(CreaseFamily.Vertical,0,1){creaseNormal=Vector2.right,creaseOffset=5.5f,degrees=180,compactLayers=true}))throw new System.Exception("half");return p;
 }
 public static void Run(){var p=House();for(int i=0;i<p.Faces.Count;i++){var f=p.Faces[i];string pts="";foreach(var v in f.points)pts+=v.ToString("F2");Debug.Log("FACE "+i+" L"+f.layer+" "+pts);foreach(int side in new[]{-1,1}){var s=new FoldStep(CreaseFamily.Vertical,0,side){creaseNormal=Vector2.right,creaseOffset=2.75f,anchor=i,flap=i+1,singlePanel=true,connectedPanel=true};var t=p.Plan(s);Debug.Log("PLAN "+i+" side "+side+" "+(t==null?"NULL":"move="+t.movingFaces.Count+" limit="+t.Constrain(180)+"/"+t.Constrain(-180)));}}}
}
