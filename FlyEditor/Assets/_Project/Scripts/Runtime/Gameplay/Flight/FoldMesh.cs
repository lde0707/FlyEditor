using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    internal sealed class FoldMesh
    {
        private readonly List<Vector3> vertices=new List<Vector3>();
        private readonly List<int> indices=new List<int>();
        private readonly List<Color> colors=new List<Color>();
        private static readonly Color Front=new Color(.99f,.975f,.92f), Back=new Color(.79f,.87f,.91f),
            Edge=new Color(.30f,.38f,.40f), Ink=new Color(.15f,.32f,.34f);
        private readonly List<Vector2> centreBand=new List<Vector2>(), dashClip=new List<Vector2>(), dashPolygon=new List<Vector2>();
        private Matrix4x4 faceFrame, rotation;
        private float sine;
        private bool workbench;
        private IReadOnlyList<PaperSheet.Face> outlinePeers;
        private readonly List<Vector2> coveredEdges=new List<Vector2>();
        private static readonly Color SoftEdge=new Color(.42f,.47f,.45f);

        public void Build(Mesh mesh,PaperSheet sheet,PaperSheet.Turn turn,float angle,
            List<PaperSheet.FoldVertex> points=null,int first=-1,int second=-1,int hoverPoint=-1,bool hoverSide=false,
            List<PaperSheet.Guide> hinge=null,PaperCornerPull pull=null,PaperMirrorFold pair=null,MirrorAxis mirror=MirrorAxis.Off,bool upper=true,PaperInsideReverse reverse=null,int third=-1,int fourth=-1,int selectedPanel=-1,Vector3 viewDirection=default,List<int> selectablePanels=null,IReadOnlyList<bool> visiblePoints=null,IReadOnlyList<float> pointOpacities=null,PaperSurfaceChoice surfaceChoice=null,Vector3? viewPosition=null)
        {
            workbench=points!=null;outlinePeers=sheet.Faces;
            bool panelOnly=turn==null && second<0;
            vertices.Clear();indices.Clear();colors.Clear();
            if(pair!=null){
                outlinePeers=pair.Preview;
                foreach(var f in pair.Preview)Surface(f,false,false);
                if(hinge!=null)foreach(var mark in hinge){
                    var a=mark.face.Original(mark.a);var b=mark.face.Original(mark.b);
                    MaterialStripe(pair.Preview,a,b);
                    MaterialStripe(pair.Preview,pair.PartnerPoint(a),pair.PartnerPoint(b));
                }
            }else if(reverse!=null){
                outlinePeers=reverse.Preview;
                for(int i=0;i<reverse.Preview.Count;i++)Surface(reverse.Preview[i],false,workbench && reverse.Highlights(i));
                foreach(var mark in reverse.Creases)
                    MaterialStripe(reverse.Preview,mark.face.Original(mark.a),mark.face.Original(mark.b));
            }else if(pull!=null){
                outlinePeers=pull.Preview;
                var edgeTurn=pull.EdgeTurn;
                for(int i=0;i<pull.Preview.Count;i++)Surface(pull.Preview[i],false,workbench && edgeTurn!=null && i>=edgeTurn.fixedFaces.Count,motionRole:workbench?pull.MotionRole(pull.Preview[i]):0);
                if(edgeTurn!=null || pull.UsesCoordinatedSurface){
                    // Draw the computed hinge, not the four constraint edges.
                    if(hinge!=null)foreach(var mark in hinge)MaterialStripe(pull.Preview,mark.face.Original(mark.a),mark.face.Original(mark.b));
                }else{
                    var basis=sheet.Faces[pull.Step.anchor];
                    var tip=basis.Original(pull.Step.pullTip);var stop=basis.Original(pull.Step.pullStop);
                    MaterialStripe(pull.Preview,tip,stop);
                    if(pull.Step.customFlanks){
                        MaterialStripe(pull.Preview,stop,basis.Original(pull.Step.pullLeft));
                        MaterialStripe(pull.Preview,stop,basis.Original(pull.Step.pullRight));
                    }
                }
            }else if(turn==null){foreach(var f in sheet.Faces){
                var choice=surfaceChoice?.For(f);
                if(choice!=null){
                    if(choice.remainder!=null)Surface(choice.remainder,false,false);
                    if(choice.chosen!=null)Surface(choice.chosen,false,false,default,true);
                }else Surface(f,false,panelOnly && Selected(f,sheet,selectedPanel));
            }}
            else
            {
                rotation=turn.Rotation(angle);
                sine=Mathf.Abs(Mathf.Sin(angle*Mathf.Deg2Rad));
                outlinePeers=turn.fixedFaces;
                foreach(var f in turn.fixedFaces)Surface(f,false,false);
                outlinePeers=turn.movingFaces;
                foreach(var f in turn.movingFaces)Surface(f,true,workbench&&turn.MotionRole(f)==1,motionRole:workbench?turn.MotionRole(f):0);
            }
            if(turn==null && reverse==null && pull==null && pair==null && points!=null && first>=0 && second>=0 && (hinge==null || hinge.Count==0 || selectedPanel>=0)){
                var va=points[first];var vb=points[second];var fa=sheet.Faces[va.anchor];var fb=sheet.Faces[vb.anchor];
                var a=fa.World(va.point,(fa.layer+.5f)*PaperSheet.Thickness)-viewDirection*.025f;
                var b=fb.World(vb.point,(fb.layer+.5f)*PaperSheet.Thickness)-viewDirection*.025f;
                var side=Vector3.Cross(viewDirection,b-a).normalized*.035f;
                Quad(a-side,b-side,b+side,a+side,new Color(.12f,.46f,.38f));
            }
            if(reverse==null && pull==null && pair==null && hinge!=null){
                foreach(var segment in hinge){
                    faceFrame=segment.face.frame;
                    var side=new Vector2(segment.a.y-segment.b.y,segment.b.x-segment.a.x).normalized*.038f;
                    HingeStripe(segment,side,(segment.layer+1)*PaperSheet.Thickness+.002f);
                    HingeStripe(segment,side,segment.layer*PaperSheet.Thickness-.002f);
                }
            }
            if((reverse==null || (reverse.UsesPacket && hoverSide && angle==0f)) && pull==null && pair==null && points!=null && (turn==null || angle==0f)){
                for(int i=0;i<points.Count;i++){
                    if(visiblePoints!=null && !visiblePoints[i])continue;
                    var vertex=points[i];var face=sheet.Faces[vertex.anchor];
                    if(selectablePanels!=null){
                        bool allowed=false;foreach(int panel in selectablePanels)if(PaperSheet.SamePanel(face,sheet.Faces[panel])){allowed=true;break;}
                        if(!allowed)continue;
                    }
                    faceFrame=face.frame;
                    sheet.PlyRange(face,vertex.point,out int bottom,out int top);
                    if(viewDirection==Vector3.zero){if(face.layer!=(upper?top:bottom))continue;}
                    else if(selectedPanel>=0 && i!=first && i!=second && !PaperSheet.SamePanel(face,sheet.Faces[selectedPanel]))continue;
                    bool chosen=i==first || i==second || i==third || i==fourth;
                    bool front=viewDirection==Vector3.zero?upper:Vector3.Dot(viewPosition.HasValue?face.World(vertex.point,0)-viewPosition.Value:viewDirection,face.frame.MultiplyVector(Vector3.up))<0;
                    float markerHeight=front?(face.layer+1)*PaperSheet.Thickness+.002f:face.layer*PaperSheet.Thickness-.002f;
                    float opacity=pointOpacities==null?1:pointOpacities[i];
                    bool highlighted=turn!=null?MovingPoint(turn,face,vertex.point):panelOnly && Selected(face,sheet,selectedPanel);
                    float tilt=Mathf.Abs(face.frame.MultiplyVector(Vector3.up).y);
                    Color paper=highlighted?new Color(.62f,.83f,.73f):((front!=face.back)?Front:Back)*(.78f+.22f*tilt);
                    Marker(vertex.point,markerHeight,chosen,i==hoverPoint,opacity,paper);
                }
            }
            if(panelOnly && selectedPanel>=0 && angle==0 && reverse==null && pull==null && pair==null){
                var face=sheet.Faces[selectedPanel];
                faceFrame=face.frame;
                for(int i=0;i<face.points.Length;i++){
                    var a=face.points[i];var b=face.points[(i+1)%face.points.Length];
                    HingeStripe(new PaperSheet.Guide{a=a,b=b},new Vector2(a.y-b.y,b.x-a.x).normalized*.018f,(face.layer+.5f)*PaperSheet.Thickness);
                }
            }
            if(mirror!=MirrorAxis.Off){
                if(pair!=null)CentreLines(sheet,pair.Preview,mirror,false);
                else if(reverse!=null)CentreLines(sheet,reverse.Preview,mirror,false);
                else if(pull!=null)CentreLines(sheet,pull.Preview,mirror,false);
                else if(turn!=null){
                    CentreLines(sheet,turn.fixedFaces,mirror,false);CentreLines(sheet,turn.movingFaces,mirror,true);
                }else CentreLines(sheet,sheet.Faces,mirror,false);
            }
            mesh.indexFormat=vertices.Count>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);
        }
        private void MaterialStripe(IReadOnlyList<PaperSheet.Face> panels,Vector3 start,Vector3 end)
        {
            foreach(var panel in panels){
                var inv=panel.material.inverse;
                var u=inv.MultiplyPoint3x4(start);var v=inv.MultiplyPoint3x4(end);
                var a=new Vector2(u.x,u.z);var b=new Vector2(v.x,v.z);
                if(!PaperSheet.Trim(panel,ref a,ref b))continue;
                var segment=new PaperSheet.Guide{a=a,b=b};
                var side=new Vector2(a.y-b.y,b.x-a.x).normalized*.038f;faceFrame=panel.frame;
                HingeStripe(segment,side,(panel.layer+1)*PaperSheet.Thickness+.002f);
                HingeStripe(segment,side,panel.layer*PaperSheet.Thickness-.002f);
            }
        }
        private void CentreLines(PaperSheet sheet,IReadOnlyList<PaperSheet.Face> faces,MirrorAxis axis,bool moving)
        {
            const float halfWidth=.036f, desiredPeriod=.37f, inkFraction=.23f/.37f;
            bool horizontal=axis==MirrorAxis.Horizontal;
            float length=horizontal?sheet.Width:sheet.Height;
            float centre=.5f*(horizontal?sheet.Height:sheet.Width);
            int perCell=Mathf.Max(1,Mathf.RoundToInt(length/(PaperSheet.GridIntervals*desiredPeriod)));
            float period=length/(perCell*PaperSheet.GridIntervals), halfInk=.5f*period*inkFraction;
            var color=new Color(.65f,.40f,.21f);
            foreach(var f in faces){
                // One ink pattern on the original sheet. Each panel owns only the
                // portion inside its material polygon, including on a shared crease.
                centreBand.Clear();
                foreach(var p in f.points){
                    var m=f.Original(p);
                    centreBand.Add(horizontal?new Vector2(m.x,m.z):new Vector2(m.z,m.x));
                }
                ClipInk(centreBand,dashClip,1,centre-halfWidth,1);
                ClipInk(dashClip,centreBand,1,centre+halfWidth,-1);
                if(centreBand.Count<3)continue;
                float min=centreBand[0].x,max=min;
                foreach(var p in centreBand){min=Mathf.Min(min,p.x);max=Mathf.Max(max,p.x);}
                faceFrame=f.frame;
                var inverse=f.material.inverse;
                int last=Mathf.FloorToInt((max+halfInk)/period);
                for(int k=Mathf.CeilToInt((min-halfInk)/period);k<=last;k++){
                    ClipInk(centreBand,dashClip,0,k*period-halfInk,1);
                    ClipInk(dashClip,dashPolygon,0,k*period+halfInk,-1);
                    if(dashPolygon.Count<3)continue;
                    for(int i=0;i<dashPolygon.Count;i++){
                        var p=dashPolygon[i];
                        var m=horizontal?new Vector3(p.x,0,p.y):new Vector3(p.y,0,p.x);
                        var v=inverse.MultiplyPoint3x4(m);
                        dashPolygon[i]=new Vector2(v.x,v.z);
                    }
                    float top=(f.layer+1)*PaperSheet.Thickness+.003f,bottom=f.layer*PaperSheet.Thickness-.003f;
                    var a=dashPolygon[0];
                    for(int i=2;i<dashPolygon.Count;i++){
                        var b=dashPolygon[i-1];var c=dashPolygon[i];
                        Tri(Vertex(a,top,moving),Vertex(b,top,moving),Vertex(c,top,moving),color);
                        Tri(Vertex(a,bottom,moving),Vertex(c,bottom,moving),Vertex(b,bottom,moving),color);
                    }
                }
            }
        }
        private static void ClipInk(List<Vector2> src,List<Vector2> dst,int coordinate,float edge,int sign)
        {
            dst.Clear();
            if(src.Count==0)return;
            var a=src[src.Count-1];float da=(a[coordinate]-edge)*sign;
            foreach(var b in src){
                float db=(b[coordinate]-edge)*sign;
                if((da<0 && db>0) || (da>0 && db<0))dst.Add(Vector2.Lerp(a,b,da/(da-db)));
                if(db>=0)dst.Add(b);
                a=b;da=db;
            }
        }
        private Vector3 Vertex(Vector2 p,float height,bool moving)
        {
            Vector3 world=faceFrame.MultiplyPoint3x4(new Vector3(p.x,height,p.y));
            return moving?rotation.MultiplyPoint3x4(world):world;
        }
        private static bool Selected(PaperSheet.Face face,PaperSheet sheet,int panel)=>
            panel>=0 && panel<sheet.Faces.Count && PaperSheet.SamePanel(face,sheet.Faces[panel]);
        private static bool MovingPoint(PaperSheet.Turn turn,PaperSheet.Face face,Vector2 point)
        {
            foreach(var part in turn.movingFaces)if(PaperSheet.SamePanel(face,part) && part.Contains(point))return true;
            return false;
        }
        private void Surface(PaperSheet.Face f,bool moving,bool highlight,Vector3 displayOffset=default,bool pending=false,int motionRole=0)
        {
            faceFrame=Matrix4x4.Translate(displayOffset)*f.frame;
            float bottom=f.layer*PaperSheet.Thickness,top=bottom+PaperSheet.Thickness;
            float tilt=Mathf.Abs(faceFrame.MultiplyVector(Vector3.up).y);
            Color a=(f.back?Back:Front)*(.78f+.22f*tilt),b=(f.back?Front:Back)*(.78f+.22f*tilt);
            if(moving){float shade=1f-.18f*sine;a*=shade;b*=shade;}
            if(highlight){a=new Color(.62f,.83f,.73f);b=a;}
            if(motionRole==1){a=new Color(.62f,.83f,.73f);b=a;}
            if(motionRole==2){a=new Color(.72f,.82f,.89f);b=a;}
            if(pending){a=new Color(.90f,.79f,.58f);b=a;}
            var p=f.points;
            for(int i=1;i<p.Length-1;i++)
            {
                Tri(Vertex(p[0],top,moving),Vertex(p[i],top,moving),Vertex(p[i+1],top,moving),a);
                Tri(Vertex(p[0],bottom,moving),Vertex(p[i+1],bottom,moving),Vertex(p[i],bottom,moving),b);
            }
            for(int i=0;i<p.Length;i++)
            {
                Vector2 u=p[i],v=p[(i+1)%p.Length];
                Quad(Vertex(u,top,moving),Vertex(v,top,moving),Vertex(v,bottom,moving),Vertex(u,bottom,moving),workbench?Color.Lerp(a,b,.5f):Edge);
                if(workbench){
                    PaperOutline(f,u,v,top,bottom,moving,a,b);continue;
                }
                Vector2 shift=new Vector2(-(v-u).y,(v-u).x).normalized*.027f*(f.back?-1f:1f);
                Quad(Vertex(u,top+.0006f,moving),Vertex(v,top+.0006f,moving),
                    Vertex(v+shift,top+.0006f,moving),Vertex(u+shift,top+.0006f,moving),Edge);
            }
            if(motionRole==2)SupportHatch(f,top,bottom,moving);
        }
        private void SupportHatch(PaperSheet.Face face,float top,float bottom,bool moving)
        {
            float lo=float.PositiveInfinity,hi=float.NegativeInfinity,start=float.PositiveInfinity,end=float.NegativeInfinity;
            foreach(var p in face.points){lo=Mathf.Min(lo,p.x-p.y);hi=Mathf.Max(hi,p.x-p.y);start=Mathf.Min(start,p.x+p.y);end=Mathf.Max(end,p.x+p.y);}
            var side=new Vector2(.012f,-.012f);var color=new Color(.48f,.64f,.74f);
            for(float offset=Mathf.Ceil(lo/.65f)*.65f;offset<hi;offset+=.65f){
                var a=new Vector2((offset+start-1)*.5f,(start-1-offset)*.5f);var b=new Vector2((offset+end+1)*.5f,(end+1-offset)*.5f);if(!PaperSheet.Trim(face,ref a,ref b))continue;
                Quad(Vertex(a-side,top+.002f,moving),Vertex(b-side,top+.002f,moving),Vertex(b+side,top+.002f,moving),Vertex(a+side,top+.002f,moving),color);
                Quad(Vertex(a-side,bottom-.002f,moving),Vertex(b-side,bottom-.002f,moving),Vertex(b+side,bottom-.002f,moving),Vertex(a+side,bottom-.002f,moving),color);
            }
        }
        // Subtle opaque strokes: tint toward the paper rather than using
        // transparency that would reveal hidden plies. No triangulation seams.
        private void PaperOutline(PaperSheet.Face face,Vector2 a,Vector2 b,float top,float bottom,bool moving,Color front,Color back)
        {
            var edge=b-a;float length=edge.magnitude;if(length<.0001f)return;
            var axis=edge/length;coveredEdges.Clear();
            foreach(var other in outlinePeers){
                if(ReferenceEquals(face,other)||!PaperSheet.SamePanel(face,other))continue;
                for(int i=0;i<other.points.Length;i++){
                    var p=other.points[i]-a;var q=other.points[(i+1)%other.points.Length]-a;
                    if(Mathf.Abs(axis.x*p.y-axis.y*p.x)>.0001f||Mathf.Abs(axis.x*q.y-axis.y*q.x)>.0001f)continue;
                    float x=Vector2.Dot(p,axis),y=Vector2.Dot(q,axis);
                    float lo=Mathf.Max(0,Mathf.Min(x,y)),hi=Mathf.Min(length,Mathf.Max(x,y));
                    if(hi-lo<.0001f)continue;
                    int k=coveredEdges.Count;while(k>0&&coveredEdges[k-1].x>lo)k--;
                    coveredEdges.Insert(k,new Vector2(lo,hi));
                }
            }
            var inset=new Vector2(-axis.y,axis.x)*.018f*(face.back?-1:1);
            var upper=Color.Lerp(front,SoftEdge,.18f);var lower=Color.Lerp(back,SoftEdge,.18f);
            float cursor=0;
            for(int i=0;i<=coveredEdges.Count;i++){
                float end=i==coveredEdges.Count?length:coveredEdges[i].x;
                if(end>cursor+.0001f){
                    var u=a+axis*cursor;var v=a+axis*end;
                    Quad(Vertex(u,top+.0005f,moving),Vertex(v,top+.0005f,moving),Vertex(v+inset,top+.0005f,moving),Vertex(u+inset,top+.0005f,moving),upper);
                    Quad(Vertex(u,bottom-.0005f,moving),Vertex(u+inset,bottom-.0005f,moving),Vertex(v+inset,bottom-.0005f,moving),Vertex(v,bottom-.0005f,moving),lower);
                }
                if(i<coveredEdges.Count)cursor=Mathf.Max(cursor,coveredEdges[i].y);
            }
        }
        private void HingeStripe(PaperSheet.Guide g,Vector2 side,float h)
        {
            Quad(Vertex(g.a-side,h,false),Vertex(g.b-side,h,false),Vertex(g.b+side,h,false),
                 Vertex(g.a+side,h,false),new Color(.12f,.46f,.38f));
        }
        private void Marker(Vector2 p,float h,bool chosen,bool hovered,float opacity,Color paper)
        {
            // The paper uses an opaque vertex-colour shader. Blend with the actual
            // underlying paper tint so fading cannot expose hidden layers.
            if(!chosen && !hovered){Dot(p,h,.075f,Color.Lerp(paper,Ink,opacity));return;}
            // Angular paper-style marker: dark rim, light separation, solid centre.
            // Disjoint rings avoid overlapping coplanar triangles on either paper side.
            float outer=chosen?(hovered?.26f:.23f):.17f;
            float rim=outer-.035f, core=chosen?.14f:.105f;
            Color ink=chosen?new Color(.08f,.30f,.26f):new Color(.51f,.31f,.12f);
            Color fill=chosen?new Color(.12f,.43f,.34f):new Color(.76f,.54f,.26f);
            DiamondRing(p,h,outer,rim,Color.Lerp(paper,ink,opacity));
            DiamondRing(p,h,rim,core,Color.Lerp(paper,new Color(1f,.99f,.94f),opacity));
            Dot(p,h,core,Color.Lerp(paper,fill,opacity));
        }
        private void DiamondRing(Vector2 p,float h,float outer,float inner,Color color)
        {
            for(int i=0;i<4;i++){
                Vector2 a=DiamondCorner(i),b=DiamondCorner((i+1)%4);
                Quad(Vertex(p+a*outer,h,false),Vertex(p+b*outer,h,false),
                     Vertex(p+b*inner,h,false),Vertex(p+a*inner,h,false),color);
            }
        }
        private static Vector2 DiamondCorner(int i)
        {
            switch(i){case 0:return Vector2.left;case 1:return Vector2.up;
                case 2:return Vector2.right;default:return Vector2.down;}
        }
        private void Dot(Vector2 p,float h,float r,Color color)
        {
            Quad(Vertex(p-Vector2.right*r,h,false),Vertex(p+Vector2.up*r,h,false),
                 Vertex(p+Vector2.right*r,h,false),Vertex(p-Vector2.up*r,h,false),color);
        }
        private void Tri(Vector3 a,Vector3 b,Vector3 c,Color tint)
        {
            int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
            colors.Add(tint);colors.Add(tint);colors.Add(tint);indices.Add(i);indices.Add(i+1);indices.Add(i+2);
        }
        private void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color tint)
        { Tri(a,b,c,tint);Tri(a,c,d,tint); }

        public Vector3 BuildAircraft(Mesh mesh,PaperSheet sheet)
        {
            Build(mesh,sheet,null,0);
            var hold=AircraftHold.Measure(sheet);
            var toFlight=Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(hold.Forward,hold.Up),hold.Up));
            var bounds=new Bounds(toFlight*(vertices[0]-hold.Grip),Vector3.zero);
            for(int i=0;i<vertices.Count;i++){
                vertices[i]=toFlight*(vertices[i]-hold.Grip);
                bounds.Encapsulate(vertices[i]);
            }
            float scale=Mathf.Min(2.8f/Mathf.Max(.01f,bounds.size.x),2.4f/Mathf.Max(.01f,bounds.size.z));
            scale=Mathf.Min(scale,1.5f/Mathf.Max(.01f,bounds.size.y));
            for(int i=0;i<vertices.Count;i++)vertices[i]*=scale;
            mesh.SetVertices(vertices);mesh.RecalculateBounds();
            return toFlight*(hold.Nose-hold.Grip)*scale;
        }
    }
}
