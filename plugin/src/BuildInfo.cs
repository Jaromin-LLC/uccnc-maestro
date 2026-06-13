namespace Plugins
{
    // The build script (make.ps1) regenerates this value at compile time with a
    // timestamp and git short hash so the running plugin can be matched to a build.
    // This checked-in copy is the fallback used by the IDE / uninstrumented builds.
    internal static class BuildInfo
    {
        public const string Id = "dev";
    }
}
