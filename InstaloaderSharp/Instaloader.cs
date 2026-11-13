using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using InstaloaderSharp.Http;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;
using InstaloaderSharp.Util;

namespace InstaloaderSharp
{
    /// <summary>
    /// Formatter that substitutes {ATTRIBUTE} by attributes of an item (_ArbitraryItemFormatter), lets None yield an
    /// empty string and formats datetimes as '%Y-%m-%d_%H-%M-%S' by default.
    /// </summary>
    public class ArbitraryItemFormatter : PyFormatter
    {
        protected readonly object Item;

        public ArbitraryItemFormatter(object item)
        {
            Item = item;
        }

        public override object GetValue(object key, List<object> args, Dictionary<string, object> kwargs)
        {
            var name = key as string;
            if (name != null)
            {
                if (name == "filename" && (Item is Post || Item is StoryItem || Item is PostSidecarNode || Item is TitlePic))
                    return "{filename}";
                if (Item != null)
                {
                    try
                    {
                        return PyRuntime.GetAttr(Item, name);
                    }
                    catch (PyAttributeError)
                    {
                    }
                }
            }
            else if (Item != null)
            {
                throw new PyTypeError("attribute name must be string, not 'int'");
            }
            return PyRuntime.ToPy(base.GetValue(key, args, kwargs));
        }

        public override string FormatField(object value, string formatSpec)
        {
            if (value is PyDateTime && string.IsNullOrEmpty(formatSpec))
                return base.FormatField(value, "%Y-%m-%d_%H-%M-%S");
            if (value == null)
                return "";
            return base.FormatField(value, formatSpec);
        }

        /// <summary>Formats the given pattern with keyword arguments.</summary>
        public string Format(string formatString, object target = null, bool withTarget = false)
        {
            var kwargs = new Dictionary<string, object>();
            if (withTarget)
                kwargs["target"] = PyRuntime.ToPy(target);
            return VFormat(formatString, new List<object>(), kwargs);
        }
    }

    /// <summary>Formatter for paths that sanitizes the substituted values (_PostPathFormatter).</summary>
    public class PostPathFormatter : ArbitraryItemFormatter
    {
        private static readonly HashSet<string> Reserved = new HashSet<string>
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private readonly bool _forceWindowsPath;

        public PostPathFormatter(object item, bool forceWindowsPath = false) : base(item)
        {
            _forceWindowsPath = forceWindowsPath;
        }

        public override object GetValue(object key, List<object> args, Dictionary<string, object> kwargs)
        {
            object ret = base.GetValue(key, args, kwargs);
            var s = ret as string;
            if (s == null)
                return ret;
            return SanitizePath(s, _forceWindowsPath);
        }

        /// <summary>
        /// Also makes formatted values such as "{date_utc:%c}" (which contains colons) safe for file names. Instaloader
        /// only sanitizes string values, so on Windows such patterns fail with "Invalid argument". Path separators are
        /// kept, so that e.g. "{date_utc:%Y/%m}" still creates subdirectories.
        /// </summary>
        public override string FormatField(object value, string formatSpec)
        {
            string ret = base.FormatField(value, formatSpec);
            if (value is string || !(_forceWindowsPath || PyOs.IsWindows))
                return ret;
            return ReplaceIllegalWindowsChars(ret);
        }

        private static string ReplaceIllegalWindowsChars(string ret)
        {
            ret = ret.Replace(':', (char)0xFF1A).Replace('<', (char)0xFE64).Replace('>', (char)0xFE65).Replace('"', (char)0xFF02);
            ret = ret.Replace('|', (char)0xFF5C).Replace('?', (char)0xFE16).Replace('*', (char)0xFF0A);
            return ret.Replace('\n', ' ').Replace('\r', ' ');
        }

        /// <summary>Replaces '/' with similar looking Division Slash and some other illegal filename characters on Windows.</summary>
        public static string SanitizePath(string ret, bool forceWindowsPath = false)
        {
            ret = ret.Replace("/", Chars.DivisionSlash);

            if (ret.StartsWith("."))
                ret = Chars.OneDotLeader + ret.Substring(1);

            if (forceWindowsPath || PyOs.IsWindows)
            {
                ret = ReplaceIllegalWindowsChars(ret.Replace('\\', (char)0xFE68));
                string root, ext;
                PyOs.SplitExt(ret, out root, out ext);
                if (Reserved.Contains(PyStr.Upper(root)))
                    root += "_";
                if (ext == ".")
                    ext = Chars.OneDotLeader;
                ret = root + ext;
            }
            return ret;
        }
    }

    /// <summary>An anonymous copy of an Instaloader; disposing it copies its error log back.</summary>
    public sealed class AnonymousInstaloader : IDisposable
    {
        private readonly Instaloader _parent;

        internal AnonymousInstaloader(Instaloader parent, Instaloader loader)
        {
            _parent = parent;
            Loader = loader;
        }

        public Instaloader Loader { get; private set; }

        public void Dispose()
        {
            _parent.Context.ErrorLog.AddRange(Loader.Context.ErrorLog);
            Loader.Context.ErrorLog = new List<string>(); // avoid double-printing of errors
            Loader.Close();
        }
    }

    /// <summary>Instaloader Class.</summary>
    public class Instaloader : IDisposable
    {
        /// <summary>Version of InstaloaderSharp.</summary>
        public const string Version = "1.1.5";

        /// <summary>
        /// Version of Python Instaloader that this is a port of. It is written into the "instaloader" section of
        /// JSON metadata and resume files, so that they are identical to the ones written by Python Instaloader.
        /// </summary>
        public const string InstaloaderVersion = "4.16";

        #region module-level functions

        private static string GetUser()
        {
            foreach (string name in new[] { "LOGNAME", "USER", "LNAME", "USERNAME" })
            {
                string user = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(user))
                    return user;
            }
            return Environment.UserName;
        }

        private static string GetTempDir()
        {
            return Path.GetTempPath().TrimEnd('\\', '/');
        }

        internal static string GetConfigDir()
        {
            if (PyOs.IsWindows)
            {
                // on Windows, use %LOCALAPPDATA%\Instaloader
                string localappdata = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (localappdata != null)
                    return PyOs.Join(localappdata, "Instaloader");
                // legacy fallback - store in temp dir if %LOCALAPPDATA% is not set
                return PyOs.Join(GetTempDir(), ".instaloader-" + GetUser());
            }
            // on Unix, use ~/.config/instaloader
            string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ??
                         PyOs.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return PyOs.Join(xdg, "instaloader");
        }

        /// <summary>Returns default session filename for given username.</summary>
        public static string GetDefaultSessionFilename(string username)
        {
            return PyOs.Join(GetConfigDir(), "session-" + username);
        }

        /// <summary>Returns legacy (until v4.4.3) default session filename for given username.</summary>
        public static string GetLegacySessionFilename(string username)
        {
            string dirname = GetTempDir() + "/" + ".instaloader-" + GetUser();
            string filename = dirname + "/" + "session-" + username;
            return filename.ToLowerInvariant();
        }

        /// <summary>Returns default filename for latest stamps database.</summary>
        public static string GetDefaultStampsFilename()
        {
            return PyOs.Join(GetConfigDir(), "latest-stamps.ini");
        }

        public static bool FormatStringContainsKey(string formatString, string key)
        {
            return PyFormatter.FormatStringContainsKey(formatString, key);
        }

        /// <summary>Python's str.format with keyword arguments only.</summary>
        internal static string PlainFormat(string formatString, params object[] keyValues)
        {
            var kwargs = new Dictionary<string, object>();
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
                kwargs[(string)keyValues[i]] = PyRuntime.ToPy(keyValues[i + 1]);
            return new PyFormatter().Format(formatString, kwargs);
        }

        #endregion

        public InstaloaderContext Context { get; private set; }

        public string DirnamePattern { get; set; }
        public string FilenamePattern { get; set; }
        public string TitlePattern { get; set; }
        public bool SanitizePaths { get; set; }
        public bool DownloadPictures { get; set; }
        public bool DownloadVideos { get; set; }
        public bool DownloadVideoThumbnails { get; set; }
        public bool DownloadGeotags { get; set; }
        public bool DownloadComments { get; set; }
        public bool SaveMetadata { get; set; }
        public bool CompressJson { get; set; }
        public string PostMetadataTxtPattern { get; set; }
        public string StoryitemMetadataTxtPattern { get; set; }
        public string ResumePrefix { get; set; }
        public bool CheckResumeBbd { get; set; }
        public string Slide { get; private set; }
        public int SlideStart { get; private set; }
        public int SlideEnd { get; private set; }

        /// <param name="sleep">not --no-sleep</param>
        /// <param name="quiet">--quiet</param>
        /// <param name="userAgent">--user-agent</param>
        /// <param name="dirnamePattern">--dirname-pattern, default is {target}</param>
        /// <param name="filenamePattern">--filename-pattern, default is {date_utc}_UTC</param>
        /// <param name="downloadPictures">not --no-pictures</param>
        /// <param name="downloadVideos">not --no-videos</param>
        /// <param name="downloadVideoThumbnails">not --no-video-thumbnails</param>
        /// <param name="downloadGeotags">--geotags</param>
        /// <param name="downloadComments">--comments</param>
        /// <param name="saveMetadata">not --no-metadata-json</param>
        /// <param name="compressJson">not --no-compress-json</param>
        /// <param name="postMetadataTxtPattern">--post-metadata-txt, default is {caption}. Set to empty string to
        /// avoid creation of post metadata txt file.</param>
        /// <param name="storyitemMetadataTxtPattern">--storyitem-metadata-txt, default is empty (=none)</param>
        /// <param name="maxConnectionAttempts">--max-connection-attempts</param>
        /// <param name="requestTimeout">--request-timeout, set per-request timeout (seconds)</param>
        /// <param name="rateController">Generator for a RateController to override rate controlling behavior</param>
        /// <param name="resumePrefix">--resume-prefix, or null for --no-resume.</param>
        /// <param name="checkResumeBbd">Whether to check the date of expiry of resume files and reject them if expired.</param>
        /// <param name="slide">--slide</param>
        /// <param name="fatalStatusCodes">--abort-on</param>
        /// <param name="iphoneSupport">not --no-iphone</param>
        /// <param name="titlePattern">--title-pattern, default is {date_utc}_UTC_{typename} if dirname_pattern
        /// contains {target} or {profile}, {target}_{date_utc}_UTC_{typename} otherwise.</param>
        /// <param name="sanitizePaths">--sanitize-paths</param>
        public Instaloader(bool sleep = true,
                           bool quiet = false,
                           string userAgent = null,
                           string dirnamePattern = null,
                           string filenamePattern = null,
                           bool downloadPictures = true,
                           bool downloadVideos = true,
                           bool downloadVideoThumbnails = true,
                           bool downloadGeotags = false,
                           bool downloadComments = false,
                           bool saveMetadata = true,
                           bool compressJson = true,
                           string postMetadataTxtPattern = null,
                           string storyitemMetadataTxtPattern = null,
                           int maxConnectionAttempts = 3,
                           double requestTimeout = 300.0,
                           Func<InstaloaderContext, RateController> rateController = null,
                           string resumePrefix = "iterator",
                           bool checkResumeBbd = true,
                           string slide = null,
                           IList<int> fatalStatusCodes = null,
                           bool iphoneSupport = true,
                           string titlePattern = null,
                           bool sanitizePaths = false)
        {
            Context = new InstaloaderContext(sleep, quiet, userAgent, maxConnectionAttempts, requestTimeout,
                                             rateController, fatalStatusCodes, iphoneSupport);

            // configuration parameters
            DirnamePattern = string.IsNullOrEmpty(dirnamePattern) ? "{target}" : dirnamePattern;
            FilenamePattern = string.IsNullOrEmpty(filenamePattern) ? "{date_utc}_UTC" : filenamePattern;
            if (titlePattern != null)
            {
                TitlePattern = titlePattern;
            }
            else
            {
                if (FormatStringContainsKey(DirnamePattern, "profile") || FormatStringContainsKey(DirnamePattern, "target"))
                    TitlePattern = "{date_utc}_UTC_{typename}";
                else
                    TitlePattern = "{target}_{date_utc}_UTC_{typename}";
            }
            SanitizePaths = sanitizePaths;
            DownloadPictures = downloadPictures;
            DownloadVideos = downloadVideos;
            DownloadVideoThumbnails = downloadVideoThumbnails;
            DownloadGeotags = downloadGeotags;
            DownloadComments = downloadComments;
            SaveMetadata = saveMetadata;
            CompressJson = compressJson;
            PostMetadataTxtPattern = postMetadataTxtPattern ?? "{caption}";
            StoryitemMetadataTxtPattern = storyitemMetadataTxtPattern ?? "";
            ResumePrefix = resumePrefix;
            CheckResumeBbd = checkResumeBbd;

            Slide = slide ?? "";
            SlideStart = 0;
            SlideEnd = -1;
            if (Slide != "")
            {
                string[] splitted = Slide.Split('-');
                if (splitted.Length == 1)
                {
                    if (splitted[0] == "last")
                    {
                        // download only last image of a sidecar
                        SlideStart = -1;
                    }
                    else
                    {
                        int value = ParseSlideInt(splitted[0]);
                        if (value > 0)
                            SlideStart = SlideEnd = value - 1;
                        else
                            throw new InvalidArgumentException("--slide parameter must be greater than 0.");
                    }
                }
                else if (splitted.Length == 2)
                {
                    if (splitted[1] == "last")
                    {
                        SlideStart = ParseSlideInt(splitted[0]) - 1;
                    }
                    else
                    {
                        int first = ParseSlideInt(splitted[0]);
                        int second = ParseSlideInt(splitted[1]);
                        if (0 < first && first < second)
                        {
                            SlideStart = first - 1;
                            SlideEnd = second - 1;
                        }
                        else
                        {
                            throw new InvalidArgumentException("Invalid data for --slide parameter.");
                        }
                    }
                }
                else
                {
                    throw new InvalidArgumentException("Invalid data for --slide parameter.");
                }
            }
        }

