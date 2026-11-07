using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // The right edge is the nose. The opened lengthwise crease is printed on the sheet.
    public enum PlanePattern { Basic=1, Dart=2, Bulldog=3 }

    public static class PaperPatterns
    {
        public static string Name(PlanePattern pattern) => pattern==PlanePattern.Basic ? "기본형" :
            pattern==PlanePattern.Dart ? "뾰족 다트" : "불도그 다트";
        public static int Length(PlanePattern pattern) => pattern==PlanePattern.Basic ? 5 :
            pattern==PlanePattern.Dart ? 7 : 8;

        // Sources for fold order. The game computes all hinge positions and mesh geometry.
        // Basic: https://www.foldnfly.com/0.html
        // Dart: https://www.foldnfly.com/1d.html
        // Bulldog: https://www.artofmanliness.com/skills/just-for-fun/how-to-make-the-worlds-best-paper-airplanes/
        // Offsets are measured on our own sheet, so both paper formats share the same landmarks.
        public static bool Next(PaperSheet sheet,PlanePattern pattern,out FoldStep step,out string instruction)
        {
            int index=sheet.Count, wings=Length(pattern)-2;
            float nose=sheet.Width, mid=sheet.Height*.5f;
            step=default;
            if(index>=Length(pattern)){instruction="도안 완성";return false;}
            if(index<2){
                bool upper=index==0;
                instruction=upper?"오른쪽 위 모서리를 중앙선에 맞추세요.":"오른쪽 아래 모서리도 중앙선에 맞추세요.";
                step=Line(upper?CreaseFamily.Falling:CreaseFamily.Rising,new Vector2(1,upper?1:-1),nose+(upper?mid:-mid));
            }else if(index==wings-1){
                instruction="긴 중앙선을 따라 동체를 반으로 접으세요.";
                step=Line(CreaseFamily.Horizontal,Vector2.up,mid);
            }else if(index<wings && index<4){
                bool upper=index==2;
                // The second hinge bisects the 45-degree nose edge and the centre line.
                float slope=Mathf.Tan(22.5f*Mathf.Deg2Rad);
                instruction=(upper?"위":"아래")+"쪽 빗변을 중앙선에 맞춰 코를 좁히세요.";
                step=Line(upper?CreaseFamily.Falling:CreaseFamily.Rising,new Vector2(slope,upper?1:-1),slope*nose+(upper?mid:-mid));
                if(pattern==PlanePattern.Bulldog)step.degrees=-180;
            }else if(index<wings){
                instruction="뾰족한 끝을 몸쪽으로 접어 코를 짧게 만드세요.";
                step=Line(CreaseFamily.Vertical,Vector2.right,nose-mid*.5f);
                step.degrees=-180;
            }else{
                bool upper=index==wings;
                instruction=upper?"윗날개를 펼치고 원하는 각도를 정하세요.":"반대쪽 날개도 펼쳐 양쪽 각도를 맞추세요.";
                var sample=sheet.Format==PaperSize.Square ? new Vector2(nose*.06f,mid*.32f) : new Vector2(nose*.12f,mid*.25f);
                int wing=-1;
                for(int i=0;i<sheet.Faces.Count;i++){
                    var f=sheet.Faces[i];
                    if(f.frame!=Matrix4x4.identity || !f.Contains(sample))continue;
                    if(wing<0 || (upper?f.layer>sheet.Faces[wing].layer:f.layer<sheet.Faces[wing].layer))wing=i;
                }
                if(wing<0)return false;
                float body=pattern==PlanePattern.Bulldog ? mid*.42f : mid*.5f;
                step=Line(CreaseFamily.Horizontal,Vector2.up,mid-body);
                step.side=-1;step.anchor=wing;step.flap=wing+1;step.degrees=upper?80f:-80f;
            }
            step.pattern=pattern;
            return true;
        }
        public sealed class Print
        {
            private struct Mark { public Vector3 a,b; public int stage; }
            private readonly List<Mark> marks=new List<Mark>();
            public readonly PaperSize size;
            public readonly PlanePattern pattern;

            public Print(PaperSize size,PlanePattern pattern)
            {
                this.size=size;this.pattern=pattern;
                var simulation=new PaperSheet(size);
                while(Next(simulation,pattern,out var step,out _)){
                    var turn=simulation.Plan(step);
                    if(turn==null)break;
                    foreach(var face in turn.movingFaces){
                        if(face.frame!=turn.frame || !PaperSheet.Intersection(face,step,out var a,out var b))continue;
                        marks.Add(new Mark{a=face.Original(a),b=face.Original(b),stage=simulation.Count});
                    }
                    if(!simulation.Finish(turn))break;
                }
            }

            // Project the real sequence's hinge segments back onto the same physical
            // patch of paper, even when that patch has been flipped or tilted.
            public void Append(PaperSheet sheet,List<PaperSheet.Guide> result)
            {
                for(int i=0;i<sheet.Faces.Count;i++){
                    var face=sheet.Faces[i];
                    var inverse=face.material.inverse;
                    foreach(var mark in marks){
                        Vector3 u=inverse.MultiplyPoint3x4(mark.a),v=inverse.MultiplyPoint3x4(mark.b);
                        Vector2 a=new Vector2(u.x,u.z),b=new Vector2(v.x,v.z);
                        if(!PaperSheet.Trim(face,ref a,ref b))continue;
                        var n=new Vector2(b.y-a.y,a.x-b.x).normalized;
                        var family=Mathf.Abs(n.x)<.001f?CreaseFamily.Horizontal:Mathf.Abs(n.y)<.001f?CreaseFamily.Vertical:
                            n.x*n.y>0?CreaseFamily.Falling:CreaseFamily.Rising;
                        var line=new FoldStep(family,mark.stage,1){anchor=i,creaseNormal=n,creaseOffset=Vector2.Dot(n,a)};
                        result.Add(new PaperSheet.Guide{line=line,a=a,b=b,layer=face.layer,face=face,blueprint=true});
                    }
                }
            }
        }
        private static FoldStep Line(CreaseFamily family,Vector2 normal,float position)
        {
            return new FoldStep(family,0,1){creaseNormal=normal,creaseOffset=position};
        }
    }
}

