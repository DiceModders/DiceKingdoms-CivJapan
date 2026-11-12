// .dkmesh loading and the meshes we put on the game's renderers.
// Format (plain text, Unity prefab space): v x y z u v [b | b0 w0 b1 w1 ...] / t a b c / part crops|sail / bones ... /
// bindpose <bone> <16 floats row-major> / anchor <object name> x y z yaw / flag x y z yaw
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace DiceModders.CivJapan;

internal sealed class Model
{
    public string Name;
    public Mesh Mesh;                                  // every triangle in one submesh
    public int[] BaseTris, CropTris, SailTris;         // split at "part crops" / "part sail" (null when absent)
    public int[] AllTris;                              // every triangle (kept so the mesh is never read back through interop)
    public string[] Bones;                             // skinned models only
    public int[] VertBones;                            // 4 per vertex
    public float[] VertWeights;                        // 4 per vertex
    public Dictionary<string, float[]> BindPoses = new();
    public bool Skinned => Bones != null;
    public bool HasFlag; public Vector3 FlagPos; public float FlagYaw;
    public List<(string name, Vector3 pos, float yaw)> Anchors = new();
    public readonly Dictionary<string, Mesh> Variants = new();
}

internal static class Models
{
    static readonly Dictionary<string, Model> Cache = new();
    static readonly Dictionary<string, bool> Files = new();
    internal static readonly HashSet<IntPtr> Ours = new();
    static Mesh _empty;

    internal const string CacheFolder = "civjapan";
    static void W(string s) => Japan.W(s);

