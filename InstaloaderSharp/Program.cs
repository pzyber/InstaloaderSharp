using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using InstaloaderSharp.Cli;
using InstaloaderSharp.Python;
using InstaloaderSharp.Util;

namespace InstaloaderSharp
{
    /// <summary>Download pictures (or videos) along with their captions and other metadata from Instagram.</summary>
    public static class Program
    {
        public enum ExitCode
        {
            Success = 0,
            NonFatalError = 1,
            InitFailure = 2,
            LoginFailure = 3,
            DownloadAborted = 4,
            UserAborted = 5,
            UnexpectedError = 99
        }

        private const string Doc = "Download pictures (or videos) along with their captions and other metadata from Instagram.";

        private static string Argv0
        {
            get
            {
                try
                {
                    string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                    return Path.GetFileNameWithoutExtension(exe);
                }
                catch (Exception)
                {
                    return "instaloader";
                }
            }
        }

        public static string UsageString()
        {
            // NOTE: duplicated in README.rst and docs/index.rst
            string argv0 = Argv0;
            string pad = new string(' ', argv0.Length);
            return string.Format(
                "\n{0} [--comments] [--geotags]\n" +
                "{1} [--stories] [--highlights] [--tagged] [--reels] [--igtv]\n" +
                "{1} [--login YOUR-USERNAME] [--fast-update]\n" +
                "{1} profile | \"#hashtag\" | %%location_id | :stories | :feed | :saved\n" +
                "{0} --help", argv0, pad);
        }

        public static List<int> HttpStatusCodeList(string codeListStr)
        {
            var codes = codeListStr.Split(',').Select(s => (int)PyInt.ParseLong(s)).ToList();
            foreach (int code in codes)
                if (!(100 <= code && code <= 599))
                    throw new ArgumentTypeError(string.Format("Invalid HTTP status code: {0}", code));
            return codes;
        }

        /// <summary>
        /// Takes an --post-filter=... or --storyitem-filter=... filter specification and makes a filter_func
        /// Callable out of it.
        /// </summary>
        public static Func<T, bool> FilterstrToFilterfunc<T>(string filterStr)
        {
            // The filter_str is parsed, then all names occurring in it are replaced by loads to item.<name>. A
            // function T->bool is returned which evaluates the filter with the item as 'item' in its namespace.
            PyExpression expression;
            try
            {
                expression = PyExpression.Parse(filterStr);
            }
            catch (PySyntaxError err)
            {
                throw new InvalidArgumentException(string.Format("Invalid filter: {0}", err.Message));
            }
            foreach (string name in expression.Names)
            {
                if (!PyRuntime.TypeHasAttr(typeof(T), name))
                    throw new InvalidArgumentException(string.Format("Invalid filter: {0} not a {1} attribute.", name, typeof(T).Name));
            }
            return item => PyRuntime.Truthy(expression.Evaluate(item));
        }

        public static List<KeyValuePair<string, string>> GetCookiesFromInstagram(string domain, string browser, string cookieFile = null)
        {
            var supportedBrowsers = new[] { "brave", "chrome", "chromium", "edge", "firefox", "librewolf", "opera", "opera_gx", "safari", "vivaldi" };
            if (!supportedBrowsers.Contains(browser))
                throw new InvalidArgumentException("Loading cookies from the specified browser failed\n" +
                                                   "Supported browsers are Brave, Chrome, Chromium, Edge, Firefox, LibreWolf, " +
                                                   "Opera, Opera_GX, Safari and Vivaldi");

            var cookies = new List<KeyValuePair<string, string>>();
            foreach (BrowserCookie cookie in BrowserCookies.Load(browser, cookieFile))
            {
                // Browsers may keep cleared copies of a cookie (value '""') for a narrower domain such as
                // www.instagram.com. Letting them override the real value sends an empty CSRF token, which
                // Instagram answers with 403 on POST requests.
                if (cookie.Domain.Contains(domain) && cookie.Value.Trim('"').Length > 0)
                {
                    int existing = cookies.FindIndex(kv => kv.Key == cookie.Name);
                    if (existing >= 0)
                        cookies[existing] = new KeyValuePair<string, string>(cookie.Name, cookie.Value);
                    else
                        cookies.Add(new KeyValuePair<string, string>(cookie.Name, cookie.Value));
                }
            }

            if (cookies.Count > 0)
                Console.Out.WriteLine("Cookies loaded successfully from {0}", browser);
            else
                throw new LoginException(string.Format("No cookies found for Instagram in {0}, Are you logged in successfully in {0}?", browser));

            return cookies;
        }

