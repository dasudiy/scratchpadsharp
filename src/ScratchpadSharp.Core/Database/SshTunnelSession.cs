using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Renci.SshNet;
using ScratchpadSharp.Core.Security;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Database;

public sealed class SshTunnelSession : IAsyncDisposable, IDisposable
{
    private readonly SshClient? client;
    private readonly ForwardedPortLocal? forward;
    private readonly Process? sshProcess;
    private readonly IDisposable? privateKey;
    private bool disposed;

    private SshTunnelSession(
        string connectionString,
        int localPort,
        SshClient? client,
        ForwardedPortLocal? forward,
        Process? sshProcess,
        IDisposable? privateKey)
    {
        ConnectionString = connectionString;
        LocalPort = localPort;
        this.client = client;
        this.forward = forward;
        this.sshProcess = sshProcess;
        this.privateKey = privateKey;
    }

    public string ConnectionString { get; }
    public int LocalPort { get; }

    public static async Task<SshTunnelSession?> OpenIfNeededAsync(
        ModuleInstanceConfig config, CancellationToken ct = default)
    {
        var ssh = config.SshTunnel;
        if (ssh is not { Enabled: true })
            return null;

        return await OpenAsync(ssh, config.ProviderId, config.ConnectionString, ct);
    }

    public static async Task<SshTunnelSession> OpenAsync(
        SshTunnelConfig ssh, string providerId, string connectionString, CancellationToken ct = default)
    {
        Validate(ssh);

        var provider = DatabaseProviderCatalog.Get(providerId);
        if (!provider.SupportsSshTunnel)
            throw new InvalidOperationException($"{provider.DisplayName} connections do not use SSH tunnels.");

        var (remoteHost, remotePort) = ResolveForwardTarget(ssh, providerId, connectionString);
        if (string.IsNullOrWhiteSpace(remoteHost))
            throw new InvalidOperationException("SSH tunnel remote host is empty.");
        if (remotePort is <= 0 or > 65535)
            throw new InvalidOperationException("SSH tunnel remote port is invalid.");

        try
        {
            var session = ssh.AuthMethod == SshAuthMethod.Agent
                ? await OpenWithOpenSshAsync(ssh, remoteHost, remotePort, ct).ConfigureAwait(false)
                : await OpenWithSshNetAsync(ssh, remoteHost, remotePort, ct).ConfigureAwait(false);

            var rewritten = DatabaseEndpoint.RewriteToLoopback(providerId, connectionString, session.LocalPort);
            return new SshTunnelSession(
                rewritten,
                session.LocalPort,
                session.Client,
                session.Forward,
                session.Process,
                session.PrivateKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"SSH tunnel to {ssh.Host}:{ssh.Port} failed: {ex.Message}", ex);
        }
    }

    public static (string Host, int Port) ResolveForwardTarget(
        SshTunnelConfig ssh, string providerId, string connectionString)
    {
        var hasExplicitHost = !string.IsNullOrWhiteSpace(ssh.RemoteHost);
        var hasExplicitPort = ssh.RemotePort > 0;
        if (hasExplicitHost && hasExplicitPort)
            return (ssh.RemoteHost.Trim(), ssh.RemotePort);

        var endpoint = DatabaseEndpoint.Parse(providerId, connectionString);
        if (!hasExplicitPort &&
            DatabaseEndpoint.NeedsExplicitPortForNamedInstance(providerId, connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server named instances need an explicit port (Server=host,port) or an SSH remote port.");
        }

        var host = hasExplicitHost ? ssh.RemoteHost.Trim() : endpoint.Host;
        var port = hasExplicitPort ? ssh.RemotePort : endpoint.Port;
        return (host, port);
    }

    public static void Validate(SshTunnelConfig ssh)
    {
        if (string.IsNullOrWhiteSpace(ssh.Host))
            throw new InvalidOperationException("SSH host is required.");
        if (ssh.Port is <= 0 or > 65535)
            throw new InvalidOperationException("SSH port must be 1-65535.");
        if (string.IsNullOrWhiteSpace(ssh.Username))
            throw new InvalidOperationException("SSH user name is required.");
        if (ssh.LocalPort is < 0 or > 65535)
            throw new InvalidOperationException("SSH local port must be 0-65535.");
        if (ssh.RemotePort is < 0 or > 65535)
            throw new InvalidOperationException("SSH remote port must be 0-65535.");

        switch (ssh.AuthMethod)
        {
            case SshAuthMethod.Password:
                if (string.IsNullOrEmpty(ssh.Password))
                    throw new InvalidOperationException("SSH password is required.");
                break;
            case SshAuthMethod.PublicKey:
                if (string.IsNullOrWhiteSpace(ssh.PrivateKeyPath))
                    throw new InvalidOperationException("SSH private key path is required.");
                if (!File.Exists(ssh.PrivateKeyPath))
                    throw new InvalidOperationException($"SSH private key file not found: {ssh.PrivateKeyPath}");
                break;
            case SshAuthMethod.Agent:
                break;
            default:
                throw new InvalidOperationException($"Unsupported SSH authentication method: {ssh.AuthMethod}.");
        }
    }

    private sealed record OpenedTunnel(
        int LocalPort,
        SshClient? Client,
        ForwardedPortLocal? Forward,
        Process? Process,
        IDisposable? PrivateKey);

    private static async Task<OpenedTunnel> OpenWithSshNetAsync(
        SshTunnelConfig ssh, string remoteHost, int remotePort, CancellationToken ct)
    {
        IDisposable? privateKey = null;
        SshClient? client = null;
        ForwardedPortLocal? forward = null;
        try
        {
            var connectionInfo = CreateConnectionInfo(ssh, out privateKey);
            client = new SshClient(connectionInfo)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };
            var hostTrusted = false;
            client.HostKeyReceived += (_, e) =>
            {
                hostTrusted = IsKnownHost(ssh.Host, ssh.Port);
                e.CanTrust = hostTrusted;
            };

            try
            {
                await client.ConnectAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!hostTrusted)
            {
                throw new InvalidOperationException(
                    $"SSH host key for {ssh.Host}:{ssh.Port} is not in known_hosts. " +
                    "Connect once from a terminal (`ssh user@host`) or use agent authentication.",
                    ex);
            }

            var boundPort = ssh.LocalPort > 0 ? (uint)ssh.LocalPort : 0u;
            forward = new ForwardedPortLocal("127.0.0.1", boundPort, remoteHost, (uint)remotePort);
            client.AddForwardedPort(forward);
            forward.Start();

            if (forward.BoundPort == 0)
                throw new InvalidOperationException("SSH tunnel did not bind a local port.");

            return new OpenedTunnel((int)forward.BoundPort, client, forward, null, privateKey);
        }
        catch
        {
            forward?.Dispose();
            if (client != null)
            {
                try { client.Disconnect(); } catch { /* ignore */ }
                client.Dispose();
            }
            privateKey?.Dispose();
            throw;
        }
    }