        private static int ParseSlideInt(string s)
        {
            try
            {
                return checked((int)PyInt.ParseLong(s));
            }
            catch (Exception ex) when (ex is PyValueError || ex is OverflowException || ex is PyOverflowError)
            {
                throw new PyValueError("invalid literal for int() with base 10: " + PyStr.Repr(s));
            }
        }

        /// <summary>Returns an anonymous, otherwise equally-configured copy of an Instaloader instance; disposing
        /// it copies its error log.</summary>
        public AnonymousInstaloader AnonymousCopy()
        {
            var newLoader = new Instaloader(
                sleep: Context.Sleep,
                quiet: Context.Quiet,
                userAgent: Context.UserAgent,
                dirnamePattern: DirnamePattern,
                filenamePattern: FilenamePattern,
                downloadPictures: DownloadPictures,
                downloadVideos: DownloadVideos,
                downloadVideoThumbnails: DownloadVideoThumbnails,
                downloadGeotags: DownloadGeotags,
                downloadComments: DownloadComments,
                saveMetadata: SaveMetadata,
                compressJson: CompressJson,
                postMetadataTxtPattern: PostMetadataTxtPattern,
                storyitemMetadataTxtPattern: StoryitemMetadataTxtPattern,
                maxConnectionAttempts: Context.MaxConnectionAttempts,
                requestTimeout: Context.RequestTimeout,
                resumePrefix: ResumePrefix,
                checkResumeBbd: CheckResumeBbd,
                slide: Slide,
                fatalStatusCodes: Context.FatalStatusCodes,
                iphoneSupport: Context.IphoneSupport,
                sanitizePaths: SanitizePaths);
            return new AnonymousInstaloader(this, newLoader);
        }

        /// <summary>Close associated session objects and repeat error log.</summary>
        public void Close()
        {
            Context.Close();
        }

        public void Dispose()
        {
            Close();
        }

        public override string ToString()
        {
            return string.Format("<Instaloader context={0}>", Context);
        }

        /// <summary>The errors that _retry_on_connection_error retries on (network errors).</summary>
        private static bool IsRetryable(Exception err)
        {
            return err is RequestException || err is ConnectionException;
        }

        /// <summary>Retries the function MaxConnectionAttempts times (_retry_on_connection_error).</summary>
        private T RetryOnConnectionError<T>(string funcName, object[] args, Func<T> func, int attempt = 1)
        {
            try
            {
                return func();
            }
            catch (Exception err) when (IsRetryable(err))
            {
                string errorString = string.Format("{0}({1}): {2}", funcName, string.Join(", ", args.Select(PyStr.Repr)), err.Message);
                if (attempt == Context.MaxConnectionAttempts)
                    throw new ConnectionException(errorString);
                Context.Error(errorString + " [retrying; skip with ^C]", repeatAtEnd: false);
                try
                {
                    Context.DoSleep();
                    return RetryOnConnectionError(funcName, args, func, attempt + 1);
                }
                catch (KeyboardInterruptException)
                {
                    Context.Error("[skipped by user]", repeatAtEnd: false);
                    throw new ConnectionException(errorString);
                }
            }
        }

        private void RequiresLogin()
        {
            if (!Context.IsLoggedIn)
                throw new LoginRequiredException("Login required.");
        }

        /// <summary>
        /// Downloads and saves picture with given url under given directory with given timestamp.
        /// Returns true, if file was actually downloaded, i.e. updated.
        /// </summary>
        public bool DownloadPic(string filename, string url, DateTimeOffset mtime, string filenameSuffix = null)
        {
            return RetryOnConnectionError("download_pic", new object[0], () => DownloadPicImpl(filename, url, mtime, filenameSuffix));
        }

        private static readonly Regex UrlExtensionRegex = new Regex(@"\.[a-z0-9]*\?");

        private bool DownloadPicImpl(string filename, string url, DateTimeOffset mtime, string filenameSuffix)
        {
            if (filenameSuffix != null)
                filename += "_" + filenameSuffix;
            Match urlmatch = UrlExtensionRegex.Match(url);
            string fileExtension = !urlmatch.Success
                ? PyStr.Slice(url, -3, null)
                : urlmatch.Value.Substring(1, urlmatch.Value.Length - 2);
            string nominalFilename = filename + "." + fileExtension;
            if (PyOs.IsFile(nominalFilename))
            {
                Context.Log(nominalFilename + " exists", end: " ", flush: true);
                return false;
            }
            Response resp = Context.GetRaw(url);
            try
            {
                string contentType = resp.Headers.Get("Content-Type");
                if (!string.IsNullOrEmpty(contentType))
                {
                    string headerExtension = "." + contentType.Split(';')[0].Split('/').Last();
                    headerExtension = PyStr.Lower(headerExtension).Replace("jpeg", "jpg");
                    filename += headerExtension;
                }
                else
                {
                    filename = nominalFilename;
                }
                if (filename != nominalFilename && PyOs.IsFile(filename))
                {
                    Context.Log(filename + " exists", end: " ", flush: true);
                    return false;
                }
                Context.WriteRaw(resp, filename);
            }
            finally
            {
                resp.Close();
            }
            PyOs.UTime(filename, DateTime.UtcNow, mtime.UtcDateTime);
            return true;
        }

        /// <summary>Saves metadata JSON file of a structure.</summary>
        public void SaveMetadataJson(string filename, IJsonExportable structure)
        {
            if (CompressJson)
                filename += ".json.xz";
            else
                filename += ".json";
            PyOs.MakeDirs(PyOs.DirName(filename));
            Structures.SaveStructureToFile(structure, filename);
            if (structure is Post || structure is StoryItem)
                // log 'json ' message when saving Post or StoryItem
                Context.Log("json", end: " ", flush: true);
        }

        private static JObject PostCommentAnswerAsDict(long id, DateTime createdAtUtc, string text, Profile owner, long likesCount)
        {
            long createdAt = (long)Math.Floor((createdAtUtc - StructureHelpers.Epoch).TotalSeconds);
            return new JObject
            {
                { "id", id },
                { "created_at", createdAt },
                { "text", text },
                { "owner", owner.AsDict() },
                { "likes_count", likesCount }
            };
        }

        private static JObject PostCommentAsDict(PostComment comment)
        {
            JObject result = PostCommentAnswerAsDict(comment.Id, comment.CreatedAtUtc, comment.Text, comment.Owner, comment.LikesCount);
            var answers = comment.Answers
                                 .Select(a => PostCommentAnswerAsDict(a.Id, a.CreatedAtUtc, a.Text, a.Owner, a.LikesCount))
                                 .ToList();
            // sorted(..., key=lambda t: int(t['id']), reverse=True) is stable
            answers = answers.Select((a, i) => new KeyValuePair<int, JObject>(i, a))
                             .OrderByDescending(kv => kv.Value["id"].ToBigInteger())
                             .ThenBy(kv => kv.Key)
                             .Select(kv => kv.Value)
                             .ToList();
            result["answers"] = new JArray(answers);
            return result;
        }

        private static List<JObject> GetUniqueComments(List<JObject> comments, bool combineAnswers = false)
        {
            if (comments == null || comments.Count == 0)
                return new List<JObject>();
            // sorted(sorted(list(comments), key=id), key=created_at, reverse=True), both stable
            List<JObject> byId = comments.Select((c, i) => new KeyValuePair<int, JObject>(i, c))
                                         .OrderBy(kv => kv.Value["id"].ToBigInteger())
                                         .ThenBy(kv => kv.Key)
                                         .Select(kv => kv.Value)
                                         .ToList();
            List<JObject> commentsList = byId.Select((c, i) => new KeyValuePair<int, JObject>(i, c))
                                             .OrderByDescending(kv => kv.Value["created_at"].ToBigInteger())
                                             .ThenBy(kv => kv.Key)
                                             .Select(kv => kv.Value)
                                             .ToList();
            var uniqueCommentsList = new List<JObject> { commentsList[0] };
            for (int i = 0; i + 1 < commentsList.Count; i++)
            {
                JObject x = commentsList[i];
                JObject y = commentsList[i + 1];
                if (!JToken.DeepEquals(x["id"], y["id"]))
                {
                    uniqueCommentsList.Add(y);
                }
                else
                {
                    JObject last = uniqueCommentsList[uniqueCommentsList.Count - 1];
                    last["likes_count"] = y.Get("likes_count") ?? JValue.Null;
                    if (combineAnswers)
                    {
                        JToken existing = last.Get("answers");
                        var combinedAnswers = existing != null && existing.Truthy
                            ? existing.AsArray().Select(a => (JObject)a).ToList()
                            : new List<JObject>();
                        if (y.ContainsKey("answers"))
                            combinedAnswers.AddRange(y["answers"].AsArray().Select(a => (JObject)a));
                        last["answers"] = new JArray(GetUniqueComments(combinedAnswers));
                    }
                }
            }
            return uniqueCommentsList;
        }

