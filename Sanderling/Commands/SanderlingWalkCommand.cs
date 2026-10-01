using Sanderling.Navigation;
using Rhino;
using Rhino.Commands;

namespace Sanderling.Commands
{
    /// <summary>
    /// SanderlingWalk - starts navigation in Walk mode (eye height, ground following), regardless of the DefaultMode setting.
    /// </summary>
    [System.Runtime.InteropServices.Guid("1d41c0d7-5b94-4cd0-b6e3-949812555502")]
    public class SanderlingWalkCommand : Command
    {
        public override string EnglishName
        {
            get { return "SanderlingWalk"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            return NavigationSession.Run(doc, NavigationMode.Walk);
        }
    }
}
