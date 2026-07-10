using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;

namespace backend.Tests;

/// <summary>
/// DavDatabaseContext resolves its sqlite path from CONFIG_PATH into a static Lazy, so the variable
/// has to be set before anything touches the context. A module initializer runs before any test.
/// </summary>
internal static class TestEnvironment
{
    private static readonly object Gate = new();
    private static bool _migrated;

    [ModuleInitializer]
    internal static void Init()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nzbdav-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("CONFIG_PATH", dir);
    }

    /// <summary>Creates a context against the temp database, applying migrations exactly once.</summary>
    internal static DavDatabaseContext NewContext()
    {
        var ctx = new DavDatabaseContext();
        lock (Gate)
        {
            if (!_migrated)
            {
                ctx.Database.Migrate();
                _migrated = true;
            }
        }

        return ctx;
    }
}
