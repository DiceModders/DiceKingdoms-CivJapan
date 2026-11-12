// DiceKingdoms CivJapan: Japan as its own civilization (ModCore). Its buildings, units, ship and ragdolls are copies of the look-alike civilization's
// (Viking) with our own 3D models; pictures are taken in the game; names, unit / technology texts, ground colours, tech tree and AI build orders come
// from the files in assets\ next to this DLL. Nothing runs every frame: everything is prepared once when the game data has loaded.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using DiceKingdoms.Game;

namespace DiceModders.CivJapan;

[BepInPlugin(Guid, Name, Version)]
[BepInDependency("DiceModders.ModCore")]
[BepInProcess("Dice-Kingdoms.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "org.dicemodders.dicekingdoms.civjapan";
    public const string Name = "DiceKingdoms CivJapan";
    public const string Version = "1.0.0";

    public override void Load()
    {
        Japan.Log = Log;
        Japan.AssetDir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "", "assets");
        try { Japan.Read(Path.Combine(Japan.AssetDir, "japan.json")); }
        catch (Exception e) { Log.LogError("could not read assets\\japan.json (install the whole folder, not only the DLL): " + e.Message); return; }
        try { JapanCiv.Register(); }
        catch (Exception e) { Log.LogError("could not register the civilization: " + e); }
    }
}

/// <summary>The civilization's data (assets\japan.json) and the shared helpers of this mod.</summary>
internal static class Japan
{
    internal static ManualLogSource Log;
    internal static string AssetDir;
    internal static string MeshDir => Path.Combine(AssetDir, "models");
    // the tech tree is simulation data, so it is built into the DLL: two players with the same mod version always have the same tree
    internal static string TechTreeJson()
    {
        using var s = typeof(Japan).Assembly.GetManifestResourceStream("techtree.json");
        if (s == null) throw new FileNotFoundException("techtree.json is not embedded in the DLL");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
    internal static string BuildOrdersFile => Path.Combine(AssetDir, "build_orders.txt");

    internal static readonly Dictionary<string, string> ModelMap = new();   // "house-viking" -> "jp-house"
    internal static readonly List<(int x, int y, Color c)> GroundCells = new();
    internal static readonly List<(int x, int y, Color c)> NatureCells = new();   // hills, trees, flowers
    internal static readonly Dictionary<string, string> Names = new();      // language code -> name
    internal static readonly Dictionary<string, Dictionary<string, string>> Texts = new();   // game text key -> language code -> text
    internal static string CivEnum = "VIKING", CivWord = "Viking";          // the look-alike civilization our copies start from
    internal static int CivId = 2;
    internal static string CivKey = "dicemodders.japan", Token = "japan";

    internal static void Read(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.TryGetProperty("civilization", out var civ))
        {
            CivEnum = civ.GetString();
            CivId = (int)Enum.Parse<DiceKingdoms.Interface.Civilization>(CivEnum);
            CivWord = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(CivEnum.ToLowerInvariant().Replace('_', ' '));
        }
        if (root.TryGetProperty("key", out var key)) CivKey = key.GetString();
        if (root.TryGetProperty("token", out var token)) Token = token.GetString();
        foreach (var kv in root.GetProperty("models").EnumerateObject()) ModelMap[kv.Name] = kv.Value.GetString();
        if (root.TryGetProperty("ground", out var gr))
            foreach (var (prop, list) in new[] { ("cells", GroundCells), ("natureCells", NatureCells) })
                if (gr.TryGetProperty(prop, out var cells))
                    foreach (var kv in cells.EnumerateObject())
                    {
                        var xy = kv.Name.Split(',');
                        if (ColorUtility.TryParseHtmlString(kv.Value.GetString(), out var col)) list.Add((int.Parse(xy[0]), int.Parse(xy[1]), col));
                    }
        if (root.TryGetProperty("name", out var nm))
            foreach (var kv in nm.EnumerateObject()) if (kv.Value.ValueKind == JsonValueKind.String) Names[kv.Name] = kv.Value.GetString();
        if (root.TryGetProperty("texts", out var tx))
            foreach (var kv in tx.EnumerateObject())
            {
                if (kv.Value.ValueKind != JsonValueKind.Object) continue;
                var langs = new Dictionary<string, string>();
                foreach (var l in kv.Value.EnumerateObject()) if (l.Value.ValueKind == JsonValueKind.String) langs[l.Name] = l.Value.GetString();
                if (langs.Count > 0) Texts[kv.Name] = langs;
            }
    }

    internal static void W(string s) => Log?.LogInfo(s);

    static readonly HashSet<string> OnceKeys = new();
    internal static void Once(string key, string s) { if (OnceKeys.Add(key)) Log?.LogWarning(s); }

    // never interpolate Unity structs (boxes to Il2CppSystem.Object -> MissingMethodException): concatenation only
    internal static string F(float f) => f.ToString("F2", CultureInfo.InvariantCulture);
}