    public static bool Exists(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!Files.TryGetValue(name, out var e)) Files[name] = e = File.Exists(Path.Combine(Japan.MeshDir, name + ".dkmesh"));
        return e;
    }

    public static bool IsOurs(Mesh m) => m != null && Ours.Contains(m.Pointer);

    public static Mesh Empty
    {
        get
        {
            if (_empty == null)
            {
                _empty = new Mesh { name = "custom-empty" };
                _empty.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Ours.Add(_empty.Pointer);
            }
            return _empty;
        }
    }

    static Mesh Keep(Mesh m) { m.hideFlags = HideFlags.DontUnloadUnusedAsset; Ours.Add(m.Pointer); return m; }

    public static Model Get(string name)
    {
        if (Cache.TryGetValue(name, out var m)) return m;
        m = null;
        try { m = Load(name); }
        catch (Exception e) { W("LOAD FAIL " + name + ": " + e); }
        Cache[name] = m;
        return m;
    }

    // ---- binary cache: the text file is parsed once; later launches read the parsed arrays (BepInEx\cache\<CacheFolder>\<name>.bin) ----
    sealed class Raw
    {
        public Vector3[] V, N; public Vector2[] UV; public int[] T;
        public int[] VB; public float[] VW; public bool Weighted;
        public int CropStart = -1, SailStart = -1;
        public string[] Bones;
        public Dictionary<string, float[]> BindPoses = new();
        public bool HasFlag; public Vector3 FlagPos; public float FlagYaw;
        public List<(string name, Vector3 pos, float yaw)> Anchors = new();
    }
    const int BinVersion = 1;
    // ModCore's shared cache folder: files this mod stops asking for (or all of them, if the mod is removed) are deleted after a few launches
    static string BinPath(string name) => DiceModders.ModCore.Tools.DiskCache.PathFor(CacheFolder, name + ".bin");

    static Model Load(string name)
    {
        var src = new FileInfo(Path.Combine(Japan.MeshDir, name + ".dkmesh"));
        string key = src.Length + ":" + src.LastWriteTimeUtc.Ticks;
        var raw = ReadBin(name, key);
        bool parsed = raw == null;
        if (parsed) { raw = Parse(src.FullName); WriteBin(name, key, raw); }
        return Build(name, raw, parsed);
    }

    static void WriteVec3(BinaryWriter w, Vector3[] a) { w.Write(a.Length); foreach (var x in a) { w.Write(x.x); w.Write(x.y); w.Write(x.z); } }
    static Vector3[] ReadVec3(BinaryReader r) { var a = new Vector3[r.ReadInt32()]; for (int i = 0; i < a.Length; i++) a[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); return a; }
    static void WriteInts(BinaryWriter w, int[] a) { w.Write(a?.Length ?? -1); if (a != null) foreach (var x in a) w.Write(x); }
    static int[] ReadInts(BinaryReader r) { int n = r.ReadInt32(); if (n < 0) return null; var a = new int[n]; for (int i = 0; i < n; i++) a[i] = r.ReadInt32(); return a; }
    static void WriteFloats(BinaryWriter w, float[] a) { w.Write(a?.Length ?? -1); if (a != null) foreach (var x in a) w.Write(x); }
    static float[] ReadFloats(BinaryReader r) { int n = r.ReadInt32(); if (n < 0) return null; var a = new float[n]; for (int i = 0; i < n; i++) a[i] = r.ReadSingle(); return a; }

    static void WriteBin(string name, string key, Raw x)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BinPath(name)));
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
            {
                w.Write(BinVersion); w.Write(key);
                WriteVec3(w, x.V); WriteVec3(w, x.N);
                w.Write(x.UV.Length); foreach (var u in x.UV) { w.Write(u.x); w.Write(u.y); }
                WriteInts(w, x.T); WriteInts(w, x.VB); WriteFloats(w, x.VW); w.Write(x.Weighted);
                w.Write(x.CropStart); w.Write(x.SailStart);
                w.Write(x.Bones?.Length ?? -1); if (x.Bones != null) foreach (var b in x.Bones) w.Write(b);
                w.Write(x.BindPoses.Count); foreach (var kv in x.BindPoses) { w.Write(kv.Key); WriteFloats(w, kv.Value); }
                w.Write(x.HasFlag); w.Write(x.FlagPos.x); w.Write(x.FlagPos.y); w.Write(x.FlagPos.z); w.Write(x.FlagYaw);
                w.Write(x.Anchors.Count); foreach (var a in x.Anchors) { w.Write(a.name); w.Write(a.pos.x); w.Write(a.pos.y); w.Write(a.pos.z); w.Write(a.yaw); }
            }
            File.WriteAllBytes(BinPath(name), ms.ToArray());
        }
        catch (Exception e) { W("mesh cache write " + name + ": " + e.Message); }
    }

    static Raw ReadBin(string name, string key)
    {
        var path = BinPath(name);
        if (!File.Exists(path)) return null;
        try
        {
            using var r = new BinaryReader(new MemoryStream(DiceModders.ModCore.Tools.DiskCache.ReadAllBytes(path)));
            if (r.ReadInt32() != BinVersion || r.ReadString() != key) return null;
            var x = new Raw { V = ReadVec3(r), N = ReadVec3(r) };
            x.UV = new Vector2[r.ReadInt32()]; for (int i = 0; i < x.UV.Length; i++) x.UV[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            x.T = ReadInts(r); x.VB = ReadInts(r); x.VW = ReadFloats(r); x.Weighted = r.ReadBoolean();
            x.CropStart = r.ReadInt32(); x.SailStart = r.ReadInt32();
            int nb = r.ReadInt32(); if (nb >= 0) { x.Bones = new string[nb]; for (int i = 0; i < nb; i++) x.Bones[i] = r.ReadString(); }
            int np = r.ReadInt32(); for (int i = 0; i < np; i++) { var k = r.ReadString(); x.BindPoses[k] = ReadFloats(r); }
            x.HasFlag = r.ReadBoolean(); x.FlagPos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); x.FlagYaw = r.ReadSingle();
            int na = r.ReadInt32(); for (int i = 0; i < na; i++) x.Anchors.Add((r.ReadString(), new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadSingle()));
            return x;
        }
        catch (Exception e) { W("mesh cache read " + name + " (rebuilding): " + e.Message); return null; }
    }

    static Raw Parse(string path)
    {
        var inv = CultureInfo.InvariantCulture;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        var vb = new List<int>(); var vw = new List<float>();
        int cropStart = -1, sailStart = -1;
        var model = new Raw();
        bool weighted = false;
        foreach (var line in File.ReadLines(path))
        {
            var p = line.Split(' ');
            switch (p[0])
            {
                case "v":
                    v.Add(new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                    uv.Add(new Vector2(float.Parse(p[4], inv), float.Parse(p[5], inv)));
                    {
                        int cnt = 0;
                        if (p.Length == 7) { vb.Add(int.Parse(p[6])); vw.Add(1f); cnt = 1; weighted = true; }
                        else if (p.Length > 7) { for (int q = 6; q + 1 < p.Length && cnt < 4; q += 2) { vb.Add(int.Parse(p[q])); vw.Add(float.Parse(p[q + 1], inv)); cnt++; } weighted = true; }
                        for (; cnt < 4; cnt++) { vb.Add(0); vw.Add(0f); }
                    }
                    break;
                case "t": t.Add(int.Parse(p[1])); t.Add(int.Parse(p[2])); t.Add(int.Parse(p[3])); break;
                case "part":
                    if (p.Length > 1 && p[1] == "crops") cropStart = t.Count;
                    else if (p.Length > 1 && p[1] == "sail") sailStart = t.Count;
                    break;
                case "bones": model.Bones = p[1..]; break;
                case "bindpose":
                    {
                        var mm = new float[16];
                        for (int q = 0; q < 16; q++) mm[q] = float.Parse(p[2 + q], inv);
                        model.BindPoses[p[1]] = mm;
                    }
                    break;
                case "flag":
                    model.HasFlag = true;
                    model.FlagPos = new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv));
                    model.FlagYaw = float.Parse(p[4], inv);
                    break;
                case "anchor":
                    if (p.Length >= 6)
                        model.Anchors.Add((string.Join(" ", p, 1, p.Length - 5),
                            new Vector3(float.Parse(p[^4], inv), float.Parse(p[^3], inv), float.Parse(p[^2], inv)), float.Parse(p[^1], inv)));
                    break;
            }
        }

        // Mesh.RecalculateNormals is stripped in this build: plain float math
        var nx = new float[v.Count]; var ny = new float[v.Count]; var nz = new float[v.Count];
        for (int i = 0; i < t.Count; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            float ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, wx = c.x - a.x, wy = c.y - a.y, wz = c.z - a.z;
            float cx = uy * wz - uz * wy, cy = uz * wx - ux * wz, cz = ux * wy - uy * wx;
            for (int k = 0; k < 3; k++) { int j = t[i + k]; nx[j] += cx; ny[j] += cy; nz[j] += cz; }
        }
        var nrm = new Vector3[v.Count];
        for (int i = 0; i < nrm.Length; i++)
        {
            float l = (float)Math.Sqrt(nx[i] * nx[i] + ny[i] * ny[i] + nz[i] * nz[i]);
            nrm[i] = l > 0 ? new Vector3(nx[i] / l, ny[i] / l, nz[i] / l) : new Vector3(0, 1, 0);
        }
        model.V = v.ToArray(); model.UV = uv.ToArray(); model.T = t.ToArray(); model.N = nrm;
        model.VB = vb.ToArray(); model.VW = vw.ToArray(); model.Weighted = weighted;
        model.CropStart = cropStart; model.SailStart = sailStart;
        return model;
    }

    static Model Build(string name, Raw x, bool parsed)
    {
        var model = new Model { Name = name, Bones = x.Bones, BindPoses = x.BindPoses, HasFlag = x.HasFlag, FlagPos = x.FlagPos, FlagYaw = x.FlagYaw, Anchors = x.Anchors };
        var mesh = new Mesh { name = "custom-" + name, indexFormat = IndexFormat.UInt32 };
        mesh.vertices = x.V;
        mesh.uv = x.UV;
        mesh.triangles = x.T;
        mesh.normals = x.N;
        model.Mesh = Keep(mesh);

        var all = x.T;
        model.AllTris = all;
        int cropStart = x.CropStart, sailStart = x.SailStart;
        int baseEnd = all.Length;
        if (cropStart >= 0) baseEnd = Math.Min(baseEnd, cropStart);
        if (sailStart >= 0) baseEnd = Math.Min(baseEnd, sailStart);
        if (cropStart >= 0) model.CropTris = all[cropStart..(sailStart > cropStart ? sailStart : all.Length)];
        if (sailStart >= 0) model.SailTris = all[sailStart..(cropStart > sailStart ? cropStart : all.Length)];
        if (baseEnd < all.Length) model.BaseTris = all[..baseEnd];
        if (model.Bones != null && x.Weighted && x.VB != null && x.VB.Length == x.V.Length * 4) { model.VertBones = x.VB; model.VertWeights = x.VW; }
        else if (model.Bones != null) { W("WARN " + name + ": 'bones' line but vertex weights do not match; loaded as a static model"); model.Bones = null; }
        var v = x.V; var t = x.T;

        W("loaded " + name + (parsed ? " (parsed text, cached)" : " (cache)") + ": " + v.Length + " verts, " + (t.Length / 3) + " tris" +
          (model.CropTris != null ? ", crops " + model.CropTris.Length / 3 : "") + (model.SailTris != null ? ", sail " + model.SailTris.Length / 3 : "") +
          (model.Skinned ? ", skinned (" + model.Bones.Length + " bones, " + model.BindPoses.Count + " bind poses)" : "") +
          ", anchors " + model.Anchors.Count + (model.HasFlag ? ", flag" : ""));
        return model;
    }

    /// <summary>
    /// Static body. One material: the whole model. Several: base triangles in the Colored-Paper slot, "part crops" in Windy-Crops
    /// (slots found by name, the order differs per civ), other slots empty. No crops part or no Windy-Crops slot: everything in Colored-Paper.
    /// </summary>
    public static Mesh ForStatic(Model model, MeshRenderer r)
    {
        if (r == null) return model.Mesh;
        var mats = r.sharedMaterials;
        if (mats == null || mats.Length <= 1) return model.Mesh;
        int slot = 0, crop = -1;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            if (mats[i].name.StartsWith("Colored-Paper", StringComparison.Ordinal)) slot = i;
            if (mats[i].name.StartsWith("Windy-Crops", StringComparison.Ordinal)) crop = i;
        }
        bool split = model.CropTris != null && model.CropTris.Length > 0 && crop >= 0 && crop != slot;
        if (slot == 0 && !split) return model.Mesh;
        var key = "s" + slot + "/" + (split ? crop : -1) + "/" + mats.Length;
        if (model.Variants.TryGetValue(key, out var c)) return c;
        c = UnityEngine.Object.Instantiate(model.Mesh);
        c.name = model.Mesh.name + "#" + key;
        int[] tris = split ? (model.BaseTris ?? Array.Empty<int>()) : model.AllTris;
        c.subMeshCount = mats.Length;
        for (int i = 0; i < mats.Length; i++) c.SetTriangles(i == slot ? tris : (split && i == crop ? model.CropTris : new int[0]), i);
        model.Variants[key] = Keep(c);
        W("material slots " + model.Name + ": body -> " + slot + " (" + mats[slot].name + ")" + (split ? ", crops -> " + crop + " (" + mats[crop].name + ")" : ""));
        return c;
    }

    /// <summary>
    /// Skinned body (units, ships, windmill): vertices follow the original rig's bones (matched by name), up to 4 weights;
    /// bind poses from the file for our bones, the original ones for the rest. Base triangles in Colored-Paper, "part sail" in Sail.
    /// </summary>
    public static Mesh ForSkinned(Model model, SkinnedMeshRenderer r)
    {
        if (!model.Skinned)
        {
            Japan.Once("noskin:" + model.Name, "WARN " + model.Name + " goes on a skinned renderer (" + r.gameObject.name + ") but has no bones: shown rigid");
            return model.Mesh;
        }
        var mats = r.sharedMaterials;
        int slot = 0, sail = -1;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            if (mats[i].name.StartsWith("Colored-Paper", StringComparison.Ordinal)) slot = i;
            if (mats[i].name.StartsWith("Sail", StringComparison.Ordinal)) sail = i;
        }
        var bones = r.bones;
        var bnames = new string[bones.Length];
        for (int j = 0; j < bones.Length; j++) bnames[j] = bones[j] != null ? bones[j].name : "";
        var key = "k" + slot + "/" + sail + "/" + mats.Length + "/" + string.Join("|", bnames);
        if (model.Variants.TryGetValue(key, out var c)) return c;

        var map = new int[model.Bones.Length];
        var missing = new List<string>();
        for (int k = 0; k < model.Bones.Length; k++)
        {
            map[k] = Array.IndexOf(bnames, model.Bones[k]);
            if (map[k] < 0) { map[k] = 0; missing.Add(model.Bones[k]); }
        }
        int nv = model.VertBones.Length / 4;
        var bi = new int[nv * 4];
        for (int i = 0; i < bi.Length; i++) bi[i] = map[model.VertBones[i]];

        // bind poses: the renderer's original ones (raw il2cpp read), ours override the bones we use (file is row-major, Unity column-major)
        var bp = new float[bones.Length * 16];
        IntPtr ob = IntPtr.Zero;
        try { var o = r.sharedMesh != null ? r.sharedMesh.bindposes : null; if (o != null) ob = o.Pointer; }
        catch (Exception e) { W("bindposes read " + model.Name + ": " + e.Message); }
        int origN = ob != IntPtr.Zero ? (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_array_length(ob) : 0;
        unsafe
        {
            float* src = ob != IntPtr.Zero ? (float*)((byte*)ob + 4 * IntPtr.Size) : null;
            for (int j = 0; j < bones.Length; j++)
                for (int q = 0; q < 16; q++) bp[j * 16 + q] = (src != null && j < origN) ? src[j * 16 + q] : ((q % 5 == 0) ? 1f : 0f);
        }
        for (int k = 0; k < model.Bones.Length; k++)
            if (model.BindPoses.TryGetValue(model.Bones[k], out var mm) && !missing.Contains(model.Bones[k]))
                for (int col = 0; col < 4; col++) for (int row = 0; row < 4; row++) bp[map[k] * 16 + col * 4 + row] = mm[row * 4 + col];

        c = UnityEngine.Object.Instantiate(model.Mesh);
        c.name = model.Mesh.name + "#" + r.gameObject.name;
        Skin.Set(c, bi, model.VertWeights, bp, nv, bones.Length);
        int[] tris = model.BaseTris ?? model.AllTris;
        c.subMeshCount = Math.Max(mats.Length, 1);
        for (int i = 0; i < c.subMeshCount; i++)
            c.SetTriangles(i == slot ? tris : (i == sail && model.SailTris != null ? model.SailTris : new int[0]), i);
        c.RecalculateBounds();
        model.Variants[key] = Keep(c);
        W("skinned " + model.Name + " on '" + r.gameObject.name + "': " + bones.Length + " rig bones, body -> slot " + slot +
          (model.SailTris != null ? ", sail -> " + sail : "") + (missing.Count > 0 ? ", BONES NOT IN RIG: " + string.Join(",", missing) : ""));
        return c;
    }
}

