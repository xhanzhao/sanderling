using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sanderling.Settings;
using Rhino;
using Rhino.DocObjects;

namespace Sanderling.Scene
{
    /// <summary>Result of preparing the navigation geometry for a session.</summary>
    internal struct SceneStats
    {
        public int Objects;
        public int Reused;
        public int Rebuilt;
        public int Meshes;
        public int Triangles;
        public double Milliseconds;
    }

    /// <summary>
    /// Persistent per-document cache of navigation geometry.
    ///
    /// The cache survives between navigation sessions. Rhino document events mark changed objects as dirty,
    /// so the next session only re-meshes what changed. Which objects take part (visibility, layer filter,
    /// object types) is evaluated again at every session start, which is cheap and needs no re-meshing.
    ///
    ///   Rhino Geometry -> filter -> cached triangle meshes (per object) -> SpatialIndex -> real-time queries
    /// </summary>
    internal sealed class NavigationScene
    {
        private static readonly Dictionary<uint, NavigationScene> Scenes = new Dictionary<uint, NavigationScene>();
        private static bool _eventsHooked;

        private readonly Dictionary<Guid, CachedObject> _objects = new Dictionary<Guid, CachedObject>();
        private readonly HashSet<Guid> _dirty = new HashSet<Guid>();
        private bool _allDirty;

        private NavigationScene()
        {
        }

        /// <summary>Gets (or creates) the cache for a document and starts listening to document events.</summary>
        public static NavigationScene ForDocument(RhinoDoc doc)
        {
            HookEvents();

            NavigationScene scene;
            if (!Scenes.TryGetValue(doc.RuntimeSerialNumber, out scene))
            {
                scene = new NavigationScene();
                Scenes[doc.RuntimeSerialNumber] = scene;
            }
            return scene;
        }

        /// <summary>Forces every object to be re-meshed the next time the scene is prepared (SanderlingRefresh).</summary>
        public void MarkAllDirty()
        {
            _allDirty = true;
        }

        /// <summary>
        /// Builds the spatial index for a navigation session: evaluates which objects take part,
        /// re-meshes new / changed objects only, and reuses everything else.
        /// </summary>
        public SpatialIndex Prepare(RhinoDoc doc, CollisionSettings settings, CollisionLayerFilter filter, out SceneStats stats)
        {
            Stopwatch timer = Stopwatch.StartNew();
            stats = new SceneStats();

            LayerInfo layers = new LayerInfo(doc, filter);

            ObjectEnumeratorSettings enumerator = new ObjectEnumeratorSettings();
            enumerator.NormalObjects = true;
            enumerator.LockedObjects = true;
            enumerator.HiddenObjects = false;
            enumerator.ReferenceObjects = true;
            enumerator.IncludeLights = false;
            enumerator.IncludeGrips = false;
            enumerator.IncludePhantoms = false;
            enumerator.ObjectTypeFilter = ObjectType.Mesh | ObjectType.Brep | ObjectType.Extrusion
                                        | ObjectType.Surface | ObjectType.SubD | ObjectType.InstanceReference;

            List<CachedObject> included = new List<CachedObject>();

            foreach (RhinoObject obj in doc.Objects.GetObjectList(enumerator))
            {
                if (obj == null || !obj.Visible) continue;
                if (!settings.AllowsType(obj.ObjectType)) continue;

                int layerIndex = obj.Attributes.LayerIndex;
                if (!layers.IsVisible(layerIndex) || !layers.PassesFilter(layerIndex)) continue;

                bool locked = obj.Attributes.Mode == ObjectMode.Locked || layers.IsLocked(layerIndex);
                if (locked && !settings.CollideLocked) continue;

                CachedObject entry;
                bool needsBuild = _allDirty || _dirty.Contains(obj.Id) || !_objects.TryGetValue(obj.Id, out entry);
                if (needsBuild)
                {
                    try
                    {
                        entry = NavigationMeshCache.Build(obj);
                    }
                    catch (Exception)
                    {
                        // Objects that cannot be meshed are skipped; navigation must not fail because of one bad object
                        entry = new CachedObject(obj.Id, obj.ObjectType);
                    }
                    _objects[obj.Id] = entry;
                    stats.Rebuilt++;
                }
                else
                {
                    entry = _objects[obj.Id];
                    stats.Reused++;
                }

                included.Add(entry);
                stats.Objects++;
            }

            _dirty.Clear();
            _allDirty = false;
            PurgeDeleted(doc);

            SpatialIndex index = new SpatialIndex(included);
            stats.Meshes = index.MeshCount;
            stats.Triangles = index.TriangleCount;
            stats.Milliseconds = timer.Elapsed.TotalMilliseconds;
            return index;
        }

