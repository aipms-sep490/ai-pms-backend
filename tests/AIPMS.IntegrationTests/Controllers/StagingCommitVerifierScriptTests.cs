using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AIPMS.IntegrationTests.Controllers;

public sealed class StagingCommitVerifierScriptTests
{
    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../scripts/verify-staging-commit.ps1"));

    private const string ValidSha1 = "3f71a30f1f03027be203025e17b2a6908d0ef84b";
    private const string ValidSha2 = "f114aad1f82eef9e586898416e92f658c93fd995";

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string ResolvePowerShellExecutable()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? ["powershell.exe", "pwsh.exe", "pwsh"]
            : ["pwsh", "powershell"];

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var candidate in candidates)
        {
            foreach (var path in paths)
            {
                try
                {
                    var full = Path.Combine(path, candidate);
                    if (File.Exists(full))
                    {
                        return full;
                    }
                }
                catch { }
            }
        }

        return candidates[0];
    }

    private static async Task<(int ExitCode, string Output)> RunScriptAsync(string apiUrl, string expectedSha, int timeoutSec = 5, int retries = 1, int delaySec = 1)
    {
        var exe = ResolvePowerShellExecutable();
        var execPolicyArg = OperatingSystem.IsWindows() ? "-ExecutionPolicy Bypass " : "";
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"-NoProfile {execPolicyArg}-File \"{ScriptPath}\" -ApiUrl \"{apiUrl}\" -ExpectedCommitSha \"{expectedSha}\" -TimeoutSeconds {timeoutSec} -Retries {retries} -RetryDelaySeconds {delaySec}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException($"Process.Start returned null for '{exe}'.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to start PowerShell executable '{exe}' on platform '{Environment.OSVersion.Platform}'. " +
                $"Ensure PowerShell (powershell.exe on Windows, pwsh on Linux/macOS) is installed and available in PATH.", ex);
        }

        using (process)
        {
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, output + "\n" + error);
        }
    }

    private static async Task RunWithMockServerAsync(
        Func<HttpListenerContext, Task> handler,
        Func<string, Task> testAction)
    {
        var port = GetFreePort();
        var prefix = $"http://localhost:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var cts = new CancellationTokenSource();
        var serverTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync();
                }
                catch (HttpListenerException) when (cts.Token.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) when (!cts.Token.IsCancellationRequested)
                {
                    try { await Task.Delay(20, cts.Token); } catch { break; }
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await handler(ctx);
                    }
                    catch
                    {
                        // Ignore handler exceptions (e.g. client disconnect / abort)
                    }
                });
            }
        });

        try
        {
            await testAction($"http://localhost:{port}");
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            try { await serverTask; } catch { }
        }
    }

    private static async Task WriteJsonResponseAsync(HttpListenerResponse resp, int statusCode, string json)
    {
        resp.StatusCode = statusCode;
        resp.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.OutputStream.Close();
    }

    [Fact]
    public async Task Case01_Http200_Exact40CharSha_ExitsZero()
    {
        await RunWithMockServerAsync(
            ctx => WriteJsonResponseAsync(ctx.Response, 200, $"{{\"status\":\"ok\",\"commitSha\":\"{ValidSha1}\"}}"),
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1);
                Assert.Equal(0, exitCode);
                Assert.Contains("[PASS] Commit SHA strictly matches expected value", output);
            });
    }

    [Fact]
    public async Task Case02_Http200_ShaMismatch_FailsWithExit8()
    {
        await RunWithMockServerAsync(
            ctx => WriteJsonResponseAsync(ctx.Response, 200, $"{{\"status\":\"ok\",\"commitSha\":\"{ValidSha2}\"}}"),
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1);
                Assert.Equal(8, exitCode);
                Assert.Contains("SHA MISMATCH", output);
            });
    }

    [Fact]
    public async Task Case03_Http200_VersionFallbackString_FailsWithExit6()
    {
        await RunWithMockServerAsync(
            ctx => WriteJsonResponseAsync(ctx.Response, 200, "{\"status\":\"ok\",\"commitSha\":\"1.0.0\"}"),
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1);
                Assert.Equal(6, exitCode);
                Assert.Contains("This is an assembly/version fallback, NOT a Git SHA", output);
            });
    }

    [Fact]
    public async Task Case04_Http200_NullOrEmptySha_FailsWithExit5()
    {
        await RunWithMockServerAsync(
            ctx => WriteJsonResponseAsync(ctx.Response, 200, "{\"status\":\"ok\",\"commitSha\":\"   \"}"),
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1);
                Assert.Equal(5, exitCode);
                Assert.Contains("empty or whitespace", output);
            });
    }

    [Fact]
    public async Task Case05_Http500_InternalServerError_FailsWithExit2()
    {
        await RunWithMockServerAsync(
            ctx => WriteJsonResponseAsync(ctx.Response, 500, "{\"status\":\"error\",\"message\":\"DB down\"}"),
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1);
                Assert.Equal(2, exitCode);
                Assert.Contains("Expected HTTP 200 OK, got HTTP 500", output);
            });
    }

    [Fact]
    public async Task Case06_Cloudflare1033_HtmlError_FailsWithExit2()
    {
        await RunWithMockServerAsync(
            async ctx =>
            {
                ctx.Response.StatusCode = 533;
                ctx.Response.ContentType = "text/html";
                var html = "<html><body><h1>Error 1033: Cloudflare Tunnel Argo Tunnel error</h1></body></html>";
                var bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.OutputStream.Close();
            },
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1);
                Assert.Equal(2, exitCode);
                Assert.Contains("Cloudflare", output);
            });
    }

    [Fact]
    public async Task Case07_ConnectionTimeout_FailsWithExit1()
    {
        var freePort = GetFreePort(); // No server running
        var (exitCode, output) = await RunScriptAsync($"http://localhost:{freePort}", ValidSha1, timeoutSec: 2, retries: 1);
        Assert.Equal(1, exitCode);
        Assert.Contains("Could not establish connection", output);
    }

    [Fact]
    public async Task Case08_ResponseNotJson_FailsWithExit3()
    {
        await RunWithMockServerAsync(
            async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "text/plain";
                var bytes = Encoding.UTF8.GetBytes("Bad Gateway plain text");
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.OutputStream.Close();
            },
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1);
                Assert.Equal(3, exitCode);
                Assert.Contains("Response content is not valid JSON", output);
            });
    }

    [Fact]
    public async Task Case09_MissingStatusProperty_FailsWithExit4()
    {
        await RunWithMockServerAsync(
            ctx => WriteJsonResponseAsync(ctx.Response, 200, $"{{\"commitSha\":\"{ValidSha1}\"}}"),
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1);
                Assert.Equal(4, exitCode);
                Assert.Contains("Required property 'status' is missing", output);
            });
    }

    [Fact]
    public async Task Case10_ShaInvalidFormat_ShortOrNonHex_FailsWithExit7()
    {
        await RunWithMockServerAsync(
            ctx => WriteJsonResponseAsync(ctx.Response, 200, "{\"status\":\"ok\",\"commitSha\":\"short-sha-xyz\"}"),
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1);
                Assert.Equal(7, exitCode);
                Assert.Contains("not a full 40-character hexadecimal Git SHA", output);
            });
    }

    [Fact]
    public async Task Case11_RetryWithTransientError_RetriesSpecifiedTimes()
    {
        int attempts = 0;
        await RunWithMockServerAsync(
            ctx =>
            {
                Interlocked.Increment(ref attempts);
                ctx.Response.Abort(); // Force connection drop
                return Task.CompletedTask;
            },
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1, timeoutSec: 2, retries: 3, delaySec: 1);
                Assert.Equal(1, exitCode);
                Assert.True(attempts >= 2, $"Expected at least 2 attempts, got {attempts}");
                Assert.Contains("Attempt 3 of 3", output);
            });
    }

    [Fact]
    public async Task Case12_StatusNotOk_FailsWithExit4()
    {
        await RunWithMockServerAsync(
            ctx => WriteJsonResponseAsync(ctx.Response, 200, $"{{\"status\":\"maintenance\",\"commitSha\":\"{ValidSha1}\"}}"),
            async url =>
            {
                var (exitCode, output) = await RunScriptAsync(url, ValidSha1);
                Assert.Equal(4, exitCode);
                Assert.Contains("System status is 'maintenance', expected 'ok'", output);
            });
    }
}
