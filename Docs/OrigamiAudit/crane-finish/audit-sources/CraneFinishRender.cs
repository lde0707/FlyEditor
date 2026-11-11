using System;using System.IO;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using FlyEditor.Flight;using System.Collections.Generic;
public static class CraneFinishRender {static string folder;static Vector3 direction;
 public static void Run(){folder=Environment.GetEnvironmentVariable("FLYEDITOR_CRANE_FINISH");var p=PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(folder,"opposite-wings.json"))).paperFolds);direction=new Vector3(12,8,8);Render(p,"crane-three-quarter");direction=new Vector3(8,-8,12);Render(p,"crane-back");direction=new Vector3(0,16,0);Render(p,"crane-front");UnityEditor.EditorApplication.Exit(0);}
    static void Render(PaperSheet paper,string name,PaperCornerPull pull=null)
    {
        var scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("Audit paper");var cameraGo=new GameObject("Audit camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
        var mesh=new Mesh();var type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldMesh");
        var method=type.GetMethod("Build");var info=method.GetParameters();var args=new object[info.Length];
        for(int i=0;i<args.Length;i++)args[i]=info[i].HasDefaultValue?info[i].DefaultValue:null;
        args[0]=mesh;args[1]=paper;args[2]=null;args[3]=0f;
        var points=new List<PaperSheet.FoldVertex>();args[4]=points;args[10]=pull;
        args[18]=Vector3.zero;
        method.Invoke(Activator.CreateInstance(type,true),args);mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var material=new Material(Shader.Find("FlyEditor/PaperFlight"));go.AddComponent<MeshRenderer>().sharedMaterial=material;
        var cam=cameraGo.AddComponent<Camera>();cam.cameraType=CameraType.Preview;cam.orthographic=true;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.91f,.94f,.92f);
        var center=mesh.bounds.center;cam.transform.position=center+direction;cam.transform.LookAt(center,new Vector3(1,0,1));cam.orthographicSize=Mathf.Max(4f,mesh.bounds.size.magnitude*.58f);
        cam.nearClipPlane=.1f;cam.farClipPlane=100;cam.scene=scene;
        var rt=new RenderTexture(1000,760,24);var tex=new Texture2D(1000,760,TextureFormat.RGB24,false);var old=RenderTexture.active;
        try {cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1000,760),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),tex.EncodeToPNG());}
        finally {cam.targetTexture=null;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