        /// <summary>Drops cache entries of objects that no longer exist in the document.</summary>
        private void PurgeDeleted(RhinoDoc doc)
        {
            List<Guid> remove = null;
            foreach (Guid id in _objects.Keys)
            {
                if (doc.Objects.FindId(id) == null)
                {
                    if (remove == null) remove = new List<Guid>();
                    remove.Add(id);
                }
            }
            if (remove != null)
            {
                foreach (Guid id in remove) _objects.Remove(id);
            }
        }

        // ------------------------------------------------------------------
        // Document events -> dirty tracking
        // ------------------------------------------------------------------

        private static void HookEvents()
        {
            if (_eventsHooked) return;
            _eventsHooked = true;

            RhinoDoc.AddRhinoObject += OnObjectChanged;
            RhinoDoc.UndeleteRhinoObject += OnObjectChanged;
            RhinoDoc.ReplaceRhinoObject += OnReplaceObject;
            RhinoDoc.InstanceDefinitionTableEvent += OnInstanceDefinitionChanged;
            RhinoDoc.CloseDocument += OnCloseDocument;
        }

        private static NavigationScene Find(RhinoDoc doc)
        {
            if (doc == null) return null;
            NavigationScene scene;
            return Scenes.TryGetValue(doc.RuntimeSerialNumber, out scene) ? scene : null;
        }

        private static void OnObjectChanged(object sender, RhinoObjectEventArgs e)
        {
            if (e == null || e.TheObject == null) return;
            NavigationScene scene = Find(e.TheObject.Document);
            if (scene != null) scene._dirty.Add(e.ObjectId);
        }

        private static void OnReplaceObject(object sender, RhinoReplaceObjectEventArgs e)
        {
            if (e == null) return;
            NavigationScene scene = Find(e.Document);
            if (scene != null) scene._dirty.Add(e.ObjectId);
        }

        private static void OnInstanceDefinitionChanged(object sender, Rhino.DocObjects.Tables.InstanceDefinitionTableEventArgs e)
        {
            if (e == null) return;
            NavigationScene scene = Find(e.Document);
            if (scene == null) return;

            // Block geometry is flattened into each instance entry, so every instance must be rebuilt
            foreach (CachedObject entry in scene._objects.Values)
            {
                if (entry.Type == ObjectType.InstanceReference) scene._dirty.Add(entry.Id);
            }
        }

        private static void OnCloseDocument(object sender, DocumentEventArgs e)
        {
            if (e == null) return;
            Scenes.Remove(e.DocumentSerialNumber);
        }

        // ------------------------------------------------------------------
        // Layer visibility / lock / filter, resolved once per session (including parent layers)
        // ------------------------------------------------------------------

        private sealed class LayerInfo
        {
            private readonly bool[] _visible;
            private readonly bool[] _locked;
            private readonly bool[] _passes;

            public LayerInfo(RhinoDoc doc, CollisionLayerFilter filter)
            {
                int count = doc.Layers.Count;
                _visible = new bool[count];
                _locked = new bool[count];
                _passes = new bool[count];

                for (int i = 0; i < count; i++)
                {
                    Layer layer = doc.Layers[i];
                    if (layer == null || layer.IsDeleted) continue;

                    bool visible = true, locked = false, inSet = false;
                    Layer current = layer;
                    int guard = 0;
                    while (current != null && guard++ < 64)
                    {
                        if (!current.IsVisible) visible = false;
                        if (current.IsLocked) locked = true;
                        if (filter.LayerIds.Contains(current.Id)) inSet = true;
                        if (current.ParentLayerId == Guid.Empty) break;
                        current = doc.Layers.FindId(current.ParentLayerId);
                    }

                    _visible[i] = visible;
                    _locked[i] = locked;
                    switch (filter.Mode)
                    {
                        case CollisionLayerMode.Exclude: _passes[i] = !inSet; break;
                        case CollisionLayerMode.Only: _passes[i] = inSet; break;
                        default: _passes[i] = true; break;
                    }
                }
            }

            public bool IsVisible(int index) { return index >= 0 && index < _visible.Length && _visible[index]; }
            public bool IsLocked(int index) { return index >= 0 && index < _locked.Length && _locked[index]; }
            public bool PassesFilter(int index) { return index >= 0 && index < _passes.Length && _passes[index]; }
        }
    }
}
