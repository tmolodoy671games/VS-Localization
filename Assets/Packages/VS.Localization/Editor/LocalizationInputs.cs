using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace VS.Localization.Editor
{
    // Connect the original YAML directly to Roslyn. No YAML copies or generated C# files.
    [InitializeOnLoad]
    public sealed class LocalizationInputs : AssetPostprocessor, IPreprocessBuildWithReport
    {
        private static string Package
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocalizationInputs).Assembly);
                if (info != null) return info.resolvedPath;
                var script = AssetDatabase.FindAssets("LocalizationInputs t:MonoScript")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Single(path => AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass() == typeof(LocalizationInputs));
                return Path.GetDirectoryName(Path.GetDirectoryName(script)).Replace('\\', '/');
            }
        }
        private static string Analyzer => Package + "/VS.Localization.Generator.dll";
        private const string Start = "-define:VS_LOCALIZATION_BEGIN";
        private const string End = "-define:VS_LOCALIZATION_END";
        private static bool queued;
        public int callbackOrder => -1000;

        static LocalizationInputs() { Queue(); }

        [MenuItem("Tools/VS.Localization/Refresh Compiler Inputs")]
        public static void RefreshInputs()
        {
            try { Sync(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (Sync()) throw new BuildFailedException("VS.Localization: compiler inputs changed. Wait for compilation and build again.");
        }

        private static bool IsImportedPath(string path) => !path.Split('/').Any(part =>
            part.StartsWith(".", StringComparison.Ordinal) || part.EndsWith("~", StringComparison.Ordinal));

        internal static bool IsCatalog(string path) => IsImportedPath(path) && path.StartsWith("Assets/", StringComparison.Ordinal) &&
            (Path.GetFileName(path) == "Strings.yaml" || path.EndsWith(".loc.yaml", StringComparison.Ordinal));

        internal static string AssemblyFor(string path)
        {
            var name = CompilationPipeline.GetAssemblyNameFromScriptPath(Path.ChangeExtension(path, ".cs"));
            if (string.IsNullOrEmpty(name)) throw new InvalidOperationException("Cannot determine assembly for " + path);
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }

        internal static string[] Catalogs() => Directory.GetFiles("Assets", "*.yaml", SearchOption.AllDirectories)
            .Select(p => p.Replace('\\', '/')).Where(IsCatalog).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        private static string ResponseFile(string path)
        {
            var definition = CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath(Path.ChangeExtension(path, ".cs"));
            if (!string.IsNullOrEmpty(definition))
            {
                if (!definition.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Place the catalog in an assembly defined under Assets: " + path);
                return Path.GetDirectoryName(definition).Replace('\\', '/') + "/csc.rsp";
            }
            var assembly = AssemblyFor(path);
            if (assembly == "Assembly-CSharp") return "Assets/csc.rsp";
            // Custom asmdefs avoid ambiguity with Unity's legacy/predefined editor assemblies.
            throw new InvalidOperationException("Use an asmdef for localization in Editor or firstpass folders: " + path);
        }

        private static bool Sync()
        {
            bool changed = false;
            var binary = File.ReadAllBytes(Package + "/Compiler~/VS.Localization.Generator.dll");
            if (Package.StartsWith("Assets/", StringComparison.Ordinal) &&
                (!File.Exists(Analyzer) || !File.ReadAllBytes(Analyzer).SequenceEqual(binary)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Analyzer));
                if (!File.Exists(Analyzer + ".meta"))
                    File.Copy(Package + "/Compiler~/VS.Localization.Generator.dll.meta", Analyzer + ".meta");
                File.WriteAllBytes(Analyzer, binary);
                AssetDatabase.ImportAsset(Analyzer, ImportAssetOptions.ForceUpdate);
                changed = true;
            }
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in Catalogs().GroupBy(ResponseFile))
            {
                var block = new StringBuilder(Start + "\n");
                var contents = new StringBuilder();
                foreach (var path in group)
                {
                    if (path.IndexOfAny(new[] { '"', '\n', '\r', '|' }) >= 0) throw new IOException("Unsupported catalog path: " + path);
                    block.Append("-additionalfile:\"").Append(path).Append("\"\n");
                    var binding = BitConverter.ToString(Encoding.UTF8.GetBytes(AssemblyFor(path) + "|" + path)).Replace("-", "");
                    block.Append("-define:VS_LOCALIZATION_BINDING_").Append(binding).Append('\n');
                    contents.Append(path).Append('\0').Append(File.ReadAllText(path)).Append('\0');
                }
                // Unity/Bee may not track files passed via csc.rsp as dependencies.
                // A deterministic symbol makes every YAML edit a compiler input change.
                using (var hash = SHA256.Create())
                {
                    var digest = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(contents.ToString()))).Replace("-", "");
                    block.Append("-define:VS_LOCALIZATION_").Append(digest).Append('\n');
                }
                expected.Add(group.Key, block.Append(End).Append('\n').ToString());
            }
            var paths = Directory.GetFiles("Assets", "csc.rsp", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/')).Where(IsImportedPath).Concat(expected.Keys).Distinct().ToArray();
            foreach (var path in paths)
            {
                var current = File.Exists(path) ? File.ReadAllText(path) : "";
                var start = current.IndexOf(Start, StringComparison.Ordinal);
                var end = current.IndexOf(End, StringComparison.Ordinal);
                if ((start < 0) != (end < 0) || (start >= 0 && end < start))
                    throw new IOException("Malformed VS.Localization block in " + path);
                var body = current;
                if (start >= 0)
                {
                    end += End.Length;
                    if (end < body.Length && body[end] == '\r') end++;
                    if (end < body.Length && body[end] == '\n') end++;
                    body = body.Remove(start, end - start);
                }
                if (expected.TryGetValue(path, out var block))
                    body += (body.Length > 0 && !body.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "") + block;
                if (current == body) continue;
                if (body.Length == 0) AssetDatabase.DeleteAsset(path);
                else
                {
                    File.WriteAllText(path, body, new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                changed = true;
            }
            if (changed) CompilationPipeline.RequestScriptCompilation();
            return changed;
        }

        private static void Queue()
        {
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += () => { queued = false; RefreshInputs(); };
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(p =>
                    IsCatalog(p) || p.EndsWith(".asmdef", StringComparison.Ordinal) ||
                    p.EndsWith(".asmref", StringComparison.Ordinal) || p.EndsWith("csc.rsp", StringComparison.Ordinal) ||
                    (p.StartsWith("Packages/", StringComparison.Ordinal) && p.EndsWith("/VS.Localization.Generator.dll", StringComparison.Ordinal))))
                Queue();
        }
    }
}
