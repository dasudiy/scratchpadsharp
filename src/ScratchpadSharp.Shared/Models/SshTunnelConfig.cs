using System.Text.Json.Serialization;

namespace ScratchpadSharp.Shared.Models;

public enum SshAuthMethod
{
    Agent,
    Password,
    PublicKey
}

/// <summary>
/// Optional SSH local-port-forward settings (DBeaver-style).
/// Database host/port in the connection string is the address as seen from the SSH server.
/// </summary>
public sealed class SshTunnelConfig
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SshAuthMethod AuthMethod { get; set; } = SshAuthMethod.Agent;

    public string Password { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string Passphrase { get; set; } = string.Empty;

    /// <summary>Override for the forwarded remote host. Empty uses the database server from the connection string.</summary>
    public string RemoteHost { get; set; } = string.Empty;

    /// <summary>Override for the forwarded remote port. 0 uses the database port from the connection string.</summary>
    public int RemotePort { get; set; }

    /// <summary>Local bind port. 0 assigns an ephemeral port.</summary>
    public int LocalPort { get; set; }

    public SshTunnelConfig Clone() => new()
    {
        Enabled = Enabled,
        Host = Host,
        Port = Port,
        Username = Username,
        AuthMethod = AuthMethod,
        Password = Password,
        PrivateKeyPath = PrivateKeyPath,
        Passphrase = Passphrase,
        RemoteHost = RemoteHost,
        RemotePort = RemotePort,
        LocalPort = LocalPort
    };
}
