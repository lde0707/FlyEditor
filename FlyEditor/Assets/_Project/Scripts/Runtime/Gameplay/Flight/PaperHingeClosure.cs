using System;
using UnityEngine;

namespace FlyEditor.Flight
{
    // Metric closure is prepared once in double precision. Drag poses use
    // the resulting rigid hinges; no stretching or model-specific ratios.
    internal static class PaperHingeClosure
    {
        private readonly struct D2
        {
            internal readonly double x,y;
            internal D2(double x,double y){this.x=x;this.y=y;}
            internal D2(Vector2 p):this(p.x,p.y){}
            internal double Length2=>x*x+y*y;
            internal double Dot(D2 b)=>x*b.x+y*b.y;
            internal Vector2 Float=>new Vector2((float)x,(float)y);
            public static D2 operator +(D2 a,D2 b)=>new D2(a.x+b.x,a.y+b.y);
            public static D2 operator -(D2 a,D2 b)=>new D2(a.x-b.x,a.y-b.y);
            public static D2 operator *(D2 a,double b)=>new D2(a.x*b,a.y*b);
        }
        private readonly struct Problem
        {
            internal readonly D2 tip,head,left,right,backLeft,backRight;
            internal readonly double scale;
            internal Problem(Vector2 t,Vector2 h,Vector2 l,Vector2 r,Vector2 bl,Vector2 br)
            {tip=new D2(t);head=new D2(h);left=new D2(l);right=new D2(r);backLeft=new D2(bl);backRight=new D2(br);scale=Math.Max((tip-head).Length2,Math.Max((left-right).Length2,1e-8));}
            internal void Hinges(D2 uv,out D2 p,out D2 q){p=head+(left-head)*uv.x;q=head+(right-head)*uv.y;}
            internal D2 Error(D2 uv)
            {
                Hinges(uv,out var p,out var q);var t=Reflect(tip,p,q);
                return new D2(((t-Reflect(left,backLeft,p)).Length2-(tip-left).Length2)/scale,((t-Reflect(right,backRight,q)).Length2-(tip-right).Length2)/scale);
            }
        }
        private static D2 Reflect(D2 point,D2 a,D2 b)
        {var axis=b-a;var delta=point-a;return a+axis*(2*axis.Dot(delta)/Math.Max(axis.Length2,1e-24))-delta;}
        private static bool MetricFit(Problem problem,D2 uv)
        {
            problem.Hinges(uv,out var p,out var q);var t=Reflect(problem.tip,p,q);
            double tolerance=Math.Sqrt(problem.scale)*1e-5;
            return (p-q).Length2>problem.scale*1e-8 && (t-problem.tip).Length2>problem.scale*1e-6 &&
                Math.Abs(Math.Sqrt((t-Reflect(problem.left,problem.backLeft,p)).Length2)-Math.Sqrt((problem.tip-problem.left).Length2))<tolerance &&
                Math.Abs(Math.Sqrt((t-Reflect(problem.right,problem.backRight,q)).Length2)-Math.Sqrt((problem.tip-problem.right).Length2))<tolerance;
        }
        private static bool Solve(Problem problem,D2 seed,out D2 solution)
        {
            var uv=seed;const double epsilon=1e-5;
            for(int iteration=0;iteration<48;iteration++){
                var e=problem.Error(uv);if(!double.IsFinite(e.x)||!double.IsFinite(e.y))break;
                if(e.Length2<1e-20){problem.Hinges(uv,out var p,out var q);
                    if((p-q).Length2>problem.scale*1e-8 && (Reflect(problem.tip,p,q)-problem.tip).Length2>problem.scale*1e-6){solution=uv;return true;}break;}
                var a=(problem.Error(uv+new D2(epsilon,0))-problem.Error(uv-new D2(epsilon,0)))*(1/(2*epsilon));
                var b=(problem.Error(uv+new D2(0,epsilon))-problem.Error(uv-new D2(0,epsilon)))*(1/(2*epsilon));
                double aa=a.Dot(a)+1e-12,bb=b.Dot(b)+1e-12,ab=a.Dot(b),det=aa*bb-ab*ab;
                if(det<1e-24)break;double ae=a.Dot(e),be=b.Dot(e);
                var delta=new D2((bb*ae-ab*be)/det,(aa*be-ab*ae)/det);bool improved=false;
                for(double weight=1;weight>=1d/256;weight*=.5){
                    var next=uv-delta*weight;if(next.x<=1e-5||next.x>=.99999||next.y<=1e-5||next.y>=.99999)continue;
                    if(problem.Error(next).Length2>=e.Length2)continue;uv=next;improved=true;break;
                }if(!improved)break;
            }
            // Input meshes are float-valued. A near-singular closure may have
            // no exact double root after float rounding; retain the strict
            // geometric length tolerance, then validate every rigid pose too.
            if(MetricFit(problem,uv)){solution=uv;return true;}
            solution=default;return false;
        }
        internal static bool TrySolve(Vector2 tip,Vector2 head,Vector2 left,Vector2 right,Vector2 backLeft,Vector2 backRight,ref Vector2 p,ref Vector2 q)
        {
            var problem=new Problem(tip,head,left,right,backLeft,backRight);
            double dl=(problem.head-problem.left).Length2,dr=(problem.head-problem.right).Length2;
            if(dl<1e-8||dr<1e-8)return false;
            var seed=new D2(Math.Sqrt((problem.head-new D2(p)).Length2/dl),Math.Sqrt((problem.head-new D2(q)).Length2/dr));
            if(!Solve(problem,seed,out var uv) && !Solve(problem,new D2(.5,.5),out uv) && !Solve(problem,new D2(.75,.75),out uv))return false;
            problem.Hinges(uv,out var a,out var b);p=a.Float;q=b.Float;return true;
        }
    }
}
