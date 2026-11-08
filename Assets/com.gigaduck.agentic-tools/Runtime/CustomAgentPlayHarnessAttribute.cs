using System;

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>
    /// Put <c>[assembly: Gigaduck.AgenticTools.AgentPlay.CustomAgentPlayHarness]</c> in a project that ships its own complete
    /// agent play harness (one that answers agentplay.py's inbox commands itself): the package's <c>AgentPlayHarness</c> then
    /// stays off, so two harnesses never read the same inbox. Projects that only add ops use <c>AgentPlayHarness.RegisterOp</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class CustomAgentPlayHarnessAttribute : Attribute
    {
    }
}
