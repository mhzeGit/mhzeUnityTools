using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.AgenticTools.Mcp
{
    /// <summary>
    /// Starts the MCP for Unity server automatically when the Editor loads, so no button press is
    /// needed. The package already implements this behind its "Auto-Start on Editor Load"
    /// preference; this script enables that preference permanently and covers the single domain
    /// load on which the preference flips from off to on, where the package's own
    /// [InitializeOnLoad] handler has already passed its pre-check and will not act until the
    /// next reload. Part of the Agentic Tools package; compiled only when MCP for Unity (com.coplaydev.unity-mcp) is installed.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpAutoStartOnLoad
    {
        private const string AutoStartPref = "MCPForUnity.AutoStartOnLoad";
        private const float FallbackGraceSeconds = 3f;
        private const double TotalTimeoutSeconds = 300.0;

        static McpAutoStartOnLoad()
        {
            if (Application.isBatchMode)
                return;

            if (EditorPrefs.GetBool(AutoStartPref, false))
                return; // the package's own auto-start owns every later load

            EditorPrefs.SetBool(AutoStartPref, true);
            Debug.Log("[MCP Auto-Start] Enabled 'Auto-Start on Editor Load' for MCP for Unity.");

            EditorApplication.delayCall += Kick;
        }

        private static void Kick() => _ = StartServerAsync();

        private static async Task StartServerAsync()
        {
            try
            {
                if (!EditorConfigurationCache.Instance.UseHttpTransport)
                    return; // stdio always auto-starts on its own

                var server = MCPServiceLocator.Server;
                double startTime = EditorApplication.timeSinceStartup;

                // Remote scope has no local server to launch; just connect the bridge.
                if (!server.IsLocalUrl())
                {
                    await MCPServiceLocator.Bridge.StartAsync();
                    return;
                }

                bool launchRequested = false;

                while (EditorApplication.timeSinceStartup - startTime < TotalTimeoutSeconds)
                {
                    if (MCPServiceLocator.Bridge.IsRunning)
                        return;

                    if (server.IsLocalHttpServerReachable())
                    {
                        if (await MCPServiceLocator.Bridge.StartAsync())
                        {
                            Debug.Log("[MCP Auto-Start] MCP server running and bridge connected.");
                            return;
                        }
                    }
                    else if (!launchRequested
                        && !server.HasManagedServerLaunchHandle
                        && EditorApplication.timeSinceStartup - startTime > FallbackGraceSeconds)
                    {
                        // Nothing is booting the server (the package's handler skipped this load),
                        // so launch it here the same way the package's auto-start would.
                        launchRequested = true;
                        server.StartLocalHttpServer(quiet: true);
                    }

                    await Task.Delay(500);
                }

                Debug.LogWarning("[MCP Auto-Start] Timed out starting the MCP server; start it from the MCP window.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Auto-Start] Failed to start the MCP server: {ex.Message}");
            }
        }
    }
}
