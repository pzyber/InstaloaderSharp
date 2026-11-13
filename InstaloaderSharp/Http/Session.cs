using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using InstaloaderSharp.Json;
using InstaloaderSharp.Util;

namespace InstaloaderSharp.Http
{
    /// <summary>An HTTP response, like requests.Response.</summary>
    public sealed class Response : IDisposable
    {
        private WinHttpRequestHandle _handle;
        private byte[] _content;
        private bool _consumed;

        internal Response() { }

        public int StatusCode { get; internal set; }

        /// <summary>Reason phrase, e.g. "Not Found".</summary>
        public string Reason { get; internal set; }

        /// <summary>Final URL of the request.</summary>
        public string Url { get; internal set; }

        public string HttpVersion { get; internal set; }

        public HttpHeaders Headers { get; internal set; }

        /// <summary>Cookies set by this response.</summary>
        public CookieJar Cookies { get; internal set; }

        /// <summary>Redirect responses that preceded this one.</summary>
        public List<Response> History { get; internal set; }

        public bool Ok { get { return StatusCode < 400; } }

        /// <summary>requests.Response.is_redirect.</summary>
        public bool IsRedirect
        {
            get
            {
                return Headers.ContainsKey("location") &&
                       (StatusCode == 301 || StatusCode == 302 || StatusCode == 303 || StatusCode == 307 || StatusCode == 308);
            }
        }

        internal void SetBody(WinHttpRequestHandle handle)
        {
            _handle = handle;
        }

        internal void SetContent(byte[] content)
        {
            _content = content;
            _consumed = true;
        }

        /// <summary>
        /// The decoded body as a stream (for streaming downloads). Can only be consumed once; afterwards (or if
        /// <see cref="Content"/> has been accessed) a stream over the buffered content is returned.
        /// </summary>
        public Stream GetBodyStream()
        {
            if (_consumed)
                return new MemoryStream(_content ?? new byte[0], false);
            _consumed = true;
            if (_handle == null)
            {
                _content = new byte[0];
                return new MemoryStream(_content, false);
            }
            Stream raw = new WinHttpBodyStream(_handle);
            return Decode(raw, Headers.Get("Content-Encoding"));
        }

        internal static Stream Decode(Stream raw, string contentEncoding)
        {
            if (string.IsNullOrEmpty(contentEncoding))
                return raw;
            Stream result = raw;
            string[] encodings = contentEncoding.Split(',');
            for (int i = encodings.Length - 1; i >= 0; i--)
            {
                string enc = encodings[i].Trim().ToLowerInvariant();
                if (enc == "gzip" || enc == "x-gzip")
                    result = new GZipStream(result, CompressionMode.Decompress);
                else if (enc == "deflate")
                    result = new ZlibOrDeflateStream(result);
                else if (enc == "identity" || enc.Length == 0)
                    continue;
                else
                    throw new RequestException("Unsupported content encoding " + enc);
            }
            return result;
        }

        /// <summary>The decoded body.</summary>
        public byte[] Content
        {
            get
            {
                if (_content == null)
                {
                    if (_consumed)
                        throw new InvalidOperationException("The content for this response was already consumed");
                    using (Stream s = GetBodyStream())
                    {
                        var ms = new MemoryStream();
                        try
                        {
                            s.CopyTo(ms);
                        }
                        catch (InvalidDataException ex)
                        {
                            throw new RequestException("Received response with content-encoding, but failed to decode it: " + ex.Message);
                        }
                        _content = ms.ToArray();
                    }
                    Close();
                }
                return _content;
            }
        }

