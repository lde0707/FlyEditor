using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;
using FlyEditor.Flight;
using UnityEditor;
using UnityEngine;

namespace FlyEditor.Editor
{
    // Bake this character's default skin and linear translation/rotation timelines.
    public static class ThrowerSpineBake
    {
        public const string DirectoryPath = "Assets/_Project/Art/Spine/Thrower";
        private const string Source = DirectoryPath + "/Thrower.json";
        private const string ClipPath = DirectoryPath + "/ThrowRight.anim";
        public const string PrefabPath = DirectoryPath + "/Resources/LaunchThrower.prefab";
        private const float Units = .01f;
        [MenuItem("Tools/FlyEditor/Rebuild Thrower from Spine JSON")]
        public static void Bake()
        {
            using var reader = JsonReaderWriterFactory.CreateJsonReader(File.ReadAllBytes(Source), XmlDictionaryReaderQuotas.Max);
            var json = XDocument.Load(reader).Root;
            if (!((string)json.Element("skeleton").Element("spine")).StartsWith("4.2.",StringComparison.Ordinal))
                throw new InvalidDataException("Expected a Spine 4.2 export.");
            var anim = json.Element("animations").Element("throw-right");
            var release = anim.Element("events").Elements().Single(e => e.Element("name").Value == "release");
            var host = new GameObject("LaunchThrower");
            var bones = new Dictionary<string,Transform>();
            var angles = new Dictionary<string,float>();
            try
            {
                foreach (var b in json.Element("bones").Elements())
                {
                    string name=b.Element("name").Value, parent=(string)b.Element("parent");
                    Transform joint = new GameObject(name).transform;
                    joint.SetParent(parent==null ? host.transform : bones[parent],false);
                    joint.localPosition = new Vector3(Number(b,"x"),Number(b,"y"),0) * Units;
                    float angle=Number(b,"rotation");
                    joint.localRotation=Quaternion.Euler(0,0,angle);
                    bones.Add(name,joint); angles.Add(name,angle);
                }
                var skin=json.Element("skins").Elements().Single(s=>(string)s.Element("name")=="default").Element("attachments");
                int order=0;
                if (!AssetDatabase.IsValidFolder(DirectoryPath+"/Materials"))
                    AssetDatabase.CreateFolder(DirectoryPath,"Materials");
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(DirectoryPath+"/ThrowerRegion.shader");
                if(shader==null) throw new InvalidDataException("ThrowerRegion.shader is missing.");
                foreach(var slot in json.Element("slots").Elements())
                {
                    string name=slot.Element("name").Value;
                    // InGame uses its actual folded 3D plane at the grip. Spine uses a preview attachment.
                    if(name=="paper") continue;
                    var part=skin.Element(name).Element(slot.Element("attachment").Value);
                    var image=AssetDatabase.LoadAssetAtPath<Sprite>(DirectoryPath+"/images/"+name+".png");
                    if(image==null) throw new InvalidDataException("Missing thrower sprite " + name);
                    var piece=new GameObject(name);
                    piece.transform.SetParent(bones[slot.Element("bone").Value],false);
                    piece.transform.localPosition=new Vector3(Number(part,"x"),Number(part,"y"),0)*Units;
                    var sr=piece.AddComponent<SpriteRenderer>();
                    sr.sprite=image; sr.sortingOrder=order++;
                    string materialPath=DirectoryPath+"/Materials/"+name+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if(material==null)
                    {
                        material=new Material(shader);
                        AssetDatabase.CreateAsset(material,materialPath);
                    }
                    material.SetTexture("_RegionTex",image.texture);
                    EditorUtility.SetDirty(material);
                    sr.sharedMaterial=material;
                    // Region sizes come from Spine, including high-resolution source art.
                    piece.transform.localScale=new Vector3(Number(part,"width")*Units/image.bounds.size.x,
                        Number(part,"height")*Units/image.bounds.size.y,1);
                }
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
                if(clip==null)
                {
                    clip=new AnimationClip { name="ThrowRight", legacy=true, frameRate=60 };
                    AssetDatabase.CreateAsset(clip,ClipPath);
                }
                clip.ClearCurves();
                foreach(var track in anim.Element("bones").Elements())
                {
                    string name=track.Name.LocalName;
                    string path=AnimationUtility.CalculateTransformPath(bones[name],host.transform);
                    var translation=track.Element("translate");
                    if (translation!=null)
                    {
                        var setup=bones[name].localPosition;
                        foreach(string axis in new[]{"x","y"})
                        {
                            float origin=axis=="x" ? setup.x : setup.y;
                            var movement=translation.Elements().Select(k=>new Keyframe(Number(k,"time"),
                                origin+Number(k,axis)*Units)).ToArray();
                            WriteCurve(clip,path,"localPosition."+axis,movement);
                        }
                    }
                    var rotation=track.Element("rotate");
                    if (rotation==null) continue;
                    var keys=rotation.Elements().ToArray();
                    var zs=new Keyframe[keys.Length]; var ws=new Keyframe[keys.Length];
                    for(int i=0;i<keys.Length;i++)
                    {
                        float theta=(angles[name]+Number(keys[i],"value"))*Mathf.Deg2Rad*.5f;
                        float t=Number(keys[i],"time");
                        zs[i]=new Keyframe(t,Mathf.Sin(theta)); ws[i]=new Keyframe(t,Mathf.Cos(theta));
                    }
                    WriteCurve(clip,path,"localRotation.z",zs); WriteCurve(clip,path,"localRotation.w",ws);
                    WriteCurve(clip,path,"localRotation.x",new[]{new Keyframe(0,0)});
                    WriteCurve(clip,path,"localRotation.y",new[]{new Keyframe(0,0)});
                }
                var player=host.AddComponent<LaunchThrower>();
                player.SetClip(clip,bones["grip"],Number(release,"time"));
                EditorUtility.SetDirty(clip);
                PrefabUtility.SaveAsPrefabAsset(host,PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
        private static float Number(XElement element,string key) => float.Parse((string)element.Element(key) ?? "0",CultureInfo.InvariantCulture);
        private static void WriteCurve(AnimationClip clip,string path,string property,Keyframe[] keys)
        {
            var curve=new AnimationCurve(keys);
            for(int i=0;i<keys.Length;i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);
            }
            clip.SetCurve(path,typeof(Transform),property,curve);
        }
    }
}
