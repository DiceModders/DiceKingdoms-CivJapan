// Japan as its own civilization through ModCore; the look-alike civilization (Viking) stays untouched.
// ModCore.Civilizations.Add registers it (id from the key, biome borrowed from the look-alike). When the game data has loaded, the look-alike's
// building / unit / ship / ragdoll prefabs are COPIED (ModCore.Tools.Prefabs), our models go on the copies, the copies' ragdoll references point to our
// ragdoll copies, and the copies are handed to civ.Skins. The game then picks them itself for Japanese players.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using DiceKingdoms.Game;
using DiceKingdoms.Interface;
using DiceModders.ModCore;
using Core = DiceModders.ModCore.ModCore;
using Tools = DiceModders.ModCore.Tools;

namespace DiceModders.CivJapan;

internal static class JapanCiv
{
    internal static ModCivilization Civ;
    static TechTree _tree;

    public static void Register()
    {
        string en = Japan.Names.TryGetValue("en", out var n) ? n : "Japan";
        Civ = Core.Civilizations.Add(Japan.CivKey, en, File.Exists(Japan.TechTreeFile) ? (Func<TechTree>)Tree : null, Japan.CivId);
        if (Japan.Names.Count > 0) Core.Civilizations.Rename(Civ.Id, en, Japan.Names);
        foreach (var kv in Japan.Texts)   // unit / building / technology texts shown while the local player plays Japan
            Core.Civilizations.SetText(Civ.Id, kv.Key, kv.Value.TryGetValue("en", out var t) ? t : new List<string>(kv.Value.Values)[0], kv.Value);
        foreach (var (x, y, c) in Japan.GroundCells) Civ.Skins.GroundColour(x, y, c);
        foreach (var (x, y, c) in Japan.NatureCells) Civ.Skins.NatureColour(x, y, c);
        // read the cache files in the background while the game is still loading
        Tools.TextureCache.Prefetch(Civ.Name + " Text");
        Tools.DiskCache.Prefetch(Tools.DiskCache.FilesOf(Models.CacheFolder));
        if (File.Exists(Japan.BuildOrdersFile))
            Japan.W(Core.Civilizations.AddBuildOrders(Civ.Id, File.ReadAllText(Japan.BuildOrdersFile)) + " AI build orders read");
        Core.Civilizations.WhenGameDataLoaded(Build);
        Japan.W("'" + en + "' registered as civilization " + Civ.Id + " (key " + Japan.CivKey + ", looks like " + Japan.CivEnum + ")");
    }

    static TechTree Tree()
    {
        if (_tree != null) return _tree;
        try { _tree = JapanTree.BuildCopy(); } catch (Exception e) { Japan.Once("tree", "tech tree FAIL " + e); }
        return _tree;
    }

