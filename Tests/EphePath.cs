namespace SharpAstrology.SwissEph.Tests;

/// <summary>
/// The ephemeris files live in <c>ephe/</c> at the root of the working copy. The path is
/// resolved by walking upwards from the test binary, so the tests do not depend on the working
/// directory.
/// </summary>
public static class EphePath
{
    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("SWISSEPH_EPHE_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Directory.Exists(configured))
                throw new DirectoryNotFoundException($"SWISSEPH_EPHE_PATH does not exist: {configured}");
            return configured;
        }
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "ephe");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"No ephe directory found above {AppContext.BaseDirectory}.");
    }
}