        /// <summary>Updates the comments JSON file of a post.</summary>
        public void UpdateComments(string filename, Post post)
        {
            string baseFilename = filename;
            filename += "_comments.json";
            List<JObject> comments;
            try
            {
                JToken loaded = PyJson.Loads(PyOs.ReadText(filename, PyOs.LocaleEncoding));
                comments = loaded.AsIterable().Select(c => (JObject)c).ToList();
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is JsonDecodeError)
            {
                comments = new List<JObject>();
            }

            Action saveComments = () =>
            {
                List<JObject> uniqueComments = GetUniqueComments(comments, combineAnswers: true);
                var answerIds = new HashSet<System.Numerics.BigInteger>();
                foreach (JObject comment in uniqueComments)
                    foreach (JToken answer in comment.Get("answers", new JArray()).AsIterable())
                        answerIds.Add(answer["id"].ToBigInteger());
                var filtered = new JArray(uniqueComments.Where(t => !answerIds.Contains(t["id"].ToBigInteger())));
                PyOs.WriteText(filename, PyJson.Dumps(filtered, 4), PyOs.LocaleEncoding);
            };

            IEnumerable<PostComment> commentsIterator = post.GetComments();
            try
            {
                NodeIteration.ResumableIteration(
                    context: Context,
                    iterator: commentsIterator,
                    load: (ctx, path) => Structures.LoadStructureFromFile(ctx, path),
                    save: (fni, path) => Structures.SaveStructureToFile(fni, path),
                    formatPath: magic => string.Format("{0}_{1}_{2}.json.xz", baseFilename, ResumePrefix, magic),
                    checkBbd: CheckResumeBbd,
                    enabled: ResumePrefix != null,
                    body: (isResuming, startIndex) =>
                    {
                        long idx = startIndex;
                        foreach (PostComment comment in commentsIterator)
                        {
                            idx++;
                            if (idx % 250 == 0)
                                Context.Log(idx.ToString(CultureInfo.InvariantCulture), end: Chars.Ellipsis, flush: true);
                            comments.Add(PostCommentAsDict(comment));
                        }
                    });
            }
            catch (Exception ex) when (ex is KeyboardInterruptException || ex is AbortDownloadException)
            {
                if (comments.Count > 0)
                    saveComments();
                throw;
            }
            if (comments.Count > 0)
            {
                saveComments();
                Context.Log("comments", end: " ", flush: true);
            }
        }

        private static string ElliptifyCaption(string caption)
        {
            string pcaption = PyStr.Strip(caption.Replace("\n", " "));
            return "[" + (PyStr.Len(pcaption) > 31 ? PyStr.Slice(pcaption, null, 29) + Chars.Ellipsis : pcaption) + "]";
        }

        /// <summary>Updates picture caption / Post metadata info</summary>
        public void SaveCaption(string filename, DateTimeOffset mtime, string caption)
        {
            filename += ".txt";
            caption += "\n";
            string pcaption = ElliptifyCaption(caption);
            byte[] bcaption = Encoding.UTF8.GetBytes(caption);
            if (File.Exists(filename))
            {
                byte[] fileCaption = File.ReadAllBytes(filename);
                if (ReplaceCrLf(fileCaption).SequenceEqual(ReplaceCrLf(bcaption)))
                {
                    Context.Log(pcaption + " unchanged", end: " ", flush: true);
                    return;
                }
                Func<int, string> getFilename = index =>
                {
                    if (index == 0)
                        return filename;
                    string root, ext;
                    PyOs.SplitExt(filename, out root, out ext);
                    return string.Format(CultureInfo.InvariantCulture, "{0}_old_{1:00}{2}", root, index, ext);
                };
                int i = 0;
                while (PyOs.IsFile(getFilename(i)))
                    i++;
                for (int index = i; index > 0; index--)
                    PyOs.Rename(getFilename(index - 1), getFilename(index));
                string oldText;
                try
                {
                    oldText = new UTF8Encoding(false, true).GetString(fileCaption);
                }
                catch (DecoderFallbackException)
                {
                    oldText = null;
                }
                if (oldText != null)
                    Context.Log(ElliptifyCaption(oldText) + " updated", end: " ", flush: true);
                else
                    Context.Log("txt updated", end: " ", flush: true);
            }
            Context.Log(pcaption, end: " ", flush: true);
            PyOs.WriteText(filename, caption, new UTF8Encoding(false));
            PyOs.UTime(filename, DateTime.UtcNow, mtime.UtcDateTime);
        }

