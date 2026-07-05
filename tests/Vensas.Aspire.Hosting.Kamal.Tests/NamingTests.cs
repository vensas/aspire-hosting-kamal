namespace Vensas.Aspire.Hosting.Kamal.Tests;

public class NamingTests
{
    [Theory]
    [InlineData("api", "api")]
    [InlineData("My.Api", "my-api")]
    [InlineData("Frontend App", "frontend-app")]
    [InlineData("worker_1", "worker_1")]
    [InlineData("--edgy--", "edgy")]
    public void SanitizeName_ProducesValidKamalServiceNames(string input, string expected)
    {
        Assert.Equal(expected, KamalServiceResource.SanitizeName(input));
    }

    [Theory]
    [InlineData("pg-password", "PG_PASSWORD")]
    [InlineData("cache.password", "CACHE_PASSWORD")]
    [InlineData("simple", "SIMPLE")]
    public void ToEnvVarName_ProducesDotenvCompatibleNames(string input, string expected)
    {
        Assert.Equal(expected, KamalValueProcessor.ToEnvVarName(input));
    }
}
