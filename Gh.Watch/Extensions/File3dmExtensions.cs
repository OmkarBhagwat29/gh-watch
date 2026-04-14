using Rhino.FileIO;
using Rhino.Geometry;

namespace Gh.Watch.Extensions
{
    public static class File3dmExtensions
    {
        public static void AddBrepEdgesToFile(this File3dm file, Brep brep)
        {

            foreach (BrepEdge edge in brep.Edges)
            {
                file.Objects.AddCurve(edge);
            }
        }
    }
}
