using System.IO;
using System.Runtime.CompilerServices;
using Babel.Core;

namespace Babel.Tests;

static class TestSetup
{
    /// <summary>Before any test: the log and the settings go to a temporary folder, never to the person's real ones.</summary>
    [ModuleInitializer]
    internal static void KeepTheTestsAway()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"babel-tests-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        Log.PathOverride = Path.Combine(dir, "babel.log");
        Settings.Folder = dir;
    }
}
