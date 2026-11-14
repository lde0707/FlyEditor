using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FlyEditor.Flight;

namespace FlyEditor.Editor
{
    public static class PlyPartitionVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool ok,string message){if(!ok)throw new Exception("[PlyPartition] "+message);}
        public static PaperSheet Seed()
        {
            var s=new PaperSheet();
            var request=new FoldStep{anchor=0,flap=1,cornerPull=true,customFlanks=true,adaptiveCreases=true,
                regionalPull=true,flexible=true,separateFoldSides=true,continuousHinge=true,neutralContacts=true,compactLayers=true,
                pullTip=Vector2.zero,pullStop=new Vector2(11,0),pullLeft=new Vector2(0,5.5f),pullRight=new Vector2(0,11)};
            var pull=PaperCornerPull.Create(s,request);
            Check(pull!=null && pull.UsesEdgeMatch,"four-handle diagonal book plan");
            Check(s.FinishPull(pull,180),"four-handle diagonal book commit");
            return s;
        }
        static bool Owned(PaperSheet.Face f,IReadOnlyList<PaperSheet.Face> moved)
        {
            Vector2 c=Vector2.zero;foreach(var p in f.points)c+=p;var material=Material(f,c/f.points.Length);
            foreach(var g in moved)if(g.Contains(Local(g,material)))return true;return false;
        }
        static FoldStep Modern(FoldStep step)
        {
            var json=JsonUtility.ToJson(step);
            return JsonUtility.FromJson<FoldStep>(json.Contains("\"materialPartition\"")?json.Replace("\"materialPartition\":false","\"materialPartition\":true"):json.TrimEnd('}')+",\"materialPartition\":true}");
        }
        public static void Nested()
        {
            var s=new PaperSheet();Check(s.ApplyStep(new FoldStep{creaseNormal=Vector2.right,creaseOffset=5.5f,side=1,degrees=180,continuousHinge=true,neutralContacts=true,compactLayers=true}),"first book");
            int top=0;for(int i=0;i<s.Faces.Count;i++)if(s.Faces[i].layer>s.Faces[top].layer)top=i;
            var request=Modern(new FoldStep{anchor=top,flap=0,cornerPull=true,customFlanks=true,adaptiveCreases=true,flexible=true,continuousHinge=true,neutralContacts=true,compactLayers=true,
                pullTip=Vector2.zero,pullStop=new Vector2(5.5f,0),pullLeft=new Vector2(0,5.5f),pullRight=new Vector2(0,11)});
            var pull=PaperCornerPull.Create(s,request);Check(pull!=null&&pull.UsesEdgeMatch,"nested edge plan");
            var previous=(PaperSheet.Turn)typeof(PaperCornerPull).GetProperty("EdgeTurn",Hidden).GetValue(pull);
            var moved=new List<PaperSheet.Face>(previous.movingFaces);Check(s.FinishPull(pull,180),"nested edge commit");
            var contact=new Vector2(1,3);top=-1;for(int i=0;i<s.Faces.Count;i++)if(s.Faces[i].Contains(contact)&&(top<0||s.Faces[i].layer>s.Faces[top].layer))top=i;
            Check(top>=0&&Owned(s.Faces[top],moved),"nested top face");int cases=0;
            foreach(float angle in new[]{-20f,-18f,0f,18f,20f})foreach(float offset in new[]{-.2f,-.5f,-1f,-2f}){
                var n=(Vector2)(Quaternion.Euler(0,0,angle)*new Vector2(1,-1).normalized);
                var line=Modern(new FoldStep{anchor=top,flap=top+1,singlePanel=true,connectedPanel=true,separateFoldSides=true,continuousHinge=true,neutralContacts=true,compactLayers=true,
                    creaseNormal=n,creaseOffset=offset,side=-1,contact=contact});var turn=s.Plan(line);
                if(turn!=null)foreach(var f in turn.movingFaces)Check(Owned(f,moved),"nested four-handle book carries untouched lower ply at "+angle+"/"+offset);
                // A parallel crease wholly inside the picked leaf must remain usable.
                if(angle==0 && offset==-.5f){
                    Check(turn!=null&&turn.Constrain(60)==60,"valid upper wing became unselectable");
                    typeof(SelectionRegionVerification).GetMethod("MeshMatches",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{s,turn,top,null});
                }cases++;
            }
            Debug.Log("[PlyPartition] PASS nested four-handle ownership "+cases+" line variants; safe rejection of incompatible single-axis paths.");
        }
        static Vector3 Material(PaperSheet.Face f,Vector2 p)=>f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
        static Vector2 Local(PaperSheet.Face f,Vector3 p){var q=f.material.inverse.MultiplyPoint3x4(p);return new Vector2(q.x,q.z);}
        static bool Lower(PaperSheet.Face f)
        {
            Vector2 centre=Vector2.zero;foreach(var p in f.points)centre+=p;var m=Material(f,centre/f.points.Length);
            return m.x<m.z;
        }
        static void Fixed(IReadOnlyList<PaperSheet.Face> before,IReadOnlyList<PaperSheet.Face> after)
        {
            foreach(var f in before)if(Lower(f))foreach(var p in f.points){var m=Material(f,p);bool found=false;
                foreach(var g in after){var uv=Local(g,m);if(!g.Contains(uv))continue;
                    Check(Vector3.Distance(f.World(p,0),g.World(uv,0))<.002f,"untouched lower ply moved");found=true;}
                Check(found,"lower ply material lost");}
        }
        static FoldStep Wing(PaperSheet s,bool skew)
        {
            var contact=new Vector2(2,7);int top=-1;
            for(int i=0;i<s.Faces.Count;i++)if(s.Faces[i].Contains(contact)&&(top<0||s.Faces[i].layer>s.Faces[top].layer))top=i;
            Check(top>=0 && !Lower(s.Faces[top]),"visible top ply");
            var n=new Vector2(1,-1).normalized;if(skew)n=Quaternion.Euler(0,0,4)*n;
            return new FoldStep{anchor=top,flap=top+1,singlePanel=true,connectedPanel=true,separateFoldSides=true,
                continuousHinge=true,neutralContacts=true,compactLayers=true,creaseNormal=n,creaseOffset=skew?.3f:-2f,side=-1,contact=contact};
        }
        public static void Verify()
        {
            Nested();
            SurfaceIdentity();
            ModernWings();
            var sheet=Seed();var invalid=sheet.Plan(Wing(sheet,true));
            if(invalid!=null){bool lower=false;foreach(var f in invalid.movingFaces)lower|=Lower(f);
                Check(!lower,"four-handle book lost ply ownership: skewed top selection carries lower ply");}
            var before=sheet.Faces;var turn=sheet.Plan(Wing(sheet,false));Check(turn!=null,"upper-only wing plan");
            foreach(var f in turn.movingFaces)Check(!Lower(f),"lower ply in selected wing");
            var preview=new List<PaperSheet.Face>();var method=typeof(PaperSheet).GetMethod("TryPreviewTurn",Hidden);
            int poses=0;foreach(float a in new[]{0f,3f,30f,60f,90f,120f,179f,180f}){
                Check((bool)method.Invoke(sheet,new object[]{turn,a,preview}),"wing preview "+a);
                Fixed(before,preview);ReverseDirectionVerification.Geometry(preview);poses++;
            }
            Check(sheet.Finish(turn,90),"upper wing commit");Fixed(before,sheet.Faces);PetalFoldingVerification.Replay(sheet);
            Check(sheet.Undo() && ReferenceEquals(before,sheet.Faces),"undo restores original material");
            Debug.Log("[PlyPartition] PASS four-handle book, lower-ply isolation, selected-line split, "+poses+" poses, area/seams, replay and undo.");
        }
        static void SurfaceIdentity()
        {
            var s=Seed();var at=new Vector2(2,7);int top=-1,bottom=-1;
            for(int i=0;i<s.Faces.Count;i++)if(s.Faces[i].Contains(at)){
                if(top<0||s.Faces[i].layer>s.Faces[top].layer)top=i;if(bottom<0||s.Faces[i].layer<s.Faces[bottom].layer)bottom=i;
            }
            var upper=s.CaptureSurface(new PaperSheet.FoldVertex(top,at));var lower=s.CaptureSurface(new PaperSheet.FoldVertex(bottom,at));
            var before=s.Faces;var oldLower=before[bottom].World(at,0);var turn=s.Plan(Modern(Wing(s,false)));Check(turn!=null&&s.Finish(turn,90),"identity split commit");
            Check(s.TryResolveSurface(upper,out var a),"upper material handle lost through split/transform/packing");
            Check(s.TryResolveSurface(lower,out var b),"lower material handle lost through split/transform/packing");
            Check(Vector3.Distance(s.Faces[b.anchor].World(b.point,0),oldLower)<.002f,"stable lower handle rebound to upper ply");
            Check(Vector3.Distance(s.Faces[a.anchor].World(a.point,0),before[top].World(at,0))>1,"stable upper handle did not follow wing");
            Check(!new PaperSheet().TryResolveSurface(upper,out _),"surface handle crossed to another sheet");
            var copy=(PaperSheet)typeof(PaperSheet).GetMethod("PlanningCopy",Hidden).Invoke(s,null);Check(copy.TryResolveSurface(upper,out _),"planning copy lost identity");
            Check(s.Undo()&&s.TryResolveSurface(upper,out var restored)&&Vector3.Distance(s.Faces[restored.anchor].World(restored.point,0),before[top].World(at,0))<.002f,"undo lost material handle");
            Debug.Log("[PlyPartition] PASS persistent runtime material handles through split, transform, layer packing, planning copy and undo; foreign sheet rejected.");
        }
        static void ModernWings()
        {
            var pick=typeof(WingSideVerification).GetMethod("Pick",BindingFlags.NonPublic|BindingFlags.Static);
            var fixedCheck=typeof(WingSideVerification).GetMethod("Fixed",BindingFlags.NonPublic|BindingFlags.Static);
            var previewMethod=typeof(PaperSheet).GetMethod("TryPreviewTurn",Hidden);int cases=0,poses=0;
            foreach(bool narrow in new[]{false,true})for(int rotation=0;rotation<4;rotation++)foreach(bool upperFirst in new[]{false,true})foreach(float tail in new[]{-1f,4.5f,3.5f,2.5f}){
                var s=WingSideVerification.Seed(narrow,rotation,out var upper);
                foreach(bool top in new[]{upperFirst,!upperFirst}){
                    var step=Modern((FoldStep)pick.Invoke(null,new object[]{s,upper,rotation,top,tail}));var before=s.Faces;var turn=s.Plan(step);
                    Check(turn!=null,"modern wing "+narrow+"/"+rotation+"/"+top+"/"+tail);
                    foreach(var f in turn.movingFaces)Check(Owned(f,upper)==top,"modern selection carried opposite wing");
                    var preview=new List<PaperSheet.Face>();foreach(float degrees in new[]{0f,3f,45f,90f}){
                        Check((bool)previewMethod.Invoke(s,new object[]{turn,top?degrees:-degrees,preview}),"modern preview");
                        fixedCheck.Invoke(null,new object[]{before,preview,upper,top});ReverseDirectionVerification.Geometry(preview);poses++;
                    }
                    Check(s.Finish(turn,top?76:-82),"modern commit");fixedCheck.Invoke(null,new object[]{before,s.Faces,upper,top});PetalFoldingVerification.Replay(s);
                }cases++;
            }
            Debug.Log("[PlyPartition] PASS modern ownership "+cases+" rotated basic/narrow and tapered two-wing sequences, "+poses+" poses; either ply first, stationary opposite wing, seams, replay.");
        }
        public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    }
}
