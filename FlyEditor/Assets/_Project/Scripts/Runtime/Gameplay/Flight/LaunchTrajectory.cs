using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // One disposable mesh per controller. The prediction never touches the live course or save.
    public sealed class LaunchTrajectory
    {
        private readonly FlightCourse course = new FlightCourse();
        private const float PreviewLength = 10f;
        private FlightModel source, preview;
        private Vector3 localNose=PaperPlaneGeometry.Nose;

        public void SetNose(Vector3 nose)
        {
            if(localNose==nose)return;
            localNose=nose;source=null;
        }
        private float cachedAngle, cachedStep, cachedHeight;
        private Matrix4x4 lastWorldToLocal;
        private bool geometryDirty;
        private readonly Mesh mesh;
        private readonly GameObject holder;
        private readonly List<Vector3> vertices = new List<Vector3>(1536);
        private readonly List<Color32> colors = new List<Color32>(1536);
        private readonly List<int> triangles = new List<int>(2304);
        private readonly List<Vector2> samples = new List<Vector2>(256);
        private readonly List<Vector3> noseSamples = new List<Vector3>(256);
        private static readonly Color32 Ink = new Color32(90,132,140,255);
        public IReadOnlyList<Vector2> Samples => samples;
        public bool Visible => holder.activeSelf;

        public LaunchTrajectory(Transform parent, Material material)
        {
            holder = new GameObject("출발 예상 궤도");
            holder.transform.SetParent(parent,false);
            mesh = new Mesh { name = "발사 궤도 점선" };
            mesh.MarkDynamic();
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            holder.AddComponent<MeshRenderer>().sharedMaterial = material;
            holder.SetActive(false);
        }
        public void Show(FlightModel ready,float angle,float dt) => Show(ready,angle,dt,ready.Height);

        public void Show(FlightModel ready,float angle,float dt,float releaseHeight)
        {
            if (!(dt>0f)) { Hide(); return; }
            if (!Visible) holder.SetActive(true);
            if (source == ready && angle == cachedAngle && dt == cachedStep && releaseHeight == cachedHeight) return;
            if (source != ready) { source = ready; preview = ready.CreatePreviewModel(); }
            cachedAngle = angle; cachedStep = dt; cachedHeight = releaseHeight;
            preview.RestartPreview(angle,releaseHeight);
            course.Reset();
            samples.Clear();
            noseSamples.Clear();
            geometryDirty = true;
            Vector2 from = new Vector2(preview.Distance,preview.Height);
            samples.Add(from);
            noseSamples.Add(PredictedNose(from));
            float distance = 0f;
            while (preview.State==FlightModel.Phase.Flying && distance<PreviewLength)
            {
                course.Step(preview,dt);
                Vector2 to = new Vector2(preview.Distance,preview.Height);
                samples.Add(to);
                Vector3 previousNose = noseSamples[noseSamples.Count-1];
                Vector3 nextNose = PredictedNose(to);
                float segment = Vector3.Distance(previousNose,nextNose);
                float remaining = PreviewLength-distance;
                if (segment >= remaining)
                {
                    noseSamples.Add(Vector3.Lerp(previousNose,nextNose,remaining/segment));
                    break;
                }
                noseSamples.Add(nextNose);
                distance += segment;
            }
        }
        private Vector3 PredictedNose(Vector2 center)
            => (Vector3)center + Quaternion.Euler(0,0,preview.Pitch) * localNose;

        // Anchor the predicted path at the supplied nose position while aiming.
        // The controller keeps the final placement for the duration of the throw.
        public void DrawFromNose(Vector3 nose)
        {
            if (!Visible || noseSamples.Count < 2) return;
            holder.transform.position = nose;
            Matrix4x4 basis = holder.transform.worldToLocalMatrix;
            basis.SetColumn(3,new Vector4(0,0,0,1));
            if (!geometryDirty && basis == lastWorldToLocal) return;
            geometryDirty = false;
            lastWorldToLocal = basis;
            vertices.Clear(); triangles.Clear(); colors.Clear();
            Vector3 origin = noseSamples[0];
            float distance = 0f;
            for (int i=1;i<noseSamples.Count;i++)
                AddDashes(noseSamples[i-1]-origin,noseSamples[i]-origin,ref distance);
            for (int i=0;i<vertices.Count;i++) vertices[i]=basis.MultiplyVector(vertices[i]);
            mesh.Clear();
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles,0);
        }
        private void AddDashes(Vector3 from,Vector3 to,ref float distance)
        {
            // The last dash starts at 9.57 m, leaving ink at the 10 m cutoff.
            const float period=.87f, dash=.47f;
            float length=Vector3.Distance(from,to);
            if (length < .00001f) return;
            Vector3 dir=(to-from)/length;
            Vector3 side=new Vector3(-dir.y,dir.x,0)*.055f;
            float end=distance+length;
            // Split at fixed world-space intervals even when one fast physics step spans several dashes.
            for(int i=Mathf.FloorToInt(distance/period); i*period < end; i++)
            {
                float lo=Mathf.Max(distance,i*period), hi=Mathf.Min(end,i*period+dash);
                if (hi<=lo) continue;
                Vector3 a=from+dir*(lo-distance), b=from+dir*(hi-distance);
                int k=vertices.Count;
                vertices.Add(a-side); vertices.Add(a+side); vertices.Add(b+side); vertices.Add(b-side);
                for(int j=0;j<4;j++) colors.Add(Ink);
                triangles.Add(k); triangles.Add(k+1); triangles.Add(k+2);
                triangles.Add(k); triangles.Add(k+2); triangles.Add(k+3);
            }
            distance=end;
        }
        public void Hide() { if (Visible) holder.SetActive(false); }
        public void Dispose() { Object.Destroy(mesh); Object.Destroy(holder); }
    }
}