        /// <summary>Encoding from the Content-Type charset, like requests' get_encoding_from_headers.</summary>
        public Encoding Encoding
        {
            get
            {
                string contentType = Headers.Get("Content-Type");
                if (contentType == null)
                    return null;
                foreach (string param in contentType.Split(';'))
                {
                    string p = param.Trim();
                    if (p.StartsWith("charset=", StringComparison.OrdinalIgnoreCase))
                    {
                        string charset = p.Substring(8).Trim('"', '\'', ' ');
                        try
                        {
                            return Encoding.GetEncoding(charset);
                        }
                        catch (ArgumentException)
                        {
                            return null;
                        }
                    }
                }
                if (contentType.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Encoding.GetEncoding(28591); // ISO-8859-1
                if (contentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new UTF8Encoding(false);
                return null;
            }
        }

        /// <summary>The body decoded to text.</summary>
        public string Text
        {
            get
            {
                Encoding enc = Encoding ?? new UTF8Encoding(false);
                string text = enc.GetString(Content);
                if (text.Length > 0 && text[0] == (char)0xFEFF)
                    text = text.Substring(1);
                return text;
            }
        }

        /// <summary>requests.Response.json(); raises <see cref="Python.JsonDecodeError"/>.</summary>
        public JToken Json()
        {
            Encoding enc = Encoding;
            string text = enc == null ? new UTF8Encoding(false).GetString(Content) : Text;
            if (text.Length > 0 && text[0] == (char)0xFEFF)
                text = text.Substring(1);
            return PyJson.Loads(text);
        }

        public void Close()
        {
            if (_handle != null)
            {
                _handle.Dispose();
                _handle = null;
            }
        }

        public void Dispose()
        {
            Close();
        }
    }

    /// <summary>"deflate" content encoding, which servers send zlib-wrapped or raw.</summary>
    internal sealed class ZlibOrDeflateStream : Stream
    {
        private readonly Stream _inner;
        private Stream _decoder;

        public ZlibOrDeflateStream(Stream inner)
        {
            _inner = inner;
        }

        private void Init()
        {
            if (_decoder != null)
                return;
            int b0 = _inner.ReadByte();
            int b1 = b0 < 0 ? -1 : _inner.ReadByte();
            bool zlib = b0 >= 0 && b1 >= 0 && (b0 & 0x0F) == 8 && ((b0 << 8) | b1) % 31 == 0;
            Stream source = _inner;
            if (!zlib)
            {
                var prefix = new List<byte>();
                if (b0 >= 0) prefix.Add((byte)b0);
                if (b1 >= 0) prefix.Add((byte)b1);
                source = new PrefixedStream(prefix.ToArray(), _inner);
            }
            _decoder = new DeflateStream(source, CompressionMode.Decompress);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Init();
            return _decoder.Read(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (_decoder != null)
                _decoder.Dispose();
            _inner.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

        private sealed class PrefixedStream : Stream
        {
            private readonly byte[] _prefix;
            private int _pos;
            private readonly Stream _rest;

            public PrefixedStream(byte[] prefix, Stream rest)
            {
                _prefix = prefix;
                _rest = rest;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_pos < _prefix.Length)
                {
                    int n = Math.Min(count, _prefix.Length - _pos);
                    Buffer.BlockCopy(_prefix, _pos, buffer, offset, n);
                    _pos += n;
                    return n;
                }
                return _rest.Read(buffer, offset, count);
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }

    /// <summary>
    /// HTTP session like requests.Session: persistent headers and cookies, redirect handling, and HTTP/2 for
    /// Instagram's API hosts (which reject HTTP/1.1 requests), like Instaloader's HTTP2Adapter.
    /// </summary>
    public sealed class Session : IDisposable
    {
        /// <summary>Hosts that are spoken to over HTTP/2 (instaloadercontext.INSTAGRAM_HOSTS).</summary>
        public static readonly string[] Http2Hosts = { "https://www.instagram.com/", "https://i.instagram.com/" };

        public const int MaxRedirects = 30;

        /// <summary>
        /// For tests: maps the URL of each request to the URL it is sent to instead, such as a local server's. The
        /// cookies and the answer stay those of the original URL.
        /// </summary>
        internal static Func<string, string> SendToForTests;

        public Session()
        {
            Headers = new HttpHeaders
            {
                { "User-Agent", "python-requests/2.32.3" },
                { "Accept-Encoding", "gzip, deflate" },
                { "Accept", "*/*" },
                { "Connection", "keep-alive" }
            };
            Cookies = new CookieJar();
        }

        public HttpHeaders Headers { get; set; }

        public CookieJar Cookies { get; set; }

        /// <summary>Timeout (seconds) applied to requests that do not specify one; null waits forever.</summary>
        public double? Timeout { get; set; }

        public Response Get(string url, JObject parameters = null, HttpHeaders headers = null, bool allowRedirects = true,
                            bool stream = false, double? timeout = null)
        {
            return Request("GET", url, parameters, null, headers, allowRedirects, stream, timeout);
        }

        public Response Post(string url, JObject data = null, HttpHeaders headers = null, bool allowRedirects = true,
                             double? timeout = null)
        {
            return Request("POST", url, null, data, headers, allowRedirects, false, timeout);
        }

        public Response Head(string url, bool allowRedirects = false, double? timeout = null)
        {
            return Request("HEAD", url, null, null, null, allowRedirects, false, timeout);
        }

        private static readonly HashSet<string> SkippedRequestHeaders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "host", "connection", "transfer-encoding", "content-length" };

        private static bool UsesHttp2(string url)
        {
            foreach (string prefix in Http2Hosts)
                if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public Response Request(string method, string url, JObject parameters, JObject data, HttpHeaders headers,
                                bool allowRedirects, bool stream, double? timeout)
        {
            string preparedUrl = UrlUtil.PrepareUrl(url, parameters);
            var merged = Headers.Copy();
            if (headers != null)
                foreach (var kv in headers)
                    merged[kv.Key] = kv.Value;
            byte[] body = null;
            if (data != null)
            {
                body = Encoding.ASCII.GetBytes(UrlUtil.UrlEncode(data));
                if (!merged.ContainsKey("Content-Type"))
                    merged["Content-Type"] = "application/x-www-form-urlencoded";
            }
            double? effectiveTimeout = timeout ?? Timeout;
            var history = new List<Response>();
            Response response = Send(method, preparedUrl, merged, body, stream || allowRedirects, effectiveTimeout);
            while (allowRedirects && response.IsRedirect)
            {
                if (history.Count >= MaxRedirects)
                {
                    response.Close();
                    throw new RequestException(string.Format("Exceeded {0} redirects.", MaxRedirects));
                }
                // Consume the body of the redirect response, so that the connection can be reused.
                try
                {
                    byte[] ignored = response.Content;
                }
                catch (RequestException)
                {
                }
                response.Close();
                history.Add(response);
                string location = response.Headers.Get("location");
                string nextUrl = UrlUtil.RequoteUri(UrlUtil.Resolve(response.Url, location));
                // requests' rebuild_method
                if (response.StatusCode == 303 && method != "HEAD")
                    method = "GET";
                if ((response.StatusCode == 302 || response.StatusCode == 301) && method == "POST")
                    method = "GET";
                if (response.StatusCode != 307 && response.StatusCode != 308)
                {
                    body = null;
                    merged.Remove("Content-Type");
                    merged.Remove("Content-Length");
                    merged.Remove("Transfer-Encoding");
                }
                response = Send(method, nextUrl, merged, body, true, effectiveTimeout);
            }
            response.History = history;
            if (!stream)
            {
                byte[] content = response.Content;
            }
            return response;
        }

        private Response Send(string method, string url, HttpHeaders headers, byte[] body, bool streamBody, double? timeout)
        {
            ParsedUrl parsed = ParsedUrl.Parse(url);
            var requestHeaders = new List<KeyValuePair<string, string>>();
            foreach (var kv in headers.Merged())
            {
                if (SkippedRequestHeaders.Contains(kv.Key) || kv.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                    continue;
                requestHeaders.Add(kv);
            }
            string cookieHeader = Cookies.GetCookieHeader(parsed);
            string explicitCookie = headers.Get("Cookie");
            if (explicitCookie != null)
                cookieHeader = explicitCookie;
            if (cookieHeader != null)
                requestHeaders.Add(new KeyValuePair<string, string>("Cookie", cookieHeader));

            bool http2 = UsesHttp2(url);
            ParsedUrl target = parsed;
            if (SendToForTests != null)
            {
                target = ParsedUrl.Parse(SendToForTests(url));
                http2 = false;
            }
            WinHttpRawResponse raw = WinHttpClient.Send(method, target, requestHeaders, body, http2, timeout);
            var response = new Response
            {
                StatusCode = raw.StatusCode,
                Reason = raw.Reason,
                Url = url,
                HttpVersion = raw.HttpVersion,
                Headers = new HttpHeaders(raw.Headers),
                Cookies = new CookieJar()
            };
            List<string> setCookies = response.Headers.GetAll("Set-Cookie");
            response.Cookies.ExtractCookies(parsed, setCookies);
            Cookies.ExtractCookies(parsed, setCookies);
            if (method == "HEAD")
            {
                raw.Handle.Dispose();
                response.SetContent(new byte[0]);
                return response;
            }
            response.SetBody(raw.Handle);
            if (http2 || !streamBody)
            {
                // The HTTP/2 adapter of Instaloader reads the (small) API responses eagerly and passes the
                // decoded body on with an accordingly updated Content-Length.
                byte[] content = response.Content;
                if (http2)
                {
                    response.Headers.Remove("content-encoding");
                    response.Headers.Remove("content-length");
                    response.Headers.Remove("transfer-encoding");
                    response.Headers.Append("Content-Length", content.Length.ToString(CultureInfo.InvariantCulture));
                }
            }
            return response;
        }

        public void Close()
        {
        }

        public void Dispose()
        {
            Close();
        }
    }
}
