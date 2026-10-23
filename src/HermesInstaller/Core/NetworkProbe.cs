using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HermesInstaller.Core
{
    /// <summary>
    /// Probes the official endpoints and mirror candidates, then lets the caller pick
    /// the first working mirror. Only reads response headers; never downloads bodies.
    /// </summary>
    public sealed class NetworkProbe : IDisposable
    {
        private readonly HttpClient _client;

        public NetworkProbe()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseProxy = true
            };
            _client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            _client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "HermesInstaller/1.0 (+https://github.com/NousResearch/hermes-agent)");
        }

        public async Task<List<NetworkResult>> RunAsync(
            IEnumerable<NetworkEndpoint> endpoints,
            Action<string, LogLevel> log,
            CancellationToken cancellationToken)
        {
            var list = new List<NetworkEndpoint>(endpoints);
            var results = new NetworkResult[list.Count];
            var gate = new SemaphoreSlim(4);

            var tasks = new List<Task>();
            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        results[index] = await ProbeOneAsync(list[index], cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        results[index] = new NetworkResult
                        {
                            Endpoint = list[index],
                            Ok = false,
                            Detail = "已取消"
                        };
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, cancellationToken));
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }

            var output = new List<NetworkResult>();
            foreach (NetworkResult result in results)
            {
                if (result != null)
                {
                    output.Add(result);
                    if (log != null)
                    {
                        log(string.Format("网络检测 {0,-28} {1,-6} {2}ms  {3}",
                            result.Endpoint.Name,
                            result.Ok ? "可用" : "失败",
                            result.LatencyMs,
                            result.Detail), result.Ok ? LogLevel.Info : LogLevel.Warn);
                    }
                }
            }
            return output;
        }

        private async Task<NetworkResult> ProbeOneAsync(NetworkEndpoint endpoint, CancellationToken cancellationToken)
        {
            var result = new NetworkResult { Endpoint = endpoint };
            string url = endpoint.Kind == NetworkKind.GitRefs
                ? endpoint.Url.TrimEnd('/') + "/info/refs?service=git-upload-pack"
                : endpoint.Url;

            var stopwatch = Stopwatch.StartNew();
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // A git ref advertisement is several MB; allow slow proxies a bit more time,
                // but not so long that the UI waits on a dead mirror.
                int timeoutSeconds = endpoint.Kind == NetworkKind.GitRefs ? 22 : 12;
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    using (HttpResponseMessage response = await _client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                        .ConfigureAwait(false))
                    {
                        stopwatch.Stop();
                        result.LatencyMs = stopwatch.ElapsedMilliseconds;
                        result.HttpStatus = (int)response.StatusCode;
                        int code = (int)response.StatusCode;
                        result.Ok = code >= 200 && code < 400;
                        result.Detail = "HTTP " + code;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    stopwatch.Stop();
                    result.LatencyMs = stopwatch.ElapsedMilliseconds;
                    result.Ok = false;
                    result.Detail = "超时";
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    result.LatencyMs = stopwatch.ElapsedMilliseconds;
                    result.Ok = false;
                    result.Detail = Simplify(ex);
                }
            }
            return result;
        }

        public async Task<string> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(12));
                    using (HttpResponseMessage response = await _client.GetAsync(Mirrors.GithubApi, cts.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Match match = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                        return match.Success ? match.Groups[1].Value : null;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static string Simplify(Exception ex)
        {
            string message = ex.Message ?? ex.GetType().Name;
            int newline = message.IndexOfAny(new[] { '\r', '\n' });
            if (newline > 0)
            {
                message = message.Substring(0, newline);
            }
            return message.Length > 120 ? message.Substring(0, 120) + "…" : message;
        }

        public void Dispose()
        {
            try { _client.Dispose(); } catch { }
        }
    }
}
