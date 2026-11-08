using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>
    /// Keeps the agent player build fast by removing shader variants that make no visible difference to a play-test.
    /// It only acts when <c>tools/agentplay/agentplay.py</c> sets the environment variable <c>AGENTPLAY_BUILD</c>, so
    /// builds of the real game keep every variant. With <c>AGENTPLAY_SHADER_REPORT</c> set to a file path it instead
    /// reports how many variants each shader, pass and keyword would compile and drops them all, a dry run that compiles
    /// nothing. Changes no asset. Editor only.
    /// </summary>
    public sealed class AgentPlayShaders : IPreprocessShaders, IPostprocessBuildWithReport
    {
        private static readonly Dictionary<string, Dictionary<string, int>> Keywords = new Dictionary<string, Dictionary<string, int>>();
        private static readonly Dictionary<string, int> Totals = new Dictionary<string, int>();

        public int callbackOrder => int.MaxValue; // after URP's own stripping

        private static string ReportPath => Environment.GetEnvironmentVariable("AGENTPLAY_SHADER_REPORT");

        private static bool Strip => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AGENTPLAY_BUILD"));

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (Strip)
            {
                // Each pair is one on/off switch; the runtime falls back to the nearest remaining variant.
                RemoveWith(data, "_MAIN_LIGHT_SHADOWS"); // the PC pipeline asset always uses cascades
                RemoveWith(data, "LOD_FADE_CROSSFADE"); // objects pop between LODs instead of fading
                RemoveWithout(data, "_SHADOWS_SOFT"); // the soft variant also serves hard-shadow lights
                RemoveWithout(data, "_LIGHT_COOKIES"); // the cookie variant also serves lights without one
            }

            if (string.IsNullOrEmpty(ReportPath)) return;

            string key = shader.name + " | " + snippet.passName + " | " + snippet.shaderType;
            Totals[key] = (Totals.TryGetValue(key, out int total) ? total : 0) + data.Count;
            if (!Keywords.TryGetValue(key, out Dictionary<string, int> counts)) Keywords[key] = counts = new Dictionary<string, int>();
            foreach (ShaderCompilerData variant in data)
                foreach (ShaderKeyword keyword in variant.shaderKeywordSet.GetShaderKeywords())
                {
                    string name = keyword.name;
                    counts[name] = (counts.TryGetValue(name, out int n) ? n : 0) + 1;
                }

            data.Clear();
        }

        private static bool Has(ShaderCompilerData variant, string keyword) =>
            variant.shaderKeywordSet.GetShaderKeywords().Any(k => k.name == keyword);

        /// <summary>Removes the variants that use the keyword, unless that would leave none.</summary>
        private static void RemoveWith(IList<ShaderCompilerData> data, string keyword) => Remove(data, v => Has(v, keyword));

        /// <summary>Removes the variants that do not use the keyword, if the keyword is used at all.</summary>
        private static void RemoveWithout(IList<ShaderCompilerData> data, string keyword) => Remove(data, v => !Has(v, keyword));

        private static void Remove(IList<ShaderCompilerData> data, Func<ShaderCompilerData, bool> unwanted)
        {
            bool[] drop = data.Select(unwanted).ToArray();
            if (drop.All(d => d) || !drop.Any(d => d)) return; // never empty a pass: that would turn it pink
            for (int i = data.Count - 1; i >= 0; i--)
                if (drop[i]) data.RemoveAt(i);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (string.IsNullOrEmpty(ReportPath)) return;

            var text = new StringBuilder();
            text.AppendLine("total variants: " + Totals.Values.Sum());
            foreach (KeyValuePair<string, int> pass in Totals.OrderByDescending(p => p.Value).Take(40))
            {
                text.AppendLine();
                text.AppendLine(pass.Value + "  " + pass.Key);
                foreach (KeyValuePair<string, int> keyword in Keywords[pass.Key].OrderByDescending(k => k.Value))
                    text.AppendLine("    " + keyword.Value + "  " + keyword.Key);
            }

            File.WriteAllText(ReportPath, text.ToString());
        }
    }
}
