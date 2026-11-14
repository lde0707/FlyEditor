namespace FlyEditor.Flight
{
    // A surface choice is not a physical turn. Keep it available when a rigid
    // fold needs additional hinges; never bypass connectivity to make it move.
    internal sealed class PaperSurfaceChoice
    {
        public readonly PaperSheet.Face source, chosen, remainder;
        private readonly System.Collections.Generic.List<PaperSurfaceChoice> choices;
        public PaperSurfaceChoice(System.Collections.Generic.IReadOnlyList<PaperSheet.Face> faces,FoldStep line,System.Collections.Generic.List<int> selected)
        {
            choices=new System.Collections.Generic.List<PaperSurfaceChoice>();
            var basis=faces[line.anchor];var normal=basis.frame.MultiplyVector(new UnityEngine.Vector3(line.Normal.x,0,line.Normal.y));
            var point=basis.World(line.Normal*(line.Position/line.Normal.sqrMagnitude),0);
            foreach(int i in selected){var local=PaperFoldTopology.WorldLine(faces[i],point,normal);local.side=line.side;local.preserveSlivers=true;choices.Add(new PaperSurfaceChoice(faces[i],local));}
        }
        public PaperSurfaceChoice For(PaperSheet.Face face)
        {
            if(ReferenceEquals(face,source))return this;
            if(choices!=null)foreach(var choice in choices)if(ReferenceEquals(choice.source,face))return choice;
            return null;
        }
        public PaperSurfaceChoice(PaperSheet.Face source,FoldStep line)
        {
            this.source=source;
            chosen=PaperSheet.Clip(source,line,line.side);
            remainder=PaperSheet.Clip(source,line,-line.side);
        }
    }
}
