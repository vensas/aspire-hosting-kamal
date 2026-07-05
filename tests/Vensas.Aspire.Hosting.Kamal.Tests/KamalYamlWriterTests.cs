using Vensas.Aspire.Hosting.Kamal.Model;

namespace Vensas.Aspire.Hosting.Kamal.Tests;

public class KamalYamlWriterTests
{
    [Fact]
    public void Serialize_MinimalConfig_EmitsRequiredKeysAndOmitsNulls()
    {
        var config = new KamalDeployConfig
        {
            Service = "api",
            Image = "my-org/api",
            Servers = new() { ["web"] = new() { Hosts = ["1.2.3.4"] } },
            Registry = new() { Server = "ghcr.io", Username = "me", Password = new List<string> { "KAMAL_REGISTRY_PASSWORD" } }
        };

        var yaml = KamalYamlWriter.Serialize(config);

        Assert.Contains("service: api", yaml);
        Assert.Contains("image: my-org/api", yaml);
        Assert.Contains("- 1.2.3.4", yaml);
        Assert.Contains("server: ghcr.io", yaml);
        Assert.Contains("- KAMAL_REGISTRY_PASSWORD", yaml);
        Assert.DoesNotContain("proxy:", yaml);
        Assert.DoesNotContain("accessories:", yaml);
        Assert.DoesNotContain("ssh:", yaml);
        Assert.DoesNotContain("volumes:", yaml);
    }

    [Fact]
    public void Serialize_FullConfig_EmitsProxyEnvAndAccessories()
    {
        var config = new KamalDeployConfig
        {
            Service = "api",
            Image = "my-org/api",
            Servers = new() { ["web"] = new() { Hosts = ["1.2.3.4"] } },
            Proxy = new() { Ssl = true, Host = "api.example.com", AppPort = 8080, Healthcheck = new() { Path = "/health" } },
            Env = new()
            {
                Clear = new() { ["FOO"] = "bar" },
                Secret = ["ConnectionStrings__db"]
            },
            Accessories = new()
            {
                ["db"] = new()
                {
                    Image = "postgres:17",
                    Host = "1.2.3.4",
                    Volumes = ["db-data:/var/lib/postgresql"],
                    Options = new() { ["entrypoint"] = "/bin/sh" }
                }
            }
        };

        var yaml = KamalYamlWriter.Serialize(config);

        Assert.Contains("ssl: true", yaml);
        Assert.Contains("host: api.example.com", yaml);
        Assert.Contains("app_port: 8080", yaml);
        Assert.Contains("path: /health", yaml);
        Assert.Contains("FOO: bar", yaml);
        Assert.Contains("- ConnectionStrings__db", yaml);
        Assert.Contains("image: postgres:17", yaml);
        Assert.Contains("- db-data:/var/lib/postgresql", yaml);
        Assert.Contains("entrypoint: /bin/sh", yaml);
    }

    [Fact]
    public void Serialize_UsesSnakeCaseAliasesForMultiWordKeys()
    {
        var config = new KamalDeployConfig
        {
            Service = "api",
            Image = "img",
            Servers = new() { ["web"] = new() { Hosts = ["h"] } },
            RetainContainers = 3,
            MinimumVersion = "2.0.0"
        };

        var yaml = KamalYamlWriter.Serialize(config);

        Assert.Contains("retain_containers: 3", yaml);
        Assert.Contains("minimum_version: 2.0.0", yaml);
    }
}
