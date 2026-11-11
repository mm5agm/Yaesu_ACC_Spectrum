global using Xunit;

namespace YaesuAccScope.Tests;

/// <summary>The captures in the repo's captures/ folder, found by walking up from the test's folder.</summary>
internal static class Captures
{
    public const int FrameLength = 4096;

    private static readonly Lazy<string> Folder = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var captures = Path.Combine(dir.FullName, "captures");
            if (Directory.Exists(Path.Combine(captures, "step5"))) return captures;
        }
        throw new DirectoryNotFoundException("Can't find captures/step5 above " + AppContext.BaseDirectory);
    });

    public static string PathOf(string name) => Path.Combine(Folder.Value, name);

    /// <summary>
    /// The frames in a step 5 capture. live_view.py --dump saves them back to back,
    /// 4096 bytes each, with no packet headers.
    /// </summary>
    public static IReadOnlyList<AccFrame> Step5(string name)
    {
        var bytes = File.ReadAllBytes(PathOf(Path.Combine("step5", name + ".bin")));
        Assert.Equal(0, bytes.Length % FrameLength);
        var frames = new List<AccFrame>();
        for (int at = 0; at < bytes.Length; at += FrameLength)
            frames.Add(AccFrame.Parse(bytes.AsSpan(at, FrameLength).ToArray()));
        return frames;
    }
}
