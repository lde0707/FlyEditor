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
        public void Build(Mesh mesh,PaperSheet sheet,PaperSheet.Turn turn,float angle,
            List<PaperSheet.FoldVertex> points=null,int first=-1,int second=-1,int hoverPoint=-1,bool hoverSide=false,
            List<PaperSheet.Guide> hinge=null,PaperCornerPull pull=null,PaperMirrorFold pair=null,MirrorAxis mirror=MirrorAxis.Off,bool upper=true,PaperInsideReverse reverse=null,int third=-1,int fourth=-1)
        {
            workbench=points!=null;
            vertices.Clear();indices.Clear();colors.Clear();
            if(pair!=null){
                foreach(var f in pair.Preview)Surface(f,false,false);
                if(hinge!=null)foreach(var mark in hinge){
                    var a=mark.face.Original(mark.a);var b=mark.face.Original(mark.b);
                    MaterialStripe(pair.Preview,a,b);
                    MaterialStripe(pair.Preview,pair.PartnerPoint(a),pair.PartnerPoint(b));
                }
            }else if(reverse!=null){
                foreach(var f in reverse.Preview)Surface(f,false,false);
                foreach(var mark in reverse.Creases)
                    MaterialStripe(reverse.Preview,mark.face.Original(mark.a),mark.face.Original(mark.b));
            }else if(pull!=null){
                foreach(var f in pull.Preview)Surface(f,false,false);
                var basis=sheet.Faces[pull.Step.anchor];
                var tip=basis.Original(pull.Step.pullTip);var stop=basis.Original(pull.Step.pullStop);
                MaterialStripe(pull.Preview,tip,stop);
                if(pull.Step.customFlanks){
                    MaterialStripe(pull.Preview,stop,basis.Original(pull.Step.pullLeft));
                    MaterialStripe(pull.Preview,stop,basis.Original(pull.Step.pullRight));
                }
            }else if(turn==null){foreach(var f in sheet.Faces)Surface(f,false,false);}
            else
            {
                rotation=turn.Rotation(angle);
                sine=Mathf.Abs(Mathf.Sin(angle*Mathf.Deg2Rad));
                foreach(var f in turn.fixedFaces)Surface(f,false,false);
                foreach(var f in turn.movingFaces)Surface(f,true,hoverSide);
            }
            if(reverse==null && pull==null && pair==null && hinge!=null){
                foreach(var segment in hinge){
                    faceFrame=segment.face.frame;
                    var side=new Vector2(segment.a.y-segment.b.y,segment.b.x-segment.a.x).normalized*.038f;
                    HingeStripe(segment,side,(segment.layer+1)*PaperSheet.Thickness+.002f);
                    HingeStripe(segment,side,segment.layer*PaperSheet.Thickness-.002f);
                }
            }
            if(reverse==null && pull==null && pair==null && points!=null && (turn==null || angle==0f)){
                for(int i=0;i<points.Count;i++){
                    var vertex=points[i];var face=sheet.Faces[vertex.anchor];
                    faceFrame=face.frame;
                    sheet.PlyRange(face,vertex.point,out int bottom,out int top);
                    if(face.layer!=(upper?top:bottom))continue;
                    bool chosen=i==first || i==second || i==third || i==fourth;
                    float radius=chosen?.14f:i==hoverPoint?.11f:.075f;
                    Color color=chosen?new Color(.12f,.52f,.40f):Ink;
                    Dot(vertex.point,(top+1)*PaperSheet.Thickness+.002f,radius,color);
                    Dot(vertex.point,bottom*PaperSheet.Thickness-.002f,radius,color);
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
        private void Surface(PaperSheet.Face f,bool moving,bool highlight)
        {
            faceFrame=f.frame;
            float bottom=f.layer*PaperSheet.Thickness,top=bottom+PaperSheet.Thickness;
            float tilt=Mathf.Abs(faceFrame.MultiplyVector(Vector3.up).y);
            Color a=(f.back?Back:Front)*(.78f+.22f*tilt),b=(f.back?Front:Back)*(.78f+.22f*tilt);
            if(moving){float shade=1f-.18f*sine;a*=shade;b*=shade;}
            if(highlight){a=Color.Lerp(a,new Color(.67f,.86f,.77f),.65f);b=a;}
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
                if(workbench)continue;
                Vector2 shift=new Vector2(-(v-u).y,(v-u).x).normalized*.027f*(f.back?-1f:1f);
                Quad(Vertex(u,top+.0006f,moving),Vertex(v,top+.0006f,moving),
                    Vertex(v+shift,top+.0006f,moving),Vertex(u+shift,top+.0006f,moving),Edge);
            }
        }
        private void HingeStripe(PaperSheet.Guide g,Vector2 side,float h)
        {
            Quad(Vertex(g.a-side,h,false),Vertex(g.b-side,h,false),Vertex(g.b+side,h,false),
                 Vertex(g.a+side,h,false),new Color(.12f,.46f,.38f));
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
