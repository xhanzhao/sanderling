using Sanderling.Navigation;
using Rhino;
using Rhino.Commands;

namespace Sanderling.Commands
{
    /// <summary>
    /// Sanderling - enters real-time navigation mode in the active viewport, starting in the DefaultMode
    /// configured with SanderlingSettings (Fly unless changed). Press F to switch modes, Esc to exit.
    /// </summary>
    [System.Runtime.InteropServices.Guid("c6369feb-c9db-4289-9db0-d5d105d1d453")]
    public class SanderlingCommand : Command
    {
        public override string EnglishName
        {
            get { return "Sanderling"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            return NavigationSession.Run(doc, null);
        }
    }
}