        private static byte[] ReplaceCrLf(byte[] data)
        {
            var result = new List<byte>(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == '\r' && i + 1 < data.Length && data[i + 1] == '\n')
                    continue;
                result.Add(data[i]);
            }
            return result.ToArray();
        }

        /// <summary>Save post location name and Google Maps link.</summary>
        public void SaveLocation(string filename, PostLocation location, DateTimeOffset mtime)
        {
            filename += "_location.txt";
            string locationString;
            if (!location.LatToken.IsNull && !location.LngToken.IsNull)
            {
                string lat = PyStr.Str(location.LatToken);
                string lng = PyStr.Str(location.LngToken);
                locationString = location.Name + "\n" +
                                 string.Format("https://maps.google.com/maps?q={0},{1}&ll={0},{1}\n", lat, lng);
            }
            else
            {
                locationString = location.Name;
            }
            File.WriteAllBytes(filename, Encoding.UTF8.GetBytes(locationString));
            PyOs.UTime(filename, DateTime.UtcNow, mtime.UtcDateTime);
            Context.Log("geo", end: " ", flush: true);
        }

        /// <summary>Returns a filename within the target path.</summary>
        public string FormatFilenameWithinTargetPath(object target, Profile ownerProfile, string identifier,
                                                     string nameSuffix, string extension)
        {
            if (FormatStringContainsKey(DirnamePattern, "profile") || FormatStringContainsKey(DirnamePattern, "target"))
            {
                object profileStr = ownerProfile != null ? (object)PyStr.Lower(ownerProfile.Username) : target;
                return PyOs.Join(PlainFormat(DirnamePattern, "profile", profileStr, "target", target),
                                 string.Format("{0}_{1}.{2}", identifier, nameSuffix, extension));
            }
            return PyOs.Join(PlainFormat(DirnamePattern),
                             string.Format("{0}_{1}_{2}.{3}", PyStr.Str(target), identifier, nameSuffix, extension));
        }

        /// <summary>
        /// Downloads and saves a picture that does not have an association with a Post or StoryItem, such as a
        /// Profile picture or a Highlight cover picture. Modification time is taken from the HTTP response headers.
        /// </summary>
        public void DownloadTitlePic(string url, object target, string nameSuffix, Profile ownerProfile)
        {
            RetryOnConnectionError("download_title_pic", new[] { url, target, nameSuffix, ownerProfile }, () =>
            {
                DownloadTitlePicImpl(url, target, nameSuffix, ownerProfile);
                return true;
            });
        }

        private void DownloadTitlePicImpl(string url, object target, string nameSuffix, Profile ownerProfile)
        {
            Response httpResponse = Context.GetRaw(url);
            try
            {
                DateTimeOffset? dateObject = null;
                byte[] picBytes;
                string lastModified = httpResponse.Headers.Get("Last-Modified");
                if (lastModified != null)
                {
                    DateTime parsed;
                    if (!DateTime.TryParseExact(lastModified, "ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture,
                                                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                        throw new PyValueError(string.Format("time data {0} does not match format '%a, %d %b %Y %H:%M:%S GMT'",
                                                             PyStr.Repr(lastModified)));
                    dateObject = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
                    picBytes = null;
                }
                else
                {
                    picBytes = httpResponse.Content;
                }
                string igFilename = url.Split('/').Last().Split('?')[0];
                var picData = new TitlePic(ownerProfile, target, nameSuffix, igFilename, dateObject);
                string dirname = new PostPathFormatter(picData, SanitizePaths).Format(DirnamePattern, target, true);
                string filenameTemplate = PyOs.Join(dirname, new PostPathFormatter(picData, SanitizePaths).Format(TitlePattern, target, true));
                string filename = PrepareFilename(filenameTemplate, () => url) + ".jpg";
                string contentLength = httpResponse.Headers.Get("Content-Length");
                if (PyOs.IsFile(filename) && (!Context.IsLoggedIn ||
                                              (contentLength != null && new FileInfo(filename).Length >= PyInt.ParseLong(contentLength))))
                {
                    Context.Log(filename + " already exists");
                    return;
                }
                PyOs.MakeDirs(PyOs.DirName(filename));
                if (picBytes != null && picBytes.Length > 0)
                    Context.WriteRaw(picBytes, filename);
                else
                    Context.WriteRaw(httpResponse, filename);
                if (dateObject.HasValue)
                    PyOs.UTime(filename, DateTime.UtcNow, dateObject.Value.UtcDateTime);
                Context.Log(""); // log output of WriteRaw() does not produce \n
            }
            finally
            {
                httpResponse.Close();
            }
        }

        /// <summary>Downloads and saves profile pic if it has not been downloaded before.</summary>
        /// <param name="profile">The profile.</param>
        /// <param name="latestStamps">Database with the last downloaded data. If not present, the profile pic is
        /// downloaded unless it already exists</param>
        public void DownloadProfilepicIfNew(Profile profile, LatestStamps latestStamps)
        {
            if (latestStamps == null)
            {
                DownloadProfilepic(profile);
                return;
            }
            string profilePicBasename = profile.ProfilePicUrlNoIphone.Split('/').Last().Split('?')[0];
            string savedBasename = latestStamps.GetProfilePic(profile.Username);
            if (savedBasename == profilePicBasename)
                return;
            DownloadProfilepic(profile);
            latestStamps.SetProfilePic(profile.Username, profilePicBasename);
        }

        /// <summary>Downloads and saves profile pic.</summary>
        public void DownloadProfilepic(Profile profile)
        {
            DownloadTitlePic(profile.ProfilePicUrl, PyStr.Lower(profile.Username), "profile_pic", profile);
        }

        /// <summary>Downloads and saves Highlight cover picture.</summary>
        public void DownloadHighlightCover(Highlight highlight, object target)
        {
            DownloadTitlePic(highlight.CoverUrl, target, "cover", highlight.OwnerProfile);
        }

        /// <summary>Downloads and saves the profile picture of a Hashtag.</summary>
        public void DownloadHashtagProfilepic(Hashtag hashtag)
        {
            DownloadTitlePic(hashtag.ProfilePicUrl, "#" + hashtag.Name, "profile_pic", null);
        }

        /// <summary>Saves internally stored session object to a dict.</summary>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public List<KeyValuePair<string, string>> SaveSession()
        {
            RequiresLogin();
            return Context.SaveSession();
        }

        /// <summary>Internally stores session object from a dict.</summary>
        public void LoadSession(string username, IEnumerable<KeyValuePair<string, string>> sessionData)
        {
            Context.LoadSession(username, sessionData);
        }

        /// <summary>Saves internally stored session object.</summary>
        /// <param name="filename">Filename, or null to use default filename.</param>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public void SaveSessionToFile(string filename = null)
        {
            RequiresLogin();
            if (filename == null)
                filename = GetDefaultSessionFilename(Context.Username);
            string dirname = PyOs.DirName(filename);
            if (dirname != "" && !PyOs.Exists(dirname))
                PyOs.MakeDirs(dirname);
            using (var sessionfile = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                Context.SaveSessionToFile(sessionfile);
                Context.Log(string.Format("Saved session to {0}.", filename));
            }
        }

        /// <summary>
        /// Internally stores session object loaded from file.
        ///
        /// If filename is null, the file with the default session path is loaded.
        /// </summary>
        /// <exception cref="FileNotFoundException">If the file does not exist.</exception>
        public void LoadSessionFromFile(string username, string filename = null)
        {
            if (filename == null)
            {
                filename = GetDefaultSessionFilename(username);
                if (!PyOs.Exists(filename))
                    filename = GetLegacySessionFilename(username);
            }
            if (!File.Exists(filename))
                throw new FileNotFoundException(string.Format("[Errno 2] No such file or directory: {0}", PyStr.Repr(filename)), filename);
            using (var sessionfile = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                Context.LoadSessionFromFile(username, sessionfile);
                Context.Log(string.Format("Loaded session from {0}.", filename));
            }
        }

        /// <summary>Returns the Instagram username to which given session object belongs, or null.</summary>
        public string TestLogin()
        {
            return Context.TestLogin();
        }

        /// <summary>Log in to instagram with given username and password and internally store session object.</summary>
        /// <exception cref="BadCredentialsException">If the provided password is wrong.</exception>
        /// <exception cref="TwoFactorAuthRequiredException">First step of 2FA login done, now call
        /// <see cref="TwoFactorLogin"/>.</exception>
        /// <exception cref="LoginException">An error happened during login (for example, an invalid response was
        /// received). Or if the provided username does not exist.</exception>
        public void Login(string user, string passwd)
        {
            Context.Login(user, passwd);
        }

        /// <summary>Second step of login if 2FA is enabled.</summary>
        /// <exception cref="InvalidArgumentException">No two-factor authentication pending.</exception>
        /// <exception cref="BadCredentialsException">2FA verification code invalid.</exception>
        public void TwoFactorLogin(string twoFactorCode)
        {
            Context.TwoFactorLogin(twoFactorCode);
        }

        /// <summary>
        /// Replace filename token inside filenameTemplate with url's filename and assure the directories exist.
        /// </summary>
        private static string PrepareFilename(string filenameTemplate, Func<string> url)
        {
            string filename;
            if (filenameTemplate.Contains("{filename}"))
            {
                string urlPath = ParsedUrlPath(url());
                string root, ext;
                PyOs.SplitExt(PyOs.BaseName(urlPath), out root, out ext);
                filename = filenameTemplate.Replace("{filename}", root);
            }
            else
            {
                filename = filenameTemplate;
            }
            PyOs.MakeDirs(PyOs.DirName(filename));
            return filename;
        }

        /// <summary>urllib.parse.urlparse(url).path</summary>
        private static string ParsedUrlPath(string url)
        {
            string rest = url;
            int scheme = rest.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                rest = rest.Substring(scheme + 3);
                int slash = rest.IndexOf('/');
                rest = slash >= 0 ? rest.Substring(slash) : "";
            }
            int cut = rest.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0)
                rest = rest.Substring(0, cut);
            int semicolon = rest.LastIndexOf(';');
            if (semicolon >= 0 && semicolon > rest.LastIndexOf('/'))
                rest = rest.Substring(0, semicolon);
            return rest;
        }

        /// <summary>Format filename of a Post or StoryItem according to filename-pattern parameter.</summary>
        public string FormatFilename(object item, object target = null)
        {
            return new PostPathFormatter(item, SanitizePaths).Format(FilenamePattern, target, true);
        }

        private bool AlreadyDownloaded(string path)
        {
            if (!PyOs.IsFile(path))
                return false;
            Context.Log(path + " exists", end: " ", flush: true);
            return true;
        }

        private static int PyMod(int a, int n)
        {
            return ((a % n) + n) % n;
        }

        /// <summary>
        /// Download everything associated with one instagram post node, i.e. picture, caption and video.
        /// </summary>
        /// <param name="post">Post to download.</param>
        /// <param name="target">Target name, i.e. profile name, #hashtag, :feed; for filename.</param>
        /// <returns>True if something was downloaded, False otherwise, i.e. file was already there</returns>
        public bool DownloadPost(Post post, object target)
        {
            string dirname = new PostPathFormatter(post, SanitizePaths).Format(DirnamePattern, target, true);
            string filenameTemplate = PyOs.Join(dirname, FormatFilename(post, target));
            string filename = PrepareFilename(filenameTemplate, () => post.Url);

            // Download the image(s) / video thumbnail and videos within sidecars if desired
            bool downloaded = true;
            string typename = post.Typename;
            if (typename == "GraphSidecar")
            {
                if ((DownloadPictures || DownloadVideos) && post.Mediacount > 0)
                {
                    int mediacount = post.Mediacount;
                    int first = PyMod(SlideStart, mediacount);
                    int last = PyMod(SlideEnd, mediacount);
                    if (!AllAlreadyDownloaded(filenameTemplate, post, first, last))
                    {
                        int edgeNumber = first + 1;
                        foreach (PostSidecarNode sidecarNode in post.GetSidecarNodes(SlideStart, SlideEnd))
                        {
                            string suffix = edgeNumber.ToString(CultureInfo.InvariantCulture);
                            if (FilenamePattern.Contains("{filename}"))
                                suffix = null;
                            string videoUrl = sidecarNode.VideoUrl;
                            if (DownloadPictures && (videoUrl == null || DownloadVideoThumbnails))
                            {
                                PostSidecarNode node = sidecarNode;
                                string sidecarFilename = PrepareFilename(filenameTemplate, () => node.DisplayUrl);
                                // Download sidecar picture or video thumbnail (--no-pictures implies --no-video-thumbnails)
                                downloaded &= DownloadPic(sidecarFilename, sidecarNode.DisplayUrl, post.DateLocal, suffix);
                            }
                            if (videoUrl != null && DownloadVideos)
                            {
                                string sidecarFilename = PrepareFilename(filenameTemplate, () => videoUrl);
                                // Download sidecar video if desired
                                downloaded &= DownloadPic(sidecarFilename, videoUrl, post.DateLocal, suffix);
                            }
                            edgeNumber++;
                        }
                    }
                    else
                    {
                        downloaded = false;
                    }
                }
            }
            else if (typename == "GraphImage")
            {
                // Download picture
                if (DownloadPictures)
                    downloaded = !AlreadyDownloaded(filename + ".jpg") && DownloadPic(filename, post.Url, post.DateLocal);
            }
            else if (typename == "GraphVideo")
            {
                // Download video thumbnail (--no-pictures implies --no-video-thumbnails)
                if (DownloadPictures && DownloadVideoThumbnails)
                {
                    Context.ErrorCatcher(string.Format("Video thumbnail of {0}", post), () =>
                    {
                        downloaded = !AlreadyDownloaded(filename + ".jpg") && DownloadPic(filename, post.Url, post.DateLocal);
                    });
                }
            }
            else
            {
                Context.Error(string.Format("Warning: {0} has unknown typename: {1}", post, typename));
            }

            // Save caption if desired
            string metadataString = PyStr.Strip(new ArbitraryItemFormatter(post).Format(PostMetadataTxtPattern));
            if (metadataString.Length > 0)
                SaveCaption(filename, post.DateLocal, metadataString);

            // Download video if desired
            if (post.IsVideo && DownloadVideos)
                downloaded &= !AlreadyDownloaded(filename + ".mp4") && DownloadPic(filename, post.VideoUrl, post.DateLocal);

            // Download geotags if desired
            if (DownloadGeotags)
            {
                PostLocation location = post.Location;
                if (location != null)
                    SaveLocation(filename, location, post.DateLocal);
            }

            // Update comments if desired
            if (DownloadComments)
                UpdateComments(filename, post);

            // Save metadata as JSON if desired.
            if (SaveMetadata)
                SaveMetadataJson(filename, post);

            Context.Log();
            return downloaded;
        }

        private bool AllAlreadyDownloaded(string pathBase, Post post, int first, int last)
        {
            if (FilenamePattern.Contains("{filename}"))
                // full URL needed to evaluate actual filename, cannot determine at
                // this point if all sidecar nodes were already downloaded.
                return false;
            int idx = first + 1;
            for (int i = first; i <= last; i++)
            {
                bool isVideo = post.GetIsVideos()[i];
                if (DownloadPictures && (!isVideo || DownloadVideoThumbnails))
                {
                    if (!AlreadyDownloaded(string.Format("{0}_{1}.jpg", pathBase, idx)))
                        return false;
                }
                if (isVideo && DownloadVideos)
                {
                    if (!AlreadyDownloaded(string.Format("{0}_{1}.mp4", pathBase, idx)))
                        return false;
                }
                idx++;
            }
            return true;
        }

        /// <summary>
        /// Get available stories from followees or all stories of users whose ID are given.
        /// Does not mark stories as seen.
        /// To use this, one needs to be logged in
        /// </summary>
        /// <param name="userids">List of user IDs to be processed in terms of downloading their stories, or null.</param>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public IEnumerable<Story> GetStories(IList<long> userids = null)
        {
            RequiresLogin();
            return GetStoriesImpl(userids);
        }

        private IEnumerable<Story> GetStoriesImpl(IList<long> userids)
        {
            var ids = new List<JToken>();
            if (userids == null || userids.Count == 0)
            {
                // The story tray as the app shows it. Instaloader 4.15.4 uses a GraphQL query for this, which
                // Instagram no longer answers with data.
                JObject tray = Context.GetIphoneJson("api/v1/feed/reels_tray/", new JObject());
                JToken reels = tray.Get("tray");
                if (reels == null || reels.Type != JTokenType.Array)
                    throw new BadResponseException("Bad stories reel JSON.");
                foreach (JToken reel in reels.AsIterable())
                {
                    // Skip placeholder entries and reels that are not a user's story (e.g. live broadcasts)
                    JToken reelType = reel.Get("reel_type");
                    if (reelType != null && reelType.Type == JTokenType.String && (string)reelType != "user_reel")
                        continue;
                    JToken user = reel.Get("user");
                    if (user.IsNone() || user.Get("pk").IsNone())
                        continue;
                    ids.Add(user["pk"].ToInt64());
                }
            }
            else
            {
                foreach (long id in userids)
                    ids.Add(id);
            }

            const int useridsPerQuery = 50;
            for (int i = 0; i < ids.Count; i += useridsPerQuery)
            {
                List<JToken> chunkIds = ids.Skip(i).Take(useridsPerQuery).ToList();
                List<Story> iphoneStories = GetIphoneStories(chunkIds);
                if (iphoneStories != null)
                {
                    foreach (Story story in iphoneStories)
                        yield return story;
                    continue;
                }
                var chunk = new JArray(chunkIds);
                JToken stories = Context.GraphqlQuery("303a4ae99711322310f25250d988f3b7",
                                                      new JObject { { "reel_ids", chunk }, { "precomposed_overlay", false } })["data"];
                foreach (JToken media in stories["reels_media"].AsIterable())
                    yield return new Story(Context, (JObject)media);
            }
        }

        /// <summary>
        /// The stories of the given users from the app's reels_media, or null if the app does not answer.
        /// Instaloader's GraphQL query is redirected to Instagram's home page for some accounts; it is used when
        /// the app does not answer, and with --no-iphone.
        /// </summary>
        private List<Story> GetIphoneStories(IList<JToken> userids)
        {
            if (!Context.IphoneSupport)
                return null;
            JObject data;
            try
            {
                data = Context.GetIphoneJson("api/v1/feed/reels_media/?" +
                                             string.Join("&", userids.Select(id => "reel_ids=" + PyStr.Str(id))),
                                             new JObject());
            }
            catch (Exception err) when (err is QueryReturnedBadRequestException || err is ConnectionException)
            {
                Context.Error(string.Format("Unable to fetch stories from the app: {0}", err.Message));
                return null;
            }
            JToken reels = data.Get("reels").Or(new JObject());
            var stories = new List<Story>();
            foreach (JToken id in userids)
            {
                JToken reel = reels.Get(PyStr.Str(id));
                // Users without a current story are left out, as in the GraphQL query.
                if (reel is JObject && reel.Get("items").Or(new JArray()).Truthy && reel.Get("user") is JObject)
                    stories.Add(Story.FromIphoneReel(Context, (JObject)reel));
            }
            return stories;
        }

        /// <summary>
        /// Download available stories from user followees or all stories of users whose ID are given.
        /// Does not mark stories as seen.
        /// To use this, one needs to be logged in
        /// </summary>
        /// <param name="userids">List of user IDs (long) or Profiles to be processed in terms of downloading their stories</param>
        /// <param name="fastUpdate">If true, abort when first already-downloaded picture is encountered</param>
        /// <param name="filenameTarget">Replacement for {target} in dirname_pattern and filename_pattern
        /// or null if profile name should be used instead</param>
        /// <param name="storyitemFilter">function(storyitem), which returns True if given StoryItem should be downloaded</param>
        /// <param name="latestStamps">Database with the last times each user was scraped</param>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public void DownloadStories(IEnumerable<object> userids = null,
                                    bool fastUpdate = false,
                                    string filenameTarget = ":stories",
                                    Func<StoryItem, bool> storyitemFilter = null,
                                    LatestStamps latestStamps = null)
        {
            RequiresLogin();
            List<long> ids = null;
            int? profileCount;
            List<object> useridList = userids == null ? null : userids.ToList();
            if (useridList == null || useridList.Count == 0)
            {
                Context.Log("Retrieving all visible stories...");
                profileCount = null;
            }
            else
            {
                ids = useridList.Select(p => p is Profile ? ((Profile)p).Userid : Convert.ToInt64(p, CultureInfo.InvariantCulture)).ToList();
                profileCount = ids.Count;
            }

            int i = 0;
            foreach (Story userStory in GetStories(ids))
            {
                i++;
                string name = userStory.OwnerUsername;
                string msg;
                if (profileCount.HasValue)
                {
                    int w = profileCount.Value.ToString(CultureInfo.InvariantCulture).Length;
                    msg = string.Format("[{0}/{1}] Retrieving stories from profile {2}.",
                                        i.ToString(CultureInfo.InvariantCulture).PadLeft(w),
                                        profileCount.Value.ToString(CultureInfo.InvariantCulture).PadLeft(w), name);
                }
                else
                {
                    msg = string.Format("[{0}] Retrieving stories from profile {1}.", i.ToString(CultureInfo.InvariantCulture).PadLeft(3), name);
                }
                Context.Log(msg);
                int totalcount = userStory.Itemcount;
                int count = 1;
                DateTimeOffset lastScraped = default(DateTimeOffset);
                DateTimeOffset scrapedTimestamp = default(DateTimeOffset);
                if (latestStamps != null)
                {
                    lastScraped = latestStamps.GetLastStoryTimestamp(name);
                    scrapedTimestamp = DateTimeOffset.Now;
                }
                foreach (StoryItem item in userStory.GetItems())
                {
                    if (latestStamps != null)
                    {
                        if (item.DateLocal <= lastScraped)
                            break;
                    }
                    if (storyitemFilter != null && !storyitemFilter(item))
                    {
                        Context.Log(string.Format("<{0} skipped>", item), flush: true);
                        continue;
                    }
                    Context.Log(string.Format("[{0}/{1}] ", count.ToString(CultureInfo.InvariantCulture).PadLeft(3),
                                              totalcount.ToString(CultureInfo.InvariantCulture).PadLeft(3)), end: "", flush: true);
                    count++;
                    bool stop = false;
                    StoryItem storyItem = item;
                    Context.ErrorCatcher(string.Format("Download story from user {0}", name), () =>
                    {
                        bool downloaded = DownloadStoryitem(storyItem, !string.IsNullOrEmpty(filenameTarget) ? filenameTarget : name);
                        if (fastUpdate && !downloaded)
                            stop = true;
                    });
                    if (stop)
                        break;
                }
                if (latestStamps != null)
                    latestStamps.SetLastStoryTimestamp(name, scrapedTimestamp);
            }
        }

        /// <summary>Download one user story.</summary>
        /// <param name="item">Story item, as in story['items'] for story in GetStories()</param>
        /// <param name="target">Replacement for {target} in dirname_pattern and filename_pattern</param>
        /// <returns>True if something was downloaded, False otherwise, i.e. file was already there</returns>
        public bool DownloadStoryitem(StoryItem item, object target)
        {
            DateTimeOffset dateLocal = item.DateLocal;
            string dirname = new PostPathFormatter(item, SanitizePaths).Format(DirnamePattern, target, true);
            string filenameTemplate = PyOs.Join(dirname, FormatFilename(item, target));
            string filename = PrepareFilename(filenameTemplate, () => item.Url);
            bool downloaded = false;
            bool videoUrlFetchFailed = false;
            if (item.IsVideo && DownloadVideos)
            {
                string videoUrl = item.VideoUrl;
                if (!string.IsNullOrEmpty(videoUrl))
                {
                    filename = PrepareFilename(filenameTemplate, () => videoUrl);
                    downloaded |= !AlreadyDownloaded(filename + ".mp4") && DownloadPic(filename, videoUrl, dateLocal);
                }
                else
                {
                    videoUrlFetchFailed = true;
                }
            }
            if (videoUrlFetchFailed || !item.IsVideo || DownloadVideoThumbnails)
                downloaded = !AlreadyDownloaded(filename + ".jpg") && DownloadPic(filename, item.Url, dateLocal);
            // Save caption if desired
            string metadataString = PyStr.Strip(new ArbitraryItemFormatter(item).Format(StoryitemMetadataTxtPattern));
            if (metadataString.Length > 0)
                SaveCaption(filename, item.DateLocal, metadataString);
            // Save metadata as JSON if desired.
            if (SaveMetadata)
                SaveMetadataJson(filename, item);
            Context.Log();
            return downloaded;
        }

        /// <summary>Get all highlights from a user. To use this, one needs to be logged in.</summary>
        /// <param name="user">Profile of the user whose highlights should get fetched.</param>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public IEnumerable<Highlight> GetHighlights(Profile user)
        {
            RequiresLogin();
            return GetHighlightsImpl(user.Userid, user);
        }

        /// <summary>Get all highlights from a user. To use this, one needs to be logged in.</summary>
        /// <param name="userid">ID of the user whose highlights should get fetched.</param>
        public IEnumerable<Highlight> GetHighlights(long userid)
        {
            RequiresLogin();
            return GetHighlightsImpl(userid, null);
        }

        private IEnumerable<Highlight> GetHighlightsImpl(long userid, Profile user)
        {
            IEnumerable<Highlight> highlights = GetIphoneHighlights(userid, user) ?? GetGraphqlHighlights(userid, user);
            foreach (Highlight highlight in highlights)
                yield return highlight;
        }

        /// <summary>
        /// The highlights of a user from the app's highlights tray, or null if the app does not answer.
        /// Instaloader's GraphQL query is redirected to Instagram's home page for some accounts; it is used when
        /// the app does not answer, and with --no-iphone.
        /// </summary>
        private List<Highlight> GetIphoneHighlights(long userid, Profile user)
        {
            if (Context.IphoneSupport)
            {
                JObject tray = null;
                try
                {
                    tray = Context.GetIphoneJson(string.Format(CultureInfo.InvariantCulture,
                                                               "api/v1/highlights/{0}/highlights_tray/", userid), new JObject());
                }
                catch (Exception err) when (err is QueryReturnedBadRequestException || err is ConnectionException)
                {
                    Context.Error(string.Format("Unable to fetch highlights from the app: {0}", err.Message));
                }
                JToken trayItems = tray == null ? null : tray.Get("tray");
                if (trayItems != null && trayItems.Type == JTokenType.Array)
                {
                    var highlights = new List<Highlight>();
                    foreach (JToken item in trayItems.AsIterable())
                    {
                        if (item is JObject && !item.Get("id").IsNone())
                            highlights.Add(Highlight.FromIphoneTrayItem(Context, (JObject)item, user));
                    }
                    return highlights;
                }
            }
            return null;
        }

        private IEnumerable<Highlight> GetGraphqlHighlights(long userid, Profile user)
        {
            JToken data = Context.GraphqlQuery("7c16654f22c819fb63d1183034a5162f",
                                               new JObject
                                               {
                                                   { "user_id", userid },
                                                   { "include_chaining", false },
                                                   { "include_reel", false },
                                                   { "include_suggested_users", false },
                                                   { "include_logged_out_extras", false },
                                                   { "include_highlight_reels", true }
                                               })["data"]["user"]["edge_highlight_reels"];
            if (data.IsNull)
                throw new BadResponseException("Bad highlights reel JSON.");
            foreach (JToken edge in data["edges"].AsIterable())
                yield return new Highlight(Context, (JObject)edge["node"], user);
        }

        /// <summary>
        /// Download available highlights from a user whose ID is given.
        /// To use this, one needs to be logged in.
        /// </summary>
        /// <param name="user">Profile of the user whose highlights should get downloaded.</param>
        /// <param name="fastUpdate">If true, abort when first already-downloaded picture is encountered</param>
        /// <param name="filenameTarget">Replacement for {target} in dirname_pattern and filename_pattern
        /// or null if profile name and the highlights' titles should be used instead</param>
        /// <param name="storyitemFilter">function(storyitem), which returns True if given StoryItem should be downloaded</param>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public void DownloadHighlights(Profile user, bool fastUpdate = false, string filenameTarget = null,
                                       Func<StoryItem, bool> storyitemFilter = null)
        {
            DownloadHighlightsImpl(GetHighlights(user), fastUpdate, filenameTarget, storyitemFilter);
        }

        /// <summary>Download available highlights from a user whose ID is given.</summary>
        public void DownloadHighlights(long userid, bool fastUpdate = false, string filenameTarget = null,
                                       Func<StoryItem, bool> storyitemFilter = null)
        {
            DownloadHighlightsImpl(GetHighlights(userid), fastUpdate, filenameTarget, storyitemFilter);
        }

        private void DownloadHighlightsImpl(IEnumerable<Highlight> highlights, bool fastUpdate, string filenameTarget,
                                            Func<StoryItem, bool> storyitemFilter)
        {
            RequiresLogin();
            List<Highlight> userHighlights = highlights.ToList();
            int width = userHighlights.Count.ToString(CultureInfo.InvariantCulture).Length;
            int highlightNumber = 0;
            foreach (Highlight userHighlight in userHighlights)
            {
                highlightNumber++;
                string name = userHighlight.OwnerUsername;
                object highlightTarget = !string.IsNullOrEmpty(filenameTarget)
                    ? (object)filenameTarget
                    : new TargetPath(PostPathFormatter.SanitizePath(name, SanitizePaths),
                                     PostPathFormatter.SanitizePath(userHighlight.Title, SanitizePaths));
                Context.Log(string.Format("[{0}/{1}] Retrieving highlights \"{2}\" from profile {3}",
                                          highlightNumber.ToString(CultureInfo.InvariantCulture).PadLeft(width),
                                          userHighlights.Count.ToString(CultureInfo.InvariantCulture).PadLeft(width),
                                          userHighlight.Title, name));
                DownloadHighlightCover(userHighlight, highlightTarget);
                // Retrieving one highlight's items can fail on its own; continue with the other highlights then.
                // (Instaloader stops downloading the profile's highlights.)
                int totalcount = 0;
                List<StoryItem> highlightItems = null;
                Highlight currentHighlight = userHighlight;
                Context.ErrorCatcher(string.Format("Retrieve highlights \"{0}\" from user {1}", userHighlight.Title, name), () =>
                {
                    totalcount = currentHighlight.Itemcount;
                    highlightItems = currentHighlight.GetItems().ToList();
                });
                if (highlightItems == null)
                    continue;
                int count = 1;
                foreach (StoryItem item in highlightItems)
                {
                    if (storyitemFilter != null && !storyitemFilter(item))
                    {
                        Context.Log(string.Format("<{0} skipped>", item), flush: true);
                        continue;
                    }
                    Context.Log(string.Format("[{0}/{1}] ", count.ToString(CultureInfo.InvariantCulture).PadLeft(3),
                                              totalcount.ToString(CultureInfo.InvariantCulture).PadLeft(3)), end: "", flush: true);
                    count++;
                    bool stop = false;
                    StoryItem storyItem = item;
                    Context.ErrorCatcher(string.Format("Download highlights \"{0}\" from user {1}", userHighlight.Title, name), () =>
                    {
                        bool downloaded = DownloadStoryitem(storyItem, highlightTarget);
                        if (fastUpdate && !downloaded)
                            stop = true;
                    });
                    if (stop)
                        break;
                }
            }
        }

        private static string FormatCount(long number, long total)
        {
            int w = total.ToString(CultureInfo.InvariantCulture).Length;
            return string.Format("[{0}/{1}] ", number.ToString(CultureInfo.InvariantCulture).PadLeft(w),
                                 total.ToString(CultureInfo.InvariantCulture).PadLeft(w));
        }

        /// <summary>Download the Posts returned by given Post Iterator.</summary>
        /// <param name="posts">Post Iterator to loop through.</param>
        /// <param name="target">Target name.</param>
        /// <param name="fastUpdate">--fast-update.</param>
        /// <param name="postFilter">--post-filter.</param>
        /// <param name="maxCount">Maximum count of Posts to download (--count).</param>
        /// <param name="totalCount">Total number of posts returned by given iterator.</param>
        /// <param name="ownerProfile">Associated profile, if any.</param>
        /// <param name="takewhile">Expression evaluated for each post. Once it returns false, downloading stops.</param>
        /// <param name="possiblyPinned">Number of posts that might be pinned. These posts do not cause download
        /// to stop even if they've already been downloaded.</param>
        public void PostsDownloadLoop(IEnumerable<Post> posts,
                                      object target,
                                      bool fastUpdate = false,
                                      Func<Post, bool> postFilter = null,
                                      long? maxCount = null,
                                      long? totalCount = null,
                                      Profile ownerProfile = null,
                                      Func<Post, bool> takewhile = null,
                                      int possiblyPinned = 0)
        {
            long? displayedCount = (totalCount == null || maxCount != null && maxCount < totalCount) ? maxCount : totalCount;
            object sanitizedTarget = target;
            var targetString = target as string;
            if (targetString != null)
                sanitizedTarget = PostPathFormatter.SanitizePath(targetString, SanitizePaths);
            if (takewhile == null)
                takewhile = _ => true;
            NodeIteration.ResumableIteration(
                context: Context,
                iterator: posts,
                load: (ctx, path) => Structures.LoadStructureFromFile(ctx, path),
                save: (fni, path) => Structures.SaveStructureToFile(fni, path),
                formatPath: magic => FormatFilenameWithinTargetPath(sanitizedTarget, ownerProfile, ResumePrefix ?? "", magic, "json.xz"),
                checkBbd: CheckResumeBbd,
                enabled: ResumePrefix != null,
                body: (isResuming, startIndex) =>
                {
                    long number = startIndex;
                    foreach (Post post in posts)
                    {
                        number++;
                        bool shouldStop = !takewhile(post);
                        if (shouldStop && number <= possiblyPinned)
                            continue;
                        if ((maxCount != null && number > maxCount) || shouldStop)
                            break;
                        if (displayedCount != null)
                            Context.Log(FormatCount(number, displayedCount.Value), end: "", flush: true);
                        else
                            Context.Log(string.Format("[{0}] ", number.ToString(CultureInfo.InvariantCulture).PadLeft(3)), end: "", flush: true);
                        if (postFilter != null)
                        {
                            try
                            {
                                if (!postFilter(post))
                                {
                                    Context.Log(string.Format("{0} skipped", post));
                                    continue;
                                }
                            }
                            catch (Exception err) when (err is InstaloaderException || err is PyKeyError || err is PyTypeError)
                            {
                                Context.Error(string.Format("{0} skipped. Filter evaluation failed: {1}", post, err.Message));
                                continue;
                            }
                        }
                        bool stop = false;
                        Post currentPost = post;
                        long currentNumber = number;
                        Context.ErrorCatcher(string.Format("Download {0} of {1}", post, PyStr.Str(target)), () =>
                        {
                            // The PostChangedException gets raised if the Post's id/shortcode changed while obtaining
                            // additional metadata. This is most likely the case if a HTTP redirect takes place while
                            // resolving the shortcode URL.
                            // The `post_changed` variable keeps the fast-update functionality alive: A Post which is
                            // obained after a redirect has probably already been downloaded as a previous Post of the
                            // same Profile.
                            // Observed in issue #225: https://github.com/instaloader/instaloader/issues/225
                            bool postChanged = false;
                            bool downloaded;
                            while (true)
                            {
                                try
                                {
                                    downloaded = DownloadPost(currentPost, target);
                                    break;
                                }
                                catch (PostChangedException)
                                {
                                    postChanged = true;
                                }
                            }
                            if (fastUpdate && !downloaded && !postChanged && currentNumber > possiblyPinned)
                            {
                                // disengage fast_update for first post when resuming
                                if (!isResuming || currentNumber > 0)
                                    stop = true;
                            }
                        });
                        if (stop)
                            break;
                    }
                });
        }

        /// <summary>Get Posts of the user's feed.</summary>
        /// <returns>Iterator over Posts of the user's feed.</returns>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public IEnumerable<Post> GetFeedPosts()
        {
            RequiresLogin();
            return GetFeedPostsImpl();
        }

        private const string FeedConnection = "xdt_api__v1__feed__timeline__connection";
        private const string FeedQueryName = "PolarisFeedTimelineRootV2Query";

        /// <summary>Depth-first search for all values of the given key in a JSON structure.</summary>
        private static IEnumerable<JToken> FindAll(JToken obj, string key)
        {
            var dict = obj as JObject;
            if (dict != null)
            {
                JToken value = dict.Get(key);
                if (value != null)
                    yield return value;
                foreach (JToken child in dict.Values)
                    foreach (JToken found in FindAll(child, key))
                        yield return found;
            }
            var list = obj as JArray;
            if (list != null)
                foreach (JToken child in list)
                    foreach (JToken found in FindAll(child, key))
                        yield return found;
        }

        /// <summary>All JSON objects in a structure for which the predicate holds.</summary>
        private static IEnumerable<JObject> FindObjects(JToken obj, Func<JObject, bool> predicate)
        {
            var dict = obj as JObject;
            if (dict != null)
            {
                if (predicate(dict))
                    yield return dict;
                foreach (JToken child in dict.Values)
                    foreach (JObject found in FindObjects(child, predicate))
                        yield return found;
            }
            var list = obj as JArray;
            if (list != null)
                foreach (JToken child in list)
                    foreach (JObject found in FindObjects(child, predicate))
                        yield return found;
        }

        /// <summary>
        /// The feed as Instagram's web app loads it: the first page is embedded in the home page, together with the
        /// GraphQL query (doc_id and variables) that the web app uses to load further pages. Posts of followed
        /// accounts are yielded; posts that Instagram suggests from other accounts are skipped. (Instaloader
        /// 4.15.5 pages through the app's api/v1/feed/timeline/ instead, which returns fewer posts per request,
        /// between suggestions; 4.15.4 uses a query that Instagram no longer answers with the feed.)
        /// </summary>
        private IEnumerable<Post> GetFeedPostsImpl()
        {
            List<JToken> scripts = Context.GetPageScripts("", loggedIn: true);
            JObject preloader = null;
            JObject connection = null;
            foreach (JToken script in scripts)
            {
                if (preloader == null)
                    preloader = FindObjects(script, o => JToken.DeepEquals(o.Get("queryName"), FeedQueryName) &&
                                                         !o.Get("queryID").IsNone() && o.Get("variables") is JObject)
                                .FirstOrDefault();
                if (connection == null)
                    connection = FindAll(script, FeedConnection).OfType<JObject>().FirstOrDefault();
            }
            if (connection == null)
                throw new BadResponseException("Unable to find the feed in Instagram's home page.");
            while (true)
            {
                foreach (JToken edge in connection.Get("edges", new JArray()).AsIterable())
                {
                    // "media" holds posts of followed accounts, "explore_story" suggested posts
                    JObject media = edge.Get("node").Or(new JObject()).Get("media") as JObject;
                    if (media == null || media.Get("code").IsNone() ||
                        StructureHelpers.MediaTypeName(media.Get("media_type"), false) == null)
                        continue;
                    yield return Post.FromIphoneStruct(Context, media);
                }
                JToken pageInfo = connection.Get("page_info").Or(new JObject());
                if (!pageInfo.Get("has_next_page").Or(false).Truthy || pageInfo.Get("end_cursor").IsNone() || preloader == null)
                    break;
                JObject variables = JObject.Merge((JObject)preloader["variables"],
                                                  new JObject { { "after", pageInfo["end_cursor"] }, { "first", 12 } });
                JToken next = Context.DocIdGraphqlQuery((string)preloader["queryID"], variables,
                                                           friendlyName: (string)preloader["queryName"])
                                     .Get("data").Or(new JObject()).Get(FeedConnection);
                if (next.IsNone() || !(next is JObject) || next.Get("edges").Or(new JArray()).Count == 0)
                    break;
                connection = (JObject)next;
            }
        }

        /// <summary>Download pictures from the user's feed.</summary>
        /// <param name="maxCount">Maximum count of pictures to download</param>
        /// <param name="fastUpdate">If true, abort when first already-downloaded picture is encountered</param>
        /// <param name="postFilter">function(post), which returns True if given picture should be downloaded</param>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public void DownloadFeedPosts(long? maxCount = null, bool fastUpdate = false, Func<Post, bool> postFilter = null)
        {
            RequiresLogin();
            Context.Log("Retrieving pictures from your feed...");
            PostsDownloadLoop(GetFeedPosts(), ":feed", fastUpdate, postFilter, maxCount: maxCount);
        }

        /// <summary>Download user's saved pictures.</summary>
        /// <param name="maxCount">Maximum count of pictures to download</param>
        /// <param name="fastUpdate">If true, abort when first already-downloaded picture is encountered</param>
        /// <param name="postFilter">function(post), which returns True if given picture should be downloaded</param>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public void DownloadSavedPosts(long? maxCount = null, bool fastUpdate = false, Func<Post, bool> postFilter = null)
        {
            RequiresLogin();
            Context.Log("Retrieving saved posts...");
            NodeIterator<Post> nodeIterator = Profile.OwnProfile(Context).GetSavedPosts();
            PostsDownloadLoop(nodeIterator, ":saved", fastUpdate, postFilter, maxCount: maxCount, totalCount: nodeIterator.Count);
        }

        /// <summary>Get Posts which are listed by Instagram for a given Location.</summary>
        /// <returns>Iterator over Posts of a location's posts</returns>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public IEnumerable<Post> GetLocationPosts(string location)
        {
            RequiresLogin();
            return GetLocationPostsImpl(location);
        }

        private IEnumerable<Post> GetLocationPostsImpl(string location)
        {
            foreach (Post post in new SectionIterator<Post>(
                Context,
                d => (JObject)d["native_location_data"]["recent"],
                m => Post.FromIphoneStruct(Context, m),
                string.Format("explore/locations/{0}/", location)))
                yield return post;
        }

        /// <summary>Download pictures of one location.</summary>
        /// <param name="location">Location to download, as Instagram numerical ID</param>
        /// <param name="maxCount">Maximum count of pictures to download</param>
        /// <param name="postFilter">function(post), which returns True if given picture should be downloaded</param>
        /// <param name="fastUpdate">If true, abort when first already-downloaded picture is encountered</param>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public void DownloadLocation(string location, long? maxCount = null, Func<Post, bool> postFilter = null,
                                     bool fastUpdate = false)
        {
            RequiresLogin();
            Context.Log(string.Format("Retrieving pictures for location {0}...", location));
            PostsDownloadLoop(GetLocationPosts(location), "%" + location, fastUpdate, postFilter, maxCount: maxCount);
        }

        /// <summary>Get Posts which are worthy of exploring suggested by Instagram.</summary>
        /// <returns>Iterator over Posts of the user's suggested posts.</returns>
        /// <exception cref="LoginRequiredException">If called without being logged in.</exception>
        public NodeIterator<Post> GetExplorePosts()
        {
            RequiresLogin();
            return new NodeIterator<Post>(
                Context,
                "df0dcc250c2b18d9fd27c5581ef33c7c",
                d => (JObject)d["data"]["user"]["edge_web_discover_media"],
                n => new Post(Context, n),
                queryReferer: "https://www.instagram.com/explore/");
        }

        /// <summary>Get Posts associated with a #hashtag.</summary>
        [Obsolete("Use Hashtag.GetPostsResumable().")]
        public IEnumerable<Post> GetHashtagPosts(string hashtag)
        {
            return Hashtag.FromName(Context, hashtag).GetPostsResumable();
        }

        /// <summary>Download pictures of one hashtag.</summary>
        /// <param name="hashtag">Hashtag to download, as instance of Hashtag, or string without leading '#'</param>
        /// <param name="maxCount">Maximum count of pictures to download</param>
        /// <param name="postFilter">function(post), which returns True if given picture should be downloaded</param>
        /// <param name="fastUpdate">If true, abort when first already-downloaded picture is encountered</param>
        /// <param name="profilePic">not --no-profile-pic.</param>
        /// <param name="posts">not --no-posts.</param>
        public void DownloadHashtag(object hashtag, long? maxCount = null, Func<Post, bool> postFilter = null,
                                    bool fastUpdate = false, bool profilePic = true, bool posts = true)
        {
            RequiresLogin();
            var name = hashtag as string;
            if (name != null)
            {
                Context.ErrorCatcher(string.Format("Get hashtag #{0}", name), () =>
                {
                    hashtag = Hashtag.FromName(Context, name);
                });
            }
            var tag = hashtag as Hashtag;
            if (tag == null)
                return;
            string target = "#" + tag.Name;
            if (profilePic)
            {
                Context.ErrorCatcher(string.Format("Download profile picture of {0}", target), () =>
                {
                    DownloadHashtagProfilepic(tag);
                });
            }
            if (posts)
            {
                Context.Log(string.Format("Retrieving pictures with hashtag #{0}...", tag.Name));
                PostsDownloadLoop(tag.GetPostsResumable(), target, fastUpdate, postFilter, maxCount: maxCount);
            }
            if (SaveMetadata)
            {
                string jsonFilename = string.Format("{0}/{1}", PlainFormat(DirnamePattern, "profile", target, "target", target), target);
                SaveMetadataJson(jsonFilename, tag);
            }
        }

        /// <summary>Download all posts where a profile is tagged.</summary>
        public void DownloadTagged(Profile profile, bool fastUpdate = false, object target = null,
                                   Func<Post, bool> postFilter = null, LatestStamps latestStamps = null)
        {
            Context.Log(string.Format("Retrieving tagged posts for profile {0}.", profile.Username));
            Func<Post, bool> postsTakewhile = null;
            if (latestStamps != null)
            {
                DateTimeOffset lastScraped = latestStamps.GetLastTaggedTimestamp(profile.Username);
                postsTakewhile = p => p.DateLocal > lastScraped;
            }
            NodeIterator<Post> taggedPosts = profile.GetTaggedPosts();
            object effectiveTarget = target != null && PyRuntime.Truthy(PyRuntime.ToPy(target))
                ? target
                : new TargetPath(PostPathFormatter.SanitizePath(profile.Username, SanitizePaths),
                                 PostPathFormatter.SanitizePath(":tagged", SanitizePaths));
            PostsDownloadLoop(taggedPosts, effectiveTarget, fastUpdate, postFilter, takewhile: postsTakewhile);
            if (latestStamps != null && taggedPosts.FirstItem != null)
                latestStamps.SetLastTaggedTimestamp(profile.Username, taggedPosts.FirstItem.DateLocal);
        }

        /// <summary>Download reels videos of a profile.</summary>
        public void DownloadReels(Profile profile, bool fastUpdate = false, Func<Post, bool> postFilter = null,
                                  LatestStamps latestStamps = null)
        {
            Context.Log(string.Format("Retrieving reels videos for profile {0}.", profile.Username));
            Func<Post, bool> postsTakewhile = null;
            if (latestStamps != null)
            {
                DateTimeOffset lastScraped = latestStamps.GetLastReelsTimestamp(profile.Username);
                postsTakewhile = p => p.DateLocal > lastScraped;
            }
            NodeIterator<Post> reels = profile.GetReels();
            PostsDownloadLoop(reels, profile.Username, fastUpdate, postFilter, ownerProfile: profile,
                              takewhile: postsTakewhile, possiblyPinned: 3);
            if (latestStamps != null && reels.FirstItem != null)
                latestStamps.SetLastReelsTimestamp(profile.Username, reels.FirstItem.DateLocal);
        }

        /// <summary>
        /// Download IGTV videos of a profile.
        ///
        /// Instagram has retired IGTV; former IGTV videos are regular posts and reels now, which are downloaded
        /// with the profile's posts and with --reels. This method only reports that. (Instaloader 4.15.4 queries
        /// the discontinued IGTV connection and fails with KeyError: 'edges'.)
        /// </summary>
        public void DownloadIgtv(Profile profile, bool fastUpdate = false, Func<Post, bool> postFilter = null,
                                 LatestStamps latestStamps = null)
        {
            Context.Log(string.Format("IGTV has been retired by Instagram; the former IGTV videos of {0} are " +
                                      "downloaded as part of its posts and reels (--reels).", profile.Username));
        }

        private string GetIdFilename(string profileName)
        {
            if (FormatStringContainsKey(DirnamePattern, "profile") || FormatStringContainsKey(DirnamePattern, "target"))
            {
                string lower = PyStr.Lower(profileName);
                return PyOs.Join(PlainFormat(DirnamePattern, "profile", lower, "target", lower), "id");
            }
            return PyOs.Join(PlainFormat(DirnamePattern), string.Format("{0}_id", PyStr.Lower(profileName)));
        }

        /// <summary>Load ID of profile from profile directory.</summary>
        public long? LoadProfileId(string profileName)
        {
            string idFilename = GetIdFilename(profileName);
            try
            {
                return PyInt.ParseLong(Encoding.UTF8.GetString(File.ReadAllBytes(idFilename)));
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException ||
                                       ex is PyValueError || ex is PyOverflowError)
            {
                return null;
            }
        }

        /// <summary>Store ID of profile on profile directory.</summary>
        public void SaveProfileId(Profile profile)
        {
            PyOs.MakeDirs(PlainFormat(DirnamePattern, "profile", profile.Username, "target", profile.Username));
            PyOs.WriteText(GetIdFilename(profile.Username), profile.Userid.ToString(CultureInfo.InvariantCulture) + "\n",
                           PyOs.LocaleEncoding);
            Context.Log(string.Format("Stored ID {0} for profile {1}.", profile.Userid, profile.Username));
        }

        /// <summary>
        /// Consult locally stored ID of profile with given name, check whether ID matches and whether name
        /// has changed and return current name of the profile, and store ID of profile.
        /// </summary>
        /// <param name="profileName">Profile name</param>
        /// <param name="latestStamps">Database of downloaded data. If present, IDs are retrieved from it,
        /// otherwise from the target directory</param>
        /// <returns>Instance of current profile</returns>
        public Profile CheckProfileId(string profileName, LatestStamps latestStamps = null)
        {
            Profile profile = null;
            ProfileNotExistsException profileNameNotExistsErr = null;
            try
            {
                profile = Profile.FromUsername(Context, profileName);
            }
            catch (ProfileNotExistsException err)
            {
                profileNameNotExistsErr = err;
            }
            long? profileId = latestStamps == null ? LoadProfileId(profileName) : latestStamps.GetProfileId(profileName);
            if (profileId != null)
            {
                if (profile == null || profileId.Value != profile.Userid)
                {
                    if (profile != null)
                        Context.Log(string.Format("Profile {0} does not match the stored unique ID {1}.", profileName, profileId));
                    else
                        Context.Log(string.Format("Trying to find profile {0} using its unique ID {1}.", profileName, profileId));
                    Profile profileFromId = Profile.FromId(Context, profileId.Value);
                    string newname = profileFromId.Username;
                    if (profileName == newname)
                    {
                        Context.Error(string.Format("Warning: Profile {0} could not be retrieved by its name, but by its ID.", profileName));
                        return profileFromId;
                    }
                    Context.Error(string.Format("Profile {0} has changed its name to {1}.", profileName, newname));
                    if (latestStamps == null)
                    {
                        if (FormatStringContainsKey(DirnamePattern, "profile") || FormatStringContainsKey(DirnamePattern, "target"))
                        {
                            PyOs.Rename(PlainFormat(DirnamePattern, "profile", PyStr.Lower(profileName), "target", PyStr.Lower(profileName)),
                                        PlainFormat(DirnamePattern, "profile", PyStr.Lower(newname), "target", PyStr.Lower(newname)));
                        }
                        else
                        {
                            PyOs.Rename(string.Format("{0}/{1}_id", PlainFormat(DirnamePattern), PyStr.Lower(profileName)),
                                        string.Format("{0}/{1}_id", PlainFormat(DirnamePattern), PyStr.Lower(newname)));
                        }
                    }
                    else
                    {
                        latestStamps.RenameProfile(profileName, newname);
                    }
                    return profileFromId;
                }
                // profile exists and profile id matches saved id
                return profile;
            }
            if (profile != null)
            {
                if (latestStamps == null)
                    SaveProfileId(profile);
                else
                    latestStamps.SaveProfileId(profile.Username, profile.Userid);
                return profile;
            }
            if (profileNameNotExistsErr != null)
                throw profileNameNotExistsErr;
            throw new ProfileNotExistsException(string.Format("Profile {0} does not exist.", profileName));
        }

        /// <summary>High-level method to download set of profiles.</summary>
        /// <param name="profiles">Set of profiles to download.</param>
        /// <param name="profilePic">not --no-profile-pic.</param>
        /// <param name="posts">not --no-posts.</param>
        /// <param name="tagged">--tagged.</param>
        /// <param name="igtv">--igtv.</param>
        /// <param name="highlights">--highlights.</param>
        /// <param name="stories">--stories.</param>
        /// <param name="fastUpdate">--fast-update.</param>
        /// <param name="postFilter">--post-filter.</param>
        /// <param name="storyitemFilter">--storyitem-filter.</param>
        /// <param name="raiseErrors">Whether LoginRequiredException and PrivateProfileNotFollowedException should be
        /// raised or catched and printed with InstaloaderContext.ErrorCatcher.</param>
        /// <param name="latestStamps">--latest-stamps.</param>
        /// <param name="maxCount">Maximum count of posts to download.</param>
        /// <param name="reels">--reels.</param>
        public void DownloadProfiles(ICollection<Profile> profiles,
                                     bool profilePic = true, bool posts = true,
                                     bool tagged = false,
                                     bool igtv = false,
                                     bool highlights = false,
                                     bool stories = false,
                                     bool fastUpdate = false,
                                     Func<Post, bool> postFilter = null,
                                     Func<StoryItem, bool> storyitemFilter = null,
                                     bool raiseErrors = false,
                                     LatestStamps latestStamps = null,
                                     long? maxCount = null,
                                     bool reels = false)
        {
            Action<string, Action> errorHandler;
            if (raiseErrors)
                errorHandler = (info, action) => action();
            else
                errorHandler = Context.ErrorCatcher;

            int i = 0;
            foreach (Profile profile in profiles)
            {
                i++;
                Context.Log(FormatCount(i, profiles.Count).TrimEnd(' ') + string.Format(" Downloading profile {0}", profile.Username));
                errorHandler(profile.Username, () =>
                {
                    string profileName = profile.Username;

                    // Download profile picture
                    if (profilePic)
                    {
                        Context.ErrorCatcher(string.Format("Download profile picture of {0}", profileName), () =>
                        {
                            DownloadProfilepicIfNew(profile, latestStamps);
                        });
                    }

                    // Save metadata as JSON if desired.
                    if (SaveMetadata)
                    {
                        string jsonFilename = PyOs.Join(PlainFormat(DirnamePattern, "profile", profileName, "target", profileName),
                                                        string.Format("{0}_{1}", profileName, profile.Userid));
                        SaveMetadataJson(jsonFilename, profile);
                    }

                    // Catch some errors
                    if (tagged || igtv || highlights || posts)
                    {
                        if (!Context.IsLoggedIn && profile.IsPrivate)
                            throw new LoginRequiredException("Login required.");
                        if (Context.Username != profile.Username && profile.IsPrivate && !profile.FollowedByViewer)
                            throw new PrivateProfileNotFollowedException("Private but not followed.");
                    }

                    // Download tagged, if requested
                    if (tagged)
                    {
                        Context.ErrorCatcher(string.Format("Download tagged of {0}", profileName), () =>
                        {
                            DownloadTagged(profile, fastUpdate: fastUpdate, postFilter: postFilter, latestStamps: latestStamps);
                        });
                    }

                    // Download reels, if requested
                    if (reels)
                    {
                        Context.ErrorCatcher(string.Format("Download reels of {0}", profileName), () =>
                        {
                            DownloadReels(profile, fastUpdate: fastUpdate, postFilter: postFilter, latestStamps: latestStamps);
                        });
                    }

                    // Download IGTV, if requested
                    if (igtv)
                    {
                        Context.ErrorCatcher(string.Format("Download IGTV of {0}", profileName), () =>
                        {
                            DownloadIgtv(profile, fastUpdate: fastUpdate, postFilter: postFilter, latestStamps: latestStamps);
                        });
                    }

                    // Download highlights, if requested
                    if (highlights)
                    {
                        Context.ErrorCatcher(string.Format("Download highlights of {0}", profileName), () =>
                        {
                            DownloadHighlights(profile, fastUpdate: fastUpdate, storyitemFilter: storyitemFilter);
                        });
                    }

                    // Iterate over pictures and download them
                    if (posts)
                    {
                        Context.Log(string.Format("Retrieving posts from profile {0}.", profileName));
                        Func<Post, bool> postsTakewhile = null;
                        if (latestStamps != null)
                        {
                            DateTimeOffset lastScraped = latestStamps.GetLastPostTimestamp(profileName);
                            postsTakewhile = p => p.DateLocal > lastScraped;
                        }
                        NodeIterator<Post> postsToDownload = profile.GetPosts();
                        PostsDownloadLoop(postsToDownload, profileName, fastUpdate, postFilter,
                                          totalCount: profile.Mediacount, ownerProfile: profile,
                                          takewhile: postsTakewhile, possiblyPinned: 3, maxCount: maxCount);
                        if (latestStamps != null && postsToDownload.FirstItem != null)
                            latestStamps.SetLastPostTimestamp(profileName, postsToDownload.FirstItem.DateLocal);
                    }
                });
            }

            if (stories && profiles.Count > 0)
            {
                Context.ErrorCatcher("Download stories", () =>
                {
                    Context.Log("Downloading stories");
                    DownloadStories(userids: profiles.Cast<object>().ToList(), fastUpdate: fastUpdate, filenameTarget: null,
                                    storyitemFilter: storyitemFilter, latestStamps: latestStamps);
                });
            }
        }

        /// <summary>Download one profile</summary>
        [Obsolete("Use DownloadProfiles.")]
        public void DownloadProfile(object profileName,
                                    bool profilePic = true, bool profilePicOnly = false,
                                    bool fastUpdate = false,
                                    bool downloadStories = false, bool downloadStoriesOnly = false,
                                    bool downloadTagged = false, bool downloadTaggedOnly = false,
                                    Func<Post, bool> postFilter = null,
                                    Func<StoryItem, bool> storyitemFilter = null)
        {
            // Get profile main page json
            // check if profile does exist or name has changed since last download
            // and update name and json data if necessary
            Profile profile = profileName as Profile ?? CheckProfileId(PyStr.Lower((string)profileName));

            string name = profile.Username;

            // Save metadata as JSON if desired.
            if (SaveMetadata)
            {
                string jsonFilename = string.Format("{0}/{1}_{2}", PlainFormat(DirnamePattern, "profile", name, "target", name),
                                                    name, profile.Userid);
                SaveMetadataJson(jsonFilename, profile);
            }

            if (Context.IsLoggedIn && profile.HasBlockedViewer && !profile.IsPrivate)
                // raising ProfileNotExistsException invokes "trying again anonymously" logic
                throw new ProfileNotExistsException(string.Format("Profile {0} has blocked you", name));

            // Download profile picture
            if (profilePic || profilePicOnly)
            {
                Context.ErrorCatcher(string.Format("Download profile picture of {0}", name), () =>
                {
                    DownloadProfilepic(profile);
                });
            }
            if (profilePicOnly)
                return;

            // Catch some errors
            if (profile.IsPrivate)
            {
                if (!Context.IsLoggedIn)
                    throw new LoginRequiredException(string.Format("profile {0} requires login", name));
                if (!profile.FollowedByViewer && Context.Username != profile.Username)
                    throw new PrivateProfileNotFollowedException(string.Format("Profile {0}: private but not followed.", name));
            }
            else
            {
                if (Context.IsLoggedIn && !(downloadStories || downloadStoriesOnly))
                    Context.Log(string.Format("profile {0} could also be downloaded anonymously.", name));
            }

            // Download stories, if requested
            if (downloadStories || downloadStoriesOnly)
            {
                if (profile.HasViewableStory)
                {
                    Context.ErrorCatcher(string.Format("Download stories of {0}", name), () =>
                    {
                        DownloadStories(userids: new List<object> { profile.Userid }, filenameTarget: name,
                                        fastUpdate: fastUpdate, storyitemFilter: storyitemFilter);
                    });
                }
                else
                {
                    Context.Log(string.Format("{0} does not have any stories.", name));
                }
            }
            if (downloadStoriesOnly)
                return;

            // Download tagged, if requested
            if (downloadTagged || downloadTaggedOnly)
            {
                Context.ErrorCatcher(string.Format("Download tagged of {0}", name), () =>
                {
                    DownloadTagged(profile, fastUpdate: fastUpdate, postFilter: postFilter);
                });
            }
            if (downloadTaggedOnly)
                return;

            // Iterate over pictures and download them
            Context.Log(string.Format("Retrieving posts from profile {0}.", name));
            PostsDownloadLoop(profile.GetPosts(), name, fastUpdate, postFilter, totalCount: profile.Mediacount,
                              ownerProfile: profile);
        }

        /// <summary>Logs in and internally stores session, asking user for password interactively.</summary>
        /// <exception cref="InvalidArgumentException">when in quiet mode.</exception>
        /// <exception cref="LoginException">If the provided username does not exist.</exception>
        /// <exception cref="ConnectionException">If connection to Instagram failed.</exception>
        public void InteractiveLogin(string username)
        {
            if (Context.Quiet)
                throw new InvalidArgumentException("Quiet mode requires given password or valid session file.");
            try
            {
                string password = null;
                while (password == null)
                {
                    password = ConsoleInput.GetPass(string.Format("Enter Instagram password for {0}: ", username));
                    try
                    {
                        Login(username, password);
                    }
                    catch (BadCredentialsException err)
                    {
                        Console.Error.WriteLine(err.Message);
                        password = null;
                    }
                }
            }
            catch (TwoFactorAuthRequiredException)
            {
                while (true)
                {
                    try
                    {
                        string code = ConsoleInput.Input("Enter 2FA verification code: ");
                        TwoFactorLogin(code);
                        break;
                    }
                    catch (BadCredentialsException err)
                    {
                        Console.Error.WriteLine(err.Message);
                    }
                }
            }
        }

        /// <summary>Returns whether any error has been reported and stored to be repeated at program termination.</summary>
        public bool HasStoredErrors { get { return Context.HasStoredErrors; } }
    }

    /// <summary>Console input like Python's input() and getpass.getpass().</summary>
    public static class ConsoleInput
    {
        /// <summary>Python's input(prompt). Raises KeyboardInterruptException on ^C, EndOfStreamException on EOF.</summary>
        public static string Input(string prompt)
        {
            Console.Out.Write(prompt);
            Console.Out.Flush();
            string line = Console.In.ReadLine();
            Interrupt.Check();
            if (line == null)
                throw new EndOfStreamException("EOF when reading a line");
            return line;
        }

        /// <summary>Python's getpass.getpass(prompt): reads a line without echo.</summary>
        public static string GetPass(string prompt)
        {
            if (Console.IsInputRedirected)
                return Input(prompt);
            Console.Out.Write(prompt);
            Console.Out.Flush();
            var sb = new StringBuilder();
            while (true)
            {
                Interrupt.Check();
                if (!Console.KeyAvailable)
                {
                    System.Threading.Thread.Sleep(20);
                    continue;
                }
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                    break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                        sb.Length--;
                    continue;
                }
                if (key.KeyChar == 3)
                {
                    Interrupt.Raise();
                    continue;
                }
                if (key.KeyChar != '\0')
                    sb.Append(key.KeyChar);
            }
            Console.Out.WriteLine();
            return sb.ToString();
        }
    }
}
