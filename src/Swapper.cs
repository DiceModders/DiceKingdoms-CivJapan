// Puts our models on buildings, units, inhabitants, ragdolls and ships.
// Pieces are matched by the ORIGINAL mesh name ("<group>-mesh" / "<group>-mesh_LODn"), because the game clones some pieces
// (wall sides) and renames the objects. Prefab assets are swapped too, so every later copy (pool, ragdoll, lobby) starts with our meshes.
using System;
using System.Collections.Generic;
using UnityEngine;
using DiceKingdoms.Game;

namespace DiceModders.CivJapan;

internal static class Swapper
{
    static readonly string[] Civs = { "Europe", "Middle East", "Viking", "Ancient", "Maya" };

    // per root: one renderer and the mesh it had after our last look; unchanged = nothing to do
    static readonly Dictionary<int, (MeshFilter mf, SkinnedMeshRenderer smr, IntPtr mesh)> Seen = new();
    static readonly HashSet<string> Described = new();
    static readonly HashSet<int> Logged = new();

    internal static string Kebab(string s) => s.ToLower().Replace(' ', '-');

    /// <summary>"House Viking", "House Viking (1)", pooled "House Viking &lt;12&gt; #0003", "...(Clone)" -> "House Viking".</summary>
    internal static string BaseName(string n)
    {
        foreach (var cut in new[] { " <", " (", "(" }) { var i = n.IndexOf(cut); if (i >= 0) n = n.Substring(0, i); }
        return n.Trim();
    }

    static string GroupOf(string n)
    {
        if (n == null) return null;
        int i = n.IndexOf("-mesh_LOD");
        if (i > 0) return n.Substring(0, i);
        if (n.EndsWith("-mesh")) return n.Substring(0, n.Length - 5);
        return null;
    }

    static string Piece(string group, string mainGroup, string mainModel)
    {
        if (mainModel == null || !group.StartsWith(mainGroup + "-")) return null;
        var m = mainModel + "-" + group.Substring(mainGroup.Length + 1);
        return Models.Exists(m) ? m : null;
    }

    /// <summary>A model from the overlay map by group, or a piece file of a mapped group.</summary>
    static string FromMap(string g)
    {
        if (Japan.ModelMap.TryGetValue(g, out var m)) return Models.Exists(m) ? m : null;
        foreach (var kv in Japan.ModelMap)
        {
            var p = Piece(g, kv.Key, kv.Value);
            if (p != null) return p;
        }
        return null;
    }

    static bool Unchanged(GameObject go)
    {
        if (!Seen.TryGetValue(go.GetInstanceID(), out var s)) return false;
        if (s.mf != null) return s.mf.sharedMesh != null ? s.mf.sharedMesh.Pointer == s.mesh : s.mesh == IntPtr.Zero;
        if (s.smr != null) return s.smr.sharedMesh != null ? s.smr.sharedMesh.Pointer == s.mesh : s.mesh == IntPtr.Zero;
        return s.mesh == IntPtr.Zero;
    }

    // ------------------------------------------------------------------ buildings

    public static void Building(BuildingVisual bv, string why)
    {
        if (bv == null) return;
        var go = bv.gameObject;
        var prefab = BaseName(go.name);
        var civWord = Japan.CivWord;
        if (!prefab.EndsWith(" " + civWord)) return;
        var b = Kebab(prefab.Substring(0, prefab.Length - civWord.Length - 1));
        var own = b + "-" + Kebab(civWord);
        Japan.ModelMap.TryGetValue(own, out var mainName);
        if (Unchanged(go)) return;

        var civPrefixes = new List<string>();
        foreach (var c in Civs) civPrefixes.Add(b + "-" + Kebab(c));
        var filters = go.GetComponentsInChildren<MeshFilter>(true);
        var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool hasOwn = false;
        foreach (var mf in filters) { var g = GroupOf(mf.sharedMesh != null ? mf.sharedMesh.name : null) ?? GroupOf(mf.gameObject.name); if (g != null && (g == own || g.StartsWith(own + "-"))) { hasOwn = true; break; } }
        if (!hasOwn) foreach (var r in skinned) { var g = GroupOf(r.sharedMesh != null ? r.sharedMesh.name : null) ?? GroupOf(r.gameObject.name); if (g != null && (g == own || g.StartsWith(own + "-"))) { hasOwn = true; break; } }
        var keep = hasOwn ? own : b + "-europe";

        string ModelFor(string g)
        {
            if (g == null) return null;
            if (g == keep) return Models.Exists(mainName) ? mainName : null;
            if (g.StartsWith(keep + "-")) return Piece(g, keep, mainName);
            if (g != own) { var x = FromMap(g); if (x != null) return x; }   // e.g. ship-viking inside Dock Viking, inhabitants
            foreach (var cp in civPrefixes) if (g == cp || g.StartsWith(cp + "-")) return "";   // another civ's body in the same prefab
            return null;
        }

        var main = Models.Exists(mainName) ? Models.Get(mainName) : null;
        Root(go, prefab, filters, skinned, ModelFor, main, why, "kept group " + keep);
    }