    private static async Task<OpenedTunnel> OpenWithOpenSshAsync(
        SshTunnelConfig ssh, string remoteHost, int remotePort, CancellationToken ct)
    {
        var sshExe = FindOpenSshClient()
                     ?? throw new InvalidOperationException(
                         "SSH agent authentication requires the OpenSSH client (`ssh`) on PATH.");

        Exception? lastError = null;
        var attempts = ssh.LocalPort > 0 ? 1 : 3;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var localPort = ssh.LocalPort > 0 ? ssh.LocalPort : GetFreeTcpPort();
            var stderr = new StringBuilder();
            var psi = new ProcessStartInfo
            {
                FileName = sshExe,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-N");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("BatchMode=yes");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("ExitOnForwardFailure=yes");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("ServerAliveInterval=30");
            psi.ArgumentList.Add("-L");
            psi.ArgumentList.Add($"127.0.0.1:{localPort}:{remoteHost}:{remotePort}");
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(ssh.Port.ToString());
            psi.ArgumentList.Add($"{ssh.Username.Trim()}@{ssh.Host.Trim()}");

            Process? process = null;
            try
            {
                process = Process.Start(psi)
                          ?? throw new InvalidOperationException("Failed to start the OpenSSH client.");
                process.OutputDataReceived += (_, _) => { };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        lock (stderr) stderr.AppendLine(e.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await WaitForLocalPortAsync(process, localPort, () =>
                {
                    lock (stderr) return stderr.ToString();
                }, ct).ConfigureAwait(false);
                return new OpenedTunnel(localPort, null, null, process, null);
            }
            catch (Exception ex) when (ssh.LocalPort <= 0 && attempt < attempts - 1)
            {
                lastError = ex;
                TryKill(process);
            }
            catch
            {
                TryKill(process);
                throw;
            }
        }

        throw lastError ?? new InvalidOperationException("Failed to open an SSH tunnel.");
    }

    private static async Task WaitForLocalPortAsync(
        Process process, int localPort, Func<string> readStderr, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                var stderr = readStderr().Trim();
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(stderr)
                        ? $"ssh exited with code {process.ExitCode}."
                        : stderr);
            }

            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(200);
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, localPort), timeoutCts.Token)
                    .ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }

        TryKill(process);
        throw new TimeoutException("Timed out waiting for the SSH tunnel to listen on localhost.");
    }

    private static ConnectionInfo CreateConnectionInfo(SshTunnelConfig ssh, out IDisposable? privateKey)
    {
        privateKey = null;
        var methods = ssh.AuthMethod switch
        {
            SshAuthMethod.Password => CreatePasswordMethods(ssh.Username.Trim(), ssh.Password),
            SshAuthMethod.PublicKey => CreatePublicKeyMethods(ssh, out privateKey),
            _ => throw new InvalidOperationException($"Unsupported SSH authentication method: {ssh.AuthMethod}.")
        };

        return new ConnectionInfo(ssh.Host.Trim(), ssh.Port, ssh.Username.Trim(), methods)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static AuthenticationMethod[] CreatePasswordMethods(string username, string password)
    {
        var keyboard = new KeyboardInteractiveAuthenticationMethod(username);
        keyboard.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
            {
                var text = prompt.Request ?? string.Empty;
                prompt.Response = text.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                                  e.Prompts.Count == 1
                    ? password
                    : string.Empty;
            }
        };

        return
        [
            new PasswordAuthenticationMethod(username, password),
            keyboard
        ];
    }

    private static AuthenticationMethod[] CreatePublicKeyMethods(SshTunnelConfig ssh, out IDisposable? privateKey)
    {
        var path = ssh.PrivateKeyPath.Trim();
        var keyFile = string.IsNullOrEmpty(ssh.Passphrase)
            ? new PrivateKeyFile(path)
            : new PrivateKeyFile(path, ssh.Passphrase);
        privateKey = keyFile;
        return [new PrivateKeyAuthenticationMethod(ssh.Username.Trim(), keyFile)];
    }

    private static string? cachedOpenSshClient;
    private static bool openSshClientResolved;

    private static string? FindOpenSshClient()
    {
        if (openSshClientResolved)
            return cachedOpenSshClient;

        var names = OperatingSystem.IsWindows() ? new[] { "ssh.exe", "ssh" } : new[] { "ssh" };
        foreach (var name in names)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = name,
                    ArgumentList = { "-V" },
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var probe = Process.Start(psi);
                if (probe == null)
                    continue;
                if (!probe.WaitForExit(2000))
                {
                    TryKill(probe);
                    continue;
                }

                cachedOpenSshClient = name;
                openSshClientResolved = true;
                return name;
            }
            catch
            {
                /* try next */
            }
        }

        openSshClientResolved = true;
        return null;
    }

    private static bool IsKnownHost(string host, int port)
    {
        host = host.Trim();
        if (string.IsNullOrEmpty(host))
            return false;

        var lookup = port is 22 or 0 ? host : $"[{host}]:{port}";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ssh-keygen",
                ArgumentList = { "-F", lookup },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var probe = Process.Start(psi);
            if (probe == null)
                return false;
            if (!probe.WaitForExit(2000))
            {
                TryKill(probe);
                return false;
            }

            return probe.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void TryKill(Process? process)
    {
        if (process == null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            /* ignore */
        }

        process.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;
        disposed = true;

        try
        {
            if (forward is { IsStarted: true })
                forward.Stop();
        }
        catch
        {
            /* ignore */
        }

        forward?.Dispose();

        try
        {
            if (client is { IsConnected: true })
                client.Disconnect();
        }
        catch
        {
            /* ignore */
        }

        client?.Dispose();
        privateKey?.Dispose();
        TryKill(sshProcess);
        await Task.CompletedTask;
    }
}