        public static void ImportSession(string browser, Instaloader instaloader, string cookiefile)
        {
            List<KeyValuePair<string, string>> cookie = GetCookiesFromInstagram("instagram", browser, cookiefile);
            if (cookie != null)
            {
                instaloader.Context.UpdateCookies(cookie);
                string username = instaloader.TestLogin();
                if (string.IsNullOrEmpty(username))
                    throw new LoginException(string.Format("Not logged in. Are you logged in successfully in {0}?", browser));
                instaloader.Context.Username = username;
                Console.Out.WriteLine("{0} has been successfully logged in.", username);
                Console.Out.WriteLine("Next time use --login={0} to reuse the same session.", username);
            }
        }

        /// <summary>An insertion-ordered set of profiles.</summary>
        private sealed class ProfileSet : ICollection<Profile>
        {
            private readonly List<Profile> _items = new List<Profile>();
            private readonly HashSet<Profile> _set = new HashSet<Profile>();

            public void Add(Profile item)
            {
                if (_set.Add(item))
                    _items.Add(item);
            }

            public void Clear()
            {
                _items.Clear();
                _set.Clear();
            }

            public bool Contains(Profile item) { return _set.Contains(item); }
            public void CopyTo(Profile[] array, int arrayIndex) { _items.CopyTo(array, arrayIndex); }

            public bool Remove(Profile item)
            {
                _items.Remove(item);
                return _set.Remove(item);
            }

            public int Count { get { return _items.Count; } }
            public bool IsReadOnly { get { return false; } }
            public IEnumerator<Profile> GetEnumerator() { return _items.GetEnumerator(); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }

