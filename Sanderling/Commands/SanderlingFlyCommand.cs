using Sanderling.Navigation;
using Rhino;
using Rhino.Commands;

namespace Sanderling.Commands
{
    /// <summary>
    /// SanderlingFly - starts navigation in Fly mode, regardless of the DefaultMode setting.
    /// </summary>
    [System.Runtime.InteropServices.Guid("8b22490f-04a7-4a67-8911-60f5c91f534c")]
    public class SanderlingFlyCommand : Command
    {
        public override string EnglishName
        {
            get { return "SanderlingFly"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            return NavigationSession.Run(doc, NavigationMode.Fly);
        }
    }
}
