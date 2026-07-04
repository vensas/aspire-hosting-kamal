namespace Aspire.Hosting.Kamal.Tests;

public class DotnetDockerfileGeneratorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kamal-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string CreateProject(string relativePath, string targetFramework = "net10.0")
    {
        var projectPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, $"<Project><PropertyGroup><TargetFramework>{targetFramework}</TargetFramework></PropertyGroup></Project>");
        return projectPath;
    }

    [Fact]
    public void Generate_UsesDetectedTfmRelativeProjectPathAndAppPort()
    {
        var projectPath = CreateProject("src/Web/Web.csproj", "net9.0");

        var dockerfile = DotnetDockerfileGenerator.Generate(projectPath, _root, 8080);

        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build", dockerfile);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:9.0", dockerfile);
        Assert.Contains("dotnet publish \"src/Web/Web.csproj\"", dockerfile);
        Assert.Contains("ENV ASPNETCORE_HTTP_PORTS=8080", dockerfile);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Web.dll\"]", dockerfile);
    }

    [Fact]
    public void DetectDotnetVersion_FallsBackWhenNoTargetFramework()
    {
        var projectPath = Path.Combine(_root, "Empty.csproj");
        File.WriteAllText(projectPath, "<Project></Project>");

        Assert.Equal("10.0", DotnetDockerfileGenerator.DetectDotnetVersion(projectPath));
    }

    [Fact]
    public void FindRepositoryRoot_PrefersGitDirectory()
    {
        var projectPath = CreateProject("src/Api/Api.csproj");
        Directory.CreateDirectory(Path.Combine(_root, ".git"));

        Assert.Equal(_root, DotnetDockerfileGenerator.FindRepositoryRoot(projectPath));
    }

    [Fact]
    public void FindRepositoryRoot_FindsSolutionFile()
    {
        var projectPath = CreateProject("solution/src/Api/Api.csproj");
        File.WriteAllText(Path.Combine(_root, "solution", "App.sln"), "");

        Assert.Equal(Path.Combine(_root, "solution"), DotnetDockerfileGenerator.FindRepositoryRoot(projectPath));
    }

    [Fact]
    public void FindRepositoryRoot_FallsBackToProjectDirectory()
    {
        var projectPath = CreateProject("standalone/Api.csproj");

        Assert.Equal(Path.Combine(_root, "standalone"), DotnetDockerfileGenerator.FindRepositoryRoot(projectPath));
    }
}