/// <summary>
/// Bone weights and bind poses through raw il2cpp memory (BoneWeight is stripped from this build; see the wiki's custom building models guide).
/// Copied from IconRender's SetSkin.
/// </summary>
internal static unsafe class Skin
{
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    delegate void SetBoneWeightsArr(IntPtr self, IntPtr array);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    delegate void SetBindposes(IntPtr self, IntPtr array);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    delegate int GetBindposeCount(IntPtr self);
    static SetBoneWeightsArr setBW; static SetBindposes setBP; static GetBindposeCount getBPC;
    static IntPtr weightClass, matrixClass;

    static IntPtr FindClass(string ns, string name)
    {
        uint count = 0;
        IntPtr* asms = (IntPtr*)Il2CppInterop.Runtime.IL2CPP.il2cpp_domain_get_assemblies(Il2CppInterop.Runtime.IL2CPP.il2cpp_domain_get(), ref count);
        for (uint i = 0; i < count; i++)
        {
            IntPtr img = Il2CppInterop.Runtime.IL2CPP.il2cpp_assembly_get_image(asms[i]);
            IntPtr k = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_from_name(img, ns, name);
            if (k != IntPtr.Zero) return k;
        }
        return IntPtr.Zero;
    }

    static bool Init()
    {
        if (setBW != null) return true;
        IntPtr fn = Il2CppInterop.Runtime.IL2CPP.il2cpp_resolve_icall("UnityEngine.Mesh::SetBoneWeightsImpl");
        IntPtr fb = Il2CppInterop.Runtime.IL2CPP.il2cpp_resolve_icall("UnityEngine.Mesh::set_bindposes");
        IntPtr fc = Il2CppInterop.Runtime.IL2CPP.il2cpp_resolve_icall("UnityEngine.Mesh::GetBindposeCount");
        foreach (var (ns, cn) in new[] { ("UnityEngine", "BoneWeight"), ("Unity.Mathematics", "float4x2"), ("Unity.Mathematics", "float2x4"),
                                         ("Unity.Mathematics", "double4"), ("Unity.Mathematics", "int4x2"), ("Unity.Mathematics", "uint4x2") })
        {
            var k = FindClass(ns, cn);
            if (k != IntPtr.Zero && Il2CppInterop.Runtime.IL2CPP.il2cpp_class_array_element_size(k) == 32) { weightClass = k; break; }
        }
        matrixClass = Il2CppInterop.Runtime.Il2CppClassPointerStore<Matrix4x4>.NativeClassPtr;
        if (matrixClass == IntPtr.Zero) matrixClass = FindClass("UnityEngine", "Matrix4x4");
        Japan.W("skin init: weights icall " + fn + ", bindposes icall " + fb + ", 32-byte class " + weightClass + ", matrix class " + matrixClass);
        if (fn == IntPtr.Zero || fb == IntPtr.Zero || weightClass == IntPtr.Zero || matrixClass == IntPtr.Zero) return false;
        setBW = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<SetBoneWeightsArr>(fn);
        setBP = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<SetBindposes>(fb);
        if (fc != IntPtr.Zero) getBPC = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<GetBindposeCount>(fc);
        return true;
    }

