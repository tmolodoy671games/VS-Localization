using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using VS.Localization.Generator;

static class Program
{
    static int assertions;
    static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
    static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
        .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p)).ToArray();
    static (GeneratorDriver, Compilation) Generate(string yaml, string usage = "", string path = "Strings.yaml", string assembly = "Test", params AdditionalText[] extra)
    {
        var compilation = CSharpCompilation.Create(assembly + (assembly == "Test" ? Guid.NewGuid().ToString("N") : ""),
            new[] { CSharpSyntaxTree.ParseText(usage) }, References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new StringsGenerator().AsSourceGenerator() },
            new AdditionalText[] { new TextFile(path, yaml) }.Concat(extra));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver, output);
    }
    static void Main()
    {
        const string yaml = "stringName:\n  en: English\n  ru: Русский\nstringName2:\n  en: '{arg1:int} {arg2}'\n  ru: '{arg2} {arg1} {arg2}'\nbrace:\n  en: '{{hello}}'\nkeyword:\n  en: '{class}'\nshadow:\n  en: '{Locale} {locale} {_locale}'\n";
        var (driver, output) = Generate(yaml, "public static class Use { public static string Run() => new Strings(\"ru-RU\").StringName + \"|\" + new Strings(\"ru\").StringName2(arg2: \"тест\", arg1: 5) + \"|\" + new Strings(\"xx\").Brace + \"|\" + new Strings(null).Keyword(@class: null); }");
        Check(!driver.GetRunResult().Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Generator diagnostics");
        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Check(emit.Success, string.Join("\n", emit.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        Check(!assembly.GetType("Strings").IsPublic && assembly.GetType("Strings").IsClass, "internal class");
        Check((string)assembly.GetType("Use").GetMethod("Run").Invoke(null, null) == "Русский|тест 5 тест|{hello}|", "runtime translations/named args/fallback");
        Check(assembly.GetType("Strings").GetMethod("StringName2").GetParameters()[0].ParameterType == typeof(int), "typed arg");
        Check(driver.GetRunResult().Results.Single().GeneratedSources.Single().HintName == "Strings.g.cs", "virtual hint name");
        foreach (var bad in new[] {
            "", "x:\n  en: '{unclosed'", "x:\n  en: bad}", "x:\n  en: one\nx:\n  en: two",
            "hello_world:\n  en: a\nHelloWorld:\n  en: b", "Locale:\n  en: a", "Strings:\n  en: a",
            "x:\n  en: '{n:int}'\n  ru: '{n:string}'", "x:\n  en: '{n:Widget}'",
            "x:\n  en: '{a}'\n  ru: '{b}'", "x:\n  en: a\ny:\n  ru: б", "x:\n   en: a",
            "x:\n  en: |\n    text", "x:\n  en: a\n  EN: b", "x:\n  en: '{n:int} {n:string}'" })
        {
            var result = Generate(bad).Item1.GetRunResult();
            Check(result.Diagnostics.Any(d => d.Id == "VSL001"), "Expected VSL001: " + bad);
            Check(result.Results.All(r => r.GeneratedSources.Length == 0), "Invalid catalog emitted source");
        }
        var duplicate = Generate("x:\n  en: x", extra: new TextFile("Other/Strings.yaml", "y:\n  en: y")).Item1.GetRunResult();
        Check(duplicate.Diagnostics.Count(d => d.Id == "VSL002") == 2, "duplicate type diagnostics");
        var before = new TextFile("Strings.yaml", "value:\n  en: before");
        var comp = CSharpCompilation.Create("Incremental", references: References);
        GeneratorDriver incremental = CSharpGeneratorDriver.Create(new[] { new StringsGenerator().AsSourceGenerator() }, new[] { before });
        incremental = incremental.RunGenerators(comp).ReplaceAdditionalText(before, new TextFile("Strings.yaml", "value:\n  en: after")).RunGenerators(comp);
        Check(incremental.GetRunResult().Results.Single().GeneratedSources.Single().SourceText.ToString().Contains("after"), "incremental edit");
        Check(!incremental.GetRunResult().Results.Single().GeneratedSources.Single().SourceText.ToString().Contains("before"), "no stale translation");
        foreach (var type in new[] {"int", "long", "float", "double", "decimal", "bool", "string"})
        {
            var test = Generate("x:\n  en: '{value:" + type + "}'");
            Check(!test.Item2.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), "compile " + type);
        }
        var binding = "VS_LOCALIZATION_BINDING_" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("Target|Assets/Strings.yaml"));
        var options = new CSharpParseOptions(preprocessorSymbols: new[] { binding });
        GeneratorDriver scoped = CSharpGeneratorDriver.Create(new[] { new StringsGenerator().AsSourceGenerator() }, new[] { new TextFile("/project/Assets/Strings.yaml", "x:\n  en: x") }, options);
        Check(scoped.RunGenerators(CSharpCompilation.Create("Other")).GetRunResult().Results.Single().GeneratedSources.Length == 0, "no generation in other assembly");
        Check(scoped.RunGenerators(CSharpCompilation.Create("Target")).GetRunResult().Results.Single().GeneratedSources.Length == 1, "generation in bound assembly");
        GeneratorDriver removed = CSharpGeneratorDriver.Create(new[] { new StringsGenerator().AsSourceGenerator() }, new[] { before });
        removed = removed.RunGenerators(comp).RemoveAdditionalTexts(System.Collections.Immutable.ImmutableArray.Create<AdditionalText>(before)).RunGenerators(comp);
        Check(removed.GetRunResult().Results.Single().GeneratedSources.Length == 0, "removal drops generated class");
        var diagnostic = Generate("x:\n en: broken indentation").Item1.GetRunResult().Diagnostics.Single();
        Check(diagnostic.Location.GetLineSpan().StartLinePosition.Line == 1, "YAML diagnostic line");
        Console.WriteLine($"PASS: {assertions} assertions; generated C# compiled and executed.");
    }
    sealed class TextFile(string path, string text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
