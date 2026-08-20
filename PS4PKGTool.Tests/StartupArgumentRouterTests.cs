using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Startup;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class StartupArgumentRouterTests
{
    [TestMethod]
    public void NoArguments_PreservesNormalStartupRoute()
    {
        StartupRoute route = StartupArgumentRouter.Route(Array.Empty<string>());

        Assert.AreEqual(StartupRouteKind.NormalApplication, route.Kind);
    }

    [TestMethod]
    public void OnePkgPath_RoutesToMiniViewer_AndPreservesSpaces()
    {
        string directory = CreateTemporaryDirectory("pkg route with spaces");
        string packagePath = Path.Combine(directory, "Example Package.PKG");
        File.WriteAllBytes(packagePath, new byte[] { 1 });

        try
        {
            StartupRoute route = StartupArgumentRouter.Route(new[] { packagePath });

            Assert.AreEqual(StartupRouteKind.MiniPkgViewer, route.Kind);
            Assert.AreEqual(Path.GetFullPath(packagePath), route.PackagePath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void MissingPkgPath_IsInvalidAndNeverRoutesToMain()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pkg");

        StartupRoute route = StartupArgumentRouter.Route(new[] { missingPath });

        Assert.AreEqual(StartupRouteKind.InvalidArguments, route.Kind);
        Assert.IsFalse(string.IsNullOrWhiteSpace(route.ErrorMessage));
    }

    [TestMethod]
    public void WrongExtension_IsInvalid()
    {
        string path = Path.GetTempFileName();
        try
        {
            StartupRoute route = StartupArgumentRouter.Route(new[] { path });
            Assert.AreEqual(StartupRouteKind.InvalidArguments, route.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ZeroBytePkg_IsInvalid()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pkg");
        File.WriteAllBytes(path, Array.Empty<byte>());
        try
        {
            StartupRoute route = StartupArgumentRouter.Route(new[] { path });
            Assert.AreEqual(StartupRouteKind.InvalidArguments, route.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void MultipleArguments_AreInvalidAndNeverRouteToMain()
    {
        StartupRoute route = StartupArgumentRouter.Route(new[] { "one.pkg", "two.pkg" });
        Assert.AreEqual(StartupRouteKind.InvalidArguments, route.Kind);
    }

    private static string CreateTemporaryDirectory(string suffix)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + " " + suffix);
        Directory.CreateDirectory(path);
        return path;
    }
}
