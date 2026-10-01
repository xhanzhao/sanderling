using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;

namespace Sanderling.Settings
{
    /// <summary>
    /// Collision body and geometry filter settings (global, persisted in the plug-in settings).
    /// Lengths are in meters.
    /// </summary>
    internal sealed class CollisionSettings
    {
        public const double DefaultBodyRadius = 0.25;
        public const double DefaultBodyHeight = 1.80;

        /// <summary>Radius of the collision capsule (m).</summary>
        public double BodyRadius { get; set; }
        /// <summary>Height of the collision capsule (m); always kept above the eye height.</summary>
        public double BodyHeight { get; set; }
        /// <summary>Whether collision is on when a Walk session starts (C toggles it only for the current session).</summary>
        public bool CollisionEnabled { get; set; }
        /// <summary>Show physics timing and triangle counts in the HUD.</summary>
        public bool ShowStats { get; set; }

        // Object types used for ground and collision
        public bool CollideMeshes { get; set; }
        public bool CollideBreps { get; set; }      // Breps and surfaces
        public bool CollideExtrusions { get; set; }
        public bool CollideSubD { get; set; }
        public bool CollideBlocks { get; set; }
        public bool CollideLocked { get; set; }

        public CollisionSettings()
        {
            ResetToDefaults();
        }

        public void ResetToDefaults()
        {
            BodyRadius = DefaultBodyRadius;
            BodyHeight = DefaultBodyHeight;
            CollisionEnabled = true;
            ShowStats = false;
            CollideMeshes = true;
            CollideBreps = true;
            CollideExtrusions = true;
            CollideSubD = true;
            CollideBlocks = true;
            CollideLocked = true;
        }

        /// <summary>Whether objects of this type take part in ground detection and collision.</summary>
        public bool AllowsType(ObjectType type)
        {
            switch (type)
            {
                case ObjectType.Mesh: return CollideMeshes;
                case ObjectType.Brep:
                case ObjectType.Surface: return CollideBreps;
                case ObjectType.Extrusion: return CollideExtrusions;
                case ObjectType.SubD: return CollideSubD;
                case ObjectType.InstanceReference: return CollideBlocks;
                default: return false;
            }
        }

        // ---- Persistence ----
        private const string KeyBodyRadius = "BodyRadius";
        private const string KeyBodyHeight = "BodyHeight";
        private const string KeyCollision = "Collision";
        private const string KeyShowStats = "ShowStats";
        private const string KeyMeshes = "CollideMeshes";
        private const string KeyBreps = "CollideBreps";
        private const string KeyExtrusions = "CollideExtrusions";
        private const string KeySubD = "CollideSubD";
        private const string KeyBlocks = "CollideBlocks";
        private const string KeyLocked = "CollideLocked";

        public static CollisionSettings Load(PersistentSettings store)
        {
            CollisionSettings s = new CollisionSettings();
            if (store == null) return s;

            try
            {
                s.BodyRadius = Clamp(store.GetDouble(KeyBodyRadius, DefaultBodyRadius), 0.05, 1.0);
                s.BodyHeight = Clamp(store.GetDouble(KeyBodyHeight, DefaultBodyHeight), 0.5, 3.0);
                s.CollisionEnabled = store.GetBool(KeyCollision, true);
                s.ShowStats = store.GetBool(KeyShowStats, false);
                s.CollideMeshes = store.GetBool(KeyMeshes, true);
                s.CollideBreps = store.GetBool(KeyBreps, true);
                s.CollideExtrusions = store.GetBool(KeyExtrusions, true);
                s.CollideSubD = store.GetBool(KeySubD, true);
                s.CollideBlocks = store.GetBool(KeyBlocks, true);
                s.CollideLocked = store.GetBool(KeyLocked, true);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Sanderling: Failed to read collision settings; using defaults. ({0})", ex.Message);
                s.ResetToDefaults();
            }
            return s;
        }

        public void Save(PersistentSettings store)
        {
            if (store == null) return;
            store.SetDouble(KeyBodyRadius, BodyRadius);
            store.SetDouble(KeyBodyHeight, BodyHeight);
            store.SetBool(KeyCollision, CollisionEnabled);
            store.SetBool(KeyShowStats, ShowStats);
            store.SetBool(KeyMeshes, CollideMeshes);
            store.SetBool(KeyBreps, CollideBreps);
            store.SetBool(KeyExtrusions, CollideExtrusions);
            store.SetBool(KeySubD, CollideSubD);
            store.SetBool(KeyBlocks, CollideBlocks);
            store.SetBool(KeyLocked, CollideLocked);
        }

        private static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            return v < min ? min : (v > max ? max : v);
        }
    }

    internal enum CollisionLayerMode
    {
        /// <summary>All visible layers take part.</summary>
        All = 0,
        /// <summary>The selected layers (and their sub-layers) are ignored.</summary>
        Exclude = 1,
        /// <summary>Only the selected layers (and their sub-layers) take part.</summary>
        Only = 2
    }

    /// <summary>
    /// Per-document layer filter for ground detection and collision.
    /// Stored in the document's string table, so it is saved with the .3dm file.
    /// </summary>
    internal sealed class CollisionLayerFilter
    {
        private const string Section = "Sanderling";
        private const string EntryMode = "LayerFilterMode";
        private const string EntryLayers = "LayerFilterLayers";

        public CollisionLayerFilter()
        {
            Mode = CollisionLayerMode.All;
            LayerIds = new HashSet<Guid>();
        }

        public CollisionLayerMode Mode { get; set; }
        public HashSet<Guid> LayerIds { get; private set; }

        public static CollisionLayerFilter Load(RhinoDoc doc)
        {
            CollisionLayerFilter f = new CollisionLayerFilter();
            if (doc == null) return f;

            try
            {
                string mode = doc.Strings.GetValue(Section, EntryMode);
                if (mode == "Exclude") f.Mode = CollisionLayerMode.Exclude;
                else if (mode == "Only") f.Mode = CollisionLayerMode.Only;

                string layers = doc.Strings.GetValue(Section, EntryLayers);
                if (!string.IsNullOrEmpty(layers))
                {
                    foreach (string part in layers.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        Guid id;
                        if (Guid.TryParse(part, out id)) f.LayerIds.Add(id);
                    }
                }
            }
            catch (Exception)
            {
                // Missing or malformed entries fall back to "All"
            }
            return f;
        }

        public void Save(RhinoDoc doc)
        {
            if (doc == null) return;
            doc.Strings.SetString(Section, EntryMode, Mode.ToString());

            List<string> parts = new List<string>();
            foreach (Guid id in LayerIds) parts.Add(id.ToString());
            doc.Strings.SetString(Section, EntryLayers, string.Join(";", parts));
        }

        /// <summary>Human-readable summary, e.g. "Exclude 3 layers".</summary>
        public string Describe(RhinoDoc doc)
        {
            if (Mode == CollisionLayerMode.All) return "All visible layers";

            List<string> names = new List<string>();
            foreach (Guid id in LayerIds)
            {
                Layer layer = doc != null ? doc.Layers.FindId(id) : null;
                if (layer != null && !layer.IsDeleted) names.Add(layer.FullPath);
            }
            string list = names.Count > 0 ? string.Join(", ", names) : "(no layers selected)";
            return (Mode == CollisionLayerMode.Exclude ? "Exclude: " : "Only: ") + list;
        }
    }
}
