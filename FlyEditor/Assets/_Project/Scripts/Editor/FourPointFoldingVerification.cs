using System;
using System.Collections.Generic;
using UnityEngine;
using FlyEditor.Flight;

namespace FlyEditor.Editor
{
    internal static class FourPointFoldingVerification
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException("[FourPoint] "+message);}
        static FoldStep Fan(int anchor,Vector2 tip,Vector2 stop,Vector2 left,Vector2 right)=>new FoldStep{
            cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,regionalPull=true,
            anchor=anchor,flap=anchor+1,pullTip=tip,pullStop=stop,pullLeft=left,pullRight=right};
        public static void Run()
        {
            int cases=0;
            foreach(float reach in new[]{1.8f,2.2f,2.75f})foreach(bool upper in new[]{false,true})foreach(bool swapped in new[]{false,true}){
                var sheet=new PaperSheet();
                Check(sheet.ApplyStep(new FoldStep(CreaseFamily.Vertical,0,1){creaseNormal=Vector2.right,creaseOffset=5.5f}),"stack setup");
                int index=upper?1:0;var fixedFace=sheet.Faces[1-index];var before=sheet.Faces;
                var left=new Vector2(0,2*reach);var right=new Vector2(2*reach,0);
                var step=Fan(index,Vector2.zero,new Vector2(reach,reach),swapped?right:left,swapped?left:right);
                var pull=PaperCornerPull.Create(sheet,step);int sign=upper?1:-1;
                Check(pull!=null && pull.Pose(0),"selected fan creation");
                Check(!pull.Pose(-sign*5),"passed through unselected ply");
                for(int angle=1;angle<=180;angle++)Check(pull.Pose(sign*angle),"selected fold stopped "+reach+" / "+upper+" / "+angle);
                Check(sheet.FinishPull(pull,sign*180),"commit selected fold");
                Check(Has(sheet.Faces,fixedFace),"unselected ply was rebuilt or moved");
                Metrics(sheet);Replay(sheet);
                Check(sheet.Undo() && ReferenceEquals(before,sheet.Faces),"undo did not restore exact source");cases++;
            }
            var stack=new PaperSheet();stack.ApplyStep(new FoldStep(CreaseFamily.Vertical,0,1){creaseNormal=Vector2.right,creaseOffset=5.5f});
            stack.ApplyStep(new FoldStep(CreaseFamily.Horizontal,0,1){creaseNormal=Vector2.up,creaseOffset=5.5f});
            int top=0;for(int i=1;i<stack.Faces.Count;i++)if(stack.Faces[i].layer>stack.Faces[top].layer)top=i;
            var fixedFaces=new List<PaperSheet.Face>(stack.Faces);fixedFaces.RemoveAt(top);
            var four=Fan(top,Vector2.zero,new Vector2(2.75f,2.75f),new Vector2(0,5.5f),new Vector2(5.5f,0));
            var folded=PaperCornerPull.Create(stack,four);Check(folded!=null,"four-ply selection");
            for(int angle=1;angle<=180;angle++)Check(folded.Pose(angle),"four-ply closure "+angle);
            Check(stack.FinishPull(folded,180),"four-ply commit");
            foreach(var f in fixedFaces)Check(Has(stack.Faces,f),"one of three untouched plies moved");
            Metrics(stack);Replay(stack);
            // A free fan must not tear the fixed seam merely because its
            // closed-form pose has no triangle-triangle penetration.
            var constrained=new PaperSheet();constrained.ApplyStep(new FoldStep(CreaseFamily.Vertical,0,1){creaseNormal=Vector2.right,creaseOffset=5.5f});
            var fixedPly=constrained.Faces[0];
            var fan=PaperCornerPull.Create(constrained,Fan(1,new Vector2(0,5.5f),new Vector2(2.2f,5.5f),new Vector2(0,3.3f),new Vector2(0,7.7f)));
            float safeAngle=0; Check(fan!=null && fan.TryMove(0,135,out safeAngle),"constrained fan start");
            if(Mathf.Abs(safeAngle)>=1){Check(constrained.FinishPull(fan,safeAngle),"constrained commit");Metrics(constrained);Replay(constrained);}
            Check(Has(constrained.Faces,fixedPly),"fixed material seam moved");
            var pocket=new PaperSheet();pocket.ApplyStep(new FoldStep(CreaseFamily.Falling,11,1));pocket.ApplyStep(new FoldStep(CreaseFamily.Rising,0,-1));
            var open=MaterialFan(pocket,2,new Vector2(0,11),new Vector2(5.5f,5.5f),Vector2.zero,new Vector2(11,11));
            Check(pocket.FinishPull(PaperCornerPull.Create(pocket,open),90),"connected pocket opening");Metrics(pocket);
            int anchor=-1;for(int i=0;i<pocket.Faces.Count;i++)if(pocket.Faces[i].Contains(Local(pocket.Faces[i],new Vector2(0,11)))){anchor=i;break;}
            Check(anchor>=0,"pocket handle lost");
            var press=MaterialFan(pocket,anchor,new Vector2(0,11),new Vector2(5.5f,5.5f),new Vector2(0,5.5f),new Vector2(5.5f,11));
            var pressing=PaperCornerPull.Create(pocket,press);
            float contact=0; Check(pressing!=null && pressing.TryMove(0,-90,out contact),"pocket contact solve");
            Check(contact< -89 && contact>= -90,"contact left a coarse angle gap: "+contact);
            Check(pocket.FinishPull(pressing,contact),"contact commit");Metrics(pocket);Replay(pocket);
            Debug.Log("[FourPoint] safe constrained fan="+safeAngle+"; refined pocket contact="+contact);
            Debug.Log("[FourPoint] PASS "+cases+" variable-geometry/side/flank cases; four-ply isolation; continuous contact, original area, seams, JSON replay and undo.");
        }
        static Vector2 Local(PaperSheet.Face face,Vector2 original){var p=face.material.inverse.MultiplyPoint3x4(new Vector3(original.x,0,original.y));return new Vector2(p.x,p.z);}
        static FoldStep MaterialFan(PaperSheet sheet,int anchor,Vector2 tip,Vector2 stop,Vector2 left,Vector2 right)
        {var f=sheet.Faces[anchor];return Fan(anchor,Local(f,tip),Local(f,stop),Local(f,left),Local(f,right));}
        static bool Has(IReadOnlyList<PaperSheet.Face> faces,PaperSheet.Face target){foreach(var f in faces)if(ReferenceEquals(f,target))return true;return false;}
        static void Replay(PaperSheet sheet)
        {
            var json=JsonUtility.ToJson(new ResearchSave{paperFolds=sheet.Recipe()});
            var recipe=JsonUtility.FromJson<ResearchSave>(json).paperFolds;
            Check(recipe[recipe.Length-1].regionalPull,"new selection policy lost in JSON");
            var replay=PaperSheet.Replay(recipe);Check(replay.Count==sheet.Count && replay.Faces.Count==sheet.Faces.Count,"replay count");
            for(int i=0;i<sheet.Faces.Count;i++){
                var a=sheet.Faces[i];var b=replay.Faces[i];Check(a.layer==b.layer && a.points.Length==b.points.Length,"replay topology");
                for(int j=0;j<a.points.Length;j++)Check(Vector3.Distance(a.World(a.points[j],0),b.World(b.points[j],0))<.0001f,"replay pose");
            }
        }
        public static void Metrics(PaperSheet sheet)
        {
            float area=0;var seams=new Dictionary<Vector2Int,Vector3>();
            foreach(var f in sheet.Faces){
                float part=0;
                for(int i=0;i<f.points.Length;i++){
                    var p=f.points[i];var q=f.points[(i+1)%f.points.Length];part+=p.x*q.y-q.x*p.y;
                    var original=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
                    var key=new Vector2Int(Mathf.RoundToInt(original.x*10000),Mathf.RoundToInt(original.z*10000));
                    var world=f.World(p,(f.layer+.5f)*PaperSheet.Thickness);
                    Check(float.IsFinite(world.x)&&float.IsFinite(world.y)&&float.IsFinite(world.z),"nonfinite geometry");
                    if(seams.TryGetValue(key,out var other))Check(Vector3.Distance(world,other)<.04f,"shared material seam tore");else seams.Add(key,world);
                }area+=Mathf.Abs(part)*.5f;
            }Check(Mathf.Abs(area-sheet.Width*sheet.Height)<.01f,"material area changed");
        }
    }
}
