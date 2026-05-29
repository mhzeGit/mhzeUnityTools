using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GitIntegration
{

    [Serializable]
    public class GitCommitInfo
    {
        public string Hash;
        public string ShortHash;
        public string Author;
        public string AuthorEmail;
        public string Date;
        public string RelativeDate;
        public string Message;
        public List<string> Parents = new List<string>(); // full parent hashes
        public List<string> Refs    = new List<string>(); // branch/tag labels (%D)
        public List<GitFileChange> Files = new List<GitFileChange>();
    }

    [Serializable]
    public class GitFileChange
    {
        public string Status;      // A, M, D, R, etc.
        public string FilePath;
        public string OldPath;     // for renames
    }

    [Serializable]
    public class GitBranchInfo
    {
        public string Name;
        public bool IsCurrent;
        public bool IsRemote;
        public string LastCommitHash;
        public string LastCommitMessage;
        public string LastCommitDate;
        public string TrackingBranch;
        public int Ahead;
        public int Behind;
    }

    [Serializable]
    public class GitStatusEntry
    {
        public string Status;
        public string FilePath;
    }

    [Serializable]
    public class GitUserConfig
    {
        public string UserName;
        public string UserEmail;
    }

    [Serializable]
    public class GitRemoteInfo
    {
        public string Name;
        public string FetchUrl;
        public string PushUrl;
    }


    public static class GitOperations
    {
        private static string _repoRoot;
        private static bool _repoRootResolved;

        public static string RepoRoot
        {
            get
            {
                if (!_repoRootResolved)
                {
                    _repoRootResolved = true;
                    _repoRoot = ResolveRepoRoot();
                }
                return _repoRoot ?? "";
            }
        }

        private static string ResolveRepoRoot()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --show-toplevel",
                    WorkingDirectory = Application.dataPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using (var proc = new Process { StartInfo = psi })
                {
                    proc.Start();
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(10000);
                    if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                        return output.Trim().Replace("/", "\\");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Git] Could not resolve repo root: {ex.Message}");
            }
            return null;
        }

        public static void InvalidateRepoRoot()
        {
            _repoRoot = null;
            _repoRootResolved = false;
        }


        public static (string output, string error, int exitCode) RunGit(string arguments, int timeoutMs = 30000)
        {
            string root = RepoRoot;
            string workDir = string.IsNullOrEmpty(root) ? Application.dataPath : root;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using (var proc = new Process { StartInfo = psi })
                {
                    var stdout = new StringBuilder();
                    var stderr = new StringBuilder();

                    proc.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    if (!proc.WaitForExit(timeoutMs))
                    {
                        proc.Kill();
                        return ("", "Git process timed out.", -1);
                    }

                    proc.WaitForExit(); // flush async buffers

                    return (stdout.ToString().TrimEnd(), stderr.ToString().TrimEnd(), proc.ExitCode);
                }
            }
            catch (Exception ex)
            {
                return ("", $"Failed to run git: {ex.Message}", -1);
            }
        }


        public static bool IsGitInstalled()
        {
            var (output, _, code) = RunGit("--version");
            return code == 0 && output.StartsWith("git version");
        }

        public static string GetGitVersion()
        {
            var (output, _, code) = RunGit("--version");
            return code == 0 ? output.Replace("git version ", "") : "unknown";
        }

        public static bool IsInsideRepo()
        {
            var (output, _, code) = RunGit("rev-parse --is-inside-work-tree");
            return code == 0 && output.Trim() == "true";
        }


        public static bool InitRepo()
        {
            var (_, error, code) = RunGit("init");
            if (code != 0) Debug.LogError($"[Git] init failed: {error}");
            InvalidateRepoRoot();
            return code == 0;
        }

        public static bool AddRemote(string name, string url)
        {
            var (_, error, code) = RunGit($"remote add {name} {url}");
            if (code != 0 && !error.Contains("already exists"))
            {
                Debug.LogError($"[Git] remote add failed: {error}");
                return false;
            }
            return true;
        }

        public static bool SetRemoteUrl(string name, string url)
        {
            var (_, error, code) = RunGit($"remote set-url {name} {url}");
            if (code != 0) Debug.LogError($"[Git] remote set-url failed: {error}");
            return code == 0;
        }


        public static GitUserConfig GetUserConfig()
        {
            var (name, _, _) = RunGit("config user.name");
            var (email, _, _) = RunGit("config user.email");
            return new GitUserConfig { UserName = name.Trim(), UserEmail = email.Trim() };
        }

        public static bool SetUserConfig(string userName, string email)
        {
            var (_, e1, c1) = RunGit($"config user.name \"{userName}\"");
            var (_, e2, c2) = RunGit($"config user.email \"{email}\"");
            if (c1 != 0) Debug.LogError($"[Git] config user.name failed: {e1}");
            if (c2 != 0) Debug.LogError($"[Git] config user.email failed: {e2}");
            return c1 == 0 && c2 == 0;
        }


        public static List<GitRemoteInfo> GetRemotes()
        {
            var list = new List<GitRemoteInfo>();
            var (output, _, code) = RunGit("remote -v");
            if (code != 0) return list;

            var seen = new HashSet<string>();
            foreach (var line in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                string name = parts[0];
                string url = parts[1];
                bool isFetch = line.Contains("(fetch)");

                if (!seen.Contains(name))
                {
                    seen.Add(name);
                    list.Add(new GitRemoteInfo { Name = name, FetchUrl = url, PushUrl = url });
                }
                else
                {
                    var existing = list.Find(r => r.Name == name);
                    if (existing != null)
                    {
                        if (isFetch) existing.FetchUrl = url;
                        else existing.PushUrl = url;
                    }
                }
            }
            return list;
        }


        public static bool Fetch()
        {
            var (_, error, code) = RunGit("fetch --all", 60000);
            if (code != 0) Debug.LogWarning($"[Git] fetch failed: {error}");
            return code == 0;
        }


        public static string GetCurrentBranch()
        {
            var (output, _, code) = RunGit("branch --show-current");
            if (code == 0 && !string.IsNullOrEmpty(output.Trim()))
                return output.Trim();

            var (o2, _, c2) = RunGit("rev-parse --short HEAD");
            return c2 == 0 ? $"(detached {o2.Trim()})" : "(unknown)";
        }

        public static List<GitBranchInfo> GetBranches(bool includeRemote = true)
        {
            string args = includeRemote ? "branch -a --format=%(refname:short)|%(objectname:short)|%(subject)|%(creatordate:short)|%(upstream:short)|%(HEAD)" : "branch --format=%(refname:short)|%(objectname:short)|%(subject)|%(creatordate:short)|%(upstream:short)|%(HEAD)";
            var (output, _, code) = RunGit(args);
            var list = new List<GitBranchInfo>();
            if (code != 0) return list;

            foreach (var line in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('|');
                if (parts.Length < 4) continue;

                var b = new GitBranchInfo
                {
                    Name = parts[0].Trim(),
                    LastCommitHash = parts.Length > 1 ? parts[1].Trim() : "",
                    LastCommitMessage = parts.Length > 2 ? parts[2].Trim() : "",
                    LastCommitDate = parts.Length > 3 ? parts[3].Trim() : "",
                    TrackingBranch = parts.Length > 4 ? parts[4].Trim() : "",
                    IsCurrent = parts.Length > 5 && parts[5].Trim() == "*",
                    IsRemote = parts[0].Trim().StartsWith("origin/"),
                };
                list.Add(b);
            }

            var current = list.Find(b => b.IsCurrent);
            if (current != null && !string.IsNullOrEmpty(current.TrackingBranch))
            {
                var (ab, _, abc) = RunGit($"rev-list --left-right --count {current.TrackingBranch}...HEAD");
                if (abc == 0)
                {
                    var abParts = ab.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (abParts.Length == 2)
                    {
                        int.TryParse(abParts[0], out current.Behind);
                        int.TryParse(abParts[1], out current.Ahead);
                    }
                }
            }

            return list;
        }


        public static List<GitStatusEntry> GetStatus()
        {
            var (output, _, code) = RunGit("status --porcelain=v1");
            var list = new List<GitStatusEntry>();
            if (code != 0) return list;

            foreach (var line in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line) || line.Length < 4) continue;
                string status = line.Substring(0, 2).Trim();
                string path = line.Substring(3).Trim().Trim('"');
                list.Add(new GitStatusEntry { Status = status, FilePath = path });
            }
            return list;
        }

        public static int GetUncommittedCount()
        {
            return GetStatus().Count;
        }


        private const string LOG_FORMAT = "%H|%h|%an|%ae|%ai|%ar|%s";
        private const char LOG_SEPARATOR = '|';

        public static List<GitCommitInfo> GetLog(int count = 100, string filePath = null)
        {
            string pathArg = string.IsNullOrEmpty(filePath) ? "" : $"--follow -- \"{NormalizeToRepoRelative(filePath)}\"";
            string args = $"log --pretty=format:\"{LOG_FORMAT}\" -n {count} {pathArg}";
            var (output, _, code) = RunGit(args);
            var list = new List<GitCommitInfo>();
            if (code != 0) return list;

            foreach (var line in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(new[] { LOG_SEPARATOR }, 7);
                if (parts.Length < 7) continue;
                list.Add(new GitCommitInfo
                {
                    Hash = parts[0].Trim(),
                    ShortHash = parts[1].Trim(),
                    Author = parts[2].Trim(),
                    AuthorEmail = parts[3].Trim(),
                    Date = parts[4].Trim(),
                    RelativeDate = parts[5].Trim(),
                    Message = parts[6].Trim(),
                });
            }
            return list;
        }

        public static List<GitCommitInfo> GetLogAll(int count = 300)
        {
            string args = $"log --all --topo-order -n {count} --pretty=format:\"%H%x1F%h%x1F%P%x1F%an%x1F%ae%x1F%ai%x1F%ar%x1F%s%x1F%D\"";
            var (output, _, code) = RunGit(args);
            var list = new List<GitCommitInfo>();
            if (code != 0) return list;

            foreach (var line in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\x1F');
                if (parts.Length < 9) continue;

                var parentList = parts[2].Trim()
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

                var refList = parts[8].Trim()
                    .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim())
                    .Where(r => !string.IsNullOrEmpty(r))
                    .ToList();

                list.Add(new GitCommitInfo
                {
                    Hash         = parts[0].Trim(),
                    ShortHash    = parts[1].Trim(),
                    Parents      = parentList,
                    Author       = parts[3].Trim(),
                    AuthorEmail  = parts[4].Trim(),
                    Date         = parts[5].Trim(),
                    RelativeDate = parts[6].Trim(),
                    Message      = parts[7].Trim(),
                    Refs         = refList,
                });
            }
            return list;
        }

        public static List<GitFileChange> GetCommitFiles(string commitHash)
        {
            var (output, _, code) = RunGit($"diff-tree --no-commit-id -r --name-status {commitHash}");
            var list = new List<GitFileChange>();
            if (code != 0) return list;

            foreach (var line in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;

                var fc = new GitFileChange
                {
                    Status = parts[0].Trim(),
                    FilePath = parts[parts.Length - 1].Trim(),
                };
                if (parts.Length > 2)
                    fc.OldPath = parts[1].Trim();

                list.Add(fc);
            }
            return list;
        }


        public static string GetDiff(string commitHash, string filePath = null)
        {
            string fileArg = string.IsNullOrEmpty(filePath) ? "" : $"-- \"{NormalizeToRepoRelative(filePath)}\"";
            var (output, _, code) = RunGit($"diff {commitHash}~1 {commitHash} {fileArg}");
            return code == 0 ? output : $"(Could not retrieve diff for {commitHash})";
        }

        public static string GetDiffForWorkingCopy(string filePath = null)
        {
            string fileArg = string.IsNullOrEmpty(filePath) ? "" : $"-- \"{NormalizeToRepoRelative(filePath)}\"";
            var (output, _, code) = RunGit($"diff {fileArg}");
            return code == 0 ? output : "";
        }

        public static string GetFileDiffBetweenCommits(string fromHash, string toHash, string filePath)
        {
            var (output, _, code) = RunGit($"diff {fromHash} {toHash} -- \"{NormalizeToRepoRelative(filePath)}\"");
            return code == 0 ? output : "";
        }

        public static string GetFileContentAtCommit(string commitHash, string filePath)
        {
            string relPath = NormalizeToRepoRelative(filePath).Replace("\\", "/");
            var (output, _, code) = RunGit($"show {commitHash}:\"{relPath}\"");
            return code == 0 ? output : null;
        }


        public static bool RestoreFileFromCommit(string commitHash, string filePath)
        {
            string relPath = NormalizeToRepoRelative(filePath);
            var (_, error, code) = RunGit($"checkout {commitHash} -- \"{relPath}\"");
            if (code != 0)
            {
                Debug.LogError($"[Git] restore failed: {error}");
                return false;
            }
            RunGit($"reset HEAD -- \"{relPath}\"");
            return true;
        }

        public static bool DiscardLocalChanges(string filePath)
        {
            string relPath = NormalizeToRepoRelative(filePath);
            var (_, error, code) = RunGit($"checkout HEAD -- \"{relPath}\"");
            if (code != 0)
            {
                Debug.LogError($"[Git] discard failed: {error}");
                return false;
            }
            return true;
        }


        public static bool HasGitIgnore()
        {
            if (string.IsNullOrEmpty(RepoRoot)) return false;
            return File.Exists(Path.Combine(RepoRoot, ".gitignore"));
        }

        public static bool CreateUnityGitIgnore()
        {
            if (string.IsNullOrEmpty(RepoRoot)) return false;
            string path = Path.Combine(RepoRoot, ".gitignore");
            string content = @"# Unity generated
/[Ll]ibrary/
/[Tt]emp/
/[Oo]bj/
/[Bb]uild/
/[Bb]uilds/
/[Ll]ogs/
/[Uu]ser[Ss]ettings/

# MemoryCaptures
/[Mm]emoryCaptures/

# Recordings
/[Rr]ecordings/

# Asset meta data should be tracked with git-lfs or normally
# Uncomment if using LFS for large binary files
# *.psd filter=lfs diff=lfs merge=lfs -text
# *.fbx filter=lfs diff=lfs merge=lfs -text

# Autogenerated Coverage Report
/[Cc]ode[Cc]overage/

# Autogenerated VS/Rider solution and target files
ExportedObj/
.consulo/
*.csproj
*.unityproj
*.sln
*.suo
*.tmp
*.user
*.userprefs
*.pidb
*.booproj
*.svd
*.pdb
*.mdb
*.opendb
*.VC.db

# Unity3D Generated File on Crash Reports
sysinfo.txt

# Builds
*.apk
*.aab
*.unitypackage
*.app

# Crashlytics
crashlytics-build.properties

# Packed Addressables
/[Aa]ssets/[Aa]ddressable[Aa]ssets[Dd]ata/*/*.bin*

# TextMeshPro
/[Aa]ssets/[Tt]ext[Mm]esh*[Pp]ro/

# Plastic SCM
ignore.conf
plastic.selector
";
            File.WriteAllText(path, content, Encoding.UTF8);
            return true;
        }


        public static bool IsLfsInstalled()
        {
            var (output, _, code) = RunGit("lfs version");
            return code == 0 && output.Contains("git-lfs");
        }

        public static bool InitLfs()
        {
            var (_, error, code) = RunGit("lfs install");
            if (code != 0) Debug.LogError($"[Git] lfs install failed: {error}");
            return code == 0;
        }


        public static string NormalizeToRepoRelative(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";
            string normalized = fullPath.Replace("\\", "/");
            string root = RepoRoot.Replace("\\", "/");
            if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(root.Length).TrimStart('/');
            return normalized;
        }

        public static string StatusToLabel(string status)
        {
            switch (status.ToUpper())
            {
                case "A": return "Added";
                case "M": return "Modified";
                case "D": return "Deleted";
                case "R": return "Renamed";
                case "C": return "Copied";
                case "U": return "Unmerged";
                case "??": return "Untracked";
                case "!!": return "Ignored";
                default: return status;
            }
        }

        public static Color StatusToColor(string status)
        {
            switch (status.ToUpper())
            {
                case "A":
                case "??": return new Color(0.4f, 0.9f, 0.4f);   // green
                case "M": return new Color(0.9f, 0.8f, 0.3f);    // yellow
                case "D": return new Color(0.9f, 0.35f, 0.35f);  // red
                case "R":
                case "C": return new Color(0.5f, 0.7f, 1f);      // blue
                default: return Color.white;
            }
        }
    }
}
