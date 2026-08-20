using System;
using System.IO;

namespace modterm
{
    internal static class ShellExecutable
    {
        public static bool TryResolve(string path, out string resolvedPath)
        {
            resolvedPath = path ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            path = path.Trim().Trim('"');
            resolvedPath = path;

            if (File.Exists(path))
            {
                resolvedPath = Path.GetFullPath(path);
                return true;
            }

            if (Path.IsPathRooted(path))
                return false;

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string rawDir in pathEnv.Split(Path.PathSeparator))
            {
                string dir = rawDir.Trim().Trim('"');
                if (string.IsNullOrEmpty(dir))
                    continue;

                if (TryFileInDirectory(dir, path, out resolvedPath))
                    return true;
            }

            string? systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrEmpty(systemRoot) && TryFileInDirectory(systemRoot, path, out resolvedPath))
                return true;

            resolvedPath = path;
            return false;
        }

        public static string FormatMissingMessage(Shell shell)
        {
            string name = string.IsNullOrWhiteSpace(shell.Name) ? "the selected shell" : $"\"{shell.Name}\"";
            string path = string.IsNullOrWhiteSpace(shell.Path) ? "(no path configured)" : shell.Path;
            return $"The shell {name} was not found at:\n{path}\n\nChoose another shell in the Configuration and Theme Editor, or install the program and try again.";
        }

        public static string FormatLaunchFailureMessage(Shell shell, Exception exception)
        {
            string name = string.IsNullOrWhiteSpace(shell.Name) ? "the selected shell" : $"\"{shell.Name}\"";
            string path = string.IsNullOrWhiteSpace(shell.Path) ? "(no path configured)" : shell.Path;
            return $"Modterm could not start {name}.\n{path}\n\n{exception.Message}";
        }

        private static bool TryFileInDirectory(string directory, string fileName, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            try
            {
                string candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    return true;
                }

                if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    string exe = candidate + ".exe";
                    if (File.Exists(exe))
                    {
                        resolvedPath = exe;
                        return true;
                    }
                }
            }
            catch (ArgumentException)
            {
            }

            return false;
        }
    }
}
