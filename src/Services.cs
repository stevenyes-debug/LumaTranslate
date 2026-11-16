using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LumaTranslate
{
    public class AppSettings
    {
        public string SourceLanguage { get; set; }
        public string TargetLanguage { get; set; }
        public string CaptureHotkey { get; set; }
        public string SelectionHotkey { get; set; }
        public bool StartMinimized { get; set; }
        public bool CloseToTray { get; set; }

        public AppSettings()
        {
            SourceLanguage = "auto";
            TargetLanguage = "zh-CN";
            CaptureHotkey = "Ctrl+Alt+A";
            SelectionHotkey = "Ctrl+Alt+D";
            StartMinimized = false;
            CloseToTray = true;
        }
    }

    public class HistoryEntry
    {
        public string SourceText { get; set; }
        public string TranslatedText { get; set; }
        public string SourceLanguage { get; set; }
        public string TargetLanguage { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class SettingsStore
    {
        private readonly string directory;
        private readonly object sync = new object();
        public string LastWarning { get; private set; }
        public string DirectoryPath { get { return directory; } }

        public SettingsStore(string directory = null)
        {
            this.directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaTranslate");
        }

        public AppSettings Load()
        {
            lock (sync)
            {
                LastWarning = null;
                try
                {
                    string json = ReadFile("settings.json");
                    return json == null ? new AppSettings() : Normalize(Serializer().Deserialize<AppSettings>(json));
                }
                catch (Exception ex)
                {
                    if (!IsStorageError(ex)) throw;
                    LastWarning = "设置文件无法读取，已使用默认设置；原文件仍保留。";
                    return new AppSettings();
                }
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            lock (sync)
            {
                WriteFile("settings.json", Serializer().Serialize(Normalize(settings)));
            }
        }

        public List<HistoryEntry> LoadHistory()
        {
            lock (sync)
            {
                LastWarning = null;
                try
                {
                    string json = ReadFile("history.json");
                    if (json == null) return new List<HistoryEntry>();
                    List<HistoryEntry> entries = Serializer().Deserialize<List<HistoryEntry>>(json);
                    if (entries == null) throw new ArgumentException("History is null.");
                    return CleanHistory(entries);
                }
                catch (Exception ex)
                {
                    if (!IsStorageError(ex)) throw;
                    LastWarning = "历史记录无法读取，已显示空列表；原文件仍保留。";
                    return new List<HistoryEntry>();
                }
            }
        }

        public void SaveHistory(IEnumerable<HistoryEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException("entries");
            lock (sync)
            {
                WriteFile("history.json", Serializer().Serialize(CleanHistory(entries)));
            }
        }

        private static List<HistoryEntry> CleanHistory(IEnumerable<HistoryEntry> entries)
        {
            return entries.Where(e => e != null && !String.IsNullOrWhiteSpace(e.SourceText)
                && !String.IsNullOrWhiteSpace(e.TranslatedText) && e.SourceText.Length <= 5000
                && e.TranslatedText.Length <= 30000 && TranslationService.IsLanguage(e.SourceLanguage, false)
                && TranslationService.IsLanguage(e.TargetLanguage, false))
                .OrderByDescending(e => e.CreatedAt).Take(50).ToList();
        }

        private static AppSettings Normalize(AppSettings value)
        {
            if (value == null) throw new ArgumentException("Settings are null.");
            AppSettings defaults = new AppSettings();
            return new AppSettings
            {
                SourceLanguage = TranslationService.IsLanguage(value.SourceLanguage, true) ? value.SourceLanguage : defaults.SourceLanguage,
                TargetLanguage = TranslationService.IsLanguage(value.TargetLanguage, false) ? value.TargetLanguage : defaults.TargetLanguage,
                CaptureHotkey = ValidHotkeyText(value.CaptureHotkey) ? value.CaptureHotkey.Trim() : defaults.CaptureHotkey,
                SelectionHotkey = ValidHotkeyText(value.SelectionHotkey) ? value.SelectionHotkey.Trim() : defaults.SelectionHotkey,
                StartMinimized = value.StartMinimized,
                CloseToTray = value.CloseToTray
            };
        }

        private static bool ValidHotkeyText(string value)
        {
            return !String.IsNullOrWhiteSpace(value) && value.Length <= 64;
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 32 };
        }

        private string ReadFile(string name)
        {
            string path = Path.Combine(directory, name);
            // File.Exists hides access failures. Open explicitly so those failures reach LastWarning.
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // JSON's limit counts UTF-16 characters; UTF-8 CJK needs three bytes per character.
                    if (stream.Length > 8 * 1024 * 1024) throw new IOException("File is too large.");
                    using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                        return reader.ReadToEnd();
                }
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        private void WriteFile(string name, string json)
        {
            LastWarning = null;
            string temporary = null;
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, name);
                temporary = Path.Combine(directory, "." + name + "." + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else
                {
                    try { File.Move(temporary, path); }
                    catch (IOException)
                    {
                        if (!File.Exists(path)) throw;
                        File.Replace(temporary, path, null);
                    }
                }
                temporary = null;
            }
            catch (Exception ex)
            {
                if (!IsStorageError(ex)) throw;
                LastWarning = "无法保存到本地，请检查磁盘空间与文件夹写入权限。";
                throw new IOException(LastWarning, ex);
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static bool IsStorageError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is InvalidOperationException || ex is FormatException || ex is OverflowException
                || ex is System.Security.SecurityException;
        }
    }

    public class TranslationResult
    {
        public string Text { get; set; }
        public string SourceLanguage { get; set; }
        public string TargetLanguage { get; set; }
        public string Provider { get; set; }
    }

    public sealed class TranslationException : Exception
    {
        public TranslationException(string message) : base(message) { }
        public TranslationException(string message, Exception inner) : base(message, inner) { }
    }

    internal sealed class TranslationPart
    {
        internal string Text;
        internal bool IsLiteral;
        internal TranslationPart(string text, bool literal) { Text = text; IsLiteral = literal; }
    }

    public sealed class TranslationService : IDisposable
    {
        private const int MaxRequestBytes = 500;
        private const int MaxConcurrentRequests = 3;
        private readonly HttpClient client;
        private readonly TimeSpan operationTimeout;
        private readonly object cacheLock = new object();
        private readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>();
        private readonly object inFlightLock = new object();
        private readonly Dictionary<string, SharedSegment> inFlight = new Dictionary<string, SharedSegment>();
        private readonly SemaphoreSlim requestSlots = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);
        private volatile bool disposed;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly string[] Languages = { "zh-CN", "en", "ja", "ko", "fr", "de", "es", "ru" };

        private sealed class CacheEntry
        {
            internal TranslationResult Result;
            internal DateTime Expires;
        }

        private sealed class SharedSegment
        {
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal readonly TaskCompletionSource<string> Completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal string Key;
            internal readonly List<CancellationToken> Subscribers = new List<CancellationToken>();
            internal bool Finished;
        }

        public TranslationService() : this(CreateDefaultHandler(), TimeSpan.FromSeconds(90)) { }

        internal static HttpClientHandler CreateDefaultHandler()
        {
            // Keep Windows' configured proxy behavior. Disabling it can break managed networks.
            return new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                MaxConnectionsPerServer = MaxConcurrentRequests,
                UseCookies = false
            };
        }

        // Injectable transport keeps error, Unicode, timeout, and cancellation tests independent of the network.
        public TranslationService(HttpMessageHandler handler, TimeSpan operationTimeout)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            if (operationTimeout <= TimeSpan.Zero || operationTimeout.TotalMilliseconds > Int32.MaxValue)
                throw new ArgumentOutOfRangeException("operationTimeout");
            this.operationTimeout = operationTimeout;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            client = new HttpClient(handler, true);
            client.Timeout = TimeSpan.FromSeconds(20);
            client.MaxResponseContentBufferSize = 1024 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LumaTranslate/1.1");
        }

        public async Task<TranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken token)
        {
            if (disposed) throw new ObjectDisposedException("TranslationService");
            token.ThrowIfCancellationRequested();
            if (String.IsNullOrWhiteSpace(text)) throw new TranslationException("请先输入或选取要翻译的文字。");
            if (text.Length > 5000) throw new TranslationException("一次最多翻译 5,000 个字符，请缩短文字后重试。");
            try { StrictUtf8.GetByteCount(text); }
            catch (EncoderFallbackException ex) { throw new TranslationException("文字包含不完整的 Unicode 字符，请重新复制后重试。", ex); }
            if (!IsLanguage(sourceLanguage, true) || !IsLanguage(targetLanguage, false))
                throw new TranslationException("请选择支持的源语言和目标语言。");
            string source = sourceLanguage == "auto" ? DetectLanguage(text) : sourceLanguage;
            if (source == targetLanguage)
            {
                token.ThrowIfCancellationRequested();
                return new TranslationResult { Text = text, SourceLanguage = source, TargetLanguage = targetLanguage, Provider = "原文（相同语言）" };
            }
            string key = source + "\0" + targetLanguage + "\0" + text;
            lock (cacheLock)
            {
                CacheEntry cached;
                if (cache.TryGetValue(key, out cached) && cached.Expires > DateTime.UtcNow)
                {
                    token.ThrowIfCancellationRequested();
                    return CopyResult(cached.Result);
                }
            }

            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(operationTimeout);
                try
                {
                    string output = await TranslatePartsAsync(SplitText(text), source, targetLanguage, timeout.Token).ConfigureAwait(false);
                    timeout.Token.ThrowIfCancellationRequested();
                    TranslationResult result = new TranslationResult
                    {
                        Text = output, SourceLanguage = source, TargetLanguage = targetLanguage, Provider = "MyMemory"
                    };
                    lock (cacheLock)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        if (cache.Count >= 32)
                        {
                            string oldest = cache.OrderBy(item => item.Value.Expires).First().Key;
                            cache.Remove(oldest);
                        }
                        cache[key] = new CacheEntry { Result = CopyResult(result), Expires = DateTime.UtcNow.AddMinutes(10) };
                    }
                    return result;
                }
                catch (OperationCanceledException ex)
                {
                    if (token.IsCancellationRequested) throw new OperationCanceledException("翻译已取消。", ex, token);
                    throw new TranslationException("翻译请求超时，请检查网络后重试。", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new TranslationException("暂时无法连接在线翻译服务，请检查网络后重试。", ex);
                }
                catch (IOException ex)
                {
                    throw new TranslationException("读取翻译服务的响应失败，请重试。", ex);
                }
            }
        }

        private async Task<string> TranslatePartsAsync(List<TranslationPart> parts, string source, string target, CancellationToken token)
        {
            using (CancellationTokenSource batch = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                List<string> texts = parts.Where(p => !p.IsLiteral).Select(p => p.Text).Distinct(StringComparer.Ordinal).ToList();
                Dictionary<string, Task<string>> unique = new Dictionary<string, Task<string>>(StringComparer.Ordinal);
                List<Task<string>> pending = new List<Task<string>>();
                int next = 0;
                Exception failure = null;
                try
                {
                    // Only prepare a small moving window. This prevents a failed or cancelled batch
                    // from leaving hundreds of queued segments behind and wasting the free quota.
                    while (next < texts.Count || pending.Count > 0)
                    {
                        while (next < texts.Count && pending.Count < MaxConcurrentRequests)
                        {
                            batch.Token.ThrowIfCancellationRequested();
                            string text = texts[next++];
                            Task<string> task = TranslateBatchSegmentAsync(text, source, target, batch);
                            unique.Add(text, task);
                            pending.Add(task);
                        }
                        Task<string> completed = await Task.WhenAny(pending).ConfigureAwait(false);
                        await completed.ConfigureAwait(false);
                        pending.Remove(completed);
                    }
                }
                catch (Exception ex)
                {
                    batch.Cancel();
                    failure = ex;
                }
                if (failure != null)
                {
                    // Observe every started task before disposing the CTS. WhenAll preserves a real
                    // HTTP fault ahead of the sibling cancellations caused by that fault.
                    await Task.WhenAll(unique.Values).ConfigureAwait(false);
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                }
                token.ThrowIfCancellationRequested();
                StringBuilder output = new StringBuilder();
                foreach (TranslationPart part in parts)
                    output.Append(part.IsLiteral ? part.Text : unique[part.Text].Result);
                return output.ToString();
            }
        }

        private async Task<string> TranslateBatchSegmentAsync(string text, string source, string target, CancellationTokenSource batch)
        {
            try { return await GetSharedSegmentAsync(text, source, target, batch.Token).ConfigureAwait(false); }
            catch
            {
                batch.Cancel();
                throw;
            }
        }

        private async Task<string> GetSharedSegmentAsync(string text, string source, string target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string key = source + "\0" + target + "\0" + text;
            SharedSegment entry;
            bool start = false;
            lock (inFlightLock)
            {
                if (disposed) throw new ObjectDisposedException("TranslationService");
                if (!inFlight.TryGetValue(key, out entry))
                {
                    entry = new SharedSegment { Key = key };
                    // A caller may cancel before the transport faults. Observe abandoned faults too.
                    ObserveFailure(entry.Completion.Task);
                    inFlight.Add(key, entry);
                    start = true;
                }
                entry.Subscribers.Add(token);
            }
            if (start) StartSharedSegment(entry, text, source, target);
            try
            {
                Task<string> task = entry.Completion.Task;
                if (!task.IsCompleted)
                {
                    TaskCompletionSource<bool> cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (token.Register(() => cancelled.TrySetResult(true)))
                        await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                return await task.ConfigureAwait(false);
            }
            finally
            {
                lock (inFlightLock)
                {
                    entry.Subscribers.Remove(token);
                    if (entry.Subscribers.Count == 0 && !entry.Finished)
                    {
                        SharedSegment current;
                        if (inFlight.TryGetValue(key, out current) && Object.ReferenceEquals(current, entry)) inFlight.Remove(key);
                        // One caller cancelling must not cancel a request another caller still needs.
                        if (!entry.Completion.Task.IsCompleted) entry.Cancellation.Cancel();
                    }
                }
            }
        }

        private void StartSharedSegment(SharedSegment entry, string text, string source, string target)
        {
            // The runner catches transport failures and publishes them through Completion.
            // It also owns its CTS lifetime, even if all callers have already cancelled.
            Task runner = RunSharedSegmentAsync(entry, text, source, target);
            ObserveFailure(runner);
        }

        private static void ObserveFailure(Task task)
        {
            task.ContinueWith(t => { var observed = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task RunSharedSegmentAsync(SharedSegment entry, string text, string source, string target)
        {
            bool entered = false;
            try
            {
                await requestSlots.WaitAsync(entry.Cancellation.Token).ConfigureAwait(false);
                entered = true;
                entry.Cancellation.Token.ThrowIfCancellationRequested();
                lock (inFlightLock)
                {
                    // Cancelled callers may not yet have resumed their finally blocks. Check their
                    // tokens before dispatch so queued work cannot consume quota after cancellation.
                    if (disposed || !entry.Subscribers.Any(t => !t.IsCancellationRequested))
                        throw new OperationCanceledException(entry.Cancellation.Token);
                }
                string result = await TranslateSegmentAsync(text, source, target, entry.Cancellation.Token).ConfigureAwait(false);
                entry.Cancellation.Token.ThrowIfCancellationRequested();
                entry.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { entry.Completion.TrySetCanceled(); }
            catch (Exception ex) { entry.Completion.TrySetException(ex); }
            finally
            {
                // Do not release a connection slot while an uncooperative transport is still running.
                if (entered) requestSlots.Release();
                lock (inFlightLock)
                {
                    entry.Finished = true;
                    SharedSegment current;
                    if (inFlight.TryGetValue(entry.Key, out current) && Object.ReferenceEquals(current, entry)) inFlight.Remove(entry.Key);
                    entry.Cancellation.Dispose();
                }
            }
        }

        private async Task<string> TranslateSegmentAsync(string text, string source, string target, CancellationToken token)
        {
            string address = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text)
                + "&langpair=" + Uri.EscapeDataString(source + "|" + target) + "&mt=1";
            using (HttpResponseMessage response = await client.GetAsync(address, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                int code = (int)response.StatusCode;
                if (code == 429) throw new TranslationException("在线服务的免费额度已用完或请求过于频繁，请稍后再试。");
                if (code == 401 || code == 403) throw new TranslationException("在线服务拒绝了请求，可能受到额度或访问限制，请稍后再试。");
                if (!response.IsSuccessStatusCode) throw new TranslationException("在线翻译服务暂不可用（HTTP " + code.ToString(CultureInfo.InvariantCulture) + "），请稍后再试。");
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return ParseResponse(body);
            }
        }

        internal static string ParseResponse(string body)
        {
            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 }
                    .DeserializeObject(body) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is InvalidOperationException)) throw;
                throw new TranslationException("翻译服务返回了无法识别的数据，请稍后重试。", ex);
            }
            if (root == null) throw new TranslationException("翻译服务返回了无法识别的数据，请稍后重试。");
            object value;
            int status;
            if (!root.TryGetValue("responseStatus", out value)
                || !Int32.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out status))
                throw new TranslationException("翻译服务的响应缺少状态信息，请稍后重试。");
            bool quota = root.TryGetValue("quotaFinished", out value) && (Object.Equals(value, true)
                || String.Equals(value as string, "true", StringComparison.OrdinalIgnoreCase));
            string details = root.TryGetValue("responseDetails", out value) ? value as string : null;
            if (quota || status == 429 || IsQuotaWarning(details))
                throw new TranslationException("在线服务的免费额度已用完或请求过于频繁，请稍后再试。");
            if (status != 200)
            {
                if (!String.IsNullOrEmpty(details) && (details.IndexOf("LANGPAIR", StringComparison.OrdinalIgnoreCase) >= 0
                    || details.IndexOf("LANGUAGE", StringComparison.OrdinalIgnoreCase) >= 0))
                    throw new TranslationException("在线服务不支持当前语言组合，请手动选择源语言后重试。");
                throw new TranslationException("在线翻译服务返回错误（" + status.ToString(CultureInfo.InvariantCulture) + "），请稍后再试。");
            }
            Dictionary<string, object> data = root.TryGetValue("responseData", out value) ? value as Dictionary<string, object> : null;
            string translated = data != null && data.TryGetValue("translatedText", out value) ? value as string : null;
            if (String.IsNullOrWhiteSpace(translated)) throw new TranslationException("翻译服务没有返回译文，请稍后再试。");
            if (IsQuotaWarning(translated)) throw new TranslationException("在线服务的免费额度已用完，请稍后再试。");
            translated = WebUtility.HtmlDecode(translated);
            if (String.IsNullOrWhiteSpace(translated)) throw new TranslationException("翻译服务没有返回有效译文，请稍后再试。");
            return translated;
        }

        private static bool IsQuotaWarning(string value)
        {
            return !String.IsNullOrEmpty(value) && (value.IndexOf("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("USED ALL AVAILABLE FREE", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("QUOTA EXCEEDED", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool IsLanguage(string value, bool allowAuto)
        {
            return (allowAuto && value == "auto") || Array.IndexOf(Languages, value) >= 0;
        }

        // This is a script heuristic, not statistical language identification. Latin text defaults to English.
        // Japanese without kana, other Cyrillic languages, and French/German/Spanish need an explicit source choice.
        public static string DetectLanguage(string text)
        {
            int han = 0, kana = 0, hangul = 0, cyrillic = 0;
            foreach (char c in text ?? String.Empty)
            {
                if ((c >= '\u3040' && c <= '\u30ff') || (c >= '\uff66' && c <= '\uff9f')) kana++;
                else if ((c >= '\uac00' && c <= '\ud7af') || (c >= '\u1100' && c <= '\u11ff') || (c >= '\u3130' && c <= '\u318f')) hangul++;
                else if ((c >= '\u3400' && c <= '\u4dbf') || (c >= '\u4e00' && c <= '\u9fff') || (c >= '\uf900' && c <= '\ufaff')) han++;
                else if (c >= '\u0400' && c <= '\u052f') cyrillic++;
            }
            if (kana > 0) return "ja";
            if (hangul > 0 && hangul >= han) return "ko";
            if (han > 0 && han >= cyrillic) return "zh-CN";
            if (cyrillic > 0) return "ru";
            return "en";
        }

        internal static List<TranslationPart> SplitText(string text)
        {
            StrictUtf8.GetByteCount(text);
            List<TranslationPart> parts = new List<TranslationPart>();
            // Literal separators preserve blank paragraphs, CRLF, indentation, tabs, and repeated spaces.
            foreach (string block in Regex.Split(text, @"(\r\n|\r|\n|\u2028|\u2029|[ \t\f\v\u00a0]{2,}|\t)"))
            {
                if (block.Length == 0) continue;
                int position = 0;
                while (position < block.Length)
                {
                    int start = position;
                    while (position < block.Length && Char.IsWhiteSpace(block[position])) position++;
                    if (position > start) parts.Add(new TranslationPart(block.Substring(start, position - start), true));
                    if (position == block.Length) break;
                    start = position;
                    int bytes = 0;
                    int boundary = -1;
                    while (position < block.Length)
                    {
                        int width = Char.IsHighSurrogate(block[position]) ? 2 : 1;
                        int nextBytes = StrictUtf8.GetByteCount(block.Substring(position, width));
                        if (bytes + nextBytes > MaxRequestBytes) break;
                        bytes += nextBytes;
                        position += width;
                        char last = block[position - 1];
                        if (Char.IsWhiteSpace(last) || ".!?。！？;；".IndexOf(last) >= 0) boundary = position;
                    }
                    if (position < block.Length && boundary > start && boundary - start >= (position - start) / 2) position = boundary;
                    int contentEnd = position;
                    while (contentEnd > start && Char.IsWhiteSpace(block[contentEnd - 1])) contentEnd--;
                    if (contentEnd > start) parts.Add(new TranslationPart(block.Substring(start, contentEnd - start), false));
                    if (contentEnd < position) parts.Add(new TranslationPart(block.Substring(contentEnd, position - contentEnd), true));
                }
            }
            return parts;
        }

        private static TranslationResult CopyResult(TranslationResult value)
        {
            return new TranslationResult { Text = value.Text, SourceLanguage = value.SourceLanguage,
                TargetLanguage = value.TargetLanguage, Provider = value.Provider };
        }

        public void Dispose()
        {
            lock (inFlightLock)
            {
                if (disposed) return;
                disposed = true;
                foreach (SharedSegment entry in inFlight.Values.ToArray())
                    if (!entry.Finished) entry.Cancellation.Cancel();
            }
            client.Dispose();
            lock (cacheLock) cache.Clear();
            // SemaphoreSlim has no native handle here. Active runners retain it until their final release.
        }
    }
}
