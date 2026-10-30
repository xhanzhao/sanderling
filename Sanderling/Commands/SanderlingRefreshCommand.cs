using System;
using System.Globalization;
using Sanderling.Navigation;
using Sanderling.Scene;
using Sanderling.Settings;
using Rhino;
using Rhino.Commands;

namespace Sanderling.Commands
{
    /// <summary>
    /// SanderlingRefresh - discards the cached navigation geometry of the active document and rebuilds it.
    /// Normally not needed: changed objects are detected automatically. Use it if Walk mode collides with
    /// geometry that no longer exists, or ignores geometry that was just added.
    /// </summary>
    [System.Runtime.InteropServices.Guid("c04fd614-e09d-4eac-ab56-785f6fa2774f")]
    public class SanderlingRefreshCommand : Command
    {
        public override string EnglishName
        {
            get { return "SanderlingRefresh"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (NavigationSession.IsRunning || doc == null) return Result.Nothing;

            PersistentSettings store = SanderlingPlugIn.Instance != null ? SanderlingPlugIn.Instance.Settings : null;
            CollisionSettings settings = CollisionSettings.Load(store);
            CollisionLayerFilter filter = CollisionLayerFilter.Load(doc);

            RhinoApp.WriteLine("Sanderling: Rebuilding navigation geometry... (press Esc to cancel)");
            RhinoApp.Wait();

            NavigationScene scene = NavigationScene.ForDocument(doc);
            scene.MarkAllDirty();

            bool escape = false;
            EventHandler onEscape = (sender, e) => escape = true;
            RhinoApp.EscapeKeyPressed += onEscape;

            SceneStats stats;
            SpatialIndex index;
            try
            {
                using (BuildProgress progress = new BuildProgress("Sanderling: Rebuilding geometry", () => escape, null))
                {
                    index = scene.Prepare(doc, settings, filter, progress, out stats);
                }
            }
            finally
            {
                RhinoApp.EscapeKeyPressed -= onEscape;
            }

            if (index == null)
            {
                RhinoApp.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Sanderling: Rebuild cancelled after {0} objects. The rest is rebuilt the next time Walk mode starts.",
                    stats.Rebuilt));
                return Result.Cancel;
            }

            RhinoApp.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "Sanderling: Navigation geometry rebuilt - {0} objects, {1} meshes, {2} triangles ({3:0} ms).",
                stats.Objects, stats.Meshes, stats.Triangles, stats.Milliseconds));
            return Result.Success;
        }
    }
}
