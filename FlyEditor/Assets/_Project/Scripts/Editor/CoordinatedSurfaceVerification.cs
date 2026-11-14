using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class CoordinatedSurfaceVerification {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
  static void Check(bool ok,string message){if(!ok)throw new Exception("[CoordinatedSurface] "+message);}
  static Vector3 Material(PaperSheet.Face f,Vector2 p)=>f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
  static Vector2 Local(PaperSheet.Face f,Vector3 p){var q=f.material.inverse.MultiplyPoint3x4(p);return new Vector2(q.x,q.z);}
  static PaperSheet Load()=>PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_COMPLEX_ROOT")+"/checkpoint-seven.json")).paperFolds);
  static FoldStep Request(bool hints=false){var step=JsonUtility.FromJson<FoldStep>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_COMPLEX_ROOT")+"/intersection-petal.json"));
   if(!hints){step.pullLeft=new Vector2(7.7781744f,0);step.pullRight=new Vector2(11,3.2218256f);}step.materialPartition=step.coordinatedSurface=true;return step;}
  static List<PaperSheet.Face> Faces(PaperSheet s,Matrix4x4 transform,int gap,bool split){
   var ctor=typeof(PaperSheet.Face).GetConstructor(F,null,new[]{typeof(Vector2[]),typeof(int),typeof(bool),typeof(Matrix4x4),typeof(Matrix4x4),typeof(PaperSheet.Face),typeof(bool)},null);
   var result=new List<PaperSheet.Face>();var clip=typeof(PaperSheet).GetMethod("Clip",F);
   foreach(var f in s.Faces){var parts=new List<PaperSheet.Face>{f};if(split){Vector2 c=Vector2.zero;foreach(var p in f.points)c+=p;c/=f.points.Length;var line=new FoldStep{creaseNormal=new Vector2(1,.37f),creaseOffset=Vector2.Dot(new Vector2(1,.37f),c)};parts.Clear();foreach(int sign in new[]{-1,1}){var part=(PaperSheet.Face)clip.Invoke(null,new object[]{f,line,sign});if(part!=null)parts.Add(part);}}
    foreach(var part in parts)result.Add((PaperSheet.Face)ctor.Invoke(new object[]{part.points,part.layer*gap,part.back,transform*part.frame,part.material,part,false}));
   }return result;
  }
  static void Fixed(IReadOnlyList<PaperSheet.Face> before,IReadOnlyList<PaperSheet.Face> preview,Func<PaperSheet.Face,int> role){
   foreach(var f in preview)if(role(f)==0)foreach(var p in f.points){var m=Material(f,p);bool found=false;foreach(var g in before){var q=Local(g,m);if(!g.Contains(q))continue;Check(Vector3.Distance(f.World(p,0),g.World(q,0))<.002f,"stationary material moved");found=true;}Check(found,"stationary material lost");}
  }
  static void ClosureMath(){var t=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.PaperHingeClosure");var method=t.GetMethod("TrySolve",F);int cases=0;
   foreach(float height in new[]{3f,4f,7f})foreach(float wing in new[]{1f,2f})foreach(float rear in new[]{.5f,1f,3f}){
    float initial=height*wing/(height+wing);var p=new Vector2(initial,initial);var q=new Vector2(initial,-initial);
    object[] args={Vector2.zero,new Vector2(height,0),new Vector2(0,wing),new Vector2(0,-wing),new Vector2(-rear,0),new Vector2(-rear,0),p,q};
    Check((bool)method.Invoke(null,args),"metric closure "+height+"/"+wing+"/"+rear);p=(Vector2)args[6];q=(Vector2)args[7];
    double k=(double)wing*wing*height*(height+rear)/(height*height+wing*wing);float expected=(float)((Math.Sqrt(rear*rear+4*k)-rear)*.5);
    Check(Mathf.Abs(p.x-expected)<.0003f&&Mathf.Abs(q.x-expected)<.0003f,"closure differs from independently derived four-bar root "+height+"/"+wing+"/"+rear+" p="+p.ToString("F6")+" q="+q.ToString("F6")+" expected="+expected);cases++;
   }Debug.Log("[CoordinatedSurface] PASS "+cases+" metric closure proportions; hinge positions derived from lengths, independent analytic oracle.");
  }
  static Vector2 Rotate(Vector2 p,int r){while(r-->0)p=new Vector2(11-p.y,p.x);return p;}
  static PaperCornerPull StandardPlan(PaperSheet sheet,Vector2 tip,Vector2 head,Vector2 left,Vector2 right,int sign){
   for(int i=0;i<sheet.Faces.Count;i++){var f=sheet.Faces[i];var t=Local(f,new Vector3(tip.x,0,tip.y));if(!f.Contains(t))continue;
    var step=new FoldStep{anchor=i,flap=i+1,cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,regionalPull=true,coupledHinges=true,coordinatedCreases=true,coordinatedSurface=true,adaptiveCreases=true,
     connectedPetalBacking=true,partialPetalBacking=true,continuousHinge=true,neutralContacts=true,compactLayers=true,materialPartition=true,separateFoldSides=true,
     pullTip=t,pullStop=Local(f,new Vector3(head.x,0,head.y)),pullLeft=Local(f,new Vector3(left.x,0,left.y)),pullRight=Local(f,new Vector3(right.x,0,right.y))};
    var p=PaperCornerPull.Create(sheet,step);if(p!=null&&p.UsesCoordinatedSurface&&p.Pose(sign*3))return p;
   }return null;
  }
  static void StandardPockets(){int cases=0,poses=0;for(int r=0;r<4;r++)foreach(bool swap in new[]{false,true}){
   var sheet=PetalFoldingVerification.Square(r);
   foreach(bool front in new[]{true,false}){
    var t=Rotate(front?new Vector2(0,11):new Vector2(11,0),r);var h=Rotate(new Vector2(5.5f,5.5f),r);
    var l=Rotate(front?new Vector2(0,5.5f):new Vector2(5.5f,0),r);var rr=Rotate(front?new Vector2(5.5f,11):new Vector2(11,5.5f),r);
    var plan=StandardPlan(sheet,t,h,swap?rr:l,swap?l:rr,front?1:-1);Check(plan!=null,"standard pocket "+r+"/"+swap+"/"+front);
    foreach(float a in new[]{0f,3f,30f,60f,90f,120f,150f,180f}){Check(plan.Pose(front?a:-a),"standard pose");ReverseDirectionVerification.Geometry(plan.Preview);poses++;}
    Check(sheet.FinishPull(plan,front?180:-180),"standard commit");PetalFoldingVerification.Replay(sheet);cases++;
   }
  }Debug.Log("[CoordinatedSurface] PASS "+cases+" ordinary front/back pocket operations, "+poses+" poses; four paper rotations, either flank order, material seams/area and replay.");}
  public static void Verify(){ClosureMath();StandardPockets();int cases=0,poses=0;
   foreach(float rotation in new[]{0f,37f,180f,273f})foreach(bool swap in new[]{false,true})foreach(bool split in new[]{false,true})foreach(int gap in new[]{1,3})foreach(bool hints in new[]{false,true}){
    var filter=Environment.GetEnvironmentVariable("FLYEDITOR_COMPLEX_ROTATION");if(filter!=null&&rotation!=float.Parse(filter,System.Globalization.CultureInfo.InvariantCulture))continue;
    var sheet=Load();Check(sheet.Count==7,"checkpoint");var position=gap==1?new Vector3(2,-1,3):new Vector3(1000,-2000,3000);var transform=Matrix4x4.TRS(position,Quaternion.AngleAxis(rotation,new Vector3(1,2,3).normalized),Vector3.one);
    var source=Faces(sheet,transform,gap,split);typeof(PaperSheet).GetField("faces",F).SetValue(sheet,source);var step=Request();
    step=Request(hints);var original=Load();var basis=original.Faces[step.anchor];var tip=Material(basis,step.pullTip);int anchor=-1;for(int i=0;i<source.Count;i++){var q=Local(source[i],tip);if(source[i].Contains(q)&&(anchor<0||source[i].layer>source[anchor].layer))anchor=i;}
    Check(anchor>=0,"visible anchor");var selected=source[anchor];step.anchor=anchor;step.flap=anchor+1;step.pullTip=Local(selected,tip);step.pullStop=Local(selected,Material(basis,step.pullStop));step.pullLeft=Local(selected,Material(basis,step.pullLeft));step.pullRight=Local(selected,Material(basis,step.pullRight));if(swap){var q=step.pullLeft;step.pullLeft=step.pullRight;step.pullRight=q;}
    var p=PaperCornerPull.Create(sheet,step);Check(p!=null&&p.UsesCoordinatedSurface&&p.ResolvedHingeHints==hints,"generic support graph "+rotation+"/"+swap+"/"+split+"/"+gap+"/hints="+hints);
    var role=(Func<PaperSheet.Face,int>)Delegate.CreateDelegate(typeof(Func<PaperSheet.Face,int>),p,typeof(PaperCornerPull).GetMethod("MotionRole",F));
    foreach(float angle in new[]{0f,3f,30f,60f,90f,120f,150f,179.9f,180f,45f,0f,90f,180f}){
     bool validPose=p.Pose(angle);string reason=p.LastFailure;
     Check(validPose,"pose "+rotation+"/"+swap+"/"+split+"/"+gap+"/hints="+hints+"/"+angle+" reason="+reason);ReverseDirectionVerification.Geometry(p.Preview);Fixed(source,p.Preview,role);poses++;
    }
    float primary=0,support=0;foreach(var f in p.Preview){if(role(f)==1)primary+=PaperSheet.Area(f.points);if(role(f)==2)support+=PaperSheet.Area(f.points);}Check(primary>.01f&&support>.01f,"moving roles missing");
    var valid=new List<PaperSheet.Face>(p.Preview);Check(!p.Pose(float.NaN)&&!p.Pose(181),"nonfinite angle accepted");Check(p.Preview.Count==valid.Count&&ReferenceEquals(p.Preview[0],valid[0]),"invalid request changed preview");
    Check(p.Pose(0),"reset preview");valid=new List<PaperSheet.Face>(p.Preview);var validTip=p.Tip;
    Check(!p.Pose(-5),"blocked direction passed through stationary body");Check(ReferenceEquals(p.Preview[0],valid[0])&&(p.Tip-validTip).sqrMagnitude<1e-10f,"blocked request changed preview");
    Check(ReferenceEquals(source,sheet.Faces),"preview changed live source");
    Check(sheet.FinishPull(p,90),"partial commit");ReverseDirectionVerification.Geometry(sheet.Faces);Check(sheet.Undo()&&ReferenceEquals(source,sheet.Faces),"undo did not restore source");cases++;
   }
   RejectedHints();
   var replay=Load();var request=Request(true);var fold=PaperCornerPull.Create(replay,request);Check(fold!=null&&replay.FinishPull(fold,180),"full commit");ReverseDirectionVerification.Geometry(replay.Faces);PetalFoldingVerification.Replay(replay);
   File.WriteAllText(Environment.GetEnvironmentVariable("FLYEDITOR_COMPLEX_ROOT")+"/continued-eight.json",JsonUtility.ToJson(new ResearchSave{paperFolds=replay.Recipe()},true));
   Debug.Log("[CoordinatedSurface] PASS "+cases+" rotated/translated, flank-order, fragmented and layer-gap cases; "+poses+" poses; fixed material, seams/area, atomic invalid input, commit/undo, saved replay.");
  }
  public static void RejectedHints(){
   var rejected=Request(true);rejected.pullLeft+=new Vector2(.2f,.1f);var bad=PaperCornerPull.Create(Load(),rejected);Check(bad==null||!bad.ResolvedHingeHints,"arbitrary interior point was silently rebound");
   var sheet=Load();var request=Request(true);var basis=sheet.Faces[request.anchor];var world=basis.World(request.pullLeft,0);var expected=Material(basis,request.pullLeft);bool checkedHidden=false;
   foreach(var f in sheet.Faces){if(f.material==basis.material)continue;var at=f.frame.inverse.MultiplyPoint3x4(world);var point=new Vector2(at.x,at.z);if(Mathf.Abs(at.y)>.0001f||!f.Contains(point))continue;var other=Material(f,point);if((other-expected).sqrMagnitude<.01f)continue;
    var hidden=request;hidden.pullLeft=Local(basis,other);var p=PaperCornerPull.Create(sheet,hidden);Check(p==null||!p.ResolvedHingeHints,"hidden overlapping material was rebound to visible leaf");checkedHidden=true;break;
   }
   Check(checkedHidden,"hidden material negative case missing");Debug.Log("[CoordinatedSurface] PASS arbitrary-point and hidden-overlap hints rejected; material boundary ownership required.");
  }
  public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
 }
}
