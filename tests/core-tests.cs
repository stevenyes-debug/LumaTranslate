using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using LumaTranslate;

internal static class CoreTests
{
    private static int passed;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    private sealed class Handler : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Send;
        internal readonly List<string> Queries = new List<string>();
        internal int Count;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Count);
            string query = request.RequestUri.Query.TrimStart('?').Split('&').First(v => v.StartsWith("q="));
            string q = Uri.UnescapeDataString(query.Substring(2));
            lock (Queries) Queries.Add(q);
            Assert(request.RequestUri.Scheme == "https", "HTTPS only");
            Assert(Utf8.GetByteCount(q) <= 500, "provider byte limit");
            Assert(!q.Contains("\r") && !q.Contains("\n"), "one paragraph per provider request");
            return Send == null ? Json(q) : await Send(request, token);
        }
    }

    private sealed class PendingRequest
    {
        internal string Text;
        internal string Pair;
        internal readonly TaskCompletionSource<HttpResponseMessage> Response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Complete(string text) { Response.TrySetResult(Json(text)); }
    }

    // Every response is released explicitly by its test. Timers only guard against a broken test
    // hanging forever; neither parallelism nor ordering assertions depend on elapsed time.
    private sealed class ControlledHandler : HttpMessageHandler
    {
        private readonly object sync = new object();
        private readonly Queue<PendingRequest> pending = new Queue<PendingRequest>();
        private readonly SemaphoreSlim arrivals = new SemaphoreSlim(0);
        internal readonly List<PendingRequest> Requests = new List<PendingRequest>();
        internal bool RespectCancellation = true;
        internal int Count;
        internal int Active;
        internal int PeakActive;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string[] query = request.RequestUri.Query.TrimStart('?').Split('&');
            PendingRequest item = new PendingRequest
            {
                Text = Uri.UnescapeDataString(query.First(v => v.StartsWith("q=")).Substring(2)),
                Pair = Uri.UnescapeDataString(query.First(v => v.StartsWith("langpair=")).Substring(9))
            };
            Assert(Utf8.GetByteCount(item.Text) <= 500, "parallel transport byte bound");
            lock (sync)
            {
                Count++;
                Active++;
                PeakActive = Math.Max(PeakActive, Active);
                Requests.Add(item);
                pending.Enqueue(item);
            }
            arrivals.Release();
            try
            {
                using (token.Register(delegate
                {
                    item.Cancelled.TrySetResult(true);
                    if (RespectCancellation) item.Response.TrySetCanceled();
                }))
                    return await item.Response.Task.ConfigureAwait(false);
            }
            finally { lock (sync) Active--; }
        }

        internal async Task<PendingRequest> NextAsync()
        {
            Assert(await arrivals.WaitAsync(TimeSpan.FromSeconds(5)), "expected HTTP request was dispatched");
            lock (sync) return pending.Dequeue();
        }
    }

    private static async Task Guard(Task task)
    {
        Assert(await Task.WhenAny(task, Task.Delay(5000)) == task, "operation must settle without waiting for transport timeout");
        await task;
    }

    private static HttpResponseMessage Json(string text)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JavaScriptSerializer().Serialize(new
            { responseStatus = 200, quotaFinished = false, responseData = new { translatedText = text } }), Encoding.UTF8, "application/json") };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }

    private static async Task ExpectFailure(Func<Task> action, Type expected, string messagePart)
    {
        try { await action(); }
        catch (Exception ex)
        {
            Assert(expected.IsInstanceOfType(ex), "expected " + expected.Name + "; got " + ex.GetType().Name);
            if (messagePart != null) Assert(ex.Message.Contains(messagePart), "friendly error: " + ex.Message);
            return;
        }
        throw new Exception("Expected failure did not occur.");
    }

    private static async Task Run(string name, Func<Task> body)
    {
        await body();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static Task Sync(Action action) { action(); return Task.FromResult(0); }

    private static async Task Tests()
    {
        await Run("defaults and script detection", () => Sync(delegate
        {
            AppSettings s = new AppSettings();
            Assert(s.SourceLanguage == "auto" && s.TargetLanguage == "zh-CN" && s.CloseToTray, "settings defaults");
            Assert(TranslationService.DetectLanguage("中文") == "zh-CN", "Chinese");
            Assert(TranslationService.DetectLanguage("日本語を翻訳") == "ja", "kana takes precedence over han");
            Assert(TranslationService.DetectLanguage("한국어") == "ko", "Korean");
            Assert(TranslationService.DetectLanguage("Привет") == "ru", "Cyrillic heuristic");
            Assert(TranslationService.DetectLanguage("bonjour") == "en", "Latin fallback is explicitly English heuristic");
        }));

        await Run("UTF-8 segmentation preserves every code unit and separator", () => Sync(delegate
        {
            string[] samples = { new string('中', 167), new string('a', 501), String.Concat(Enumerable.Repeat("😀", 180)),
                "  first paragraph\r\n\r\n\tsecond   paragraph\n\n最後の段落  ",
                new string('a', 496) + " 😀 中文 end", String.Concat(Enumerable.Repeat("hello world. ", 100)),
                "\t \r\n\n\u2028\u2029", "start\u00a0\u00a0end" };
            foreach (string input in samples)
            {
                List<TranslationPart> parts = TranslationService.SplitText(input);
                Assert(String.Concat(parts.Select(p => p.Text)) == input, "lossless segment concatenation");
                foreach (TranslationPart part in parts)
                {
                    int bytes = Utf8.GetByteCount(part.Text);
                    Assert(part.IsLiteral || (bytes <= 500 && bytes > 0), "500 UTF-8 bytes, not 500 characters");
                    Assert(!part.IsLiteral || String.IsNullOrWhiteSpace(part.Text), "only whitespace skipped");
                }
            }
        }));

        await Run("generated Unicode splitting corpus", () => Sync(delegate
        {
            Random random = new Random(217);
            string[] alphabet = { "a", "中", "😀", "あ", "é", "\r\n", " ", "   ", "\t", ".", "\u2028" };
            for (int sample = 0; sample < 100; sample++)
            {
                StringBuilder builder = new StringBuilder();
                for (int n = 0; n < 800; n++) builder.Append(alphabet[random.Next(alphabet.Length)]);
                string text = builder.ToString();
                List<TranslationPart> parts = TranslationService.SplitText(text);
                Assert(String.Concat(parts.Select(p => p.Text)) == text, "random corpus preserves text");
                foreach (TranslationPart p in parts) Assert(p.IsLiteral || Utf8.GetByteCount(p.Text) <= 500, "random byte bound");
            }
        }));

        await Run("live pipeline preserves paragraphs and encodes ampersand/Unicode", async delegate
        {
            Handler handler = new Handler();
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(5)))
            {
                string input = "  " + new string('中', 250) + " 😀\r\n\r\n\tA & B? # + /   tail  \n";
                TranslationResult result = await service.TranslateAsync(input, "zh-CN", "en", CancellationToken.None);
                Assert(result.Text == input, "echo transport reconstruction");
                Assert(handler.Count >= 3, "long Unicode goes through multiple requests");
                Assert(result.Provider == "MyMemory" && result.SourceLanguage == "zh-CN", "metadata");
            }
        });

        await Run("default HTTP handler enables compression and three connections while preserving system proxy", () => Sync(delegate
        {
            using (HttpClientHandler handler = TranslationService.CreateDefaultHandler())
            {
                Assert(handler.MaxConnectionsPerServer == 3, "three connections per server");
                Assert(handler.AutomaticDecompression == (DecompressionMethods.GZip | DecompressionMethods.Deflate), "gzip and deflate");
                Assert(handler.UseProxy && handler.Proxy == null, "Windows proxy remains enabled without override");
            }
        }));

        await Run("three requests execute concurrently and reconstruct original order after reverse completion", async delegate
        {
            ControlledHandler handler = new ControlledHandler();
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(10)))
            {
                Task<TranslationResult> result = service.TranslateAsync("  one\r\n\r\n\ttwo   three\nfour\nfive\nsix  ", "en", "zh-CN", CancellationToken.None);
                PendingRequest first = await handler.NextAsync();
                PendingRequest second = await handler.NextAsync();
                PendingRequest third = await handler.NextAsync();
                Assert(handler.Count == 3 && handler.Active == 3, "three pending HTTP requests before any response");
                third.Complete("T-" + third.Text);
                PendingRequest fourth = await handler.NextAsync();
                fourth.Complete("T-" + fourth.Text);
                PendingRequest fifth = await handler.NextAsync();
                fifth.Complete("T-" + fifth.Text);
                PendingRequest sixth = await handler.NextAsync();
                sixth.Complete("T-" + sixth.Text);
                second.Complete("T-" + second.Text);
                Assert(!result.IsCompleted, "first paragraph still pending");
                first.Complete("T-" + first.Text);
                await Guard(result);
                Assert(result.Result.Text == "  T-one\r\n\r\n\tT-two   T-three\nT-four\nT-five\nT-six  ", "order and whitespace preserved");
                Assert(handler.PeakActive == 3 && handler.Count == 6, "bounded concurrency proven without stopwatch thresholds");
            }
        });

        await Run("global concurrency bound applies across simultaneous translation calls", async delegate
        {
            ControlledHandler handler = new ControlledHandler();
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(10)))
            {
                Task<TranslationResult> a = service.TranslateAsync("a\nb\nc\nd", "en", "zh-CN", CancellationToken.None);
                Task<TranslationResult> b = service.TranslateAsync("e\nf\ng\nh", "en", "zh-CN", CancellationToken.None);
                List<PendingRequest> active = new List<PendingRequest> { await handler.NextAsync(), await handler.NextAsync(), await handler.NextAsync() };
                Assert(handler.Active == 3 && handler.Count == 3, "both calls share the same three slots");
                for (int completed = 0; completed < 8; completed++)
                {
                    PendingRequest item = active[0];
                    active.RemoveAt(0);
                    item.Complete(item.Text.ToUpperInvariant());
                    if (completed < 5) active.Add(await handler.NextAsync());
                }
                await Guard(Task.WhenAll(a, b));
                Assert(a.Result.Text == "A\nB\nC\nD" && b.Result.Text == "E\nF\nG\nH", "independent output order");
                Assert(handler.PeakActive == 3, "cross-call peak stays three");
            }
        });

        await Run("duplicate paragraphs and in-flight text across calls share one request", async delegate
        {
            ControlledHandler handler = new ControlledHandler();
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(10)))
            {
                Task<TranslationResult> a = service.TranslateAsync("same\nsame\nother\nsame", "en", "zh-CN", CancellationToken.None);
                Task<TranslationResult> b = service.TranslateAsync("same\nextra", "auto", "zh-CN", CancellationToken.None);
                PendingRequest[] pending = { await handler.NextAsync(), await handler.NextAsync(), await handler.NextAsync() };
                Assert(handler.Count == 3 && pending.Count(p => p.Text == "same") == 1, "same segment has exactly one HTTP request");
                foreach (PendingRequest item in pending.Reverse()) item.Complete("T-" + item.Text);
                await Guard(Task.WhenAll(a, b));
                Assert(a.Result.Text == "T-same\nT-same\nT-other\nT-same" && b.Result.Text == "T-same\nT-extra", "dedup retains every paragraph occurrence");
                Assert(handler.Count == 3, "no repeated requests after reconstruction");
            }
        });

        await Run("same text with different language pairs is not merged", async delegate
        {
            ControlledHandler handler = new ControlledHandler();
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(10)))
            {
                Task<TranslationResult> a = service.TranslateAsync("same", "en", "zh-CN", CancellationToken.None);
                Task<TranslationResult> b = service.TranslateAsync("same", "en", "ja", CancellationToken.None);
                PendingRequest first = await handler.NextAsync();
                PendingRequest second = await handler.NextAsync();
                Assert(first.Pair != second.Pair, "language pair belongs to in-flight key");
                first.Complete(first.Pair);
                second.Complete(second.Pair);
                await Guard(Task.WhenAll(a, b));
                Assert(a.Result.Text == "en|zh-CN" && b.Result.Text == "en|ja", "languages cannot contaminate each other");
            }
        });

        await Run("cancelling one subscriber leaves the shared request alive for another", async delegate
        {
            ControlledHandler handler = new ControlledHandler();
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(10)))
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                Task<TranslationResult> a = service.TranslateAsync("shared\nfirst-only", "en", "zh-CN", cancel.Token);
                Task<TranslationResult> b = service.TranslateAsync("shared\nsecond-only", "en", "zh-CN", CancellationToken.None);
                PendingRequest[] pending = { await handler.NextAsync(), await handler.NextAsync(), await handler.NextAsync() };
                PendingRequest shared = pending.Single(p => p.Text == "shared");
                PendingRequest firstOnly = pending.Single(p => p.Text == "first-only");
                PendingRequest secondOnly = pending.Single(p => p.Text == "second-only");
                cancel.Cancel();
                await Guard(ExpectFailure(() => a, typeof(OperationCanceledException), null));
                await Guard(firstOnly.Cancelled.Task);
                Assert(!shared.Cancelled.Task.IsCompleted && !secondOnly.Cancelled.Task.IsCompleted, "shared and other subscriber remain active");
                shared.Complete("共同");
                secondOnly.Complete("第二");
                await Guard(b);
                Assert(b.Result.Text == "共同\n第二" && handler.Count == 3, "remaining subscriber succeeds without retry");
            }
        });

        await Run("last subscriber cancellation stops queued chunks without waiting for uncooperative HTTP", async delegate
        {
            ControlledHandler handler = new ControlledHandler { RespectCancellation = false };
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(10)))
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                Task<TranslationResult> result = service.TranslateAsync("a\nb\nc\nd\ne\nf", "en", "zh-CN", cancel.Token);
                PendingRequest[] pending = { await handler.NextAsync(), await handler.NextAsync(), await handler.NextAsync() };
                cancel.Cancel();
                await Guard(ExpectFailure(() => result, typeof(OperationCanceledException), null));
                foreach (PendingRequest item in pending) await Guard(item.Cancelled.Task);
                Assert(handler.Count == 3 && handler.Active == 3, "cancelled callers return while old transports still own slots");
                foreach (PendingRequest item in pending) item.Complete("STALE");
                Task<TranslationResult> retry = service.TranslateAsync("a", "en", "zh-CN", CancellationToken.None);
                PendingRequest fresh = await handler.NextAsync();
                Assert(fresh.Text == "a" && handler.Count == 4, "cancelled queued chunks never dispatch and retry uses fresh request");
                fresh.Complete("FRESH");
                await Guard(retry);
                Assert(retry.Result.Text == "FRESH" && handler.PeakActive == 3, "stale success cannot poison cache or exceed connection cap");
            }
        });

        await Run("parallel HTTP failure cancels siblings and preserves the real error", async delegate
        {
            ControlledHandler handler = new ControlledHandler { RespectCancellation = false };
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(10)))
            {
                Task<TranslationResult> result = service.TranslateAsync("bad\nslow\nalso-slow\nqueued", "en", "zh-CN", CancellationToken.None);
                PendingRequest[] pending = { await handler.NextAsync(), await handler.NextAsync(), await handler.NextAsync() };
                pending.Single(p => p.Text == "bad").Response.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                await Guard(ExpectFailure(() => result, typeof(TranslationException), "503"));
                Assert(handler.Count == 3, "remaining batch chunks do not dispatch after first failure");
                foreach (PendingRequest item in pending.Where(p => p.Text != "bad"))
                {
                    await Guard(item.Cancelled.Task);
                    item.Complete("IGNORED");
                }
            }
        });

        await Run("one failing batch does not cancel a shared chunk needed by another batch", async delegate
        {
            ControlledHandler handler = new ControlledHandler();
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(10)))
            {
                Task<TranslationResult> a = service.TranslateAsync("bad\nshared", "en", "zh-CN", CancellationToken.None);
                Task<TranslationResult> b = service.TranslateAsync("shared", "en", "zh-CN", CancellationToken.None);
                PendingRequest first = await handler.NextAsync();
                PendingRequest second = await handler.NextAsync();
                PendingRequest bad = first.Text == "bad" ? first : second;
                PendingRequest shared = first.Text == "shared" ? first : second;
                bad.Response.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                await Guard(ExpectFailure(() => a, typeof(TranslationException), "503"));
                Assert(!shared.Cancelled.Task.IsCompleted, "failure only releases that batch's shared subscription");
                shared.Complete("survived");
                await Guard(b);
                Assert(b.Result.Text == "survived" && handler.Count == 2, "unrelated batch succeeds with the original request");
            }
        });

        await Run("disposal cancels active and queued work without dictionary races", async delegate
        {
            ControlledHandler handler = new ControlledHandler();
            TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(10));
            Task<TranslationResult> result = service.TranslateAsync("a\nb\nc\nd\ne", "en", "zh-CN", CancellationToken.None);
            await handler.NextAsync();
            await handler.NextAsync();
            await handler.NextAsync();
            service.Dispose();
            await Guard(ExpectFailure(() => result, typeof(TranslationException), "超时"));
            Assert(handler.Count == 3, "disposal prevents queued dispatch");
            await ExpectFailure(() => service.TranslateAsync("later", "en", "zh-CN", CancellationToken.None), typeof(ObjectDisposedException), null);
        });

        await Run("HTML decoding and JSON schema failures", () => Sync(delegate
        {
            Assert(TranslationService.ParseResponse("{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"A &amp; B &#x4e2d;\"}}") == "A & B 中", "HTML entity decode");
            Assert(TranslationService.ParseResponse("{\"responseStatus\":\"200\",\"responseData\":{\"translatedText\":\"valid\"}}") == "valid", "string status supported");
            string[] invalid = { "<html>503</html>", "null", "[]", "{}", "{\"responseStatus\":\"not-a-number\"}",
                "{\"responseStatus\":200,\"responseData\":null}", "{\"responseStatus\":200,\"responseData\":{\"translatedText\":7}}",
                "{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"  \"}}" };
            foreach (string json in invalid)
            {
                bool caught = false;
                try { TranslationService.ParseResponse(json); }
                catch (TranslationException) { caught = true; }
                Assert(caught, "reject malformed/missing/nonstring provider data");
            }
        }));

        await Run("quota warnings are never returned as translations", () => Sync(delegate
        {
            string[] failures = {
                "{\"responseStatus\":200,\"quotaFinished\":true,\"responseData\":{\"translatedText\":\"looks successful\"}}",
                "{\"responseStatus\":429,\"responseDetails\":\"limit\"}",
                "{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS FOR TODAY\"}}"
            };
            foreach (string json in failures)
            {
                bool caught = false;
                try { TranslationService.ParseResponse(json); }
                catch (TranslationException ex) { caught = ex.Message.Contains("额度"); }
                Assert(caught, "friendly quota rejection");
            }
        }));

        await Run("HTTP failures and network failures are actionable", async delegate
        {
            foreach (int status in new[] { 403, 429, 503 })
            {
                Handler handler = new Handler { Send = (r, t) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)) };
                using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(5)))
                    await ExpectFailure(() => service.TranslateAsync("hello", "en", "zh-CN", CancellationToken.None), typeof(TranslationException), status == 503 ? "503" : "额度");
            }
            Handler network = new Handler { Send = (r, t) => { throw new HttpRequestException("no network"); } };
            using (TranslationService service = new TranslationService(network, TimeSpan.FromSeconds(5)))
                await ExpectFailure(() => service.TranslateAsync("hello", "en", "zh-CN", CancellationToken.None), typeof(TranslationException), "网络");
        });

        await Run("user cancellation remains cancellation", async delegate
        {
            Handler handler = new Handler { Send = async (r, t) => { await Task.Delay(10000, t); return Json("late"); } };
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(5)))
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.CancelAfter(30);
                await ExpectFailure(() => service.TranslateAsync("hello", "en", "zh-CN", source.Token), typeof(OperationCanceledException), null);
            }
        });

        await Run("service deadline is a friendly timeout", async delegate
        {
            Handler handler = new Handler { Send = async (r, t) => { await Task.Delay(10000, t); return Json("late"); } };
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromMilliseconds(25)))
                await ExpectFailure(() => service.TranslateAsync("hello", "en", "zh-CN", CancellationToken.None), typeof(TranslationException), "超时");
        });

        await Run("cancelled late result cannot enter cache or return success", async delegate
        {
            Handler handler = new Handler { Send = async (r, t) => { await Task.Delay(60); return Json("late"); } };
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(5)))
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.CancelAfter(10);
                await ExpectFailure(() => service.TranslateAsync("hello", "en", "zh-CN", source.Token), typeof(OperationCanceledException), null);
                handler.Send = null;
                TranslationResult result = await service.TranslateAsync("hello", "en", "zh-CN", CancellationToken.None);
                Assert(handler.Count == 2 && result.Text == "hello", "cancelled response not cached");
            }
        });

        await Run("cache is bounded in behavior and returns isolated result objects", async delegate
        {
            Handler handler = new Handler();
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(5)))
            {
                TranslationResult first = await service.TranslateAsync("hello", "auto", "zh-CN", CancellationToken.None);
                first.Text = "mutated";
                TranslationResult second = await service.TranslateAsync("hello", "en", "zh-CN", CancellationToken.None);
                Assert(handler.Count == 1 && second.Text == "hello", "cache copy and resolved source key");
                using (CancellationTokenSource source = new CancellationTokenSource())
                {
                    source.Cancel();
                    await ExpectFailure(() => service.TranslateAsync("hello", "en", "zh-CN", source.Token), typeof(OperationCanceledException), null);
                }
                await service.TranslateAsync("hello", "en", "ja", CancellationToken.None);
                Assert(handler.Count == 2, "target is part of cache key");
                for (int i = 0; i < 35; i++) await service.TranslateAsync("entry " + i, "en", "zh-CN", CancellationToken.None);
                await service.TranslateAsync("hello", "en", "zh-CN", CancellationToken.None);
                Assert(handler.Count == 38, "32-entry bounded cache evicts old result");
            }
        });

        await Run("failed responses are never cached", async delegate
        {
            Handler handler = new Handler { Send = (r, t) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) };
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(5)))
            {
                await ExpectFailure(() => service.TranslateAsync("hello", "en", "zh-CN", CancellationToken.None), typeof(TranslationException), "503");
                handler.Send = null;
                Assert((await service.TranslateAsync("hello", "en", "zh-CN", CancellationToken.None)).Text == "hello", "retry succeeds");
                Assert(handler.Count == 2, "retry reached transport");
            }
        });

        await Run("validation and same-language behavior do not send network requests", async delegate
        {
            Handler handler = new Handler();
            using (TranslationService service = new TranslationService(handler, TimeSpan.FromSeconds(5)))
            {
                await ExpectFailure(() => service.TranslateAsync("  ", "en", "zh-CN", CancellationToken.None), typeof(TranslationException), "文字");
                await ExpectFailure(() => service.TranslateAsync(new string('a', 5001), "en", "zh-CN", CancellationToken.None), typeof(TranslationException), "5,000");
                await ExpectFailure(() => service.TranslateAsync("bad\ud800", "en", "zh-CN", CancellationToken.None), typeof(TranslationException), "Unicode");
                await ExpectFailure(() => service.TranslateAsync("hello", "invalid", "zh-CN", CancellationToken.None), typeof(TranslationException), "语言");
                TranslationResult result = await service.TranslateAsync("原文", "zh-CN", "zh-CN", CancellationToken.None);
                Assert(result.Text == "原文" && result.Provider.Contains("相同语言"), "same-language truthfulness");
                Assert(handler.Count == 0, "zero network for validation/same language");
            }
        });

        await Run("persistence defaults, round trip, atomic overwrite and corrupt recovery", () => Sync(delegate
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core-test-data-" + Guid.NewGuid().ToString("N"));
            SettingsStore store = new SettingsStore(directory);
            Assert(store.Load().CaptureHotkey == "Ctrl+Alt+A" && store.LastWarning == null, "missing files default quietly");
            AppSettings custom = new AppSettings { TargetLanguage = "ja", CaptureHotkey = "Ctrl+Shift+F8", StartMinimized = true, CloseToTray = false };
            store.Save(custom);
            AppSettings loaded = store.Load();
            Assert(loaded.TargetLanguage == "ja" && loaded.CaptureHotkey == "Ctrl+Shift+F8" && loaded.StartMinimized && !loaded.CloseToTray, "settings round trip");
            custom.TargetLanguage = "ko";
            store.Save(custom);
            Assert(store.Load().TargetLanguage == "ko", "atomic replacement");
            Assert(Directory.GetFiles(directory, "*.tmp").Length == 0, "no temporary debris");
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{broken", Encoding.UTF8);
            Assert(store.Load().SourceLanguage == "auto" && store.LastWarning != null, "corrupt settings recovered with warning");
            Assert(File.ReadAllText(Path.Combine(directory, "settings.json")) == "{broken", "corrupt original is preserved on load");
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"SourceLanguage\":\"bad\",\"TargetLanguage\":\"auto\",\"CaptureHotkey\":null}", Encoding.UTF8);
            loaded = store.Load();
            Assert(loaded.SourceLanguage == "auto" && loaded.TargetLanguage == "zh-CN" && loaded.CloseToTray, "partial and unsupported settings normalized");
            List<HistoryEntry> entries = Enumerable.Range(0, 55).Select(i => new HistoryEntry { SourceText = "source " + i, TranslatedText = "译文 😀 " + i,
                SourceLanguage = "en", TargetLanguage = "zh-CN", CreatedAt = new DateTime(2026, 1, 1).AddMinutes(i) }).ToList();
            entries.Add(null);
            store.SaveHistory(entries);
            List<HistoryEntry> history = store.LoadHistory();
            Assert(history.Count == 50 && history[0].SourceText == "source 54" && history[0].TranslatedText.Contains("😀"), "history limit, ordering, Unicode");
            foreach (HistoryEntry entry in entries.Where(e => e != null)) entry.TranslatedText = new string('中', 20000);
            store.SaveHistory(entries);
            Assert(new FileInfo(Path.Combine(directory, "history.json")).Length > 2 * 1024 * 1024, "CJK fixture exceeds two megabytes");
            Assert(store.LoadHistory().Count == 50 && store.LastWarning == null, "UTF-8 bytes do not incorrectly limit valid JSON characters");
            File.WriteAllText(Path.Combine(directory, "history.json"), "[invalid", Encoding.UTF8);
            Assert(store.LoadHistory().Count == 0 && store.LastWarning != null, "history corruption warning");
            // Leave the isolated evidence directory under work for inspection; no recursive deletion required.
        }));

        await Run("unwritable storage surfaces a friendly error", () => Sync(delegate
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core-file-blocker-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(path, "blocking file");
            SettingsStore store = new SettingsStore(path);
            bool caught = false;
            try { store.Save(new AppSettings()); }
            catch (IOException ex) { caught = ex.Message.Contains("无法保存") && store.LastWarning != null; }
            Assert(caught, "save failure does not silently claim success");
        }));
    }

    public static int Main()
    {
        try { Tests().GetAwaiter().GetResult(); Console.WriteLine("ALL " + passed + " CORE TESTS PASSED"); return 0; }
        catch (Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
    }
}
