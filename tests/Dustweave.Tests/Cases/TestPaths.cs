// Test assets may belong to an independent source export while the caller keeps its own cwd.
internal static class TestPaths
{
    public static string SourceRoot
    {
        get
        {
            var root = Path.GetFullPath(Environment.GetEnvironmentVariable("DUSTWEAVE_TEST_SOURCE_ROOT") ?? Environment.CurrentDirectory);
            if (!File.Exists(Path.Combine(root, "src/Core/Dustweave.Core.csproj")))
                throw new InvalidDataException("The test source root is incomplete.");
            return root;
        }
    }
}
