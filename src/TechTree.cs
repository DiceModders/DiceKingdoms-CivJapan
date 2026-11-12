// Japan's tech tree: a copy of the look-alike civilization's tree (ModCore.Civilizations.CloneTree) with the layout from assets\techtree.json.
// Only Japanese players use it; the look-alike civilization keeps its own tree.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using DiceKingdoms.Game;
using Core = DiceModders.ModCore.ModCore;

namespace DiceModders.CivJapan;

internal static class JapanTree
{
    sealed class SlotData
    {
        public string Name; public TechnologyName Tech; public int X, Y;
        public List<(TechnologyName to, int slot, int from, int toPort)> Links = new();
    }

    /// <summary>The copy with our layout (ModCore asks for it whenever the game needs Japan's tree; the caller keeps it).</summary>
    public static TechTree BuildCopy()
    {
        var wanted = Read(Japan.TechTreeFile);
        if (wanted == null) return null;
        var copy = Core.Civilizations.CloneTree(Japan.CivId);
        Write(copy, wanted);
        Japan.W("tech tree: layout written into '" + copy.name + "' (" + wanted.Count + " slots)");
        return copy;
    }

    static void Write(TechTree tree, List<SlotData> data)
    {
        var slots = tree.slots;
        if (slots.Length != data.Count) Japan.W("tech tree WARN: the file has " + data.Count + " slots, the game " + slots.Length);
        int n = Math.Min(slots.Length, data.Count);
        for (int i = 0; i < n; i++)
        {
            var s = slots[i];
            var d = data[i];
            s.name = d.Name;
            s.technology = d.Tech;
            var p = s.position; p.x = d.X; p.y = d.Y; s.position = p;
            var links = new Il2CppStructArray<TechTree.Slot.Link>(d.Links.Count);
            for (int k = 0; k < d.Links.Count; k++)
            {
                var (to, slot, from, toPort) = d.Links[k];
                links[k] = new TechTree.Slot.Link { toTechnology = to, toSlot = slot, fromPort = from, toPort = toPort };
            }
            s.links = links;
            slots[i] = s;
        }
        tree.dependencies?.Clear();   // GetDependencies caches per technology; the old links would stay otherwise
    }

    /// <summary>
    /// techtree.json: slot, technology, position, unlocks. The game's links also have ports (which side of a box a line uses):
    /// as in the game's own trees, a box with several lines on one side spreads them by direction (-1 / 0 / +1 = towards lower / same / higher row).
    /// </summary>
    static List<SlotData> Read(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement slotsEl = default;
        foreach (var civ in doc.RootElement.EnumerateObject())
            if (civ.Value.ValueKind == JsonValueKind.Object && civ.Value.TryGetProperty("slots", out slotsEl)) break;
        if (slotsEl.ValueKind != JsonValueKind.Array) { Japan.W("tech tree: no 'slots' array in " + path); return null; }

        var list = new List<SlotData>();
        var unlocks = new List<string[]>();
        foreach (var el in slotsEl.EnumerateArray())
        {
            var tech = el.GetProperty("technology").GetString();
            var pos = el.GetProperty("position");
            var d = new SlotData { Name = tech, Tech = Enum.Parse<TechnologyName>(tech), X = pos[0].GetInt32(), Y = pos[1].GetInt32() };
            var u = new List<string>();
            if (el.TryGetProperty("unlocks", out var ul)) foreach (var x in ul.EnumerateArray()) u.Add(x.GetString());
            int idx = el.TryGetProperty("slot", out var se) ? se.GetInt32() : list.Count;
            while (list.Count <= idx) { list.Add(null); unlocks.Add(null); }
            list[idx] = d; unlocks[idx] = u.ToArray();
        }
        if (list.Contains(null)) throw new InvalidDataException("slot numbers have gaps");
        var slotOf = new Dictionary<string, int>();
        for (int i = 0; i < list.Count; i++) slotOf[list[i].Name] = i;
        var outCount = new int[list.Count]; var inCount = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
            foreach (var u in unlocks[i])
                if (slotOf.TryGetValue(u, out int j)) { outCount[i]++; inCount[j]++; }
                else Japan.W("tech tree WARN: " + list[i].Name + " unlocks unknown " + u);
        for (int i = 0; i < list.Count; i++)
            foreach (var u in unlocks[i])
            {
                if (!slotOf.TryGetValue(u, out int j)) continue;
                int dy = Math.Sign(list[j].Y - list[i].Y);
                list[i].Links.Add((list[j].Tech, j, outCount[i] > 1 ? dy : 0, inCount[j] > 1 ? -dy : 0));
            }
        return list;
    }
}
