using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace VS.Localization.Generator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class StringsGenerator : IIncrementalGenerator
    {
        private static readonly DiagnosticDescriptor Invalid = new DiagnosticDescriptor(
            "VSL001", "Invalid localization catalog", "{0}", "VS.Localization", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor Duplicate = new DiagnosticDescriptor(
            "VSL002", "Duplicate localization type", "Multiple catalogs generate '{0}' in this assembly", "VS.Localization", DiagnosticSeverity.Error, true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var catalogs = context.AdditionalTextsProvider
                .Where(file => file.Path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                               file.Path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                .Select((file, token) => new Catalog(file.Path, file.GetText(token)?.ToString()));
            var settings = context.ParseOptionsProvider.Select((options, _) =>
                ((CSharpParseOptions)options).PreprocessorSymbolNames
                    .Where(s => s.StartsWith("VS_LOCALIZATION_BINDING_", StringComparison.Ordinal))
                    .Select(DecodeBinding).Where(s => s != null).ToArray());
            var assembly = context.CompilationProvider.Select((compilation, _) => compilation.AssemblyName);
            context.RegisterSourceOutput(catalogs.Collect().Combine(settings).Combine(assembly), (output, input) =>
            {
                var bindings = input.Left.Right;
                var applicable = input.Left.Left.Where(c => bindings.Length == 0 || bindings.Any(b =>
                    b[0] == input.Right && (c.Path.Replace('\\', '/') == b[1] ||
                        c.Path.Replace('\\', '/').EndsWith("/" + b[1], StringComparison.Ordinal))));
                foreach (var group in applicable.GroupBy(c => c.Name, StringComparer.Ordinal))
                {
                    if (group.Count() > 1)
                    {
                        foreach (var file in group)
                            output.ReportDiagnostic(Diagnostic.Create(Duplicate, file.Location(0), file.Name));
                        continue;
                    }
                    var catalog = group.Single();
                    try
                    {
                        if (catalog.Text == null) throw new FormatException("Cannot read catalog: " + catalog.Path);
                        output.AddSource(catalog.Name + ".g.cs",
                            LocalizationGenerator.Generate(catalog.Text, catalog.Name).GetText(Encoding.UTF8));
                    }
                    catch (FormatException exception)
                    {
                        var match = Regex.Match(exception.Message, @"^Line (\d+):");
                        var line = match.Success ? int.Parse(match.Groups[1].Value) - 1 : 0;
                        output.ReportDiagnostic(Diagnostic.Create(Invalid, catalog.Location(line), exception.Message));
                    }
                }
            });
        }

        private static string[] DecodeBinding(string symbol)
        {
            var hex = symbol.Substring("VS_LOCALIZATION_BINDING_".Length);
            if (hex.Length % 2 != 0) return null;
            try
            {
                var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                var parts = Encoding.UTF8.GetString(bytes).Split('|');
                return parts.Length == 2 ? parts : null;
            }
            catch (FormatException) { return null; }
        }

        private sealed class Catalog
        {
            public readonly string Path;
            public readonly string Name;
            public readonly string Text;
            private readonly string diagnosticPath;

            public Catalog(string path, string text)
            {
                Path = path;
                Text = text;
                var filename = System.IO.Path.GetFileName(path);
                Name = System.IO.Path.GetFileNameWithoutExtension(filename);
                if (Name.EndsWith(".loc", StringComparison.Ordinal)) Name = Name.Substring(0, Name.Length - 4);
                diagnosticPath = path;
            }

            public Location Location(int line)
            {
                var text = SourceText.From(Text ?? "");
                line = Math.Max(0, Math.Min(line, text.Lines.Count - 1));
                var position = new LinePosition(line, 0);
                return Microsoft.CodeAnalysis.Location.Create(diagnosticPath,
                    new TextSpan(text.Lines[line].Start, 0), new LinePositionSpan(position, position));
            }
        }
    }
}
