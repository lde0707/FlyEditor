using System.Threading;

namespace FlyEditor.Flight
{
    // Runtime material identity survives transforms and stack renumbering.
    // A split creates descendants; coincident world positions never weld them.
    internal sealed class PaperMaterialRegion
    {
        static long nextId;
        internal readonly long id=Interlocked.Increment(ref nextId);
        readonly PaperMaterialRegion parent;
        internal PaperMaterialRegion(PaperMaterialRegion parent=null){this.parent=parent;}
        internal bool DescendsFrom(PaperMaterialRegion other)
        {
            for(var at=this;at!=null;at=at.parent)if(ReferenceEquals(at,other))return true;
            return false;
        }
        internal bool Related(PaperMaterialRegion other)=>other!=null&&(DescendsFrom(other)||other.DescendsFrom(this));
    }
}