        /// <summary>Download set of profiles, hashtags etc. and handle logging in and session files if desired.</summary>
        public static ExitCode MainImpl(Instaloader instaloader, List<string> targetlist,
                                        string username = null, string password = null,
                                        string sessionfile = null,
                                        bool downloadProfilePic = true, bool downloadPosts = true,
                                        bool downloadStories = false,
                                        bool downloadHighlights = false,
                                        bool downloadTagged = false,
                                        bool downloadReels = false,
                                        bool downloadIgtv = false,
                                        bool fastUpdate = false,
                                        string latestStampsFile = null,
                                        long? maxCount = null, string postFilterStr = null,
                                        string storyitemFilterStr = null,
                                        string browser = null,
                                        string cookiefile = null)
        {
            // Parse and generate filter function
            Func<Post, bool> postFilter = null;
            if (postFilterStr != null)
            {
                postFilter = FilterstrToFilterfunc<Post>(postFilterStr);
                instaloader.Context.Log(string.Format("Only download posts with property \"{0}\".", postFilterStr));
            }
            Func<StoryItem, bool> storyitemFilter = null;
            if (storyitemFilterStr != null)
            {
                storyitemFilter = FilterstrToFilterfunc<StoryItem>(storyitemFilterStr);
                instaloader.Context.Log(string.Format("Only download storyitems with property \"{0}\".", storyitemFilterStr));
            }
            LatestStamps latestStamps = null;
            if (latestStampsFile != null)
            {
                latestStamps = new LatestStamps(latestStampsFile);
                instaloader.Context.Log(string.Format("Using latest stamps from {0}.", latestStampsFile));
            }
            // load cookies if browser is not None
            if (!string.IsNullOrEmpty(browser))
                ImportSession(browser.ToLowerInvariant(), instaloader, cookiefile);
            // Login, if desired
            if (username != null)
            {
                if (!Regex.IsMatch(username, @"^[A-Za-z0-9._]+$"))
                    instaloader.Context.Error(string.Format("Warning: Parameter \"{0}\" for --login is not a valid username.", username));
                try
                {
                    instaloader.LoadSessionFromFile(username, sessionfile);
                }
                catch (FileNotFoundException err)
                {
                    if (sessionfile != null)
                        Console.Error.WriteLine(err.Message);
                    instaloader.Context.Log("Session file does not exist yet - Logging in.");
                }
                if (!instaloader.Context.IsLoggedIn || username != instaloader.TestLogin())
                {
                    if (password != null)
                    {
                        try
                        {
                            instaloader.Login(username, password);
                        }
                        catch (TwoFactorAuthRequiredException)
                        {
                            // https://github.com/instaloader/instaloader/issues/1217
                            instaloader.Context.Error("Warning: There have been reports of 2FA currently not working. " +
                                                      "Consider importing session cookies from your browser with " +
                                                      "--load-cookies.");
                            while (true)
                            {
                                try
                                {
                                    string code = ConsoleInput.Input("Enter 2FA verification code: ");
                                    instaloader.TwoFactorLogin(code);
                                    break;
                                }
                                catch (BadCredentialsException err)
                                {
                                    Console.Error.WriteLine(err.Message);
                                }
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            instaloader.InteractiveLogin(username);
                        }
                        catch (KeyboardInterruptException)
                        {
                            Console.Error.WriteLine("\nInterrupted by user.");
                            return ExitCode.UserAborted;
                        }
                    }
                }
                instaloader.Context.Log(string.Format("Logged in as {0}.", username));
            }
            // Try block for KeyboardInterrupt (save session on ^C)
            var profiles = new ProfileSet();
            var anonymousRetryProfiles = new ProfileSet();
            ExitCode exitCode = ExitCode.Success;
            try
            {
                // Generate set of profiles, already downloading non-profile targets
                foreach (string originalTarget in targetlist)
                {
                    if ((originalTarget.EndsWith(".json") || originalTarget.EndsWith(".json.xz")) && PyOs.IsFile(originalTarget))
                    {
                        string jsonTarget = originalTarget;
                        instaloader.Context.ErrorCatcher(jsonTarget, () =>
                        {
                            IJsonExportable structure = Structures.LoadStructureFromFile(instaloader.Context, jsonTarget);
                            var post = structure as Post;
                            var storyItem = structure as StoryItem;
                            var profile = structure as Profile;
                            if (post != null)
                            {
                                if (postFilter != null && !postFilter(post))
                                {
                                    instaloader.Context.Log(string.Format("<{0} ({1}) skipped>", post, jsonTarget), flush: true);
                                    return;
                                }
                                instaloader.Context.Log(string.Format("Downloading {0} ({1})", post, jsonTarget));
                                instaloader.DownloadPost(post, PyOs.DirName(jsonTarget));
                            }
                            else if (storyItem != null)
                            {
                                if (storyitemFilter != null && !storyitemFilter(storyItem))
                                {
                                    instaloader.Context.Log(string.Format("<{0} ({1}) skipped>", storyItem, jsonTarget), flush: true);
                                    return;
                                }
                                instaloader.Context.Log(string.Format("Attempting to download {0} ({1})", storyItem, jsonTarget));
                                instaloader.DownloadStoryitem(storyItem, PyOs.DirName(jsonTarget));
                            }
                            else if (profile != null)
                            {
                                throw new InvalidArgumentException(string.Format(
                                    "Profile JSON are ignored. Pass \"{0}\" to download that profile", profile.Username));
                            }
                            else
                            {
                                throw new InvalidArgumentException(string.Format("{0} JSON file not supported as target",
                                                                                 structure.NodeType));
                            }
                        });
                        continue;
                    }
                    // strip '/' characters to be more shell-autocompletion-friendly
                    string target = originalTarget.TrimEnd('/');
                    instaloader.Context.ErrorCatcher(target, () =>
                    {
                        if (Regex.IsMatch(target, @"^@[A-Za-z0-9._]+$"))
                        {
                            instaloader.Context.Log(string.Format("Retrieving followees of {0}...", target.Substring(1)));
                            Profile profile = Profile.FromUsername(instaloader.Context, target.Substring(1));
                            foreach (Profile followee in profile.GetFollowees())
                            {
                                instaloader.SaveProfileId(followee);
                                profiles.Add(followee);
                            }
                        }
                        else if (Regex.IsMatch(target, @"^#[\p{L}\p{N}_]+$"))
                        {
                            instaloader.DownloadHashtag(target.Substring(1), maxCount: maxCount, fastUpdate: fastUpdate,
                                                        postFilter: postFilter, profilePic: downloadProfilePic, posts: downloadPosts);
                        }
                        else if (Regex.IsMatch(target, @"^-[A-Za-z0-9_-]+$"))
                        {
                            instaloader.DownloadPost(Post.FromShortcode(instaloader.Context, target.Substring(1)), target);
                        }
                        else if (Regex.IsMatch(target, @"^%[0-9]+$"))
                        {
                            instaloader.DownloadLocation(target.Substring(1), maxCount: maxCount, fastUpdate: fastUpdate,
                                                         postFilter: postFilter);
                        }
                        else if (target == ":feed")
                        {
                            instaloader.DownloadFeedPosts(fastUpdate: fastUpdate, maxCount: maxCount, postFilter: postFilter);
                        }
                        else if (target == ":stories")
                        {
                            instaloader.DownloadStories(fastUpdate: fastUpdate, storyitemFilter: storyitemFilter);
                        }
                        else if (target == ":saved")
                        {
                            instaloader.DownloadSavedPosts(fastUpdate: fastUpdate, maxCount: maxCount, postFilter: postFilter);
                        }
                        else if (Regex.IsMatch(target, @"^[A-Za-z0-9._]+$"))
                        {
                            bool downloadProfileContent = downloadPosts || downloadTagged || downloadReels || downloadIgtv;
                            try
                            {
                                Profile profile = instaloader.CheckProfileId(target, latestStamps);
                                if (instaloader.Context.IsLoggedIn && profile.HasBlockedViewer)
                                {
                                    if (downloadProfilePic || (downloadProfileContent && !profile.IsPrivate))
                                        throw new ProfileNotExistsException(string.Format(
                                            "{0} blocked you; But we download her anonymously.", target));
                                    instaloader.Context.Error(string.Format("{0} blocked you.", target));
                                }
                                else
                                {
                                    profiles.Add(profile);
                                }
                            }
                            catch (ProfileNotExistsException err)
                            {
                                // Not only our profile.has_blocked_viewer condition raises ProfileNotExistsException,
                                // check_profile_id() also does, since access to blocked profile may be responded with 404.
                                if (instaloader.Context.IsLoggedIn && (downloadProfilePic || downloadProfileContent))
                                {
                                    instaloader.Context.Log(err.Message);
                                    instaloader.Context.Log("Trying again anonymously, helps in case you are just blocked.");
                                    using (AnonymousInstaloader anonymous = instaloader.AnonymousCopy())
                                    {
                                        instaloader.Context.ErrorCatcher(() =>
                                        {
                                            anonymousRetryProfiles.Add(anonymous.Loader.CheckProfileId(target, latestStamps));
                                            instaloader.Context.Error(string.Format("Warning: {0} will be downloaded anonymously (\"{1}\").",
                                                                                    target, err.Message));
                                        });
                                    }
                                }
                                else
                                {
                                    throw;
                                }
                            }
                        }
                        else
                        {
                            string targetType;
                            switch (target.Length > 0 ? target[0] : ' ')
                            {
                                case '#': targetType = "hashtag"; break;
                                case '%': targetType = "location"; break;
                                case '-': targetType = "shortcode"; break;
                                default: targetType = "username"; break;
                            }
                            throw new ProfileNotExistsException(string.Format("Invalid {0} {1}", targetType, target));
                        }
                    });
                }
                if (profiles.Count > 1)
                    instaloader.Context.Log(string.Format("Downloading {0} profiles: {1}", profiles.Count,
                                                          string.Join(" ", profiles.Select(p => p.Username))));
                if (instaloader.Context.IphoneSupport && profiles.Count > 0 && (downloadProfilePic || downloadPosts) &&
                    !instaloader.Context.IsLoggedIn)
                    instaloader.Context.Log("Hint: Login to download higher-quality versions of pictures.");
                instaloader.DownloadProfiles(
                    profiles,
                    downloadProfilePic,
                    downloadPosts,
                    downloadTagged,
                    downloadIgtv,
                    downloadHighlights,
                    downloadStories,
                    fastUpdate,
                    postFilter,
                    storyitemFilter,
                    latestStamps: latestStamps,
                    reels: downloadReels);
                if (anonymousRetryProfiles.Count > 0)
                {
                    instaloader.Context.Log(string.Format("Downloading anonymously: {0}",
                                                          string.Join(" ", anonymousRetryProfiles.Select(p => p.Username))));
                    using (AnonymousInstaloader anonymous = instaloader.AnonymousCopy())
                    {
                        anonymous.Loader.DownloadProfiles(
                            anonymousRetryProfiles,
                            downloadProfilePic,
                            downloadPosts,
                            downloadTagged,
                            downloadIgtv,
                            fastUpdate: fastUpdate,
                            postFilter: postFilter,
                            latestStamps: latestStamps,
                            reels: downloadReels);
                    }
                }
            }
            catch (KeyboardInterruptException)
            {
                Console.Error.WriteLine("\nInterrupted by user.");
                exitCode = ExitCode.UserAborted;
            }
            catch (AbortDownloadException exc)
            {
                // The messages of InstaloaderSharp's own aborts end with a period already.
                Console.Error.WriteLine("\nDownload aborted: {0}.", exc.Message.TrimEnd('.'));
                exitCode = ExitCode.DownloadAborted;
            }
            // Save session if it is useful
            if (instaloader.Context.IsLoggedIn)
                instaloader.SaveSessionToFile(sessionfile);
            // User might be confused if Instaloader does nothing
            if (targetlist.Count == 0)
            {
                if (instaloader.Context.IsLoggedIn)
                {
                    // Instaloader did at least save a session file
                    instaloader.Context.Log("No targets were specified, thus nothing has been downloaded.");
                }
                else
                {
                    // Instaloader did not do anything
                    instaloader.Context.Log("usage:" + UsageString());
                    exitCode = ExitCode.InitFailure;
                }
            }
            return exitCode;
        }

        internal static ArgParser CreateParser()
        {
            var parser = new ArgParser
            {
                Prog = Argv0,
                Usage = UsageString(),
                Description = Doc,
                Epilog = "The complete documentation can be found at https://instaloader.github.io/.",
                VersionString = Instaloader.Version
            };

            ArgGroup gTargets = parser.AddGroup("What to Download",
                                                "Specify a list of targets. For each of these, Instaloader creates a folder " +
                                                "and downloads all posts. The following targets are supported:");
            parser.Add(gTargets, new ArgAction
            {
                Dest = "profile",
                Kind = ArgKind.Positional,
                Help = "Download profile. If an already-downloaded profile has been renamed, Instaloader " +
                       "automatically finds it by its unique ID and renames the folder likewise."
            });
            Action<string, string, string> dummyPositional = (dest, metavar, help) =>
                gTargets.Actions.Add(new ArgAction { Dest = dest, Kind = ArgKind.Positional, Metavar = metavar, Help = help });
            dummyPositional("_at_profile", "@profile",
                            "Download all followees of profile. Requires login. Consider using :feed rather than @yourself.");
            dummyPositional("_hashtag", "\"#hashtag\"", "Download #hashtag.");
            dummyPositional("_location", "%location_id", "Download %%location_id. Requires login.");
            dummyPositional("_feed", ":feed", "Download pictures from your feed. Requires login.");
            dummyPositional("_stories", ":stories", "Download the stories of your followees. Requires login.");
            dummyPositional("_saved", ":saved", "Download the posts that you marked as saved. Requires login.");
            dummyPositional("_singlepost", "-- -shortcode", "Download the post with the given shortcode");
            dummyPositional("_json", "filename.json[.xz]", "Re-Download the given object.");
            dummyPositional("_fromfile", "+args.txt", "Read targets (and options) from given textfile.");

            ArgGroup gPost = parser.AddGroup("What to Download of each Post");
            ArgGroup gProf = parser.AddGroup("What to Download of each Profile");

            Func<string[], string, bool, ArgAction> flag = (opts, help, suppress) =>
                new ArgAction { OptionStrings = opts, Kind = ArgKind.StoreTrue, Help = help, Suppress = suppress };
            Func<string[], string, string, ArgAction> store = (opts, metavar, help) =>
                new ArgAction { OptionStrings = opts, Kind = ArgKind.Store, Metavar = metavar, Help = help };

            parser.Add(gProf, flag(new[] { "-P", "--profile-pic-only" }, null, true));
            parser.Add(gProf, flag(new[] { "--no-posts" }, "Do not download regular posts.", false));
            parser.Add(gProf, flag(new[] { "--no-profile-pic" }, "Do not download profile picture.", false));
            parser.Add(gPost, store(new[] { "--slide" }, null, "Set what image/interval of a sidecar you want to download."));
            parser.Add(gPost, flag(new[] { "--no-pictures" },
                                   "Do not download post pictures. Cannot be used together with --fast-update. " +
                                   "Implies --no-video-thumbnails, does not imply --no-videos.", false));
            parser.Add(gPost, flag(new[] { "-V", "--no-videos" }, "Do not download videos.", false));
            parser.Add(gPost, flag(new[] { "--no-video-thumbnails" }, "Do not download thumbnails of videos.", false));
            parser.Add(gPost, flag(new[] { "-G", "--geotags" },
                                   "Download geotags when available. Geotags are stored as a " +
                                   "text file with the location's name and a Google Maps link. " +
                                   "This may require an additional request to the Instagram " +
                                   "server for each picture.", false));
            parser.Add(gPost, flag(new[] { "-C", "--comments" },
                                   "Download and update comments for each post. " +
                                   "This requires an additional request to the Instagram " +
                                   "server for each post, which is why it is disabled by default. Requires login.", false));
            parser.Add(gPost, flag(new[] { "--no-captions" }, "Do not create txt files.", false));
            parser.Add(gPost, new ArgAction
            {
                OptionStrings = new[] { "--post-metadata-txt" },
                Kind = ArgKind.Append,
                Help = "Template to write in txt file for each Post."
            });
            parser.Add(gPost, new ArgAction
            {
                OptionStrings = new[] { "--storyitem-metadata-txt" },
                Kind = ArgKind.Append,
                Help = "Template to write in txt file for each StoryItem."
            });
            parser.Add(gPost, flag(new[] { "--no-metadata-json" }, "Do not create a JSON file containing the metadata of each post.", false));
            parser.Add(gPost, flag(new[] { "--metadata-json" }, null, true));
            parser.Add(gPost, flag(new[] { "--no-compress-json" }, "Do not xz compress JSON files, rather create pretty formatted JSONs.", false));
            parser.Add(gProf, flag(new[] { "-s", "--stories" }, "Also download stories of each profile that is downloaded. Requires login.", false));
            parser.Add(gProf, flag(new[] { "--stories-only" }, null, true));
            parser.Add(gProf, flag(new[] { "--highlights" }, "Also download highlights of each profile that is downloaded. Requires login.", false));
            parser.Add(gProf, flag(new[] { "--tagged" }, "Also download posts where each profile is tagged.", false));
            parser.Add(gProf, flag(new[] { "--reels" }, "Also download Reels videos.", false));
            parser.Add(gProf, flag(new[] { "--igtv" }, "IGTV has been retired by Instagram; former IGTV videos are " +
                                                         "downloaded as posts and reels. Kept for compatibility.", false));

            ArgGroup gCond = parser.AddGroup("Which Posts to Download");
            parser.Add(gCond, flag(new[] { "-F", "--fast-update" },
                                   "For each target, stop when encountering the first already-downloaded picture. This " +
                                   "flag is recommended when you use Instaloader to update your personal Instagram archive.", false));
            parser.Add(gCond, new ArgAction
            {
                OptionStrings = new[] { "--latest-stamps" },
                Kind = ArgKind.OptionalValue,
                Metavar = "STAMPSFILE",
                Const = Instaloader.GetDefaultStampsFilename(),
                Help = "Store the timestamps of latest media scraped for each profile. This allows updating " +
                       "your personal Instagram archive even if you delete the destination directories. " +
                       "If STAMPSFILE is not provided, defaults to " + Instaloader.GetDefaultStampsFilename()
            });
            parser.Add(gCond, store(new[] { "--post-filter", "--only-if" }, "filter",
                                    "Expression that, if given, must evaluate to True for each post to be downloaded. Must be " +
                                    "a syntactically valid python expression. Variables are evaluated to " +
                                    "instaloader.Post attributes. Example: --post-filter=viewer_has_liked."));
            parser.Add(gCond, store(new[] { "--storyitem-filter" }, "filter",
                                    "Expression that, if given, must evaluate to True for each storyitem to be downloaded. " +
                                    "Must be a syntactically valid python expression. Variables are evaluated to " +
                                    "instaloader.StoryItem attributes."));
            parser.Add(gCond, store(new[] { "-c", "--count" }, null,
                                    "Do not attempt to download more than COUNT posts. " +
                                    "Applies to #hashtag, %%location_id, :feed, and :saved."));

            ArgGroup gLogin = parser.AddGroup("Login (Download Private Profiles)",
                                              "Instaloader can login to Instagram. This allows downloading private profiles. " +
                                              "To login, pass the --login option. Your session cookie (not your password!) " +
                                              "will be saved to a local file to be reused next time you want Instaloader " +
                                              "to login. Instead of --login, the --load-cookies option can be used to " +
                                              "import a session from a browser.");
            parser.Add(gLogin, store(new[] { "-l", "--login" }, "YOUR-USERNAME", "Login name (profile name) for your Instagram account."));
            parser.Add(gLogin, store(new[] { "-b", "--load-cookies" }, "BROWSER-NAME", "Browser name to load cookies from Instagram"));
            parser.Add(gLogin, store(new[] { "-B", "--cookiefile" }, "COOKIE-FILE", "Cookie file of a profile to load cookies"));
            parser.Add(gLogin, store(new[] { "-f", "--sessionfile" }, null,
                                     "Path for loading and storing session key file. " +
                                     "Defaults to " + Instaloader.GetDefaultSessionFilename("<login_name>")));
            parser.Add(gLogin, store(new[] { "-p", "--password" }, "YOUR-PASSWORD",
                                     "Password for your Instagram account. Without this option, " +
                                     "you'll be prompted for your password interactively if " +
                                     "there is not yet a valid session file."));

            ArgGroup gHow = parser.AddGroup("How to Download");
            parser.Add(gHow, store(new[] { "--dirname-pattern" }, null,
                                   "Name of directory where to store posts. {profile} is replaced by the profile name, " +
                                   "{target} is replaced by the target you specified, i.e. either :feed, #hashtag or the " +
                                   "profile name. Defaults to '{target}'."));
            parser.Add(gHow, store(new[] { "--filename-pattern" }, null,
                                   "Prefix of filenames for posts and stories, relative to the directory given with " +
                                   "--dirname-pattern. {profile} is replaced by the profile name," +
                                   "{target} is replaced by the target you specified, i.e. either :feed" +
                                   "#hashtag or the profile name. Defaults to '{date_utc}_UTC'"));
            parser.Add(gHow, store(new[] { "--title-pattern" }, null,
                                   "Prefix of filenames for profile pics, hashtag profile pics, and highlight covers. " +
                                   "Defaults to '{date_utc}_UTC_{typename}' if --dirname-pattern contains '{target}' " +
                                   "or '{dirname}', or if --dirname-pattern is not specified. Otherwise defaults to " +
                                   "'{target}_{date_utc}_UTC_{typename}'."));
            parser.Add(gHow, store(new[] { "--resume-prefix" }, "PREFIX",
                                   "Prefix for filenames that are used to save the information to resume an interrupted " +
                                   "download."));
            parser.Add(gHow, flag(new[] { "--sanitize-paths" },
                                  "Sanitize paths so that the resulting file and directory names are valid on both " +
                                  "Windows and Unix.", false));
            parser.Add(gHow, flag(new[] { "--no-resume" },
                                  "Do not resume a previously-aborted download iteration, and do not save such information " +
                                  "when interrupted.", false));
            parser.Add(gHow, flag(new[] { "--use-aged-resume-files" }, null, true));
            parser.Add(gHow, store(new[] { "--user-agent" }, null,
                                   string.Format("User Agent to use for HTTP requests. Defaults to '{0}'.", InstaloaderContext.DefaultUserAgent())));
            parser.Add(gHow, flag(new[] { "-S", "--no-sleep" }, null, true));
            parser.Add(gHow, new ArgAction
            {
                OptionStrings = new[] { "--max-connection-attempts" },
                Kind = ArgKind.Store,
                Metavar = "N",
                Default = 3L,
                Type = s => (object)PyInt.ParseLong(s),
                TypeName = "int",
                Help = "Maximum number of connection attempts until a request is aborted. Defaults to 3. If a " +
                       "connection fails, it can be manually skipped by hitting CTRL+C. Set this to 0 to retry " +
                       "infinitely."
            });
            parser.Add(gHow, flag(new[] { "--commit-mode" }, null, true));
            parser.Add(gHow, new ArgAction
            {
                OptionStrings = new[] { "--request-timeout" },
                Kind = ArgKind.Store,
                Metavar = "N",
                Default = 300.0,
                Type = s => (object)ParseFloat(s),
                TypeName = "float",
                Help = "Seconds to wait before timing out a connection request. Defaults to 300."
            });
            parser.Add(gHow, new ArgAction
            {
                OptionStrings = new[] { "--abort-on" },
                Kind = ArgKind.Store,
                Metavar = "STATUS_CODES",
                Type = s => HttpStatusCodeList(s),
                TypeName = "http_status_code_list",
                Help = "Comma-separated list of HTTP status codes that cause Instaloader to abort, bypassing all " +
                       "retry logic."
            });
            parser.Add(gHow, flag(new[] { "--no-iphone" }, "Do not attempt to download iPhone version of images and videos.", false));

            ArgGroup gMisc = parser.AddGroup("Miscellaneous Options");
            parser.Add(gMisc, flag(new[] { "-q", "--quiet" },
                                   "Disable user interaction, i.e. do not print messages (except errors) and fail " +
                                   "if login credentials are needed but not given. This makes Instaloader suitable as a " +
                                   "cron job.", false));
            parser.Add(gMisc, new ArgAction { OptionStrings = new[] { "-h", "--help" }, Kind = ArgKind.Help, Help = "Show this help message and exit." });
            parser.Add(gMisc, new ArgAction { OptionStrings = new[] { "--version" }, Kind = ArgKind.Version, Help = "Show version number and exit." });
            return parser;
        }

        /// <summary>Python's float(s).</summary>
        private static double ParseFloat(string s)
        {
            string t = PyStr.Strip(s).Replace("_", "");
            double d;
            string lower = t.ToLowerInvariant();
            if (lower == "inf" || lower == "+inf" || lower == "infinity" || lower == "+infinity")
                return double.PositiveInfinity;
            if (lower == "-inf" || lower == "-infinity")
                return double.NegativeInfinity;
            if (lower == "nan" || lower == "+nan" || lower == "-nan")
                return double.NaN;
            if (t.Length > 0 && !t.Contains(",") &&
                double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return d;
            throw new PyValueError("could not convert string to float: " + PyStr.Repr(s));
        }

        public static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (IOException)
            {
            }
            Interrupt.Install();
            try
            {
                return (int)MainWithArgs(args);
            }
            catch (ArgParserExit exit)
            {
                return exit.Code;
            }
            catch (KeyboardInterruptException)
            {
                Console.Error.WriteLine("\nInterrupted by user.");
                return (int)ExitCode.UserAborted;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Traceback (most recent call last):");
                Console.Error.WriteLine(ex.ToString());
                var py = ex as PyException;
                Console.Error.WriteLine("{0}: {1}", py != null ? py.PythonName : ex.GetType().Name, ex.Message);
                return (int)ExitCode.NonFatalError;
            }
        }

        private static List<string> StringList(object value)
        {
            var list = value as List<object>;
            return list == null ? null : list.Select(o => (string)o).ToList();
        }

        public static ExitCode MainWithArgs(string[] argv)
        {
            ArgParser parser = CreateParser();
            Dictionary<string, object> args = parser.Parse(argv);
            Func<string, bool> flag = name => (bool)args[name];
            Func<string, string> str = name => args[name] as string;

            ExitCode exitCode;
            try
            {
                bool stories = flag("stories");
                if (str("login") == null && str("load_cookies") == null && (stories || flag("stories_only")))
                {
                    Console.Error.WriteLine("Login is required to download stories.");
                    stories = false;
                    if (flag("stories_only"))
                        throw new InvalidArgumentException();
                }

                var profileArgs = (List<string>)args["profile"];
                if (profileArgs.Contains(":feed-all") || profileArgs.Contains(":feed-liked"))
                    throw new InvalidArgumentException(":feed-all and :feed-liked were removed. Use :feed as target and " +
                                                       "eventually --post-filter=viewer_has_liked.");

                List<string> postMetadataTxt = StringList(args["post_metadata_txt"]);
                List<string> storyitemMetadataTxt = StringList(args["storyitem_metadata_txt"]);
                string postMetadataTxtPattern = postMetadataTxt != null && postMetadataTxt.Count > 0 ? string.Join("\n", postMetadataTxt) : null;
                string storyitemMetadataTxtPattern = storyitemMetadataTxt != null && storyitemMetadataTxt.Count > 0
                    ? string.Join("\n", storyitemMetadataTxt) : null;

                if (flag("no_captions"))
                {
                    if (string.IsNullOrEmpty(postMetadataTxtPattern) && string.IsNullOrEmpty(storyitemMetadataTxtPattern))
                    {
                        postMetadataTxtPattern = "";
                        storyitemMetadataTxtPattern = "";
                    }
                    else
                    {
                        throw new InvalidArgumentException("--no-captions and --post-metadata-txt or --storyitem-metadata-txt " +
                                                           "given; That contradicts.");
                    }
                }

                if (flag("no_resume") && !string.IsNullOrEmpty(str("resume_prefix")))
                    throw new InvalidArgumentException("--no-resume and --resume-prefix given; That contradicts.");
                string resumePrefix = !flag("no_resume")
                    ? (!string.IsNullOrEmpty(str("resume_prefix")) ? str("resume_prefix") : "iterator")
                    : null;

                if (flag("no_pictures") && flag("fast_update"))
                    throw new InvalidArgumentException("--no-pictures and --fast-update cannot be used together.");

                if (!string.IsNullOrEmpty(str("login")) && !string.IsNullOrEmpty(str("load_cookies")))
                    throw new InvalidArgumentException("--load-cookies and --login cannot be used together.");

                // Determine what to download
                bool downloadProfilePic = !flag("no_profile_pic") || flag("profile_pic_only");
                bool downloadPosts = !(flag("no_posts") || flag("stories_only") || flag("profile_pic_only"));
                bool downloadStories = stories || flag("stories_only");

                var loader = new Instaloader(sleep: !flag("no_sleep"), quiet: flag("quiet"), userAgent: str("user_agent"),
                                             dirnamePattern: str("dirname_pattern"), filenamePattern: str("filename_pattern"),
                                             downloadPictures: !flag("no_pictures"),
                                             downloadVideos: !flag("no_videos"), downloadVideoThumbnails: !flag("no_video_thumbnails"),
                                             downloadGeotags: flag("geotags"),
                                             downloadComments: flag("comments"), saveMetadata: !flag("no_metadata_json"),
                                             compressJson: !flag("no_compress_json"),
                                             postMetadataTxtPattern: postMetadataTxtPattern,
                                             storyitemMetadataTxtPattern: storyitemMetadataTxtPattern,
                                             maxConnectionAttempts: (int)(long)args["max_connection_attempts"],
                                             requestTimeout: (double)args["request_timeout"],
                                             resumePrefix: resumePrefix,
                                             checkResumeBbd: !flag("use_aged_resume_files"),
                                             slide: str("slide"),
                                             fatalStatusCodes: args["abort_on"] as List<int>,
                                             iphoneSupport: !flag("no_iphone"),
                                             titlePattern: str("title_pattern"),
                                             sanitizePaths: flag("sanitize_paths"));
                long? count = null;
                if (str("count") != null)
                    count = PyInt.ParseLong(str("count"));
                exitCode = MainImpl(loader,
                                    profileArgs,
                                    username: str("login") != null ? PyStr.Lower(str("login")) : null,
                                    password: str("password"),
                                    sessionfile: str("sessionfile"),
                                    downloadProfilePic: downloadProfilePic,
                                    downloadPosts: downloadPosts,
                                    downloadStories: downloadStories,
                                    downloadHighlights: flag("highlights"),
                                    downloadTagged: flag("tagged"),
                                    downloadReels: flag("reels"),
                                    downloadIgtv: flag("igtv"),
                                    fastUpdate: flag("fast_update"),
                                    latestStampsFile: str("latest_stamps"),
                                    maxCount: count,
                                    postFilterStr: str("post_filter"),
                                    storyitemFilterStr: str("storyitem_filter"),
                                    browser: str("load_cookies"),
                                    cookiefile: str("cookiefile"));
                loader.Close();
                if (loader.HasStoredErrors)
                    exitCode = ExitCode.NonFatalError;
            }
            catch (InvalidArgumentException err)
            {
                Console.Error.WriteLine(err.Message);
                exitCode = ExitCode.InitFailure;
            }
            catch (LoginException err)
            {
                Console.Error.WriteLine(err.Message);
                exitCode = ExitCode.LoginFailure;
            }
            catch (InstaloaderException err)
            {
                Console.Out.WriteLine("Fatal error: {0}", err.Message);
                exitCode = ExitCode.UnexpectedError;
            }
            return exitCode;
        }
    }
}
