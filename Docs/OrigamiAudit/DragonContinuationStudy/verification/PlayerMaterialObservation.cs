using System;using System.IO;using System.Collections.Generic;using System.Reflection;using UnityEngine;using FlyEditor.Flight;
// Read-only snapshots of the actual workbench preview; no fold/selection calls.
public static class PlayerMaterialObservation {
 [Serializable]public class Sample {public int face,layer;public long region;public Vector3 material,world,surface;}
 [Serializable]public class Snapshot {public int folds;public float angle;public bool hingeHints;public string solver;public Sample[] sheet,moving,fixedFaces,rendered,primary,assisted;}
 static Sample[] Samples(IReadOnlyList<PaperSheet.Face> faces){var result=new List<Sample>();if(faces==null)return result.ToArray();
 for(int i=0;i<faces.Count;i++){var f=faces[i];Vector2 centre=Vector2.zero;foreach(var p in f.points)centre+=p;centre/=f.points.Length;
 var points=new List<Vector2>{centre};foreach(var p in f.points)points.Add(Vector2.Lerp(p,centre,.01f));foreach(var p in points)result.Add(new Sample{face=i,layer=f.layer,region=f.MaterialRegionId,material=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y)),world=f.World(p,0),surface=f.World(p,(f.layer+.5f)*PaperSheet.Thickness)});}
 return result.ToArray();}
 public static void Write(string folder,int id,object desk){if(desk==null)return;var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=desk.GetType();var sheet=(PaperSheet)type.GetField("sheet",flags).GetValue(desk);if(sheet==null)return;
 var turn=type.GetField("turn",flags).GetValue(desk) as PaperSheet.Turn;var pull=type.GetField("pull",flags).GetValue(desk) as PaperCornerPull;
 var primary=new List<PaperSheet.Face>();var assisted=new List<PaperSheet.Face>();var stationary=new List<PaperSheet.Face>();
 if(pull!=null&&pull.UsesCoordinatedSurface){var role=(Func<PaperSheet.Face,int>)Delegate.CreateDelegate(typeof(Func<PaperSheet.Face,int>),pull,typeof(PaperCornerPull).GetMethod("MotionRole",flags));foreach(var f in pull.Preview){int r=role(f);if(r==1)primary.Add(f);else if(r==2)assisted.Add(f);else stationary.Add(f);}}
 var value=new Snapshot{folds=sheet.Count,angle=(float)type.GetField("angle",flags).GetValue(desk),hingeHints=pull!=null&&pull.ResolvedHingeHints,solver=pull==null?"none":pull.UsesCoordinatedSurface?"coordinated":pull.UsesEdgeMatch?"edge-match":"other",sheet=Samples(sheet.Faces),moving=Samples(turn?.movingFaces),fixedFaces=Samples(pull!=null&&pull.UsesCoordinatedSurface?stationary:turn?.fixedFaces),rendered=Samples(pull?.Preview??sheet.Faces),primary=Samples(primary),assisted=Samples(assisted)};
 File.WriteAllText(Path.Combine(folder,"material-"+id.ToString("000")+".json"),JsonUtility.ToJson(value,true));
 }
}