public sealed class SshTunnelScope : IAsyncDisposable
{
    private readonly List<SshTunnelSession> sessions;
    private readonly Dictionary<string, string> liveConnectionStrings;

    private SshTunnelScope(List<SshTunnelSession> sessions, Dictionary<string, string> liveConnectionStrings)
    {
        this.sessions = sessions;
        this.liveConnectionStrings = liveConnectionStrings;
    }

    public bool IsEmpty => sessions.Count == 0;

    public static async Task<SshTunnelScope> OpenAsync(
        IEnumerable<ModuleInstanceConfig> modules, CancellationToken ct = default)
    {
        var opened = new List<SshTunnelSession>();
        var liveConnectionStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var module in modules)
            {
                var live = await ModuleSecrets.UnlockAsync(module, ct).ConfigureAwait(false);
                var session = await SshTunnelSession.OpenIfNeededAsync(live, ct).ConfigureAwait(false);
                if (session != null)
                {
                    opened.Add(session);
                    liveConnectionStrings[module.Id] = session.ConnectionString;
                }
                else
                    liveConnectionStrings[module.Id] = live.ConnectionString;
            }

            return new SshTunnelScope(opened, liveConnectionStrings);
        }
        catch
        {
            for (var i = opened.Count - 1; i >= 0; i--)
                await opened[i].DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public IReadOnlyList<ModuleSourceFile> RewriteSources(
        IReadOnlyList<ModuleInstanceConfig> modules,
        IReadOnlyList<ModuleSourceFile> sources)
    {
        if (liveConnectionStrings.Count == 0)
            return sources;

        var result = new List<ModuleSourceFile>(sources.Count);
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var module = i < modules.Count ? modules[i] : null;
            if (module != null &&
                liveConnectionStrings.TryGetValue(module.Id, out var liveCs) &&
                !string.Equals(module.ConnectionString, liveCs, StringComparison.Ordinal))
            {
                if (!EfScaffoldGenerator.TryReplaceBakedConnectionString(
                        source.SourceText, module.ConnectionString, liveCs, out var rewritten))
                {
                    throw new InvalidOperationException(
                        $"Could not rewrite the baked connection string in module '{module.DisplayName}' for the live connection. Regenerate the model and try again.");
                }

                result.Add(new ModuleSourceFile { FileName = source.FileName, SourceText = rewritten });
            }
            else
                result.Add(source);
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = sessions.Count - 1; i >= 0; i--)
            await sessions[i].DisposeAsync().ConfigureAwait(false);
    }
}
