using Rhino.PlugIns;

namespace Sanderling
{
    /// <summary>
    /// Sanderling plug-in entry point.
    /// Each .rhp may contain only one class derived from PlugIn.
    /// </summary>
    public class SanderlingPlugIn : PlugIn
    {
        public SanderlingPlugIn()
        {
            Instance = this;
        }

        /// <summary>Plug-in singleton (created when Rhino loads the plug-in).</summary>
        public static SanderlingPlugIn Instance { get; private set; }

        /// <summary>
        /// Load at startup so the Sanderling toolbar (Sanderling.rui next to the .rhp) is available right away,
        /// not only after the first Sanderling command. Loading is cheap: nothing is set up until a command runs.
        /// </summary>
        public override PlugInLoadTime LoadTime
        {
            get { return PlugInLoadTime.AtStartup; }
        }

        // Plug-in settings are persisted through the base class Settings (PersistentSettings)
        // and saved automatically when Rhino closes. See Settings/NavigationSettings.cs for reading/writing.
    }
}
