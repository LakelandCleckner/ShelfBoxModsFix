// Shelf and Box Mods Fix
// Author: JustChoochit
// Compatibility fixes for "What's in the Box?" and "What's on the Shelf?" on the current
// TCG Card Shop Simulator build. Runtime Harmony patches only; the original mods are not modified.

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShelfBoxModsFix
{
    [BepInPlugin(Guid, "Shelf and Box Mods Fix", "1.0.0")]
    [BepInDependency("james.tcgsim.whatsinthebox", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("james.tcgsim.whatsontheshelf", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "justchoochit.shelfboxmodsfix";
        internal static ManualLogSource Log;
        internal static BepInEx.Configuration.ConfigEntry<bool> Use3D;
        internal static BepInEx.Configuration.ConfigEntry<float> Yaw, Pitch;
        internal static BepInEx.Configuration.ConfigEntry<int> Res;

        void Awake()
        {
            Log = Logger;
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("tcgsim.shelfmodsfix") ||
                BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("local.shelfmodsfix"))
            {
                Log.LogError("An old ShelfModsFix test build is also installed. Delete BepInEx\\plugins\\ShelfModsFix and " +
                             "BepInEx\\patchers\\ShelfModsFix. Shelf and Box Mods Fix is disabled until then to avoid double-patching.");
                return;
            }
            Use3D = Config.Bind("Product picture", "Use3DRender", true,
                "Render the product's 3D model for the panel picture. Off = use the product image / price tag picture.");
            Yaw = Config.Bind("Product picture", "Yaw", 0f,
                new BepInEx.Configuration.ConfigDescription("Turn the model left/right (degrees). Use 180 if you see the back of a pack.",
                    new BepInEx.Configuration.AcceptableValueRange<float>(-180f, 180f)));
            Pitch = Config.Bind("Product picture", "Pitch", 12f,
                new BepInEx.Configuration.ConfigDescription("Look down at the model (degrees).",
                    new BepInEx.Configuration.AcceptableValueRange<float>(-60f, 60f)));
            Res = Config.Bind("Product picture", "Resolution", 512,
                new BepInEx.Configuration.ConfigDescription("Render size in pixels.", new BepInEx.Configuration.AcceptableValueRange<int>(128, 1024)));
            System.EventHandler clear = (_, __) => ModelRender.ClearCache();
            Yaw.SettingChanged += clear; Pitch.SettingChanged += clear; Res.SettingChanged += clear; Use3D.SettingChanged += clear;
            if (AccessTools.Field(typeof(UI_PriceTag), "m_Icon") == null)
                Log.LogWarning("UI_PriceTag.m_Icon is missing: install ShelfBoxModsFix.Patcher.dll in BepInEx\\patchers, or both mods will fail on product shelves and boxes.");
            var h = new Harmony(Guid);
            int n = 0;
            n += TryPatch(h, "WhatsInTheBox.BoxReader:GetTarget", prefix: nameof(Patches.GetTargetPrefix));
            n += TryPatch(h, "WhatsInTheBox.BoxReader:Read", postfix: nameof(Patches.ArtworkPostfix));
            n += TryPatch(h, "WhatsOnTheShelf.ShelfReader:Read", postfix: nameof(Patches.ArtworkPostfix));
            n += TryPatch(h, "WhatsInTheBox.StockReader:CountStorage", postfix: nameof(Patches.CountStoragePostfix));
            n += TryPatch(h, "WhatsOnTheShelf.StockReader:CountStorage", postfix: nameof(Patches.CountStoragePostfix));
            Log.LogInfo($"Shelf and Box Mods Fix loaded: {n} patches applied.");
        }

        static int TryPatch(Harmony h, string target, string prefix = null, string postfix = null)
        {
            try
            {
                var m = AccessTools.Method(target);
                if (m == null) { Log.LogWarning("Not found (mod missing?): " + target); return 0; }
                h.Patch(m,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix));
                return 1;
            }
            catch (Exception e) { Log.LogError("Patch failed for " + target + ": " + e); return 0; }
        }
    }

    static class Patches
    {
        static readonly AccessTools.FieldRef<InteractionPlayerController, InteractablePackagingBox_Item> HeldBox =
            AccessTools.FieldRefAccess<InteractionPlayerController, InteractablePackagingBox_Item>("m_CurrentHoldingItemBox");
        static readonly AccessTools.FieldRef<InteractionPlayerController, InteractablePackagingBox_Item> HoverBox =
            AccessTools.FieldRefAccess<InteractionPlayerController, InteractablePackagingBox_Item>("m_CurrentRaycastedPackageBoxItem");
        static readonly FieldInfo HoverObject = AccessTools.Field(typeof(InteractionPlayerController), "m_CurrentRaycastObject");
        static readonly FieldInfo StorageField = AccessTools.Field(typeof(InteractionPlayerController), "m_CurrentStorageCompartment");
        static readonly FieldInfo CompItemType = AccessTools.Field(typeof(ShelfCompartment), "m_ItemType");
        static readonly FieldInfo CompAmount = AccessTools.Field(typeof(ShelfCompartment), "m_ItemAmount");
        static readonly FieldInfo CompMax = AccessTools.Field(typeof(ShelfCompartment), "m_MaxItemCount");
        static readonly FieldInfo BoxOpened = AccessTools.Field(typeof(InteractablePackagingBox_Item), "m_IsBoxOpened");

        // ---- Boxes stored on storage shelves are now data records, not GameObjects. ----
        // Present the record the player would take next through a hidden, inactive proxy box.
        static GameObject _proxyGo;
        static InteractablePackagingBox_Item _proxyBox;
        static ShelfCompartment _proxyComp;

        static InteractablePackagingBox_Item Proxy(StoredBoxRecord rec)
        {
            if (!_proxyBox)
            {
                _proxyGo = new GameObject("ShelfBoxModsFix_StoredBoxProxy");
                _proxyGo.SetActive(false);                 // never Awake/OnEnable
                _proxyGo.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(_proxyGo);
                _proxyComp = _proxyGo.AddComponent<ShelfCompartment>();
                _proxyBox = _proxyGo.AddComponent<InteractablePackagingBox_Item>();
            }
            CompItemType.SetValue(_proxyComp, rec.itemType);
            CompAmount.SetValue(_proxyComp, rec.amount);
            CompMax.SetValue(_proxyComp, MaxFor(rec.itemType, rec.isBigBox));
            _proxyBox.m_ItemCompartment = _proxyComp;
            _proxyBox.m_IsBigBox = rec.isBigBox;
            _proxyBox.m_IsStored = true;
            BoxOpened?.SetValue(_proxyBox, false);
            return _proxyBox;
        }

        // Box capacity is geometric (computed when a real box is filled), so learn it from real boxes.
        static readonly Dictionary<long, int> MaxCache = new Dictionary<long, int>();
        static float _nextScan;
        static long Key(EItemType t, bool big) => ((long)(int)t << 1) | (big ? 1L : 0L);

        static void Learn(InteractablePackagingBox_Item box)
        {
            if (!box || box == _proxyBox || !box.m_ItemCompartment) return;
            try
            {
                var c = box.m_ItemCompartment;
                var t = c.GetItemType();
                if ((int)t < 0) return;
                int max = (int)CompMax.GetValue(c);
                if (max > 0) MaxCache[Key(t, box.m_IsBigBox)] = max;
            }
            catch { }
        }

        static int MaxFor(EItemType t, bool big)
        {
            if (MaxCache.TryGetValue(Key(t, big), out int m)) return m;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 3f;
                foreach (var b in Object.FindObjectsByType<InteractablePackagingBox_Item>(FindObjectsSortMode.None)) Learn(b);
                if (MaxCache.TryGetValue(Key(t, big), out m)) return m;
            }
            int measured = Measure(t, big);
            if (measured > 0) { MaxCache[Key(t, big)] = measured; return measured; }
            try
            {
                var list = InventoryBase.GetRestockDataUsingItemType(t);
                if (list != null)
                    foreach (var rd in list)
                        if (rd != null && rd.isBigBox == big && rd.amount > 0) return rd.amount;
            }
            catch { }
            return -1; // mod displays "?"
        }

        // Builds a throwaway copy of the box prefab under an inactive parent (so no Awake/OnEnable runs),
        // sizes its compartment for the product exactly like FillBoxWithItem does, and reads the capacity.
        static Transform _measureHolder;
        static readonly HashSet<long> MeasureFailed = new HashSet<long>();
        static int Measure(EItemType t, bool big)
        {
            if (MeasureFailed.Contains(Key(t, big))) return -1;
            InteractablePackagingBox_Item inst = null;
            try
            {
                var prefab = RestockManager.GetPackageBoxPrefab(big);
                if (!prefab) return -1;
                if (!_measureHolder)
                {
                    var holder = new GameObject("ShelfBoxModsFix_MeasureHolder");
                    holder.SetActive(false);
                    holder.hideFlags = HideFlags.HideAndDontSave;
                    Object.DontDestroyOnLoad(holder);
                    _measureHolder = holder.transform;
                }
                inst = Object.Instantiate(prefab, _measureHolder);
                var c = inst.m_ItemCompartment;
                if (!c) { MeasureFailed.Add(Key(t, big)); return -1; }
                CompMax.SetValue(c, 0);
                // On a never-awakened copy the price-tag and position lists are null or empty, so these throw
                // partway through. Item size and m_MaxItemCount are written before those list accesses,
                // so the capacity is still valid afterwards.
                try { c.SetCompartmentItemType(t); } catch (Exception) { }
                try { c.CalculatePositionList(); } catch (Exception) { }
                int max = (int)CompMax.GetValue(c);
                if (max <= 0) MeasureFailed.Add(Key(t, big));
                return max;
            }
            catch (Exception e)
            {
                MeasureFailed.Add(Key(t, big));
                WarnOnce("measure", "Box capacity measure failed: " + e.Message);
                return -1;
            }
            finally
            {
                if (inst) Object.Destroy(inst.gameObject);
            }
        }

        // ---- Product pictures for EPL custom products: crop them from EPL's price-tag atlas. ----
        // EPL lays cell i at column i % grid, row i / grid counted from the top of the texture.
        static readonly AccessTools.FieldRef<UI_PriceTag, int> TagCell =
            AccessTools.FieldRefAccess<UI_PriceTag, int>("m_IconIndexCell");
        static UI_PriceTag[] _tags = new UI_PriceTag[0];
        static float _nextTagScan;
        static readonly Dictionary<long, Sprite> SpriteCache = new Dictionary<long, Sprite>();

        static Sprite AtlasSprite(int itemType)
        {
            if (Time.unscaledTime >= _nextTagScan || _tags.Length == 0)
            {
                _nextTagScan = Time.unscaledTime + 5f;
                _tags = Object.FindObjectsByType<UI_PriceTag>(FindObjectsSortMode.None);
            }
            UI_PriceTag src = null; int idx = -1;
            foreach (var tag in _tags)   // a tag already showing this product: use its own cell
                if (tag && tag.m_InstancedRenderer && (int)tag.GetItemType() == itemType && TagCell(tag) >= 0)
                { src = tag; idx = TagCell(tag); break; }
            if (!src)
                foreach (var tag in _tags)
                    if (tag && tag.m_InstancedRenderer && tag.m_AtlasMap)
                    {
                        int i = tag.m_AtlasMap.GetItemIconIndex(itemType);
                        if (i >= 0) { src = tag; idx = i; break; }
                    }
            if (!src) return null;

            var mat = src.m_InstancedRenderer.sharedMaterial;
            var tex = mat ? mat.mainTexture as Texture2D : null;
            if (!tex) return null;
            long key = ((long)tex.GetInstanceID() << 20) ^ idx;
            if (SpriteCache.TryGetValue(key, out var s) && s) return s;

            int grid = mat.HasProperty("_AtlasGridSize") ? Mathf.Max(1, Mathf.RoundToInt(mat.GetFloat("_AtlasGridSize"))) : 32;
            int cell = tex.width / grid;
            int col = idx % grid, row = idx / grid;
            if (row >= grid) return null;
            var rect = new Rect(col * cell, tex.height - (row + 1) * cell, cell, cell);
            s = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = "ShelfBoxModsFix_" + itemType;
            SpriteCache[key] = s;
            return s;
        }

        // Full-resolution source image EPL keeps for each custom product
        // (EplRuntimeData.Assets.ItemLibrary.ItemData[itemType].Icon; all internal, so via reflection).
        static System.Collections.IDictionary _eplItems;
        static Type _eplKeyType;
        static bool _eplUnavailable;
        static readonly Dictionary<int, Sprite> EplIconCache = new Dictionary<int, Sprite>();

        static Sprite EplIcon(int id)
        {
            if (EplIconCache.TryGetValue(id, out var cached) && cached) return cached;
            if (_eplUnavailable) return null;
            try
            {
                if (_eplItems == null)
                {
                    var rt = AccessTools.TypeByName("EnhancedPrefabLoader.Core.EplRuntimeData");
                    if (rt == null) { _eplUnavailable = true; return null; } // EPL not installed
                    object assets = AccessTools.Property(rt, "Assets")?.GetValue(null);
                    if (assets == null) return null;                           // not loaded yet; retry later
                    object lib = AccessTools.Property(assets.GetType(), "ItemLibrary")?.GetValue(assets);
                    if (lib == null) return null;
                    _eplItems = AccessTools.Property(lib.GetType(), "ItemData")?.GetValue(lib) as System.Collections.IDictionary;
                    if (_eplItems == null)
                    {
                        _eplUnavailable = true;
                        WarnOnce("eplicon", "EPL item library has an unexpected shape; using price tag pictures.");
                        return null;
                    }
                    var args = _eplItems.GetType().GetGenericArguments();
                    _eplKeyType = args.Length > 0 ? args[0] : typeof(int);
                }
                object key = _eplKeyType.IsEnum ? Enum.ToObject(_eplKeyType, id)
                           : _eplKeyType == typeof(string) ? id.ToString()
                           : Convert.ChangeType(id, _eplKeyType);
                if (!_eplItems.Contains(key)) return null;
                object data = _eplItems[key];
                if (data == null) return null;
                var icon = AccessTools.Property(data.GetType(), "Icon")?.GetValue(data) as Sprite;
                if (!icon) return null;
                EplIconCache[id] = icon;
                return icon;
            }
            catch (Exception e)
            {
                _eplUnavailable = true;
                WarnOnce("eplicon", "EPL full-size icon lookup failed, using price tag pictures: " + e);
                return null;
            }
        }

        static readonly HashSet<int> ArtLogged = new HashSet<int>();

        // Postfix for both WhatsInTheBox.BoxReader.Read and WhatsOnTheShelf.ShelfReader.Read.
        public static void ArtworkPostfix(object __result)
        {
            if (__result == null) return;
            try
            {
                var tr = Traverse.Create(__result);
                if (tr.Field("Artwork").GetValue<Sprite>()) return;
                int id = tr.Field("ItemTypeId").GetValue<int>();
                if (id < 0) return;
                var model = Plugin.Use3D.Value ? ModelRender.Get(id) : null;
                var epl = model ? null : EplIcon(id);
                var sprite = model ?? epl ?? AtlasSprite(id);
                if (ArtLogged.Count < 60 && ArtLogged.Add(id))
                    Plugin.Log.LogInfo(sprite
                        ? $"Picture for item {id}: {(model ? "3D render" : epl ? "EPL product image" : "price tag crop")} {sprite.rect.width}x{sprite.rect.height} (texture {sprite.texture.width}x{sprite.texture.height})"
                        : $"Picture for item {id}: none found");
                if (!sprite) return;
                tr.Field("Artwork").SetValue(sprite);
                tr.Field("ArtworkTint").SetValue(Color.white);
                tr.Field("ArtworkSource").SetValue("price tag picture");
            }
            catch (Exception e) { WarnOnce("art", "Atlas artwork failed: " + e.Message); }
        }

        // Replaces WhatsInTheBox.BoxReader.GetTarget: same order as before, but safe on empty
        // box lists and aware of stored-box records (same pick order as the game's Take Box).
        public static bool GetTargetPrefix(InteractionPlayerController player, ref InteractablePackagingBox_Item __result)
        {
            try
            {
                __result = null;
                if (!player) return false;
                if (player.m_CurrentGameState == EGameState.HoldingBoxState)
                {
                    __result = HeldBox(player);
                    Learn(__result);
                    return false;
                }
                var hover = HoverBox(player);
                if (hover) { Learn(hover); __result = hover; return false; }
                var obj = HoverObject.GetValue(player) as InteractablePackagingBox_Item;
                if (obj) { Learn(obj); __result = obj; return false; }

                if (player.m_CurrentGameState != EGameState.DefaultState) return false;
                var storage = StorageField.GetValue(player) as InteractableStorageCompartment;
                if (!storage) return false;
                var sc = storage.GetShelfCompartment();
                if (!sc) return false;

                var list = sc.GetInteractablePackagingBoxList();
                if (list != null)
                    for (int i = list.Count - 1; i >= 0; i--)
                        if (list[i]) { Learn(list[i]); __result = list[i]; return false; }

                int n = sc.GetStoredBoxRecordCount();
                if (n > 0) __result = Proxy(sc.PeekStoredBoxRecord(n - 1));
                return false;
            }
            catch (Exception e)
            {
                WarnOnce("target", "GetTarget fallback failed: " + e);
                __result = null;
                return false;
            }
        }

        // Adds stored-box records to the "In Storage" totals of both mods.
        public static void CountStoragePostfix(ShelfManager manager, EItemType type, ref long __result)
        {
            try
            {
                if (!manager || manager.m_WarehouseShelfList == null) return;
                var seen = new HashSet<int>();
                long add = 0;
                foreach (var shelf in manager.m_WarehouseShelfList)
                {
                    if (!shelf) continue;
                    var comps = shelf.GetStorageCompartmentList();
                    if (comps == null) continue;
                    foreach (var isc in comps)
                    {
                        if (!isc) continue;
                        var sc = isc.GetShelfCompartment();
                        if (!sc || !seen.Add(sc.GetInstanceID())) continue;
                        int n = sc.GetStoredBoxRecordCount();
                        for (int i = 0; i < n; i++)
                        {
                            var r = sc.PeekStoredBoxRecord(i);
                            if (r.itemType == type && r.amount > 0) add += r.amount;
                        }
                    }
                }
                __result += add;
            }
            catch (Exception e) { WarnOnce("count", "Storage record count failed: " + e); }
        }

        static readonly HashSet<string> Warned = new HashSet<string>();
        static void WarnOnce(string key, string msg) { if (Warned.Add(key)) Plugin.Log.LogWarning(msg); }
    }

    // Renders a product's 3D model (InventoryBase.GetItemMeshData, which EPL also serves) once with a
    // hidden orthographic camera, and caches the result as a Sprite for the panels.
    static class ModelRender
    {
        const int Layer = 31;
        static readonly Color Bg = new Color(0.075f, 0.09f, 0.125f, 1f); // close to the panels' picture box
        static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();
        static readonly HashSet<int> Failed = new HashSet<int>();
        static readonly HashSet<string> Warned = new HashSet<string>();
        static GameObject _rig, _model, _second;
        static Camera _cam;
        static Light _light;
        // Same shader property the game sets in ItemMeshData.ApplyToRenderer.
        static readonly int TexIndexId = AccessTools.Field(typeof(ItemMeshData), "ID_TexIndex")?.GetValue(null) is int v
            ? v : Shader.PropertyToID("_TexIndex");

        public static void ClearCache()
        {
            foreach (var sp in Cache.Values) if (sp) { Object.Destroy(sp.texture); Object.Destroy(sp); }
            Cache.Clear(); Failed.Clear();
        }

        public static Sprite Get(int id)
        {
            if (Cache.TryGetValue(id, out var sp) && sp) return sp;
            if (Failed.Contains(id)) return null;
            try
            {
                sp = Render(id);
                if (sp) Cache[id] = sp; else Failed.Add(id);
                return sp;
            }
            catch (Exception e)
            {
                Failed.Add(id);
                if (Warned.Add(e.GetType().Name)) Plugin.Log.LogWarning($"3D render failed for item {id}, using flat picture: {e}");
                return null;
            }
        }

        static void EnsureRig()
        {
            if (_rig) return;
            _rig = new GameObject("ShelfBoxModsFix_PreviewRig") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(_rig);
            _rig.transform.position = new Vector3(0f, -10000f, 0f);

            var camGo = new GameObject("Cam"); camGo.transform.SetParent(_rig.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Bg;
            _cam.cullingMask = 1 << Layer;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;

            var lightGo = new GameObject("Light"); lightGo.transform.SetParent(_rig.transform, false);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.intensity = 1.1f;
            _light.cullingMask = 1 << Layer;
            _light.shadows = LightShadows.None;
            _light.enabled = false;

            _model = Part("Model");
            _second = Part("Secondary");
            _rig.SetActive(false);
        }

        static GameObject Part(string name)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(_rig.transform, false);
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        static bool Setup(GameObject go, Mesh mesh, Material mat, List<Material> list, int texIndex, bool arrayType)
        {
            var mf = go.GetComponent<MeshFilter>(); var mr = go.GetComponent<MeshRenderer>();
            mr.SetPropertyBlock(null);
            if (!mesh) { go.SetActive(false); return false; }
            mf.sharedMesh = mesh;
            if (list != null && list.Count > 1 && mesh.subMeshCount > 1) mr.sharedMaterials = list.ToArray();
            else if (mat) mr.sharedMaterials = new[] { mat };
            else if (list != null && list.Count > 0) mr.sharedMaterials = list.ToArray();
            else { go.SetActive(false); return false; }
            if (arrayType)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetFloat(TexIndexId, texIndex);
                mr.SetPropertyBlock(mpb);
            }
            go.SetActive(true);
            return true;
        }

        static Sprite Render(int id)
        {
            var data = InventoryBase.GetItemMeshData((EItemType)id);
            if (data == null || !data.mesh) return null;
            EnsureRig();
            bool arrayType = Convert.ToInt32(data.arrayMaterialType) != 0;
            if (!Setup(_model, data.mesh, data.material, data.materialList, data.textureIndex, arrayType)) return null;
            Setup(_second, data.meshSecondary, data.materialSecondary, null, data.secondTextureIndex, arrayType);

            _rig.SetActive(true);
            var rot = Quaternion.Euler(0f, Plugin.Yaw.Value, 0f);
            _model.transform.localRotation = rot; _second.transform.localRotation = rot;

            var b = _model.GetComponent<MeshRenderer>().bounds;
            if (_second.activeSelf) b.Encapsulate(_second.GetComponent<MeshRenderer>().bounds);
            float radius = Mathf.Max(b.extents.magnitude, 0.0001f);

            var view = Quaternion.Euler(Plugin.Pitch.Value, 0f, 0f); // look at the model's -Z face (pack fronts)
            _cam.transform.rotation = view;
            _cam.transform.position = b.center - view * Vector3.forward * (radius * 4f);
            _cam.nearClipPlane = radius * 0.5f;
            _cam.farClipPlane = radius * 8f;
            // fit the projected extents tightly
            Vector3 c = b.center, e = b.extents; float maxX = 0f, maxY = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                var p = _cam.transform.InverseTransformPoint(corner);
                maxX = Mathf.Max(maxX, Mathf.Abs(p.x)); maxY = Mathf.Max(maxY, Mathf.Abs(p.y));
            }
            _cam.orthographicSize = Mathf.Max(maxX, maxY) * 1.08f;
            _light.transform.rotation = view * Quaternion.Euler(25f, -30f, 0f);
            _light.enabled = true;

            int size = Plugin.Res.Value;
            var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = QualitySettings.antiAliasing > 1 ? QualitySettings.antiAliasing : 4;
            var prevActive = RenderTexture.active;
            bool fog = RenderSettings.fog;
            Texture2D tex = null;
            try
            {
                RenderSettings.fog = false;
                _cam.targetTexture = rt;
                _cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ShelfBoxModsFix_" + id };
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                var px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i].a = 255; // shader alpha is unreliable; background is opaque
                tex.SetPixels32(px);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply(false, true);
            }
            finally
            {
                RenderSettings.fog = fog;
                _cam.targetTexture = null;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                _light.enabled = false;
                _rig.SetActive(false);
            }
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = tex.name;
            return sprite;
        }
    }
}
