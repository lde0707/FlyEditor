using System;
using UnityEngine;
using FlyEditor.Flight;
using FlyEditor.Editor;
public static class LeafRegression {
 static void Check(bool v,string m){if(!v)throw new Exception(m);}
 public static void Run(){int cases=0;
  foreach(int index in new[]{0,2})foreach(float angle in new[]{30f,90f,180f}){
   var p=LeafProbe.House();var before=p.Faces;FourPointFoldingVerification.Metrics(p);Debug.Log("Leaf case "+index+" angle "+angle);
   var step=new FoldStep(CreaseFamily.Vertical,0,-1){creaseNormal=Vector2.right,creaseOffset=2.75f,anchor=index,flap=index+1,singlePanel=true,connectedPanel=true,compactLayers=true};
   var plan=p.Plan(step);Check(plan!=null,"No selected leaf");int sign=index==0?-1:1;
   Check(plan.Constrain(-sign*30)==0,"Can pass through other leaf");
   Check(p.Finish(plan,sign*angle),"Cannot finish leaf");FourPointFoldingVerification.Metrics(p);PetalFoldingVerification.Replay(p);
   var json=JsonUtility.ToJson(new ResearchSave{paperFolds=p.Recipe()});var restored=PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(json).paperFolds);Check(restored.Count==p.Count,"JSON replay failed");FourPointFoldingVerification.Metrics(restored);
   Check(p.Undo()&&ReferenceEquals(before,p.Faces),"Undo changed original");cases++;
  }
  Debug.Log("[LeafRegression] PASS "+cases+" selected upper/lower angles, area and seams, collision, JSON replay, exact undo.");
  PetalFoldingVerification.Regression();
 }
}
