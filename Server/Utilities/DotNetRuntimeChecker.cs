using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Server.Utilities
{
    /// <summary>
    /// Verifies that the server is running on the expected .NET runtime version.
    /// </summary>
    /// <remarks>
    /// This check only runs after the native apphost has already succeeded in loading a
    /// runtime. If no compatible .NET is installed at all, apphost prints its own error
    /// and exits before any managed code (including this class) gets a chance to run.
    /// The <c>StartLunaServer.bat</c> launcher that ships next to <c>Server.exe</c>
    /// covers that earlier failure mode with a pre-flight check.
    /// </remarks>
    internal static class DotNetRuntimeChecker
    {
        /// <summary>
        /// Major version of the .NET runtime the server is built against (see TargetFramework in Server.csproj).
        /// </summary>
        private const int RequiredMajorVersion = 10;

        /// <summary>
        /// Friendly name of the required runtime, shown to the user if the check fails.
        /// </summary>
        private const string RequiredRuntimeName = ".NET 10.0 Runtime";

        /// <summary>
        /// Official Microsoft download page for the required runtime.
        /// </summary>
        private const string RuntimeDownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/10.0";

        /// <summary>
        /// Name of the shared framework the base .NET runtime ships under. Needed to tell apart
        /// a "real" .NET 6 runtime install from one that only has a desktop/ASP.NET variant or
        /// just an SDK entry.
        /// </summary>
        private const string BaseRuntimeMoniker = "Microsoft.NETCore.App";

        /// <summary>
        /// Ensures the currently executing .NET runtime matches the required major version.
        /// If it does not, a clear message is written to the console and the process blocks
        /// on a key press (so the window doesn't vanish when the user double-clicked the exe)
        /// before exiting.
        /// </summary>
        public static void EnsureCorrectRuntimeOrExit()
        {
            var currentVersion = Environment.Version;
            if (currentVersion.Major == RequiredMajorVersion)
                return;

            WriteIncorrectRuntimeMessage(currentVersion);
            BlockUntilKeyPress();
            Environment.Exit(1);
        }

        private static void WriteIncorrectRuntimeMessage(Version currentVersion)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("========================================================================");
            Console.Error.WriteLine(" ERROR: Incorrect .NET runtime detected.");
            Console.Error.WriteLine("------------------------------------------------------------------------");
            Console.Error.WriteLine($" LunaServer requires the {RequiredRuntimeName} to run.");
            Console.Error.WriteLine($" Detected runtime version: {currentVersion}");
            Console.Error.WriteLine($" Process architecture:     {RuntimeInformation.ProcessArchitecture}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(" Please download and install the correct runtime from:");
            Console.Error.WriteLine($"   {RuntimeDownloadUrl}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(" On that page, pick the \"Runtime\" (or \"ASP.NET Core Runtime\") download");
            Console.Error.WriteLine(" that matches your operating system and architecture, then re-run the");
            Console.Error.WriteLine(" server.");

            WriteInstalledRuntimeDiagnostics();

            Console.Error.WriteLine("========================================================================");
            Console.Error.WriteLine();
        }

        /// <summary>
        /// Shells out to <c>dotnet --list-runtimes</c> so the operator can see exactly what
        /// is (and isn't) installed. Highlights the common misinstall case where a .NET
        /// SDK / desktop / ASP.NET variant is present but the base runtime under
        /// <see cref="BaseRuntimeMoniker"/> is missing.
        /// </summary>
        private static void WriteInstalledRuntimeDiagnostics()
        {
            if (!TryListInstalledRuntimes(out var installedRuntimes))
                return;

            Console.Error.WriteLine();
            Console.Error.WriteLine(" Installed .NET runtimes detected on this machine:");

            if (installedRuntimes.Count == 0)
            {
                Console.Error.WriteLine("   (none reported by 'dotnet --list-runtimes')");
                return;
            }

            foreach (var runtime in installedRuntimes)
                Console.Error.WriteLine($"   {runtime}");

            var hasAnyCorrectNet = false;
            var hasBaseCorrectNet = false;
            foreach (var runtime in installedRuntimes)
            {
                if (!LooksLikeMajor(runtime, RequiredMajorVersion))
                    continue;

                hasAnyCorrectNet = true;
                if (runtime.StartsWith(BaseRuntimeMoniker + " ", StringComparison.OrdinalIgnoreCase))
                    hasBaseCorrectNet = true;
            }

            Console.Error.WriteLine();
            if (hasAnyCorrectNet && !hasBaseCorrectNet)
            {
                Console.Error.WriteLine($" NOTE: A .NET {RequiredMajorVersion} install is present, but the base '{BaseRuntimeMoniker}'");
                Console.Error.WriteLine(" runtime that LunaServer needs is missing. This typically happens when");
                Console.Error.WriteLine($" only the .NET {RequiredMajorVersion} SDK listing or a specialized runtime (Desktop /");
                Console.Error.WriteLine(" ASP.NET Core) got picked up. Install the plain \".NET Runtime\" 6.0.x");
                Console.Error.WriteLine(" from the page above.");
            }
            else if (!hasAnyCorrectNet)
            {
                Console.Error.WriteLine($" NOTE: No .NET {RequiredMajorVersion}.x runtime was found. The versions listed above are");
                Console.Error.WriteLine($" not compatible with this server - install .NET {RequiredMajorVersion} as described.");
            }
        }

        /// <summary>
        /// True when <paramref name="runtimeLine"/> (e.g. "Microsoft.NETCore.App 6.0.25 [...]")
        /// reports a version whose major equals <paramref name="requiredMajor"/>. The first
        /// space-separated token after the moniker is the version.
        /// </summary>
        private static bool LooksLikeMajor(string runtimeLine, int requiredMajor)
        {
            var firstSpace = runtimeLine.IndexOf(' ');
            if (firstSpace < 0 || firstSpace + 1 >= runtimeLine.Length)
                return false;

            var afterMoniker = runtimeLine.Substring(firstSpace + 1);
            var versionEnd = afterMoniker.IndexOf(' ');
            var versionToken = versionEnd > 0 ? afterMoniker.Substring(0, versionEnd) : afterMoniker;
            var dot = versionToken.IndexOf('.');
            if (dot <= 0) return false;

            return int.TryParse(versionToken.Substring(0, dot), out var major) && major == requiredMajor;
        }

        private static bool TryListInstalledRuntimes(out List<string> runtimes)
        {
            runtimes = new List<string>();
            try
            {
                var psi = new ProcessStartInfo("dotnet", "--list-runtimes")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;

                var stdout = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(5000))
                {
                    try { proc.Kill(); } catch { /* best-effort */ }
                    return false;
                }

                foreach (var rawLine in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = rawLine.Trim();
                    // Lines look like: "Microsoft.NETCore.App 6.0.25 [C:\Program Files\dotnet\shared\...]"
                    // Strip the trailing install-path bracket so the diagnostic stays compact.
                    var bracket = line.IndexOf('[');
                    if (bracket > 0) line = line.Substring(0, bracket).TrimEnd();
                    if (line.Length > 0) runtimes.Add(line);
                }
                return true;
            }
            catch
            {
                // dotnet CLI not on PATH, or the probe failed for some other reason.
                // Skipping diagnostics is preferable to crashing the error reporter.
                return false;
            }
        }

        /// <summary>
        /// Keeps the console open until the operator acknowledges the error. Tries an
        /// interactive key press first, falls back to reading a line, and finally sleeps
        /// for a long time so a truly non-interactive launch (e.g. from a service wrapper)
        /// still leaves the message visible in logs rather than exiting instantly.
        /// </summary>
        private static void BlockUntilKeyPress()
        {
            Console.Error.WriteLine("Press any key to exit...");

            if (TryBlockOnReadKey())
                return;

            if (TryBlockOnReadLine())
                return;

            // Non-interactive launch: give an operator up to five minutes to notice the
            // message in whatever captured the output before we give up and exit.
            try { Thread.Sleep(TimeSpan.FromMinutes(5)); } catch { /* shutdown requested */ }
        }

        private static bool TryBlockOnReadKey()
        {
            try
            {
                if (Console.IsInputRedirected)
                    return false;
                Console.ReadKey(true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryBlockOnReadLine()
        {
            try
            {
                Console.In.ReadLine();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
