using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;

namespace VS.Localization.Editor
{
    // IDE project files need the same YAML inputs as the Unity compiler.
    public sealed class LocalizationProjectFiles : AssetPostprocessor
    {
        private static string OnGeneratedCSProject(string path, string content)
        {
            var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            var ns = root.Name.Namespace;
            var assembly = root.Descendants(ns + "AssemblyName").Select(e => e.Value).FirstOrDefault()
                ?? Path.GetFileNameWithoutExtension(path);
            var existing = root.Descendants(ns + "AdditionalFiles").Select(e => (string)e.Attribute("Include")).ToArray();
            var items = new XElement(ns + "ItemGroup");
            foreach (var catalog in LocalizationInputs.Catalogs().Where(p => LocalizationInputs.AssemblyFor(p) == assembly))
            {
                var fullPath = Path.GetFullPath(catalog);
                if (!existing.Any(p => p != null && Path.GetFullPath(p) == fullPath))
                    items.Add(new XElement(ns + "AdditionalFiles", new XAttribute("Include", fullPath)));
            }
            if (!items.HasElements) return content;
            root.Add(items);
            return document.ToString(SaveOptions.DisableFormatting);
        }
    }
}
