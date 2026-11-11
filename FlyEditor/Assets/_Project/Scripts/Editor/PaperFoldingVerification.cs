using System;
using System.Collections.Generic;
using UnityEngine;
using FlyEditor.Flight;

namespace FlyEditor.Editor
{
    internal static class PaperFoldingVerification
    {
        public static void Run()
        {
            var sheet=new PaperSheet();
            var guides=new List<PaperSheet.Guide>();
            foreach(CreaseFamily family in Enum.GetValues(typeof(CreaseFamily))){
                sheet.Guides(family,guides);
                Check(guides.Count==((int)family<2?11:3217),"Wrong crease count");
            }
            VerifyPointPairs(sheet);
            VerifyVertexFolds();
            VerifySeparatePlies();
            VerifySelectedPanel();
            VerifyMirrorFolds();
            VerifyInsideReverse();
            VerifyCornerPull();
            VerifyFlexibleFolds();
            VerifyContactClosure();
            VerifyOpeningPlies();
            Check(sheet.Plan(new FoldStep(CreaseFamily.Vertical,0,1))==null,"Folded on an outer boundary");
            Fold(sheet,CreaseFamily.Vertical,5,-1);
            Check(LayersAt(sheet,new Vector2(7,3))==2 && LayersAt(sheet,new Vector2(10.5f,3))==1,
                "An overhanging strip was counted as a double layer");
            Fold(sheet,CreaseFamily.Horizontal,5,-1);
            Check(LayersAt(sheet,new Vector2(7,8))==4,"Second fold did not stack four plies");
            Fold(sheet,CreaseFamily.Rising,0,1);
            Check(LayersAt(sheet,new Vector2(7,8))==8 && sheet.PeakPlies==8,"Third fold did not stack eight plies");
            var rebuilt=PaperSheet.Replay(sheet.Recipe());
            Check(rebuilt.Count==3 && LayersAt(rebuilt,new Vector2(7,8))==8,"Fold recipe replay mismatch");
            Check(sheet.Undo() && LayersAt(sheet,new Vector2(7,8))==4 && sheet.PeakPlies==4,"Undo did not restore the stack");
            Fold(sheet,CreaseFamily.Rising,0,-1);
            Check(LayersAt(sheet,new Vector2(8,7))==8,"Opposite side folded incorrectly");
            Fold(sheet,CreaseFamily.Falling,15,1);
            var accordion=new PaperSheet();
            for(int k=1;k<=accordion.FoldLimit;k++){
                var turn=accordion.Plan(new FoldStep(CreaseFamily.Vertical,k,-1){creaseNormal=Vector2.right,creaseOffset=k*accordion.Width/(accordion.FoldLimit+1),compactLayers=true});
                Check(accordion.Finish(turn) && !accordion.Finish(turn),"Accordion fold or stale turn");
            }
            Check(accordion.Plan(new FoldStep(CreaseFamily.Horizontal,5,1))==null,"Fold limit exceeded");
            VerifyDart();
            VerifyPatterns();
            VerifyAircraftGrip();
            VerifyFoldRatings();
            var angled=new PaperSheet();
            var first=angled.Plan(new FoldStep(CreaseFamily.Vertical,5,-1));
            Check(angled.Finish(first,43),"Oblique fold failed");
            angled.Guides(CreaseFamily.Vertical,guides);
            var raised=guides.Find(g=>g.line.anchor!=0 && g.line.offset==2);
            Check(raised.face!=null && angled.Finish(angled.Plan(raised.line),34f),"Could not crease the tilted flap");
            Check(angled.Plan(new FoldStep(CreaseFamily.Horizontal,5,1))==null,"Bent across a non-collinear joint");
            Debug.Log("[PaperFolding] PASS: 11+11 axis guides, 169 connected points, area, four/eight plies, opposite sides, undo and replay.");
        }
        private static void VerifyInsideReverse()
        {
            foreach(float opening in new[]{40f,100f,180f}){
                var sheet=new PaperSheet();var fold=new FoldStep(CreaseFamily.Rising,0,1);
                Check(sheet.Finish(sheet.Plan(fold),opening),"Opening crease");
                var face=sheet.Faces[0];var tip=Vector2.zero;var stop=new Vector2(3.75f,3.75f);
                var step=new FoldStep(CreaseFamily.Rising,0,1){cornerPull=true,insideReverse=true,
                    singlePanel=true,flap=1,pullTip=tip,pullStop=stop,degrees=180};
                var tuck=PaperInsideReverse.Create(sheet,step);
                Check(tuck!=null && tuck.Creases.Count==2,"Missing secondary creases "+opening);
                foreach(var line in tuck.Creases){
                    var a=line.face.material.MultiplyPoint3x4(new Vector3(line.a.x,0,line.a.y));
                    var b=line.face.material.MultiplyPoint3x4(new Vector3(line.b.x,0,line.b.y));
                    Check((a-face.material.MultiplyPoint3x4(new Vector3(stop.x,0,stop.y))).magnitude<.001f || (b-face.material.MultiplyPoint3x4(new Vector3(stop.x,0,stop.y))).magnitude<.001f,"Crease missed junction");
                }
                for(int d=0;d<=180;d+=3)Check(tuck.Pose(d),"Tuck blocked "+opening+" / "+d);
                var old=MaterialPoint(sheet,tip);var fixedCorner=MaterialPoint(sheet,new Vector2(11,11));
                Check(sheet.FinishReverse(tuck,180),"Finish tuck");
                var moved=MaterialPoint(sheet,tip);var pivot=MaterialPoint(sheet,stop);
                var axis=(old-pivot).normalized;
                Check(Vector3.Dot(moved-pivot,axis)<(old-pivot).magnitude-.5f && Mathf.Abs((moved-pivot).magnitude-(old-pivot).magnitude)<.04f,
                    "Tip did not turn inward at constant length");
                Check((MaterialPoint(sheet,new Vector2(11,11))-fixedCorner).magnitude<.04f,"Pocket did not close");
                float area=0;foreach(var f in sheet.Faces)area+=PaperSheet.Area(f.points);
                Check(Mathf.Abs(area-121f)<.001f,"Lost material at tuck");
                if(opening==180){
                    Check(sheet.PeakPlies==4,"Closed pocket did not separate caps from outer plies");
                    var levels=new HashSet<int>();foreach(var f in sheet.Faces)levels.Add(f.layer);
                    Check(levels.Count==4,"Coplanar caps sharing the same height");
                }
                var saved=JsonUtility.FromJson<ResearchSave>(JsonUtility.ToJson(new ResearchSave{paperFolds=sheet.Recipe()}));
                var rebuilt=PaperSheet.Replay(saved.paperFolds);
                Check(rebuilt.Count==2 && (MaterialPoint(rebuilt,tip)-moved).magnitude<.001f && sheet.Undo(),"Replay/undo tuck");
            }
            foreach(var axis in new[]{MirrorAxis.Horizontal,MirrorAxis.Vertical}){
                var sheet=new PaperSheet();var half=new FoldStep(CreaseFamily.Horizontal,0,1){creaseNormal=Vector2.up,creaseOffset=5.5f};
                Check(sheet.Finish(sheet.Plan(half),60),"Symmetric pocket fixture");
                var step=new FoldStep(CreaseFamily.Horizontal,0,1){creaseNormal=Vector2.up,creaseOffset=5.5f,
                    flap=1,cornerPull=true,insideReverse=true,singlePanel=true,mirror=axis,
                    pullTip=new Vector2(0,5.5f),pullStop=new Vector2(2,5.5f),degrees=180};
                Check(sheet.ApplyStep(step) && sheet.Count==2 && PaperSheet.Replay(sheet.Recipe()).Count==2,"Symmetric reverse "+axis);
                if(axis==MirrorAxis.Vertical){
                    var a=MaterialPoint(sheet,new Vector2(0,5.5f));var b=MaterialPoint(sheet,new Vector2(11,5.5f));
                    Check(Mathf.Abs(a.y-b.y)<.03f && Mathf.Abs(a.x+b.x-11)<.03f,"Asymmetric paired tuck");
                }
            }
            Debug.Log("[InsideReverse] PASS folded tip inside pocket, two flank creases, rigid lengths, 40/100/180 degree starting folds, save and undo.");
        }
        private static void VerifyMirrorFolds()
        {
            foreach(var axis in new[]{MirrorAxis.Horizontal,MirrorAxis.Vertical}){
                var paper=new PaperSheet();
                var n=axis==MirrorAxis.Horizontal?Vector2.up:Vector2.right;
                var step=new FoldStep(CreaseFamily.Horizontal,2,-1){creaseNormal=n,creaseOffset=2,flap=1,
                    singlePanel=true,mirror=axis,contact=Vector2.one};
                var pair=PaperMirrorFold.Create(paper,step);
                Check(pair!=null && pair.Pose(65) && paper.Count==0,"Atomic mirror preview "+axis);
                Check(paper.FinishPair(pair,65) && paper.Count==1,"Paired fold");
                var a=MaterialPoint(paper,Vector2.one);
                var b=MaterialPoint(paper,axis==MirrorAxis.Horizontal?new Vector2(1,10):new Vector2(10,1));
                Check(a.y>.7f && Mathf.Abs(a.y-b.y)<.001f,"Mirror angles "+axis);
                Check(Mathf.Abs((axis==MirrorAxis.Horizontal?a.z+b.z:a.x+b.x)-11)<.001f,"Mirror positions");
                var saved=JsonUtility.FromJson<ResearchSave>(JsonUtility.ToJson(new ResearchSave{paperFolds=paper.Recipe()}));
                Check(PaperSheet.Replay(saved.paperFolds).Count==1 && paper.Undo() && paper.Faces.Count==1,"Mirror save or undo");
                step.creaseOffset=4;step.side=1;step.contact=new Vector2(5,5);
                pair=PaperMirrorFold.Create(paper,step);
                Check(pair==null || !paper.FinishPair(pair,60),"Tore mirror seam");
                Check(paper.Count==0 && paper.Faces.Count==1,"Partial mirror commit");
            }
            var folded=new PaperSheet();
            var half=new FoldStep(CreaseFamily.Horizontal,0,1){creaseNormal=Vector2.up,creaseOffset=5.5f,
                flap=1,singlePanel=true,mirror=MirrorAxis.Horizontal,contact=new Vector2(3,8),degrees=180};
            Check(folded.ApplyStep(half) && folded.Faces.Count==2,"Central crease applied more than once");
            int upper=folded.Faces[0].layer>folded.Faces[1].layer?0:1;
            var wing=new FoldStep(CreaseFamily.Horizontal,3,-1){singlePanel=true,anchor=upper,flap=upper+1,
                mirror=MirrorAxis.Horizontal,contact=new Vector2(2,1),degrees=70};
            var wrong=wing;wrong.degrees=-70;
            Check(!folded.ApplyStep(wrong) && folded.Count==1,"Mirror passed through an overlapping ply");
            Check(folded.ApplyStep(wing),"Mirrored stacked wings");
            Check(MaterialPoint(folded,new Vector2(2,10)).y>1 && MaterialPoint(folded,new Vector2(2,1)).y< -1,"Wrong stacked wing direction");

            foreach(bool stack in new[]{false,true}){
                var paper=new PaperSheet();
                if(stack)Check(paper.Finish(paper.Plan(new FoldStep(CreaseFamily.Rising,0,1)),40),"Inclined face");
                var other=paper.Faces[0];int target=paper.Faces.Count-1;
                var pull=new FoldStep(CreaseFamily.Rising,0,1){singlePanel=true,cornerPull=true,anchor=target,flap=target+1,
                    pullTip=new Vector2(11,0),pullStop=stack?new Vector2(8.1f,3.2f):new Vector2(8.1f,2f),degrees=10,
                    mirror=stack?MirrorAxis.Off:MirrorAxis.Horizontal};
                Check(paper.ApplyStep(pull),"Single-face or paired corner pull "+stack);
                if(stack){
                    bool fixedFace=false;foreach(var f in paper.Faces)fixedFace|=ReferenceEquals(f,other);
                    Check(fixedFace,"Neighbour tilted along with the selected face");
                }else{
                    var a=MaterialPoint(paper,new Vector2(11,0));var b=MaterialPoint(paper,new Vector2(11,11));
                    Check(a.y>.4f && Mathf.Abs(a.y-b.y)<.03f,"Uneven corner pull");
                }
            }
            Debug.Log("[MirrorFold] PASS two axes, paired angles, centre crease once, stacked opposite wings, fixed neighbour, paired pull, atomic save/undo.");
        }
        private static Vector3 MaterialPoint(PaperSheet sheet,Vector2 original)
        {
            foreach(var f in sheet.Faces){
                var v=f.material.inverse.MultiplyPoint3x4(new Vector3(original.x,0,original.y));var uv=new Vector2(v.x,v.z);
                if(f.Contains(uv))return f.World(uv,(f.layer+.5f)*PaperSheet.Thickness);
            }
            throw new InvalidOperationException("Lost material "+original);
        }
        private static void VerifyCornerPull()
        {
            foreach(float prior in new[]{0f,40f}){
                var sheet=new PaperSheet();
                if(prior!=0)Check(sheet.Finish(sheet.Plan(new FoldStep(CreaseFamily.Rising,0,1)),prior),"Tilt reference triangle");
                int anchor=sheet.Faces.Count-1;
                var tip=new Vector2(11,0);var stop=new Vector2(8.1f,3.2f);
                var baseFace=sheet.Faces[anchor];
                var step=new FoldStep(CreaseFamily.Rising,0,1){cornerPull=true,anchor=anchor,flap=anchor+1,
                    pullTip=tip,pullStop=stop};
                var pull=PaperCornerPull.Create(sheet,step);
                Check(pull!=null && pull.Pose(0),"Corner ends "+prior);
                for(int angle=5;angle<=40;angle+=5)Check(pull.Pose(angle),"Blocked corner "+prior+" at "+angle);
                Check((pull.Tip-baseFace.World(tip,(baseFace.layer+.5f)*PaperSheet.Thickness)).magnitude>1,"Tip did not lift");
                foreach(var corner in new[]{Vector2.zero,new Vector2(11,11)}){
                    var rest=baseFace.World(corner,(baseFace.layer+.5f)*PaperSheet.Thickness);
                    bool moved=false;
                    foreach(var f in pull.Preview)
                        if(f.Contains(corner) && (rest-f.World(corner,(f.layer+.5f)*PaperSheet.Thickness)).magnitude>.1f)moved=true;
                    Check(moved,"Lateral corner did not follow "+prior+" "+corner);
                }
                Check(sheet.FinishPull(pull,40),"Commit corner");
                var replay=PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(JsonUtility.ToJson(new ResearchSave{paperFolds=sheet.Recipe()})).paperFolds);
                Check(replay.Count==sheet.Count && (replay.SpatialBounds.size-sheet.SpatialBounds.size).sqrMagnitude<.00001f,"Pull replay");
                Check(sheet.Undo() && sheet.Faces.Count==anchor+1,"Pull undo");
            }
            foreach(bool top in new[]{true,false}){
                var stack=new PaperSheet();
                Check(stack.Finish(stack.Plan(new FoldStep(CreaseFamily.Horizontal,0,1){creaseNormal=Vector2.up,creaseOffset=5.5f})),"Fold stack");
                int upper=stack.Faces[0].layer>stack.Faces[1].layer?0:1;
                int i=top?upper:1-upper;var other=stack.Faces[1-i];
                var step=new FoldStep(CreaseFamily.Rising,0,1){cornerPull=true,anchor=i,flap=i+1,pullTip=new Vector2(11,0),pullStop=new Vector2(8.1f,2f)};
                var pull=PaperCornerPull.Create(stack,step);
                Check(pull!=null && !pull.Pose(top?-10:10),"Pulled through other ply");
                Check(pull.Pose(top?10:-10),"Could not pull free ply outward "+top);
                bool stationary=false;foreach(var f in pull.Preview)stationary|=ReferenceEquals(f,other);
                Check(stationary,"Opposite ply shifted");
            }
            Debug.Log("[CornerPull] PASS flat and tilted triangle, interior stop, moving tip and side corners, save/undo.");
        }
        private static void VerifyContactClosure()
        {
            foreach(bool four in new[]{false,true})foreach(int sign in new[]{-1,1}){
                var sheet=new PaperSheet();
                var step=new FoldStep(CreaseFamily.Rising,0,1){cornerPull=true,closeFlat=true,flexible=four,customFlanks=four,
                    pullTip=Vector2.zero,pullStop=new Vector2(5.5f,5.5f),pullLeft=new Vector2(0,11),pullRight=new Vector2(11,0)};
                var pull=PaperCornerPull.Create(sheet,step);
                Check(pull!=null,"Closure did not build");
                float limit=pull.AngleLimit;
                for(int angle=1;angle<=limit;angle++)Check(pull.Pose(sign*angle),"Closure stopped at "+angle+" four="+four+" sign="+sign);
                Check(!pull.Pose(sign*(limit+1)),"Fold went through its contacting faces");
                Check(sheet.FinishPull(pull,sign*limit),"Closure commit");CheckPaperGeometry(sheet);
                bool touching=false;
                foreach(var a in sheet.Faces)foreach(var c in sheet.Faces){
                    Vector2 centre=Vector2.zero;foreach(var point in a.points)centre+=point;centre/=a.points.Length;
                    if(a.frame==c.frame && Mathf.Abs(a.layer-c.layer)==1 && c.Contains(centre))touching=true;
                }
                Check(touching,"Closed faces did not share a thickness-separated contact plane");
                var replay=PaperSheet.Replay(sheet.Recipe());
                Check(replay.Count==1 && (replay.SpatialBounds.size-sheet.SpatialBounds.size).sqrMagnitude<.00001f,"Closure replay mismatch");
                if(four){
                    Check(sheet.SpatialBounds.size.y<PaperSheet.Thickness*2.1f,"Straight closure left an open gap");
                    var next=new FoldStep(CreaseFamily.Horizontal,5,1);
                    Check(sheet.Finish(sheet.Plan(next),45),"Cannot fold the closed stack again");
                    Check(PaperSheet.Replay(sheet.Recipe()).Count==2,"Closed stack second fold replay");
                    sheet.Undo();
                }
                Check(sheet.Undo() && sheet.Count==0,"Closure undo");
            }
            // Existing saves retain their previous fan angles; opting into closure
            // happens only in a new fold or while explicitly editing that fold.
            var old=new PaperSheet();var legacy=new FoldStep(CreaseFamily.Rising,0,1){cornerPull=true,pullTip=Vector2.zero,pullStop=new Vector2(5.5f,5.5f)};
            var legacyPull=PaperCornerPull.Create(old,legacy);
            Check(legacyPull.AngleLimit==75 && old.FinishPull(legacyPull,60),"Legacy fold changed");
            Check(PaperSheet.Replay(old.Recipe()).Count==1,"Legacy replay failed");
            Debug.Log("[ContactClosure] PASS continuous +/- fan contact and straight 180, thickness, area, seams, closed stack refold, legacy, replay and undo.");
        }
        private static void VerifyOpeningPlies()
        {
            int cases=0;
            foreach(float foldedAngle in new[]{45f,90f,150f,180f,-180f})
            foreach(bool diagonal in new[]{false,true}){
                var sheet=new PaperSheet();
                var fold=diagonal?new FoldStep(CreaseFamily.Rising,0,1):
                    new FoldStep(CreaseFamily.Horizontal,0,1){creaseNormal=Vector2.up,creaseOffset=5.5f};
                Check(sheet.Finish(sheet.Plan(fold),foldedAngle),"Opening setup");
                var original=sheet.Faces;
                for(int f=0;f<original.Count;f++){
                    bool found=false;
                    foreach(var point in original[f].points){
                        var turns=new List<PaperSheet.Turn>();sheet.OpeningTurns(new PaperSheet.FoldVertex(f,point),turns);
                        foreach(var turn in turns){
                            found=true;
                            Check(!turn.MovesOtherPlyAt(point),"Opening selected two coincident plies");
                            foreach(float requested in new[]{-60f,60f}){
                                float angle=turn.Constrain(requested);if(Mathf.Abs(angle)<2)continue;
                                var handle=original[f].World(point,(original[f].layer+.5f)*PaperSheet.Thickness);
                                Check((turn.Rotation(angle).MultiplyPoint3x4(handle)-handle).magnitude>.1f,"Opening did not move handle");
                                Check(sheet.Finish(turn,angle),"Opening commit");
                                foreach(var fixedFace in turn.fixedFaces){
                                    bool unchanged=false;foreach(var current in sheet.Faces)unchanged|=ReferenceEquals(current,fixedFace);
                                    Check(unchanged,"Unselected face moved");
                                }
                                CheckPaperGeometry(sheet);
                                var replay=PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(JsonUtility.ToJson(new ResearchSave{paperFolds=sheet.Recipe()})).paperFolds);
                                Check(replay.Count==sheet.Count && (replay.SpatialBounds.size-sheet.SpatialBounds.size).sqrMagnitude<.00001f,"Opening save replay");
                                Check(sheet.Undo() && ReferenceEquals(sheet.Faces,original),"Opening undo");cases++;
                            }
                        }
                    }
                    Check(found,"No free corner can open the ply: "+foldedAngle+" "+diagonal+" "+f);
                }
            }
            // Multiple folds: look for independent wings and keep opening them one at a time.
            var layered=new PaperSheet();
            Check(layered.Finish(layered.Plan(new FoldStep(CreaseFamily.Rising,0,1))),"Layered opening setup");
            Check(layered.Finish(layered.Plan(new FoldStep(CreaseFamily.Horizontal,0,1){creaseNormal=Vector2.up,creaseOffset=5.5f}),65),"Second opening setup");
            int available=0;
            for(int face=0;face<layered.Faces.Count;face++)foreach(var p in layered.Faces[face].points){
                var turns=new List<PaperSheet.Turn>();layered.OpeningTurns(new PaperSheet.FoldVertex(face,p),turns,true);available+=turns.Count;
            }
            Check(available>0,"Connected stack lost all opening handles");
            var wing=new PaperSheet();
            Check(wing.Finish(wing.Plan(new FoldStep(CreaseFamily.Rising,0,1))),"Independent wing setup");
            Check(wing.Finish(wing.Plan(new FoldStep(CreaseFamily.Rising,0,-1){creaseNormal=new Vector2(1,-1),creaseOffset=-5.5f,
                anchor=1,flap=2,singlePanel=true}),65),"Independent wing second crease");
            int independent=0;
            for(int face=0;face<wing.Faces.Count;face++)foreach(var p in wing.Faces[face].points){
                var turns=new List<PaperSheet.Turn>();wing.OpeningTurns(new PaperSheet.FoldVertex(face,p),turns);independent+=turns.Count;
            }
            Check(independent>0,"Second-crease free wing could not open independently");
            int repeated=VerifyRepeatedOpening(layered,true)+VerifyRepeatedOpening(wing,false);
            var contact=new PaperSheet();
            Check(contact.Finish(contact.Plan(new FoldStep(CreaseFamily.Rising,0,1)),150),"Opening contact setup");
            var closing=new List<PaperSheet.Turn>();contact.OpeningTurns(new PaperSheet.FoldVertex(1,new Vector2(11,0)),closing);
            Check(closing.Count>0 && closing[0].Constrain(180)>29 && closing[0].Constrain(180)<31,"Opening crossed the other ply at flat contact");
            Debug.Log($"[OpeningPlies] PASS {cases} angle/layer/direction commits, fixed other plies, geometry, replay, undo; repeated-fold handles={available}, repeated commits={repeated}, swept contact.");
        }
        private static int VerifyRepeatedOpening(PaperSheet sheet,bool stack)
        {
            int cases=0;var original=sheet.Faces;
            for(int face=0;face<original.Count;face++)foreach(var p in original[face].points){
                var turns=new List<PaperSheet.Turn>();sheet.OpeningTurns(new PaperSheet.FoldVertex(face,p),turns,stack);
                foreach(var turn in turns){
                    float angle=turn.Constrain(35);if(angle<5)angle=turn.Constrain(-35);
                    if(Mathf.Abs(angle)<5)continue;
                    Check(sheet.Finish(turn,angle),"Repeated opening commit");CheckPaperGeometry(sheet);
                    var replay=PaperSheet.Replay(sheet.Recipe());
                    Check(replay.Count==sheet.Count && (replay.SpatialBounds.size-sheet.SpatialBounds.size).sqrMagnitude<.00001f,"Repeated opening replay");
                    Check(sheet.Undo() && ReferenceEquals(sheet.Faces,original),"Repeated opening undo");cases++;
                }
            }
            Check(cases>0,"Repeated crease has no usable opening");return cases;
        }
        private static void VerifyFlexibleFolds()
        {
            foreach(float baseAngle in new[]{0f,70f,180f}){
                var sheet=new PaperSheet();
                if(baseAngle>0)Check(sheet.Finish(sheet.Plan(new FoldStep(CreaseFamily.Rising,0,1)),baseAngle),"Flexible setup");
                var step=new FoldStep(CreaseFamily.Rising,0,1){anchor=0,cornerPull=true,flexible=true,
                    pullTip=Vector2.zero,pullStop=new Vector2(11f/3,11f/3)};
                var pull=PaperCornerPull.Create(sheet,step);
                Check(pull!=null,"Connected fold did not build");
                int target=baseAngle==180?150:60;
                for(int angle=5;angle<=target;angle+=5)Check(pull.Pose(angle),"Connected fold stopped at "+angle+" base "+baseAngle);
                Check(sheet.FinishPull(pull,target),"Connected fold commit");
                CheckPaperGeometry(sheet);
                var replay=PaperSheet.Replay(sheet.Recipe());
                Check(replay.Count==sheet.Count && (replay.SpatialBounds.size-sheet.SpatialBounds.size).sqrMagnitude<.00001f,"Connected fold replay");
                Check(sheet.Undo() && sheet.Count==(baseAngle==0?0:1),"Connected fold undo");
            }
            var custom=new PaperSheet();
            var four=new FoldStep(CreaseFamily.Rising,0,1){cornerPull=true,flexible=true,customFlanks=true,
                pullTip=Vector2.zero,pullStop=new Vector2(5.5f,5.5f),pullLeft=new Vector2(0,11),pullRight=new Vector2(11,0)};
            Check(custom.FinishPull(PaperCornerPull.Create(custom,four),100),"Four point fold");
            CheckPaperGeometry(custom);
            var recipe=custom.Recipe();
            Check(recipe[0].customFlanks && PaperSheet.Replay(recipe).Count==1,"Four point save");
            custom.Undo();four.pullRight=four.pullLeft;
            Check(PaperCornerPull.Create(custom,four)==null,"Invalid flank selection accepted");
            var twice=new PaperSheet();four.pullRight=new Vector2(11,0);
            Check(twice.FinishPull(PaperCornerPull.Create(twice,four),55),"First free fold");
            four.pullTip=new Vector2(11,11);
            for(int i=0;i<twice.Faces.Count;i++)if(twice.Faces[i].Contains(four.pullTip)){four.anchor=i;break;}
            Check(twice.FinishPull(PaperCornerPull.Create(twice,four),35),"Second free fold on bent paper");
            CheckPaperGeometry(twice);
            Check(PaperSheet.Replay(twice.Recipe()).Count==2,"Repeated free fold replay");
            var symmetric=new PaperSheet();
            var mirrored=new FoldStep(CreaseFamily.Rising,0,1){cornerPull=true,flexible=true,customFlanks=true,mirror=MirrorAxis.Horizontal,
                pullTip=new Vector2(11,0),pullStop=new Vector2(55f/6,11f/6),pullLeft=new Vector2(22f/3,0),pullRight=new Vector2(11,11f/3)};
            Check(symmetric.FinishPair(PaperMirrorFold.Create(symmetric,mirrored),25),"Mirrored four-point fold");
            CheckPaperGeometry(symmetric);
            Check(PaperSheet.Replay(symmetric.Recipe()).Count==1,"Mirrored handles replay");
            Debug.Log("[FlexibleFolds] PASS flat, tilted, closed stack to 150, four handles to 100, repeated free fold, mirrored handles, rigid triangle metrics, seam continuity, save, undo.");
        }
        private static void CheckPaperGeometry(PaperSheet sheet)
        {
            float area=0;var seams=new Dictionary<Vector2Int,Vector3>();
            foreach(var face in sheet.Faces){
                area+=PaperSheet.Area(face.points);
                foreach(var point in face.points){
                    var original=face.material.MultiplyPoint3x4(new Vector3(point.x,0,point.y));
                    var key=new Vector2Int(Mathf.RoundToInt(original.x*10000),Mathf.RoundToInt(original.z*10000));
                    var world=face.World(point,(face.layer+.5f)*PaperSheet.Thickness);
                    Check(!float.IsNaN(world.x) && !float.IsInfinity(world.y),"Nonfinite paper");
                    if(seams.TryGetValue(key,out var other))Check((world-other).magnitude<.035f,"Torn shared seam");
                    else seams.Add(key,world);
                }
                Check(Mathf.Abs(face.frame.MultiplyVector(Vector3.right).magnitude-1)<.0001f &&
                    Mathf.Abs(face.frame.MultiplyVector(Vector3.forward).magnitude-1)<.0001f,"Stretched paper");
            }
            Check(Mathf.Abs(area-sheet.Width*sheet.Height)<.015f,"Lost paper area");
        }
        private static void VerifySeparatePlies()
        {
            foreach(bool top in new[]{true,false}){
                var sheet=new PaperSheet();
                var half=new FoldStep(CreaseFamily.Horizontal,0,1){creaseNormal=Vector2.up,creaseOffset=sheet.Height*.5f};
                Check(sheet.Finish(sheet.Plan(half)),"Half fold");
                var point=new Vector2(2,1);
                int picked=sheet.Faces[0].layer>sheet.Faces[1].layer?0:1;
                if(!top)picked=1-picked;
                var other=sheet.Faces[1-picked];
                var fold=new FoldStep(CreaseFamily.Horizontal,3,-1){flap=picked+1,anchor=picked};
                var turn=sheet.Plan(fold);
                Check(turn!=null && !turn.MovesOtherPlyAt(point) && turn.movingFaces.Count==1 &&
                    turn.movingFaces[0].layer==sheet.Faces[picked].layer,"Wrong single-ply preview");
                Check(sheet.Finish(turn,top?72:-72),"Couldn't lift selected ply");
                float still=0;
                foreach(var f in sheet.Faces)
                    if(f.frame==other.frame && f.material==other.material && f.layer==other.layer)still+=PaperSheet.Area(f.points);
                Check(Mathf.Abs(still-PaperSheet.Area(other.points))<.001f,"Opposite ply moved");
                Check(PaperSheet.Replay(sheet.Recipe()).Count==2 && sheet.Undo(),"Single ply replay or undo");
                var joined=new FoldStep(CreaseFamily.Vertical,4,-1){flap=picked+1,anchor=picked};
                var linked=sheet.Plan(joined);
                Check(linked==null || linked.MovesOtherPlyAt(point),"Silently moved both joined plies");
            }
            Debug.Log("[SeparatePlies] PASS upper and lower isolated, other ply stationary, joined guard, undo and replay.");
        }
        private static void VerifySelectedPanel()
        {
            foreach(bool upper in new[]{true,false}){
                var sheet=new PaperSheet();
                Check(sheet.Finish(sheet.Plan(new FoldStep(CreaseFamily.Horizontal,0,1){creaseOffset=5.5f,creaseNormal=Vector2.up})),"Stack setup");
                int picked=upper?1:0;var other=sheet.Faces[1-picked];
                var a=new PaperSheet.FoldVertex(picked,new Vector2(0,3));
                var b=new PaperSheet.FoldVertex(picked,new Vector2(11,3));
                Check(sheet.TryAxis(a,b,out var axis) && axis.singlePanel && axis.flap==picked+1,"Selected axis lost its ply");
                axis.side=-1;var turn=sheet.Plan(axis);
                Check(turn!=null && turn.movingFaces.Count==1 && !turn.MovesOtherPlyAt(Vector2.one),"Selected wing blocked or moved another ply");
                Check(sheet.Finish(turn,upper?35:-35),"Selected wing cannot bend");
                bool stationary=false;foreach(var face in sheet.Faces)stationary|=ReferenceEquals(face,other);
                Check(stationary && PaperSheet.Replay(sheet.Recipe()).Count==2,"Other ply changed or replay failed");
            }
            // At a shared corner the marker can be owned by the neighbouring
            // polygon. Both endpoints must resolve to the panel being cut.
            var paper=new PaperSheet();
            Check(paper.Finish(paper.Plan(new FoldStep(CreaseFamily.Rising,0,1))),"Triangle setup");
            var p=new PaperSheet.FoldVertex(0,new Vector2(5.5f,5.5f));
            var q=new PaperSheet.FoldVertex(1,new Vector2(0,5.5f));
            Check(paper.TryAxis(p,q,out var line) && line.singlePanel,"Shared-corner triangle axis");
            line.side=1;var fold=paper.Plan(line);
            if(fold==null){line.side=-1;fold=paper.Plan(line);}
            Check(fold==null,"Shared seam would tear the other ply");
            p=new PaperSheet.FoldVertex(0,new Vector2(0,5.5f));
            q=new PaperSheet.FoldVertex(1,new Vector2(5.5f,11));
            Check(paper.TryAxis(p,q,out line),"Overlapping triangle endpoints");
            line.side=1;fold=paper.Plan(line);
            Check(fold!=null && paper.Finish(fold,-35),"Free triangle corner cannot fold");
            Debug.Log("[SelectedPanel] PASS one-ply axis, free wing bend, stationary opposite ply, shared corner, replay.");
        }
        private static void VerifyVertexFolds()
        {
            var sheet=new PaperSheet();
            var points=new List<PaperSheet.FoldVertex>();sheet.CollectVertices(points);
            Check(points.Count==169,"Initial sheet vertices");
            var a=points[0];
            var b=points.Find(v=>v.point==new Vector2(sheet.Width,sheet.Height));
            Check(!sheet.TryAxis(a,a,out _) && !sheet.TryAxis(a,points[12],out _),"Degenerate or perimeter axis");
            Check(sheet.TryAxis(a,b,out var axis) && sheet.Finish(sheet.Plan(axis),45),"Manual diagonal fold");
            sheet.CollectVertices(points);
            var lifted=points.Find(v=>sheet.Faces[v.anchor].frame!=Matrix4x4.identity);
            var flat=points.Find(v=>sheet.Faces[v.anchor].frame==Matrix4x4.identity);
            Check(!sheet.TryAxis(lifted,flat,out _),"Axis crossed non-coplanar panels");
            bool folded=false;
            foreach(var next in points){
                if(!sheet.TryAxis(lifted,next,out axis))continue;
                var turn=sheet.Plan(axis);
                if(!sheet.Finish(turn,28))continue;
                folded=true;break;
            }
            Check(folded && PaperSheet.Replay(sheet.Recipe()).Count==2,"Second vertex fold or replay failed");
            Debug.Log("[VertexFolds] PASS: 169 targets, same point, perimeter, tilted panel, replay.");
        }
        private static void VerifyPointPairs(PaperSheet sheet)
        {
            var batch=new List<PaperSheet.Guide>();
            var all=new List<PaperSheet.Guide>();
            foreach(CreaseFamily family in Enum.GetValues(typeof(CreaseFamily))){
                sheet.Guides(family,batch);all.AddRange(batch);
            }
            for(int i=0;i<169;i++)
            for(int j=i+1;j<169;j++){
                var p=new Vector2(i%13,i/13)*(sheet.Width/12f);
                var q=new Vector2(j%13,j/13)*(sheet.Width/12f);
                bool border=(i%13==j%13 && (i%13==0 || i%13==12)) || (i/13==j/13 && (i/13==0 || i/13==12));
                int matches=0;
                foreach(var g in all){
                    if(Mathf.Abs(g.line.Distance(p))>.0001f || Mathf.Abs(g.line.Distance(q))>.0001f)continue;
                    if(Vector2.Dot(p-g.a,p-g.b)<.001f && Vector2.Dot(q-g.a,q-g.b)<.001f)matches++;
                }
                Check(matches==(border?0:1),"Missing or duplicated point connection "+i+" / "+j);
            }
            var skew=all.Find(g=>Mathf.Abs(g.line.Normal.x+2f*g.line.Normal.y)<.001f);
            Check(skew.face!=null && sheet.Finish(sheet.Plan(skew.line),54f),"Could not use oblique grid crease");
            var replay=PaperSheet.Replay(sheet.Recipe());
            Check(replay.Count==1 && (replay.SpatialBounds.size-sheet.SpatialBounds.size).sqrMagnitude<.00001f && sheet.Undo(),
                "Oblique fold save or undo");
            Debug.Log("[SquareGrid] PASS 169 points, 14196 pairs, oblique fold and replay");
        }
        private static void VerifyPatterns()
        {
            foreach(var size in new[]{PaperSize.Square,PaperSize.A4,PaperSize.Letter})
            foreach(PlanePattern pattern in Enum.GetValues(typeof(PlanePattern))){
                var paper=new PaperSheet(size);
                float ratio=size==PaperSize.A4 ? 297f/210f : size==PaperSize.Letter ? 11f/8.5f : 1f;
                Check(Mathf.Abs(paper.SpatialBounds.size.x/paper.SpatialBounds.size.z-ratio)<.0001f,"Sheet aspect");
                var guides=new List<PaperSheet.Guide>();
                foreach(CreaseFamily family in Enum.GetValues(typeof(CreaseFamily))){
                    paper.Guides(family,guides);
                    Check(guides.Count==((int)family<2?11:3217),"Format grid "+family+" "+guides.Count);
                }
                var print=new PaperPatterns.Print(size,pattern);
                int count=PaperPatterns.Length(pattern);
                for(int i=0;i<count;i++){
                    string id=size+" / "+pattern+" / "+(i+1);
                    Check(PaperPatterns.Next(paper,pattern,out var step,out _),"Missing hinge "+id);
                    guides.Clear();print.Append(paper,guides);
                    var turn=paper.Plan(step);
                    int printed=0;
                    foreach(var guide in guides){
                        if(guide.line.offset!=i || guide.face.frame!=turn.frame ||
                            Mathf.Abs(step.Distance(guide.a))>.001f || Mathf.Abs(step.Distance(guide.b))>.001f)continue;
                        Check(Mathf.Abs(guide.line.Distance(guide.a))<.001f && Mathf.Abs(guide.line.Distance(guide.b))<.001f,
                            "Click axis differs from ink "+id);
                        printed++;
                    }
                    Check(printed>0,"Missing real printed hinge "+id);
                    Check(turn!=null,"Disconnected hinge "+id);
                    Check(paper.Finish(turn,step.degrees),"Blocked angle "+id+" limit "+turn.Constrain(step.degrees));
                    float area=0;foreach(var f in paper.Faces)area+=PaperSheet.Area(f.points);
                    Check(Mathf.Abs(area-paper.Width*paper.Height)<.003f,"Paper area "+id);
                }
                var bounds=paper.SpatialBounds;
                Check(bounds.max.y>1 && bounds.min.y< -1,"Wings did not open "+pattern);
                if(pattern==PlanePattern.Bulldog)Check(bounds.max.x<paper.Width-1,"Blunt nose missing");
                var data=new ResearchSave{paperFolds=paper.Recipe()};
                var replay=PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(JsonUtility.ToJson(data)).paperFolds);
                Check(replay.Count==count && replay.Format==size &&
                    (replay.SpatialBounds.size-bounds.size).sqrMagnitude<.00001f,"JSON replay "+size+" / "+pattern);
                Debug.Log("[PaperPatterns] PASS "+size+" / "+pattern+" / "+count+" folds");
            }
        }
        private static void VerifyAircraftGrip()
        {
            var dart=new PaperSheet();
            while(PaperPatterns.Next(dart,PlanePattern.Basic,out var step,out _))
                Check(dart.Finish(dart.Plan(step)),"Grip fixture fold");
            foreach(int variant in new[]{0,1,2}){
                var sheet=new PaperSheet();
                foreach(var saved in dart.Recipe()){
                    var step=saved;var n=step.Normal;float k=step.Position;
                    if(variant==1){step.creaseNormal=new Vector2(n.x,-n.y);step.creaseOffset=k-sheet.Height*n.y;}
                    if(variant==2){step.creaseNormal=new Vector2(-n.y,n.x);step.creaseOffset=k-sheet.Width*n.y;}
                    Check(sheet.Finish(sheet.Plan(step)),"Transformed dart");
                }
                var hold=AircraftHold.Measure(sheet);
                Vector3 expectedUp=variant==0?Vector3.back:variant==1?Vector3.forward:Vector3.right;
                Vector3 expectedForward=variant==2?Vector3.forward:Vector3.right;
                Check(Vector3.Dot(hold.Forward,expectedForward)>.97f && Vector3.Dot(hold.Up,expectedUp)>.94f,
                    "Dart direction "+variant+" up="+hold.Up+" forward="+hold.Forward);
                bool onKeel=false;
                foreach(var f in sheet.Faces){
                    var local=f.frame.inverse.MultiplyPoint3x4(hold.Grip);
                    if(f.frame==Matrix4x4.identity && Mathf.Abs(local.y-(f.layer+.5f)*PaperSheet.Thickness)<.001f &&
                        f.Contains(new Vector2(local.x,local.z)))onKeel=true;
                }
                Check(onKeel,"Held the outer wing "+variant);
                var replay=AircraftHold.Measure(PaperSheet.Replay(sheet.Recipe()));
                Check((hold.Grip-replay.Grip).sqrMagnitude<.00001f,"Grip changed after replay");
            }
            var flat=new PaperSheet();
            var flatHold=AircraftHold.Measure(flat);
            Check((flatHold.Grip-new Vector3(5.5f,.5f*PaperSheet.Thickness,5.5f)).sqrMagnitude<.0001f,"Flat grip");
            Check(Mathf.Abs(flatHold.Nose.x-flat.Width)<.001f && Mathf.Abs(flatHold.Nose.z-flat.Height*.5f)<.001f,"Blunt leading edge");
            Check(flat.Finish(flat.Plan(new FoldStep(CreaseFamily.Rising,0,1)),43),"V-fold fixture");
            var vHold=AircraftHold.Measure(flat);
            Check((vHold.Grip-new Vector3(5.5f,.5f*PaperSheet.Thickness,5.5f)).magnitude<.12f,"V-fold missed central ridge "+vHold.Grip);
            var uneven=new PaperSheet();
            Check(uneven.Finish(uneven.Plan(new FoldStep(CreaseFamily.Vertical,3,-1)),60),"Unequal wings");
            var offCentre=AircraftHold.Measure(uneven);
            Check(Mathf.Abs(offCentre.Grip.x-3)<.04f && Mathf.Abs(offCentre.Grip.y)<.04f,"Missed off-centre spine");
            Debug.Log("[AircraftGrip] PASS keel and upper side on normal, inverted and sideways darts, V-fold, flat sheet, replay.");
        }
        private sealed class ShapeStore : IResearchStore
        {
            private ResearchSave saved=ResearchSave.NewGame();
            public bool fail;
            public ResearchSave Load()=>saved.Copy();
            public void Save(ResearchSave data){
                if(fail)throw new System.IO.IOException("Simulated save failure");
                saved=data.Copy();
            }
        }
        private static void VerifyFoldRatings()
        {
            var wide=new PaperSheet();Check(wide.Finish(wide.Plan(new FoldStep(CreaseFamily.Horizontal,5,1)),8),"Wide fold");
            var dart=new PaperSheet();
            while(PaperPatterns.Next(dart,PlanePattern.Dart,out var fold,out _))
                Check(dart.Finish(dart.Plan(fold)),"Narrow dart fold");
            var bundle=new PaperSheet();var rotated=new PaperSheet();
            for(int i=0;i<3;i++){
                float half=bundle.Height/Mathf.Pow(2,i+1);
                var h=new FoldStep(CreaseFamily.Horizontal,0,1){creaseNormal=Vector2.up,creaseOffset=half};
                var v=new FoldStep(CreaseFamily.Vertical,0,1){creaseNormal=Vector2.right,creaseOffset=half};
                Check(bundle.Finish(bundle.Plan(h)) && rotated.Finish(rotated.Plan(v)),"Fold stack");
            }
            var w=PaperFlightProfile.Measure(wide);var n=PaperFlightProfile.Measure(dart);
            var b=PaperFlightProfile.Measure(bundle);var r=PaperFlightProfile.Measure(rotated);
            Check(Mathf.Abs(n.launch/w.launch-1.4f)<.001f && w.climb>n.climb && w.descent<n.descent,"Launch/climb/descent tradeoff");
            Check(Mathf.Abs(b.impact/w.impact-1.4f)<.001f && b.momentum>w.momentum,"Stack reinforcement");
            Check(Mathf.Abs(b.launch-r.launch)+Mathf.Abs(b.momentum-r.momentum)+Mathf.Abs(b.climb-r.climb)+
                Mathf.Abs(b.descent-r.descent)+Mathf.Abs(b.impact-r.impact)<.002f,"Rotated flat stack ratings");
            foreach(var profile in new[]{w,n,b,r,PaperFlightProfile.Standard})
                foreach(float value in new[]{profile.launch,profile.momentum,profile.climb,profile.descent,profile.impact})
                    Check(value>=5f/6-.0001f && value<=7f/6+.0001f,"Out-of-range rating");

            var store=new ShapeStore();var seed=store.Load();seed.ownedNodeIds=new[]{"Launch_1","Drag_1","Climb_1","Glide_1","Impact_1"};store.Save(seed);
            var research=new FlightResearch(store);var baseline=new FlightTuning();var reference=research.Apply(baseline);
            var session=new FlightSession(baseline,research);
            Check(session.TryEquipPaper(bundle),"Equip stack");
            var applied=research.Apply(baseline);
            Check(Mathf.Abs(session.Model.Speed-reference.launchSpeed*b.launch)<.001f &&
                Mathf.Abs(applied.drag*b.momentum-reference.drag)<.00001f &&
                Mathf.Abs(applied.sink-reference.sink*b.descent)<.00001f &&
                Mathf.Abs(Mathf.Sin(applied.climbPitch*Mathf.Deg2Rad)-Mathf.Sin(reference.climbPitch*Mathf.Deg2Rad)*b.climb)<.00001f &&
                Mathf.Abs(Mathf.Sin(applied.divePitch*Mathf.Deg2Rad)-Mathf.Sin(reference.divePitch*Mathf.Deg2Rad)*b.descent)<.00001f,
                "Shape and research composition");
            var model=session.Model;store.fail=true;
            Check(!session.TryEquipPaper(wide) && model==session.Model && research.EquippedProfile.impact==b.impact,"Failed equip changed stats");
            store.fail=false;
            Check(Mathf.Abs(new FlightResearch(store).Apply(baseline).launchSpeed-applied.launchSpeed)<.0001f,"Shape lost on reload");
            Check(session.TryLaunch(0,4) && !session.TryEquipPaper(wide),"In-flight equip");
            float speed=session.Model.Speed;session.Model.StrikeObstacle(25);
            Check(Mathf.Abs(session.Model.Durability-(reference.effects.durability-25/b.impact))<.001f &&
                Mathf.Abs(session.Model.Speed-speed*(1-reference.effects.collisionSpeedLoss/b.impact))<.001f,"Impact response");
            float energy=session.Model.Energy;session.Model.CollectBoost();Check(session.Model.Energy==energy,"Pickup refilled energy");
            session.Tick(false,false,true);
            Check(Mathf.Abs(session.Model.Speed-applied.launchSpeed)<.001f && session.TryEquipPaper(new PaperSheet()) &&
                Mathf.Abs(session.Model.Speed-reference.launchSpeed)<.001f,"Retry or default reset");
            Debug.Log("[FoldRatings] PASS actual shapes, 1.4 ratio, rotated stack, research, impact, failed save, reload, retry, default.");
        }
        private static void VerifyDart()
        {
            var sheet=new PaperSheet();
            Fold(sheet,CreaseFamily.Falling,16,1); // nose corners meet at (11,5).
            Fold(sheet,CreaseFamily.Rising,6,1);
            Fold(sheet,CreaseFamily.Horizontal,5,1);
            var pick=new Vector2(3,2);
            int top=-1,bottom=-1;
            for(int i=0;i<sheet.Faces.Count;i++)if(sheet.Faces[i].Contains(pick)){
                if(top<0 || sheet.Faces[i].layer>sheet.Faces[top].layer)top=i;
                if(bottom<0 || sheet.Faces[i].layer<sheet.Faces[bottom].layer)bottom=i;
            }
            Check(top>=0 && bottom>=0 && top!=bottom,"Missing stacked wings");
            var step=new FoldStep(CreaseFamily.Horizontal,4,-1){flap=top+1,anchor=top};
            var left=sheet.Plan(step);
            Check(left!=null && left.Constrain(-90)==0,"Buried sheet passed through its neighbour");
            Check(sheet.Finish(left,76f),"First wing could not open");
            bottom=-1;
            for(int i=0;i<sheet.Faces.Count;i++)if(sheet.Faces[i].frame==Matrix4x4.identity && sheet.Faces[i].Contains(pick)){
                if(bottom<0 || sheet.Faces[i].layer<sheet.Faces[bottom].layer)bottom=i;
            }
            Check(bottom>=0,"Opposite wing unavailable");
            step.flap=bottom+1;step.anchor=bottom;
            Check(sheet.Finish(sheet.Plan(step),-82f),"Opposite wing could not open");
            var bounds=sheet.SpatialBounds;
            Check(bounds.max.y>2.5f && bounds.min.y< -2.5f,"Wing height was flattened");
            var replay=PaperSheet.Replay(sheet.Recipe());
            Check(replay.Count==5 && (replay.SpatialBounds.size-bounds.size).sqrMagnitude<.00001f,"Angles lost on replay");
            var old=new FoldStep(CreaseFamily.Vertical,5,-1){degrees=0};
            Check(PaperSheet.Replay(new[]{old}).PeakPlies==2,"Legacy flat-fold save failed");
            Debug.Log("[DartFold] PASS: two corners, body fold, opposite signed single-wing folds, collision, legacy and replay.");
        }
        private static void Fold(PaperSheet sheet,CreaseFamily family,int offset,int side)
        {
            var turn=sheet.Plan(new FoldStep(family,offset,side));
            Check(turn!=null,"Legal crease was unavailable");sheet.Finish(turn);
            float area=0;foreach(var face in sheet.Faces)area+=PaperSheet.Area(face.points);
            Check(Mathf.Abs(area-PaperSheet.Span*PaperSheet.Span)<.0001f,"Lost paper area");
        }
        private static int LayersAt(PaperSheet sheet,Vector2 sample)
        {
            var ranks=new HashSet<int>();
            foreach(var face in sheet.Faces)if(face.Contains(sample))
                Check(ranks.Add(face.layer),"Two overlapping faces share one height");
            return ranks.Count;
        }
        private static void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    }
}