    // ------------------------------------------------------------------ units, inhabitants, ragdolls, ships

    public static void Other(GameObject go, string why)
    {
        if (go == null || Unchanged(go)) return;
        var filters = go.GetComponentsInChildren<MeshFilter>(true);
        var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Root(go, BaseName(go.name), filters, skinned, g => g == null ? null : FromMap(g), null, why, null);
    }

    // ------------------------------------------------------------------ shared

    static void Root(GameObject go, string label, MeshFilter[] filters, SkinnedMeshRenderer[] skinned, Func<string, string> modelFor, Model main, string why, string note)
    {
        int swapped = 0, emptied = 0, moved = 0, already = 0;
        var used = new SortedSet<string>(); var untouched = new SortedSet<string>();
        MeshFilter firstMf = null; SkinnedMeshRenderer firstSmr = null;

        foreach (var mf in filters)
        {
            var sm = mf.sharedMesh;
            if (Models.IsOurs(sm)) { already++; firstMf ??= mf; continue; }
            var g = GroupOf(sm != null ? sm.name : null) ?? GroupOf(mf.gameObject.name);
            var model = modelFor(g);
            if (model == null)
            {
                if (g != null) untouched.Add(g);
                if (mf.gameObject.name == "flag-big" && main != null && main.HasFlag)
                {
                    mf.transform.localPosition = main.FlagPos;
                    mf.transform.localRotation = Quaternion.Euler(0f, main.FlagYaw, 0f);
                    moved++;
                }
                continue;
            }
            if (model == "") { mf.sharedMesh = Models.Empty; emptied++; continue; }
            var mdl = Models.Get(model);
            if (mdl == null) continue;
            mf.sharedMesh = Models.ForStatic(mdl, mf.GetComponent<MeshRenderer>());
            firstMf ??= mf; swapped++; used.Add(g + "=" + model);
        }
        foreach (var r in skinned)
        {
            var sm = r.sharedMesh;
            if (Models.IsOurs(sm)) { already++; if (firstMf == null) firstSmr ??= r; continue; }
            var g = GroupOf(sm != null ? sm.name : null) ?? GroupOf(r.gameObject.name);
            var model = modelFor(g);
            if (model == null) { if (g != null) untouched.Add(g); continue; }
            if (model == "") { r.sharedMesh = Models.Empty; emptied++; continue; }
            var mdl = Models.Get(model);
            if (mdl == null) continue;
            r.sharedMesh = Models.ForSkinned(mdl, r);
            if (firstMf == null) firstSmr ??= r;
            swapped++; used.Add(g + "=" + model + " (skinned)");
        }
        if (main != null && (swapped > 0 || already > 0))
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                foreach (var a in main.Anchors)
                    if (t.gameObject.name == a.name)
                    {
                        t.localPosition = a.pos;
                        // yaw 0 keeps the game's own rotation (particles may be tilted on purpose); another yaw turns the object
                        if (a.yaw != 0f) t.localRotation = Quaternion.Euler(0f, a.yaw, 0f);
                        moved++;
                    }

        // remember one renderer and its mesh: the next look skips this root unless that mesh changes
        if (firstMf == null && firstSmr == null) { if (filters.Length > 0) firstMf = filters[0]; else if (skinned.Length > 0) firstSmr = skinned[0]; }
        var cur = firstMf != null ? firstMf.sharedMesh : firstSmr != null ? firstSmr.sharedMesh : null;
        Seen[go.GetInstanceID()] = (firstMf, firstSmr, cur != null ? cur.Pointer : IntPtr.Zero);

        if (swapped == 0 && emptied == 0 && already == 0) return;   // nothing of ours here (another civ's unit and so on)
        bool asset = !go.scene.IsValid();
        if (Described.Add(label + (asset ? " [prefab]" : "")))
            Japan.W("root " + label + (asset ? " [prefab asset]" : "") + (note != null ? ", " + note : "") + ": " + string.Join(", ", used) +
                (untouched.Count > 0 ? " | untouched groups: " + string.Join(", ", untouched) : ""));
        if (swapped + emptied > 0 && (Logged.Add(go.GetInstanceID()) || why == "scan" && !asset && Logged.Count < 5000))
            Japan.W("swap[" + why + "] " + go.name + (asset ? " [prefab]" : "") + ": " + swapped + " swapped, " + emptied + " emptied, " + already + " already ours, " + moved + " moved");
    }
}