    public static void Set(Mesh c, int[] bi, float[] bw, float[] bp, int nv, int nb)
    {
        if (!Init()) { Japan.Once("skin:init", "SKIN FAIL: engine calls not found, skinned models stay rigid"); return; }
        IntPtr arr = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(weightClass, (ulong)nv);
        byte* d0 = (byte*)arr + 4 * IntPtr.Size;
        for (int i = 0; i < nv; i++)
        {
            float* f = (float*)(d0 + i * 32); int* n = (int*)(d0 + i * 32 + 16);
            for (int k = 0; k < 4; k++) { f[k] = bw[i * 4 + k]; n[k] = bw[i * 4 + k] > 0f ? bi[i * 4 + k] : 0; }
        }
        setBW(c.Pointer, arr);
        if (!c.HasVertexAttribute(VertexAttribute.BlendIndices)) Japan.W("SKIN WARN: weights did not apply on " + c.name);
        IntPtr m = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(matrixClass, (ulong)nb);
        float* q = (float*)((byte*)m + 4 * IntPtr.Size);
        for (int i = 0; i < nb * 16; i++) q[i] = bp[i];
        setBP(c.Pointer, m);
        if (getBPC != null && getBPC(c.Pointer) != nb) Japan.W("SKIN WARN: bindpose count " + getBPC(c.Pointer) + " expected " + nb + " on " + c.name);
    }
}
