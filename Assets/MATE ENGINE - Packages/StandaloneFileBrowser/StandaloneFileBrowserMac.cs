#if UNITY_STANDALONE_OSX

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SFB {
    public class StandaloneFileBrowserMac : IStandaloneFileBrowser {
        private static string EscapeAppleScript(string value) {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string DefaultLocation(string directory) {
            return string.IsNullOrEmpty(directory)
                ? ""
                : " default location (POSIX file \"" + EscapeAppleScript(directory) + "\")";
        }

        private static string RunAppleScript(string script) {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "mate-engine-sfb-" + Process.GetCurrentProcess().Id + ".applescript");

            try {
                File.WriteAllText(tempFile, script, new UTF8Encoding(false));
                var startInfo = new ProcessStartInfo {
                    FileName = "/usr/bin/osascript",
                    Arguments = "\"" + tempFile.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process process = Process.Start(startInfo)) {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0) {
                        UnityEngine.Debug.LogWarning("[StandaloneFileBrowser] " + error.Trim());
                        return "";
                    }
                    return output.TrimEnd('\r', '\n');
                }
            }
            catch (Exception ex) {
                UnityEngine.Debug.LogWarning("[StandaloneFileBrowser] Failed to show macOS dialog: " + ex.Message);
                return "";
            }
            finally {
                try {
                    File.Delete(tempFile);
                }
                catch {
                    // Temp file cleanup is best effort.
                }
            }
        }

        private static string[] ParsePaths(string output) {
            if (string.IsNullOrEmpty(output)) {
                return new string[0];
            }
            return output.Split(new[] { (char)28 }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string ListScript(string choiceExpression) {
            return "set chosen to " + choiceExpression + "\n" +
                   "if class of chosen is list then\n" +
                   "  set out to \"\"\n" +
                   "  repeat with f in chosen\n" +
                   "    set out to out & (POSIX path of f) & (ASCII character 28)\n" +
                   "  end repeat\n" +
                   "  return out\n" +
                   "else\n" +
                   "  return (POSIX path of chosen)\n" +
                   "end if\n";
        }

        public string[] OpenFilePanel(string title, string directory, ExtensionFilter[] extensions, bool multiselect) {
            string script = ListScript(
                "choose file with prompt \"" + EscapeAppleScript(title) + "\"" +
                DefaultLocation(directory) +
                (multiselect ? " with multiple selections allowed" : " without multiple selections allowed"));
            return ParsePaths(RunAppleScript(script));
        }

        public void OpenFilePanelAsync(string title, string directory, ExtensionFilter[] extensions, bool multiselect, Action<string[]> cb) {
            ThreadPool.QueueUserWorkItem(_ => {
                string[] paths = OpenFilePanel(title, directory, extensions, multiselect);
                if (cb != null) cb(paths);
            });
        }

        public string[] OpenFolderPanel(string title, string directory, bool multiselect) {
            string script = ListScript(
                "choose folder with prompt \"" + EscapeAppleScript(title) + "\"" +
                DefaultLocation(directory) +
                (multiselect ? " with multiple selections allowed" : " without multiple selections allowed"));
            return ParsePaths(RunAppleScript(script));
        }

        public void OpenFolderPanelAsync(string title, string directory, bool multiselect, Action<string[]> cb) {
            ThreadPool.QueueUserWorkItem(_ => {
                string[] paths = OpenFolderPanel(title, directory, multiselect);
                if (cb != null) cb(paths);
            });
        }

        public string SaveFilePanel(string title, string directory, string defaultName, ExtensionFilter[] extensions) {
            string script = "choose file name with prompt \"" + EscapeAppleScript(title) + "\"" +
                            (string.IsNullOrEmpty(defaultName) ? "" : " default name \"" + EscapeAppleScript(defaultName) + "\"") +
                            DefaultLocation(directory);
            return RunAppleScript(script + "\nreturn POSIX path of result\n").Trim();
        }

        public void SaveFilePanelAsync(string title, string directory, string defaultName, ExtensionFilter[] extensions, Action<string> cb) {
            ThreadPool.QueueUserWorkItem(_ => {
                string path = SaveFilePanel(title, directory, defaultName, extensions);
                if (cb != null) cb(path);
            });
        }
    }
}

#endif
