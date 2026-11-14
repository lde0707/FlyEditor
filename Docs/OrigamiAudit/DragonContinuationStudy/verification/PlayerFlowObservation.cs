using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEngine;using FlyEditor.Flight;
// Read-only geometry/mesh evidence. Never chooses points, plans a fold or sets a pose.
public static class PlayerFlowObservation
{
    [Serializable]public class FaceRecord {public Vector2[] points;public int layer;public bool back;public Matrix4x4 frame,material;public long region;}
    [Serializable]public class Record {public string phase;public float angle;public FaceRecord[] sheet,moving,stationary;public Matrix4x4 rotation;public Vector3[] meshVertices;public Color[] meshColors;public int[] meshIndices;}
    static FaceRecord[] Faces(IReadOnlyList<PaperSheet.Face> faces){var result=new List<FaceRecord>();if(faces!=null)foreach(var f in faces)result.Add(new FaceRecord{points=f.points,layer=f.layer,back=f.back,frame=f.frame,material=f.material,region=f.MaterialRegionId});return result.ToArray();}
    public static void Write(string folder,int id,object desk){if(desk==null)return;const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var t=desk.GetType();
        object Get(string n)=>t.GetField(n,flags)?.GetValue(desk);var sheet=Get("sheet") as PaperSheet;if(sheet==null)return;
        var turn=Get("turn") as PaperSheet.Turn;var mesh=Get("mesh") as Mesh;float angle=(float)Get("angle");
        var record=new Record{phase=Get("phase").ToString(),angle=angle,sheet=Faces(sheet.Faces),moving=Faces(turn?.movingFaces),stationary=Faces(turn?.fixedFaces),rotation=turn==null?Matrix4x4.identity:turn.Rotation(angle),meshVertices=mesh?.vertices,meshColors=mesh?.colors,meshIndices=mesh?.triangles};
        File.WriteAllText(Path.Combine(folder,"geometry-"+id.ToString("000")+".json"),JsonUtility.ToJson(record));
    }
}
