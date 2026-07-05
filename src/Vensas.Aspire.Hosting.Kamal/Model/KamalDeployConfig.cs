using YamlDotNet.Serialization;

namespace Vensas.Aspire.Hosting.Kamal.Model;

/// <summary>
/// Typed representation of a Kamal <c>config/deploy.yml</c> file.
/// See https://kamal-deploy.org/docs/configuration/overview/ for the schema.
/// </summary>
public sealed class KamalDeployConfig
{
    /// <summary>The Kamal service name. Container names on the hosts derive from this.</summary>
    [YamlMember(Alias = "service")]
    public string Service { get; set; } = null!;

    /// <summary>The image name (without tag) that Kamal builds and deploys, e.g. <c>my-org/web</c>.</summary>
    [YamlMember(Alias = "image")]
    public string Image { get; set; } = null!;

    /// <summary>Server roles mapping role name (e.g. <c>web</c>) to hosts and role options.</summary>
    [YamlMember(Alias = "servers")]
    public Dictionary<string, KamalServerRole> Servers { get; set; } = [];

    [YamlMember(Alias = "proxy")]
    public KamalProxy? Proxy { get; set; }

    [YamlMember(Alias = "registry")]
    public KamalRegistry Registry { get; set; } = new();

    [YamlMember(Alias = "builder")]
    public KamalBuilder? Builder { get; set; }

    [YamlMember(Alias = "env")]
    public KamalEnv? Env { get; set; }

    /// <summary>Long-lived supporting containers (databases, caches, ...) managed via <c>kamal accessory</c>.</summary>
    [YamlMember(Alias = "accessories")]
    public Dictionary<string, KamalAccessory>? Accessories { get; set; }

    [YamlMember(Alias = "volumes")]
    public List<string>? Volumes { get; set; }

    [YamlMember(Alias = "ssh")]
    public KamalSsh? Ssh { get; set; }

    [YamlMember(Alias = "aliases")]
    public Dictionary<string, string>? Aliases { get; set; }

    [YamlMember(Alias = "retain_containers")]
    public int? RetainContainers { get; set; }

    [YamlMember(Alias = "minimum_version")]
    public string? MinimumVersion { get; set; }
}

public sealed class KamalServerRole
{
    [YamlMember(Alias = "hosts")]
    public List<string> Hosts { get; set; } = [];

    [YamlMember(Alias = "cmd")]
    public string? Cmd { get; set; }

    /// <summary>Extra <c>docker run</c> options, e.g. <c>memory: 2g</c>.</summary>
    [YamlMember(Alias = "options")]
    public Dictionary<string, string>? Options { get; set; }

    /// <summary>Whether this role is routed through kamal-proxy. Defaults to true for the first role.</summary>
    [YamlMember(Alias = "proxy")]
    public bool? Proxy { get; set; }
}

public sealed class KamalProxy
{
    [YamlMember(Alias = "ssl")]
    public bool? Ssl { get; set; }

    /// <summary>Public hostname(s) the proxy routes to this app.</summary>
    [YamlMember(Alias = "host")]
    public string? Host { get; set; }

    [YamlMember(Alias = "hosts")]
    public List<string>? Hosts { get; set; }

    /// <summary>The container port the app listens on (Kamal default: 80).</summary>
    [YamlMember(Alias = "app_port")]
    public int? AppPort { get; set; }

    [YamlMember(Alias = "response_timeout")]
    public int? ResponseTimeout { get; set; }

    [YamlMember(Alias = "healthcheck")]
    public KamalHealthcheck? Healthcheck { get; set; }

    [YamlMember(Alias = "forward_headers")]
    public bool? ForwardHeaders { get; set; }
}

public sealed class KamalHealthcheck
{
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "interval")]
    public int? Interval { get; set; }

    [YamlMember(Alias = "timeout")]
    public int? Timeout { get; set; }
}

public sealed class KamalRegistry
{
    /// <summary>Registry server, e.g. <c>ghcr.io</c>. Omit for Docker Hub.</summary>
    [YamlMember(Alias = "server")]
    public string? Server { get; set; }

    /// <summary>Registry username, either a literal or a one-element secret reference list.</summary>
    [YamlMember(Alias = "username")]
    public object? Username { get; set; }

    /// <summary>Registry password; almost always a one-element secret reference list like <c>[KAMAL_REGISTRY_PASSWORD]</c>.</summary>
    [YamlMember(Alias = "password")]
    public object? Password { get; set; }
}

public sealed class KamalBuilder
{
    /// <summary>Target architecture(s): <c>amd64</c>, <c>arm64</c>, or both.</summary>
    [YamlMember(Alias = "arch")]
    public object? Arch { get; set; }

    [YamlMember(Alias = "dockerfile")]
    public string? Dockerfile { get; set; }

    [YamlMember(Alias = "context")]
    public string? Context { get; set; }

    [YamlMember(Alias = "args")]
    public Dictionary<string, string>? Args { get; set; }

    [YamlMember(Alias = "secrets")]
    public List<string>? Secrets { get; set; }

    /// <summary>Build on the deploy host instead of locally.</summary>
    [YamlMember(Alias = "remote")]
    public string? Remote { get; set; }

    [YamlMember(Alias = "cache")]
    public Dictionary<string, string>? Cache { get; set; }
}

public sealed class KamalEnv
{
    [YamlMember(Alias = "clear")]
    public Dictionary<string, string>? Clear { get; set; }

    /// <summary>Names of variables whose values come from <c>.kamal/secrets</c>.</summary>
    [YamlMember(Alias = "secret")]
    public List<string>? Secret { get; set; }
}

public sealed class KamalAccessory
{
    [YamlMember(Alias = "image")]
    public string Image { get; set; } = null!;

    /// <summary>Single host to run the accessory on. Mutually exclusive with <see cref="Hosts"/> and <see cref="Roles"/>.</summary>
    [YamlMember(Alias = "host")]
    public string? Host { get; set; }

    [YamlMember(Alias = "hosts")]
    public List<string>? Hosts { get; set; }

    /// <summary>Run the accessory on all hosts of the given app roles, e.g. <c>[web]</c>.</summary>
    [YamlMember(Alias = "roles")]
    public List<string>? Roles { get; set; }

    /// <summary>Port publishing spec, e.g. <c>127.0.0.1:5432:5432</c>. Omit to keep the accessory reachable only on the kamal docker network.</summary>
    [YamlMember(Alias = "port")]
    public string? Port { get; set; }

    [YamlMember(Alias = "cmd")]
    public string? Cmd { get; set; }

    [YamlMember(Alias = "env")]
    public KamalEnv? Env { get; set; }

    /// <summary>Named-volume or host-path mounts, e.g. <c>db-data:/var/lib/postgresql/data</c>.</summary>
    [YamlMember(Alias = "volumes")]
    public List<string>? Volumes { get; set; }

    /// <summary>Host directories (created relative to the service run directory) to mount.</summary>
    [YamlMember(Alias = "directories")]
    public List<string>? Directories { get; set; }

    [YamlMember(Alias = "files")]
    public List<string>? Files { get; set; }

    [YamlMember(Alias = "options")]
    public Dictionary<string, string>? Options { get; set; }
}

public sealed class KamalSsh
{
    [YamlMember(Alias = "user")]
    public string? User { get; set; }

    [YamlMember(Alias = "port")]
    public int? Port { get; set; }

    [YamlMember(Alias = "proxy")]
    public string? Proxy { get; set; }
}
