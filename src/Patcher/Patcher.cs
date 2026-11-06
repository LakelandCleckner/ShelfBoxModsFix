// Shelf and Box Mods Fix - preloader patcher
// Author: JustChoochit

using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Mono.Cecil;

namespace ShelfBoxModsFix
{
    // BepInEx 5 preloader patcher. Restores the UI_PriceTag.m_Icon field that the game removed when it
    // switched price tags to an instanced atlas mesh. "What's in the Box?" and "What's on the Shelf?" still
    // reference it; without it their readers fail to JIT. The field is always null, so both mods take their
    // existing "no label image" path. The mods' own files are never modified.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

        public static void Patch(AssemblyDefinition assembly)
        {
            var log = Logger.CreateLogSource("ShelfBoxModsFix.Patcher");
            var module = assembly.MainModule;
            var tag = module.GetType("UI_PriceTag");
            if (tag == null) { log.LogWarning("UI_PriceTag not found; nothing to do."); return; }
            if (tag.Fields.Any(f => f.Name == "m_Icon")) { log.LogInfo("UI_PriceTag.m_Icon already exists; nothing to do."); return; }

            var ui = module.AssemblyReferences.FirstOrDefault(a => a.Name == "UnityEngine.UI");
            if (ui == null) { log.LogWarning("Assembly-CSharp has no UnityEngine.UI reference; skipped."); return; }

            var imageType = new TypeReference("UnityEngine.UI", "Image", module, ui);
            tag.Fields.Add(new FieldDefinition("m_Icon",
                FieldAttributes.Public | FieldAttributes.NotSerialized, imageType));
            log.LogInfo("Added compatibility field UI_PriceTag.m_Icon (always null).");
        }
    }
}
