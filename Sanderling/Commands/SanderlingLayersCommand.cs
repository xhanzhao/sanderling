using System;
using System.Collections.Generic;
using Sanderling.Navigation;
using Sanderling.Settings;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input;
using Rhino.Input.Custom;

namespace Sanderling.Commands
{
    /// <summary>
    /// SanderlingLayers - chooses which layers take part in Walk mode ground detection and collision.
    ///   All     - all visible layers (default)
    ///   Exclude - ignore the selected layers and their sub-layers (e.g. glass, trees, people, context)
    ///   Only    - use only the selected layers and their sub-layers (e.g. walls, slabs, stairs in large models)
    /// The filter is stored in the document and saved with the .3dm file.
    /// </summary>
    [System.Runtime.InteropServices.Guid("38095069-3da6-487d-98d7-ee90e9717bfa")]
    public class SanderlingLayersCommand : Command
    {
        public override string EnglishName
        {
            get { return "SanderlingLayers"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (NavigationSession.IsRunning || doc == null) return Result.Nothing;

            CollisionLayerFilter filter = CollisionLayerFilter.Load(doc);
            string[] modes = { "All", "Exclude", "Only" }; // order matches the CollisionLayerMode enum values

            RhinoApp.WriteLine("Sanderling: Current layer filter - {0}", filter.Describe(doc));

            GetOption go = new GetOption();
            go.AcceptNothing(true);

            while (true)
            {
                go.ClearCommandOptions();
                go.SetCommandPrompt(string.Format("Sanderling layer filter ({0} layers selected, press Enter to save)", CountValid(doc, filter)));

                int modeIndex = go.AddOptionList("Mode", modes, (int)filter.Mode);
                int selectIndex = go.AddOption("SelectLayers");
                int clearIndex = go.AddOption("ClearLayers");

                GetResult res = go.Get();

                if (res == GetResult.Option)
                {
                    CommandLineOption opt = go.Option();
                    if (opt == null) continue;

                    if (opt.Index == modeIndex)
                    {
                        int i = opt.CurrentListOptionIndex;
                        filter.Mode = i == 1 ? CollisionLayerMode.Exclude : (i == 2 ? CollisionLayerMode.Only : CollisionLayerMode.All);
                    }
                    else if (opt.Index == selectIndex)
                    {
                        SelectLayers(doc, filter);
                    }
                    else if (opt.Index == clearIndex)
                    {
                        filter.LayerIds.Clear();
                    }
                    continue;
                }

                if (res == GetResult.Nothing) break;
                return Result.Cancel; // Esc: do not save
            }

            if (filter.Mode != CollisionLayerMode.All && CountValid(doc, filter) == 0)
            {
                RhinoApp.WriteLine(filter.Mode == CollisionLayerMode.Only
                    ? "Sanderling: Warning - Only mode with no layers selected: Walk mode will have no ground or walls."
                    : "Sanderling: Exclude mode with no layers selected behaves like All.");
            }

            filter.Save(doc);
            RhinoApp.WriteLine("Sanderling: Layer filter saved - {0}", filter.Describe(doc));
            return Result.Success;
        }

        private static void SelectLayers(RhinoDoc doc, CollisionLayerFilter filter)
        {
            List<int> current = new List<int>();
            foreach (Guid id in filter.LayerIds)
            {
                Layer layer = doc.Layers.FindId(id);
                if (layer != null && !layer.IsDeleted) current.Add(layer.Index);
            }

            int[] selected;
            bool ok = Rhino.UI.Dialogs.ShowSelectMultipleLayersDialog(current, "Sanderling - Select Layers", false, out selected);
            if (!ok || selected == null) return;

            filter.LayerIds.Clear();
            foreach (int index in selected)
            {
                if (index < 0 || index >= doc.Layers.Count) continue;
                Layer layer = doc.Layers[index];
                if (layer != null && !layer.IsDeleted) filter.LayerIds.Add(layer.Id);
            }
        }

        private static int CountValid(RhinoDoc doc, CollisionLayerFilter filter)
        {
            int n = 0;
            foreach (Guid id in filter.LayerIds)
            {
                Layer layer = doc.Layers.FindId(id);
                if (layer != null && !layer.IsDeleted) n++;
            }
            return n;
        }
    }
}