    /// <summary>Once, when the game data has loaded.</summary>
    static void Build()
    {
        var t0 = DateTime.Now;
        var registries = Resources.FindObjectsOfTypeAll<SkinRegistry>();
        if (registries.Length == 0) { Japan.W("no skin registry"); return; }
        var look = new PlayerSkins((DiceKingdoms.Interface.Civilization)Japan.CivId, registries[0]);
        string word = "-" + Swapper.Kebab(Japan.CivWord);

        // ragdolls first: units and inhabitants point to them
        var ragdolls = new Dictionary<IntPtr, Ragdoll>();
        foreach (var r in Resources.FindObjectsOfTypeAll<Ragdoll>())
        {
            if (r.gameObject.scene.IsValid() || !r.gameObject.name.EndsWith(word)) continue;
            var copy = Tools.Prefabs.Copy(r);
            Swapper.Other(copy.gameObject, "civ");
            ragdolls[r.Pointer] = copy;
        }

        int b = 0, u = 0;
        var seen = new HashSet<IntPtr>();
        var pictures = new List<Picture>();
        var typeSets = Resources.FindObjectsOfTypeAll<BuildingTypes>();
        if (typeSets.Length == 0) { Japan.W("no BuildingTypes asset"); return; }
        foreach (var bt in typeSets[0].Types)
        {
            if (bt == null) continue;
            BuildingVisual prefab;
            try { prefab = look.GetBuildingVisualPrefab(bt); } catch { continue; }
            if (prefab == null || !seen.Add(prefab.Pointer) || !Swapper.BaseName(prefab.gameObject.name).EndsWith(" " + Japan.CivWord)) continue;
            var copy = Tools.Prefabs.Copy(prefab);
            Swapper.Building(copy, "civ");
            Relink(copy.gameObject, ragdolls);
            Civ.Skins.Building(bt.name, copy);
            if (HasModel(prefab)) pictures.Add(new Picture(prefab.icons, copy.gameObject, s => copy.icons = s, false));
            b++;
        }
        foreach (Unit.Type type in Enum.GetValues(typeof(Unit.Type)))
        {
            UnitVisual prefab;
            try { prefab = look.GetUnitVisualPrefab(type); } catch { continue; }
            if (prefab == null) continue;
            var copy = Tools.Prefabs.Copy(prefab);
            Swapper.Other(copy.gameObject, "civ");
            Relink(copy.gameObject, ragdolls);
            if (Japan.ModelMap.ContainsKey(Swapper.Kebab(type.ToString()) + word))
                pictures.Add(new Picture(prefab.icons, copy.gameObject, s => copy.icons = s, true));
            Civ.Skins.Unit((int)type, copy);
            u++;
        }
        try
        {
            var ship = look.GetShipVisualPrefab();
            if (ship != null) { var copy = Tools.Prefabs.Copy(ship); Swapper.Other(copy.gameObject, "civ"); Civ.Skins.Ship(copy); }
        }
        catch (Exception e) { Japan.W("ship FAIL " + e.Message); }

        RenderPictures(pictures);
        Japan.W(b + " buildings, " + u + " units, ship, " + ragdolls.Count + " ragdolls copied; " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms");
    }

    sealed record Picture(Il2CppReferenceArray<Sprite> Original, GameObject Copy, Action<Il2CppReferenceArray<Sprite>> Set, bool Unit);

    /// <summary>A building of the look-alike that gets our model ("House Viking" -> "house-viking" in the model map).</summary>
    static bool HasModel(BuildingVisual original)
    {
        var b = Swapper.BaseName(original.gameObject.name);
        if (b.Length <= Japan.CivWord.Length) return false;
        var key = Swapper.Kebab(b.Substring(0, b.Length - Japan.CivWord.Length - 1)) + "-" + Swapper.Kebab(Japan.CivWord);
        return Japan.ModelMap.ContainsKey(key);
    }

    /// <summary>
    /// Pictures of our buildings and units taken in the game (ModCore icon renderer, cached after the first launch): they go on the copies, named like the
    /// originals with our token; when all are there, the pictures inside texts are made from them (one sheet, cached under the pictures' keys).
    /// </summary>
    static void RenderPictures(List<Picture> list)
    {
        int left = list.Count;
        var texts = new List<Tools.TextSprites.Entry>();
        var names = new List<string>();
        foreach (var p in list)
        {
            if (p.Original == null || p.Original.Length == 0 || p.Original[0] == null) { left--; continue; }
            var first = p.Original[0].rect;
            string name = Japan.CivKey + " " + p.Copy.name;
            names.Add(name);
            var item = p;
            Tools.IconRenderer.PlayerColours(name, p.Copy, p.Unit ? Tools.IconView.Unit : Tools.IconView.Building,
                Mathf.RoundToInt(first.width), Mathf.RoundToInt(first.height), sprites =>
            {
                int n = Math.Min(sprites.Length, item.Original.Length);
                var ours = new Il2CppReferenceArray<Sprite>(item.Original.Length);
                for (int i = 0; i < item.Original.Length; i++)
                {
                    var o = item.Original[i];
                    if (i >= n || o == null) { ours[i] = o; continue; }
                    var s = sprites[i];
                    s.name = SpriteName(o.name);
                    ours[i] = s;
                    texts.Add(new Tools.TextSprites.Entry(s.name, o.name, () => Tools.Sprites.ReadableCopy(s)));
                }
                item.Set(ours);
                if (--left == 0)
                {
                    var key = new System.Text.StringBuilder();
                    foreach (var k in names) key.Append(Tools.IconRenderer.CacheKey(k)).Append(';');
                    int pics = Tools.TextSprites.AddLike(Civ.Name + " Text", texts, key.ToString().GetHashCode().ToString("X8"));
                    Japan.W(list.Count + " picture sets taken in the game, " + pics + " text pictures");
                }
            });
        }
    }

    /// <summary>Every ReplaceWithRagdoll in the copy (the unit itself, inhabitants inside buildings) points to our ragdoll copy.</summary>
    static void Relink(GameObject go, Dictionary<IntPtr, Ragdoll> ragdolls)
    {
        foreach (var r in go.GetComponentsInChildren<ReplaceWithRagdoll>(true))
            if (r.ragdollPrefab != null && ragdolls.TryGetValue(r.ragdollPrefab.Pointer, out var ours)) r.ragdollPrefab = ours;
    }

    /// <summary>The sprite name for our picture: the original's with the civilization token swapped ("...-viking-red" -> "...-japan-red").</summary>
    internal static string SpriteName(string original)
    {
        if (original == null) return null;
        var v = "-" + Swapper.Kebab(Japan.CivWord) + "-";
        int at = original.LastIndexOf(v, StringComparison.Ordinal);
        return at < 0 ? original + "-" + Japan.Token : original.Substring(0, at) + "-" + Japan.Token + "-" + original.Substring(at + v.Length);
    }
}
