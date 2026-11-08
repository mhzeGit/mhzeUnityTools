using System;
using System.Collections.Generic;
using System.Linq;
using Gigaduck.AgenticTools;

// Same options as install.py (--project, --modules, --enable, --disable, --set, --force, --dry-run, --uninstall),
// same exit codes: 0 done, 1 conflicts left alone, 2 error.
internal static class Program
{
    private static int Main(string[] args)
    {
        string project = ".", modulesArg = null;
        var enable = new List<string>();
        var disable = new List<string>();
        var sets = new List<string>();
        bool force = false, dry = false, uninstall = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project": project = args[++i]; break;
                case "--modules": modulesArg = args[++i]; break;
                case "--enable": enable.Add(args[++i]); break;
                case "--disable": disable.Add(args[++i]); break;
                case "--set": sets.Add(args[++i]); break;
                case "--force": force = true; break;
                case "--dry-run": dry = true; break;
                case "--uninstall": uninstall = true; break;
                default: Console.Error.WriteLine("unknown option " + args[i]); return 2;
            }
        }

        try
        {
            string package = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("AGENTIC_PACKAGE_ROOT") ?? "."));
            var installer = new AgenticInstaller(package, project);
            List<string> modules = uninstall ? new List<string>()
                : modulesArg != null ? modulesArg.Split(',').Select(m => m.Trim()).Where(m => m.Length > 0).ToList()
                : installer.CurrentModules();
            modules.AddRange(enable.Where(m => !modules.Contains(m)));
            modules = modules.Where(m => !disable.Contains(m)).ToList();
            if (!uninstall) modules = installer.Normalize(modules);
            Dictionary<string, string> vars = installer.CurrentVariables();
            foreach (string pair in sets)
            {
                int eq = pair.IndexOf('=');
                string name = pair.Substring(0, eq);
                if (!vars.ContainsKey(name)) throw new ArgumentException("unknown variable " + name);
                vars[name] = pair.Substring(eq + 1).Trim().Replace('\\', '/').Trim('/');
            }

            AgenticInstaller.Plan plan = installer.MakePlan(modules, vars, force);
            Console.WriteLine("Agentic Tools " + installer.Version + " -> " + installer.ProjectRoot + " (installed: " + (installer.InstalledVersion ?? "nothing") + ")");
            Console.WriteLine("modules: " + (modules.Count > 0 ? string.Join(", ", modules) : "none (uninstall)"));
            foreach (string line in plan.Describe(false)) Console.WriteLine(line);
            int conflicts = plan.Conflicts.Count();
            if (!dry) installer.Apply(plan);
            Console.WriteLine((dry ? "dry run: " : "done: ") + plan.Changes + " change(s), " + conflicts + " conflict(s)" + (dry ? "; nothing written" : ""));
            return conflicts > 0 ? 1 : 0;
        }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is FormatException)
        {
            Console.Error.WriteLine("ERROR: " + e.Message);
            return 2;
        }
    }
}
