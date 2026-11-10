using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VS.Localization.Editor
{
    public static class LocalizationVerification
    {
        [InitializeOnLoadMethod]
        private static void WatchYamlEdit()
        {
            if (Environment.GetEnvironmentVariable("VS_LOCALIZATION_WATCH") != "1") return;
            EditorApplication.update += () =>
            {
                if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && EditorApplication.timeSinceStartup > 20)
                    VerifyExample();
            };
        }

        public static void VerifyExample()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp");
            var type = assembly.GetType("Strings", true);
            if (typeof(LocalizationVerification).Assembly.GetType("Strings") != null) throw new Exception("Catalog leaked into Editor assembly");
            if (type.IsPublic || !type.IsClass) throw new Exception("Expected internal class Strings");
            var isolated = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Localization.Isolated").GetType("Strings", true);
            if (isolated.GetProperty("Yes") != null || type.GetProperty("Only") != null) throw new Exception("Catalogs crossed assemblies");
            if ((string)isolated.GetProperty("Only").GetValue(Activator.CreateInstance(isolated, "ru")) != "Отдельно") throw new Exception("Isolated catalog failed");
            var strings = Activator.CreateInstance(type, "ru-RU");
            if ((string)type.GetProperty("Yes").GetValue(strings) != (Environment.GetEnvironmentVariable("VS_LOCALIZATION_EXPECTED_YES") ?? "да")) throw new Exception("Wrong translation");
            var method = type.GetMethod("ItemsCount");
            if (method.GetParameters()[1].ParameterType != typeof(int)) throw new Exception("Wrong parameter type");
            var text = (string)method.Invoke(strings, new object[] { "Анна", 3 });
            if (text != "Анна, у тебя 3 предметов.") throw new Exception("Wrong formatted text: " + text);
            if (System.IO.File.Exists("Assets/Localization/Strings.g.cs")) throw new Exception("Physical .g.cs exists");
            Debug.Log("VS.LOCALIZATION UNITY VERIFICATION PASSED: internal class, locale fallback, typed parameters, virtual C#.");
            EditorApplication.Exit(0);
        }
    }
}
