using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using InstaloaderSharp.Http;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;
using InstaloaderSharp.Util;

namespace InstaloaderSharp
{
    /// <summary>
    /// Class providing methods for (error) logging and low-level communication with Instagram.
    ///
    /// It is not thought to be instantiated directly, rather <see cref="Instaloader"/> instances maintain a context
    /// object.
    ///
    /// For logging, it provides <see cref="Log"/>, <see cref="Error"/>, <see cref="ErrorCatcher(string, Action)"/>.
    ///
    /// It provides low-level communication routines <see cref="GetJson"/>, <see cref="GraphqlQuery"/>,
    /// <see cref="GetAndWriteRaw"/> and implements mechanisms for rate controlling and error handling.
    ///
    /// Further, it provides methods for logging in and general session handles, which are used by that routines in
    /// class <see cref="Instaloader"/>.
    /// </summary>
    public class InstaloaderContext
    {
        /// <summary>
        /// Instagram rejects HTTP/1.1 requests to these hosts with "429 Too Many Requests", so they are served over
        /// HTTP/2. Media files are downloaded from the CDN, which is left on HTTP/1.1.
        /// </summary>
        public static readonly string[] InstagramHosts = Session.Http2Hosts;

        internal static readonly Random Random = new Random();

        /// <summary>Creates a session that talks to Instagram over HTTP/2.</summary>
        public static Session NewSession()
        {
            return new Session();
        }

        /// <summary>Duplicates a session.</summary>
        public static Session CopySession(Session session, double? requestTimeout = null)
        {
            Session copy = NewSession();
            copy.Cookies = CookieJar.FromDict(session.Cookies.GetDict());
            copy.Headers = session.Headers.Copy();
            copy.Timeout = requestTimeout;
            return copy;
        }

        /// <summary>Yield the data of each GraphQL query result in JSON that a page of www.instagram.com embeds.</summary>
        internal static IEnumerable<JObject> EmbeddedQueryData(JToken obj)
        {
            var dict = obj as JObject;
            if (dict != null)
            {
                JObject bbox = dict.Get("__bbox") as JObject;
                JObject result = bbox != null ? bbox.Get("result") as JObject : null;
                if (result != null && result.Get("data") is JObject)
                    yield return (JObject)result["data"];
                foreach (JToken value in dict.Values)
                    foreach (JObject data in EmbeddedQueryData(value))
                        yield return data;
            }
            else
            {
                var list = obj as JArray;
                if (list != null)
                    foreach (JToken value in list)
                        foreach (JObject data in EmbeddedQueryData(value))
                            yield return data;
            }
        }

        public static string DefaultUserAgent()
        {
            return "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 " +
                   "(KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";
        }

        public static HttpHeaders DefaultIphoneHeaders()
        {
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            long offsetSeconds = (long)offset.TotalSeconds;
            // timedelta.seconds is always in [0, 86400)
            long tzSeconds = ((offsetSeconds % 86400) + 86400) % 86400;
            int speed;
            lock (Random)
                speed = Random.Next(1000, 20001);
            return new HttpHeaders
            {
                { "User-Agent", "Instagram 361.0.0.35.82 (iPad13,8; iOS 18_0; en_US; en-US; " +
                                "scale=2.00; 2048x2732; 674117118) AppleWebKit/420+" },
                { "x-ads-opt-out", "1" },
                { "x-bloks-is-panorama-enabled", "true" },
                { "x-bloks-version-id", "16b7bd25c6c06886d57c4d455265669345a2d96625385b8ee30026ac2dc5ed97" },
                { "x-fb-client-ip", "True" },
                { "x-fb-connection-type", "wifi" },
                { "x-fb-http-engine", "Liger" },
                { "x-fb-server-cluster", "True" },
                { "x-fb", "1" },
                { "x-ig-abr-connection-speed-kbps", "2" },
                { "x-ig-app-id", "124024574287414" },
                { "x-ig-app-locale", "en-US" },
                { "x-ig-app-startup-country", "US" },
                { "x-ig-bandwidth-speed-kbps", "0.000" },
                { "x-ig-capabilities", "36r/F/8=" },
                { "x-ig-connection-speed", speed.ToString(CultureInfo.InvariantCulture) + "kbps" },
                { "x-ig-connection-type", "WiFi" },
                { "x-ig-device-locale", "en-US" },
                { "x-ig-mapped-locale", "en-US" },
                { "x-ig-timezone-offset", tzSeconds.ToString(CultureInfo.InvariantCulture) },
                { "x-ig-www-claim", "0" },
                { "x-pigeon-session-id", Guid.NewGuid().ToString() },
                { "x-tigon-is-retry", "False" },
                { "x-whatsapp", "0" }
            };
        }

        private Session _session;
        private readonly RateController _rateController;

        public InstaloaderContext(bool sleep = true, bool quiet = false, string userAgent = null,
                                  int maxConnectionAttempts = 3, double requestTimeout = 300.0,
                                  Func<InstaloaderContext, RateController> rateController = null,
                                  IList<int> fatalStatusCodes = null, bool iphoneSupport = true)
        {
            UserAgent = userAgent ?? DefaultUserAgent();
            RequestTimeout = requestTimeout;
            _session = GetAnonymousSession();
            Username = null;
            UserId = null;
            Sleep = sleep;
            Quiet = quiet;
            MaxConnectionAttempts = maxConnectionAttempts;
            GraphqlPageLength = 50;
            TwoFactorAuthPending = null;
            IphoneSupport = iphoneSupport;
            IphoneHeaders = DefaultIphoneHeaders();

            // error log, filled with Error() and printed at the end of Instaloader.Main()
            ErrorLog = new List<string>();

            _rateController = rateController != null ? rateController(this) : new RateController(this);

            // Can be set to true for testing, disables suppression of InstaloaderContext.ErrorCatcher
            RaiseAllErrors = false;

            // HTTP status codes that should cause an AbortDownloadException
            FatalStatusCodes = fatalStatusCodes != null ? new List<int>(fatalStatusCodes) : new List<int>();

            // Cache profile from id (mapping from id to Profile)
            ProfileIdCache = new Dictionary<long, Profile>();
        }

        public string UserAgent { get; set; }
        public double RequestTimeout { get; set; }

        /// <summary>The session used for requests to Instagram.</summary>
        public Session Session { get { return _session; } set { _session = value; } }

        public string Username { get; set; }

        /// <summary>
        /// The ID of the user this session is logged in as, from the session's ds_user_id cookie (as in Instaloader
        /// 4.16), or null if not logged in. It is therefore also known for sessions from browser cookies or session
        /// files, which Instaloader 4.15.5 sends as "None". Setting it has no effect; the setter is kept for
        /// compatibility.
        /// </summary>
        public JToken UserId
        {
            get
            {
                string id = _session == null ? null : _session.Cookies.Get("ds_user_id");
                return string.IsNullOrEmpty(id) ? null : (JToken)id;
            }
            set { }
        }

        public bool Sleep { get; set; }
        public bool Quiet { get; set; }
        public int MaxConnectionAttempts { get; set; }
        public int GraphqlPageLength { get; set; }

        /// <summary>(session, user, two_factor_identifier) of a pending two-factor login, or null.</summary>
        public Tuple<Session, string, string> TwoFactorAuthPending { get; set; }

        public bool IphoneSupport { get; set; }
        public HttpHeaders IphoneHeaders { get; set; }
        public List<string> ErrorLog { get; set; }
        public bool RaiseAllErrors { get; set; }
        public List<int> FatalStatusCodes { get; set; }
        public Dictionary<long, Profile> ProfileIdCache { get; private set; }

        public RateController RateController { get { return _rateController; } }

        private sealed class Restorer : IDisposable
        {
            private Action _restore;

            public Restorer(Action restore)
            {
                _restore = restore;
            }

            public void Dispose()
            {
                if (_restore != null)
                {
                    _restore();
                    _restore = null;
                }
            }
        }

        /// <summary>Temporarily makes this context anonymous; dispose the result to restore it.</summary>
        public IDisposable AnonymousCopy()
        {
            Session session = _session;
            string username = Username;
            JToken userId = UserId;
            HttpHeaders iphoneHeaders = IphoneHeaders;
            _session = GetAnonymousSession();
            Username = null;
            UserId = null;
            IphoneHeaders = DefaultIphoneHeaders();
            return new Restorer(() =>
            {
                _session.Close();
                Username = username;
                _session = session;
                UserId = userId;
                IphoneHeaders = iphoneHeaders;
            });
        }

        /// <summary>True, if this Instaloader instance is logged in.</summary>
        public bool IsLoggedIn { get { return !string.IsNullOrEmpty(Username); } }

        /// <summary>Log a message to stdout that can be suppressed with --quiet.</summary>
        public void Log(object msg = null, string end = "\n", bool flush = false)
        {
            if (!Quiet)
            {
                string text = msg == null ? "" : (msg is string ? (string)msg : PyStr.Str(msg));
                Console.Out.Write(text + end);
                if (flush)
                    Console.Out.Flush();
            }
        }

        /// <summary>Log a non-fatal error message to stderr, which is repeated at program termination.</summary>
        /// <param name="msg">Message to be printed.</param>
        /// <param name="repeatAtEnd">Set to false if the message should be printed, but not repeated at program
        /// termination.</param>
        public void Error(string msg, bool repeatAtEnd = true)
        {
            Console.Error.WriteLine(msg);
            if (repeatAtEnd)
                ErrorLog.Add(msg);
        }

        public override string ToString()
        {
            return string.Format("<InstaloaderContext is_logged_in={0}>", IsLoggedIn ? "True" : "False");
        }

        /// <summary>Returns whether any error has been reported and stored to be repeated at program termination.</summary>
        public bool HasStoredErrors { get { return ErrorLog.Count > 0; } }

        /// <summary>Print error log and close session.</summary>
        public void Close()
        {
            if (ErrorLog.Count > 0 && !Quiet)
            {
                Console.Error.WriteLine("\nErrors or warnings occurred:");
                foreach (string err in ErrorLog)
                    Console.Error.WriteLine(err);
            }
            _session.Close();
        }

        /// <summary>Runs the action, catching, printing and recording <see cref="InstaloaderException"/>s.</summary>
        /// <param name="extraInfo">String to prefix error message with.</param>
        /// <param name="action">The action.</param>
        public void ErrorCatcher(string extraInfo, Action action)
        {
            try
            {
                action();
            }
            catch (InstaloaderException err)
            {
                if (!string.IsNullOrEmpty(extraInfo))
                    Error(string.Format("{0}: {1}", extraInfo, err.Message));
                else
                    Error(err.Message);
                if (RaiseAllErrors)
                    throw;
            }
        }

        /// <summary>Runs the action, catching, printing and recording <see cref="InstaloaderException"/>s.</summary>
        public void ErrorCatcher(Action action)
        {
            ErrorCatcher(null, action);
        }

        /// <summary>Returns default HTTP header we use for requests.</summary>
        public HttpHeaders DefaultHttpHeader(bool emptySessionOnly = false)
        {
            var header = new HttpHeaders
            {
                { "Accept-Encoding", "gzip, deflate" },
                { "Accept-Language", "en-US,en;q=0.8" },
                { "Connection", "keep-alive" },
                { "Content-Length", "0" },
                { "Host", "www.instagram.com" },
                { "Origin", "https://www.instagram.com" },
                { "Referer", "https://www.instagram.com/" },
                { "User-Agent", UserAgent },
                { "X-Instagram-AJAX", "1" },
                { "X-Requested-With", "XMLHttpRequest" },
                { "x-ig-app-id", "936619743392459" }
            };
            if (emptySessionOnly)
            {
                header.Remove("Host");
                header.Remove("Origin");
                header.Remove("X-Instagram-AJAX");
                header.Remove("X-Requested-With");
            }
            return header;
        }

        private static IEnumerable<KeyValuePair<string, string>> Pairs(params string[] nameValues)
        {
            for (int i = 0; i + 1 < nameValues.Length; i += 2)
                yield return new KeyValuePair<string, string>(nameValues[i], nameValues[i + 1]);
        }

        /// <summary>Returns our default anonymous session object.</summary>
        public Session GetAnonymousSession()
        {
            Session session = NewSession();
            session.Cookies.Update(Pairs("sessionid", "", "mid", "", "ig_pr", "1", "ig_vw", "1920", "csrftoken", "",
                                         "s_network", "", "ds_user_id", ""));
            session.Headers.Update(DefaultHttpHeader(emptySessionOnly: true));
            session.Timeout = RequestTimeout;
            return session;
        }

        /// <summary>Not meant to be used directly, use <see cref="Instaloader.SaveSession"/>.</summary>
        public List<KeyValuePair<string, string>> SaveSession()
        {
            return _session.Cookies.GetDict();
        }

        public void UpdateCookies(IEnumerable<KeyValuePair<string, string>> cookie)
        {
            _session.Cookies.Update(cookie);
        }

        /// <summary>Not meant to be used directly, use <see cref="Instaloader.LoadSession"/>.</summary>
        public void LoadSession(string username, IEnumerable<KeyValuePair<string, string>> sessiondata)
        {
            Session session = NewSession();
            session.Cookies = CookieJar.FromDict(sessiondata);
            session.Headers.Update(DefaultHttpHeader());
            string csrf;
            if (!session.Cookies.ToDictionary().TryGetValue("csrftoken", out csrf))
                throw new PyKeyError("csrftoken");
            session.Headers["X-CSRFToken"] = csrf;
            session.Timeout = RequestTimeout;
            _session = session;
            Username = username;
        }

        /// <summary>Not meant to be used directly, use <see cref="Instaloader.SaveSessionToFile"/>.</summary>
        public void SaveSessionToFile(Stream sessionfile)
        {
            Pickle.DumpStringDict(SaveSession(), sessionfile);
        }

        /// <summary>Not meant to be used directly, use <see cref="Instaloader.LoadSessionFromFile"/>.</summary>
        public void LoadSessionFromFile(string username, Stream sessionfile)
        {
            LoadSession(username, Pickle.LoadStringDict(sessionfile));
        }

        /// <summary>Not meant to be used directly, use <see cref="Instaloader.TestLogin"/>.</summary>
        public string TestLogin()
        {
            // Instaloader's check below is a GraphQL query without the web app's request fields, which Instagram
            // redirects to its home page for some accounts, so that a valid session counts as not logged in. The
            // app's current_user endpoint names the logged-in user, with the token derived from the session cookies.
            if (IphoneSupport && !UserId.IsNone() && !string.IsNullOrEmpty(_session.Cookies.Get("sessionid")))
            {
                try
                {
                    // One attempt: if it fails, the check below reports the problem.
                    JObject current = GetIphoneJsonCore("api/v1/accounts/current_user/", new JObject { { "edit", "true" } },
                                                        false, MaxConnectionAttempts > 0 ? MaxConnectionAttempts : 1);
                    JToken currentUser = current.Get("user");
                    if (currentUser != null && !currentUser.Get("username").IsNone())
                        return (string)currentUser["username"];
                }
                catch (VerificationRequiredException err)
                {
                    // The session is valid, but cannot be used until the account is verified in a web browser.
                    throw new LoginException(err.Message);
                }
                catch (Exception err) when (err is AbortDownloadException || err is ConnectionException)
                {
                }
            }
            try
            {
                JObject data = GraphqlQuery("d6f4427fbe92d846298cf93df0b937d3", new JObject());
                JToken user = data["data"]["user"];
                return !user.IsNull ? (string)user["username"] : null;
            }
            catch (VerificationRequiredException err)
            {
                throw new LoginException(err.Message);
            }
            catch (Exception err) when (err is AbortDownloadException || err is ConnectionException)
            {
                Error(string.Format("Error when checking if logged in: {0}", err.Message));
                return null;
            }
        }

        /// <summary>Not meant to be used directly, use <see cref="Instaloader.Login"/>.</summary>
        /// <exception cref="BadCredentialsException">If the provided password is wrong.</exception>
        /// <exception cref="TwoFactorAuthRequiredException">First step of 2FA login done, now call
        /// <see cref="Instaloader.TwoFactorLogin"/>.</exception>
        /// <exception cref="LoginException">An error happened during login (for example, and invalid response).
        /// Or if the provided username does not exist.</exception>
        public void Login(string user, string passwd)
        {
            Session session = NewSession();
            session.Cookies.Update(Pairs("sessionid", "", "mid", "", "ig_pr", "1", "ig_vw", "1920", "ig_cb", "1",
                                         "csrftoken", "", "s_network", "", "ds_user_id", ""));
            session.Headers.Update(DefaultHttpHeader());
            session.Timeout = RequestTimeout;

            // Make a request to Instagram's root URL, which will set the session's csrftoken cookie
            // Not using GetJson() here, because we need to access the cookie
            session.Get("https://www.instagram.com/");
            // Add session's csrftoken cookie to session headers
            string csrfToken;
            if (!session.Cookies.ToDictionary().TryGetValue("csrftoken", out csrfToken))
                throw new PyKeyError("csrftoken");
            session.Headers["X-CSRFToken"] = csrfToken;

            DoSleep();
            // Workaround credits to pgrimaud.
            // See: https://github.com/pgrimaud/instagram-user-feed/commit/96ad4cf54d1ad331b337f325c73e664999a6d066
            long now = (long)Math.Floor(PyDateTime.Now().Timestamp());
            string encPassword = string.Format(CultureInfo.InvariantCulture, "#PWD_INSTAGRAM_BROWSER:0:{0}:{1}", now, passwd);
            Response login = session.Post("https://www.instagram.com/api/v1/web/accounts/login/ajax/",
                                          new JObject { { "enc_password", encPassword }, { "username", user } },
                                          allowRedirects: true);
            JObject respJson;
            try
            {
                respJson = login.Json().AsObject();
            }
            catch (JsonDecodeError err)
            {
                throw new LoginException(string.Format("Login error: JSON decode fail, {0} - {1}.", login.StatusCode, login.Reason), err);
            }
            if (respJson.Get("two_factor_required").Or(null) != null)
            {
                Session twoFactorSession = CopySession(session, RequestTimeout);
                twoFactorSession.Headers["X-CSRFToken"] = csrfToken;
                twoFactorSession.Cookies.Update(Pairs("csrftoken", csrfToken));
                TwoFactorAuthPending = Tuple.Create(twoFactorSession, user,
                                                    (string)respJson["two_factor_info"]["two_factor_identifier"]);
                throw new TwoFactorAuthRequiredException("Login error: two-factor authentication required.");
            }
            if (respJson.Get("checkpoint_url").Or(null) != null)
            {
                throw new LoginException(string.Format(
                    "Login: Checkpoint required. Point your browser to {0} - follow the instructions, then retry.",
                    PyStr.Str(respJson.Get("checkpoint_url"))));
            }
            if (!JToken.DeepEquals(respJson["status"], "ok"))
            {
                if (respJson.ContainsKey("message"))
                    throw new LoginException(string.Format("Login error: \"{0}\" status, message \"{1}\".",
                                                           PyStr.Str(respJson["status"]), PyStr.Str(respJson["message"])));
                throw new LoginException(string.Format("Login error: \"{0}\" status.", PyStr.Str(respJson["status"])));
            }
            if (!respJson.ContainsKey("authenticated"))
            {
                // Issue #472
                if (respJson.ContainsKey("message"))
                    throw new LoginException(string.Format("Login error: Unexpected response, \"{0}\".", PyStr.Str(respJson["message"])));
                throw new LoginException("Login error: Unexpected response, this might indicate a blocked IP.");
            }
            if (!respJson["authenticated"].Truthy)
            {
                if (respJson["user"].Truthy)
                {
                    // '{"authenticated": false, "user": true, "status": "ok"}'
                    throw new BadCredentialsException("Login error: Wrong password.");
                }
                // '{"authenticated": false, "user": false, "status": "ok"}'
                // Raise LoginException rather than BadCredentialException, because BadCredentialException
                // triggers re-asking of password in Instaloader.InteractiveLogin(), which makes no sense if the
                // username is invalid.
                throw new LoginException(string.Format("Login error: User {0} does not exist.", user));
            }
            // '{"authenticated": true, "user": true, "userId": ..., "oneTapPrompt": false, "status": "ok"}'
            session.Headers["X-CSRFToken"] = login.Cookies["csrftoken"];
            _session = session;
            Username = user;
            UserId = respJson["userId"];
        }

        /// <summary>Second step of login if 2FA is enabled.
        /// Not meant to be used directly, use <see cref="Instaloader.TwoFactorLogin"/>.</summary>
        /// <exception cref="InvalidArgumentException">No two-factor authentication pending.</exception>
        /// <exception cref="BadCredentialsException">2FA verification code invalid.</exception>
        public void TwoFactorLogin(string twoFactorCode)
        {
            if (TwoFactorAuthPending == null)
                throw new InvalidArgumentException("No two-factor authentication pending.");
            Session session = TwoFactorAuthPending.Item1;
            string user = TwoFactorAuthPending.Item2;
            string twoFactorId = TwoFactorAuthPending.Item3;

            Response login = session.Post("https://www.instagram.com/accounts/login/ajax/two_factor/",
                                          new JObject
                                          {
                                              { "username", user },
                                              { "verificationCode", twoFactorCode },
                                              { "identifier", twoFactorId }
                                          },
                                          allowRedirects: true);
            JObject respJson = login.Json().AsObject();
            if (!JToken.DeepEquals(respJson["status"], "ok"))
            {
                if (respJson.ContainsKey("message"))
                    throw new BadCredentialsException(string.Format("2FA error: {0}", PyStr.Str(respJson["message"])));
                throw new BadCredentialsException(string.Format("2FA error: \"{0}\" status.", PyStr.Str(respJson["status"])));
            }
            session.Headers["X-CSRFToken"] = login.Cookies["csrftoken"];
            _session = session;
            Username = user;
            TwoFactorAuthPending = null;
        }

        /// <summary>random.expovariate(lambd)</summary>
        internal static double ExpoVariate(double lambd)
        {
            double u;
            lock (Random)
                u = Random.NextDouble();
            return -Math.Log(1.0 - u) / lambd;
        }

        /// <summary>Sleep a short time if Sleep is set. Called before each request to instagram.com.</summary>
        public void DoSleep()
        {
            if (Sleep)
                Interrupt.Sleep(Math.Min(ExpoVariate(0.6), 15.0));
            else
                Interrupt.Check();
        }

        internal static string ResponseError(Response resp)
        {
            string extraFromJson = null;
            try
            {
                JToken respJson = resp.Json();
                if (respJson.ContainsKey("status"))
                {
                    extraFromJson = respJson.ContainsKey("message")
                        ? string.Format("\"{0}\" status, message \"{1}\"", PyStr.Str(respJson["status"]), PyStr.Str(respJson["message"]))
                        : string.Format("\"{0}\" status", PyStr.Str(respJson["status"]));
                }
            }
            catch (JsonDecodeError)
            {
            }
            catch (PyTypeError)
            {
            }
            return string.Format("{0} {1}{2} when accessing {3}", resp.StatusCode, resp.Reason,
                                 extraFromJson != null ? " - " + extraFromJson : "", resp.Url);
        }

        /// <summary>The number of redirects that GetJson() follows for one request.</summary>
        private const int MaxJsonRedirects = 10;

        /// <summary>What to do when Instagram asks to verify the account.</summary>
        private const string VerificationAdvice =
            "Open https://www.instagram.com/ in a web browser logged in with the account, complete the verification " +
            "there and run InstaloaderSharp again.";

        /// <summary>JSON request to Instagram.</summary>
        /// <param name="path">URL, relative to the given domain which defaults to www.instagram.com/</param>
        /// <param name="parameters">request parameters</param>
        /// <param name="host">Domain part of the URL from where to download the requested JSON; defaults to
        /// www.instagram.com</param>
        /// <param name="session">Session to use, or null to use this context's session</param>
        /// <param name="attempt">Internal retry counter.</param>
        /// <param name="responseHeaders">If given, filled with the response headers.</param>
        /// <param name="usePost">Use POST instead of GET to make the request</param>
        /// <returns>Decoded response dictionary</returns>
        /// <exception cref="AbortDownloadException">When the server responds with
        /// 'feedback_required'/'checkpoint_required'/'challenge_required'</exception>
        /// <exception cref="QueryReturnedBadRequestException">When the server responds with a 400 (and not
        /// 'feedback_required'/'checkpoint_required'/'challenge_required').</exception>
        /// <exception cref="QueryReturnedNotFoundException">When the server responds with a 404.</exception>
        /// <exception cref="ConnectionException">When query repeatedly failed.</exception>
        public JObject GetJson(string path, JObject parameters, string host = "www.instagram.com",
                               Session session = null, int attempt = 1, HttpHeaders responseHeaders = null,
                               bool usePost = false)
        {
            bool isGraphqlQuery = parameters.ContainsKey("query_hash") && path.Contains("graphql/query");
            bool isDocIdQuery = parameters.ContainsKey("doc_id") && (path.Contains("graphql/query") || path == "api/graphql");
            bool isIphoneQuery = host == "i.instagram.com";
            bool isOtherQuery = !isGraphqlQuery && !isDocIdQuery && host == "www.instagram.com";
            Session sess = session ?? _session;
            try
            {
                DoSleep();
                if (isGraphqlQuery)
                    _rateController.WaitBeforeQuery((string)parameters["query_hash"]);
                if (isDocIdQuery)
                    _rateController.WaitBeforeQuery((string)parameters["doc_id"]);
                if (isIphoneQuery)
                    _rateController.WaitBeforeQuery("iphone");
                if (isOtherQuery)
                    _rateController.WaitBeforeQuery("other");
                Response resp;
                string url = string.Format("https://{0}/{1}", host, path);
                if (usePost)
                    resp = sess.Post(url, parameters, allowRedirects: false);
                else
                    resp = sess.Get(url, parameters, allowRedirects: false);
                if (FatalStatusCodes.Contains(resp.StatusCode))
                {
                    string redirect = resp.Headers.ContainsKey("location")
                        ? string.Format(" redirect to {0}", resp.Headers["location"]) : "";
                    string body = "";
                    if ((resp.Headers.Get("Content-Type") ?? "").StartsWith("application/json"))
                    {
                        string text = resp.Text;
                        body = ": " + PyStr.Slice(text, null, 500) + (PyStr.Len(text) > 501 ? Chars.Ellipsis : "");
                    }
                    throw new AbortDownloadException(string.Format("Query to https://{0}/{1} responded with \"{2} {3}\"{4}{5}",
                                                                   host, path, resp.StatusCode, resp.Reason, redirect, body));
                }
                var redirectedTo = new HashSet<string>(StringComparer.Ordinal);
                while (resp.IsRedirect)
                {
                    string redirectUrl = resp.Headers["location"];
                    Log(string.Format("\nHTTP redirect from https://{0}/{1} to {2}", host, path, redirectUrl));
                    if (redirectUrl.StartsWith("https://www.instagram.com/accounts/login") ||
                        redirectUrl.StartsWith("https://i.instagram.com/accounts/login"))
                    {
                        if (!IsLoggedIn)
                            throw new LoginRequiredException("Redirected to login page. Use --login or --load-cookies.");
                        throw new AbortDownloadException("Redirected to login page. You've been logged out, please wait " +
                                                         "some time, recreate the session and try again");
                    }
                    string nextUrl = redirectUrl.EndsWith("/") ? redirectUrl : redirectUrl + "/";
                    // While the login is checked, the session cookies are set but no username yet. App requests send
                    // the token derived from them instead.
                    bool hasSession = IsLoggedIn || !string.IsNullOrEmpty(sess.Cookies.Get("sessionid")) ||
                                      sess.Headers.ContainsKey("authorization");
                    // Instagram redirects the queries of an account that it wants verified to its verification page.
                    // Instaloader retries them, and then reports that the session is not logged in.
                    if (hasSession && redirectUrl.Contains("/challenge"))
                        throw new VerificationRequiredException(string.Format(
                            "Instagram asks to verify the account: it redirected https://{0}/{1} to its verification " +
                            "page ({2}). {3}", host, path, redirectUrl, VerificationAdvice));
                    // For a suspended or restricted account, Instagram redirects every request to its home page, and the
                    // home page to itself, so following the redirects would never end (as in Instaloader). Stop at a
                    // redirect loop, at too many redirects, and at the pages of suspended accounts and challenges.
                    if (redirectUrl.Contains("/accounts/suspended") || redirectUrl.Contains("/challenge") ||
                        !redirectedTo.Add(nextUrl) || redirectedTo.Count > MaxJsonRedirects)
                    {
                        string message = string.Format("Instagram keeps redirecting https://{0}/{1} (to {2}).", host, path, redirectUrl);
                        if (!hasSession)
                            throw new ConnectionException(message);
                        throw new AbortDownloadException(message + " The account may be suspended, restricted or logged " +
                                                         "out; check it in a web browser.");
                    }
                    // Redirects are followed with GET. The parameters of a POST request are its body, which must
                    // not be sent again as query arguments.
                    if (redirectUrl.StartsWith(string.Format("https://{0}/", host)))
                        resp = sess.Get(nextUrl, usePost ? null : parameters, allowRedirects: false);
                    else
                        break;
                }
                if (responseHeaders != null)
                {
                    foreach (var kv in new List<KeyValuePair<string, string>>(responseHeaders))
                        responseHeaders.Remove(kv.Key);
                    foreach (var kv in resp.Headers.Merged())
                        responseHeaders[kv.Key] = kv.Value;
                }
                if (resp.StatusCode == 400)
                {
                    try
                    {
                        JToken json400 = resp.Json();
                        JToken message = json400.Get("message");
                        if (message != null && message.Type == JTokenType.String &&
                            ((string)message == "checkpoint_required" || (string)message == "challenge_required"))
                            throw new VerificationRequiredException(ResponseError(resp) + " " + VerificationAdvice);
                        if (message != null && message.Type == JTokenType.String && (string)message == "feedback_required")
                        {
                            // Raise AbortDownloadException in case of substantial Instagram
                            // requirements to stop producing more requests
                            throw new AbortDownloadException(ResponseError(resp));
                        }
                    }
                    catch (JsonDecodeError)
                    {
                    }
                    catch (PyAttributeError)
                    {
                    }
                    throw new QueryReturnedBadRequestException(ResponseError(resp));
                }
                if (resp.StatusCode == 404)
                    throw new QueryReturnedNotFoundException(ResponseError(resp));
                if (resp.StatusCode == 429)
                    throw new TooManyRequestsException(ResponseError(resp));
                if (resp.StatusCode != 200)
                    throw new ConnectionException(ResponseError(resp));
                // Instagram answers the queries of a flagged account with a verification page (a captcha) instead
                // of data, without redirecting them. Instaloader retries them and goes on with the next targets,
                // which get the same page, and which Instagram counts against the account; the verification only
                // goes away when it is completed in a web browser. So the download stops at the first such page.
                // Only the API endpoints are checked: pages queried with __a=1 (locations, hashtags) may be answered
                // with the page itself, now that Instagram no longer returns their data.
                if (IsLoggedIn && redirectedTo.Count == 0 && (path.StartsWith("graphql/") || path.StartsWith("api/")) &&
                    (resp.Headers.Get("Content-Type") ?? "").StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                    throw new VerificationRequiredException(string.Format(
                        "Instagram answered https://{0}/{1} with a web page instead of data, probably a verification " +
                        "(captcha). {2}", host, path, VerificationAdvice));
                JToken respJson = resp.Json();
                if (respJson.ContainsKey("status") && !JToken.DeepEquals(respJson["status"], "ok"))
                    throw new ConnectionException(ResponseError(resp));
                return respJson.AsObject();
            }
            catch (Exception err) when (err is ConnectionException || err is JsonDecodeError || err is RequestException)
            {
                string errorString = string.Format("JSON Query to {0}: {1}", path, err.Message);
                if (attempt == MaxConnectionAttempts)
                {
                    if (err is QueryReturnedNotFoundException)
                        throw new QueryReturnedNotFoundException(errorString, err);
                    throw new ConnectionException(errorString, err);
                }
                Error(errorString + " [retrying; skip with ^C]", repeatAtEnd: false);
                try
                {
                    if (err is TooManyRequestsException)
                    {
                        if (isGraphqlQuery)
                            _rateController.Handle429((string)parameters["query_hash"]);
                        if (isDocIdQuery)
                            _rateController.Handle429((string)parameters["doc_id"]);
                        if (isIphoneQuery)
                            _rateController.Handle429("iphone");
                        if (isOtherQuery)
                            _rateController.Handle429("other");
                    }
                    // Python's Instaloader drops use_post here, retrying POST queries with GET; keep the method.
                    return GetJson(path, parameters, host, sess, attempt + 1, responseHeaders, usePost);
                }
                catch (KeyboardInterruptException)
                {
                    Error("[skipped by user]", repeatAtEnd: false);
                    throw new ConnectionException(errorString, err);
                }
            }
        }

        private static void PrepareGraphqlSession(Session tmpsession, InstaloaderContext context, string referer)
        {
            tmpsession.Headers.Update(context.DefaultHttpHeader(emptySessionOnly: true));
            tmpsession.Headers.Remove("Connection");
            tmpsession.Headers.Remove("Content-Length");
            tmpsession.Headers["authority"] = "www.instagram.com";
            tmpsession.Headers["scheme"] = "https";
            tmpsession.Headers["accept"] = "*/*";
            if (referer != null)
                tmpsession.Headers["referer"] = UrlUtil.Quote(referer);
        }

        /// <summary>Do a GraphQL Query.</summary>
        /// <param name="queryHash">Query identifying hash.</param>
        /// <param name="variables">Variables for the Query.</param>
        /// <param name="referer">HTTP Referer, or null.</param>
        /// <returns>The server's response dictionary.</returns>
        public JObject GraphqlQuery(string queryHash, JObject variables, string referer = null)
        {
            JObject respJson;
            using (Session tmpsession = CopySession(_session, RequestTimeout))
            {
                PrepareGraphqlSession(tmpsession, this, referer);
                string variablesJson = PyJson.Dumps(variables, null, ",", ":");
                respJson = GetJson("graphql/query",
                                   new JObject { { "query_hash", queryHash }, { "variables", variablesJson } },
                                   session: tmpsession);
            }
            if (!respJson.ContainsKey("status"))
                Error("GraphQL response did not contain a \"status\" field.");
            return respJson;
        }

        private string FindCsrfToken()
        {
            foreach (Cookie c in _session.Cookies)
                if (c.Name == "csrftoken" && !string.IsNullOrEmpty(c.Value))
                    return c.Value;
            return null;
        }

        /// <summary>Do a doc_id-based GraphQL Query using method POST.</summary>
        /// <param name="docId">doc_id for the query.</param>
        /// <param name="variables">Variables for the Query.</param>
        /// <param name="referer">HTTP Referer, or null.</param>
        /// <param name="friendlyName">
        /// The web app's name of the query (fb_api_req_friendly_name), or null to look it up by <paramref name="docId"/>.
        /// </param>
        /// <param name="webApi">
        /// Query the web app's api/graphql endpoint, as the web app does, rather than graphql/query.
        /// </param>
        /// <returns>The server's response dictionary.</returns>
        public JObject DocIdGraphqlQuery(string docId, JObject variables, string referer = null, string friendlyName = null,
                                         bool webApi = false)
        {
            string csrf = FindCsrfToken();
            if (string.IsNullOrEmpty(csrf))
            {
                _session.Get("https://www.instagram.com/", timeout: RequestTimeout);
                csrf = FindCsrfToken() ?? "";
            }

            JObject respJson;
            using (Session tmpsession = CopySession(_session, RequestTimeout))
            {
                PrepareGraphqlSession(tmpsession, this, null);
                tmpsession.Headers["x-csrftoken"] = csrf;
                if (referer != null)
                    tmpsession.Headers["referer"] = UrlUtil.Quote(referer);
                string variablesJson = PyJson.Dumps(variables, null, ",", ":");
                var form = new JObject
                {
                    { "variables", variablesJson },
                    { "doc_id", docId },
                    { "server_timestamps", "true" }
                };
                if (webApi)
                    AddWebApiFields(tmpsession, form);
                else if (IsLoggedIn)
                    AddWebAppQueryFields(tmpsession, form, docId, friendlyName);
                try
                {
                    respJson = GetJson(webApi ? "api/graphql" : "graphql/query", form, session: tmpsession, usePost: true);
                }
                catch (ConnectionException)
                {
                    // The tokens may have expired; get new ones for the next query.
                    _webAppTokens = null;
                    throw;
                }
            }
            // Responses to queries with a friendly name have no "status" field.
            if (!respJson.ContainsKey("status") && !respJson.ContainsKey("data"))
                Error("GraphQL response did not contain a \"status\" field.");
            return respJson;
        }

        /// <summary>The web app's names of the doc_id queries (fb_api_req_friendly_name).</summary>
        private static readonly Dictionary<string, string> DocIdFriendlyNames = new Dictionary<string, string>
        {
            { "28975909992013618", "PolarisProfilePostsTabContentQuery_connection" },
            { "29104886249145907", "PolarisProfilePostsTabContentQuery_connection" },
            { "28728438306809751", "PolarisProfileReelsTabContentQuery_connection" },
            { "29133849526209643", "PolarisProfileTaggedTabContentQuery_connection" },
            { "27937681195819736", "PolarisProfilePageContentQuery" },
            { "27128499623469141", "PolarisPostRootQuery" }
        };

        /// <summary>The tokens of the web app's queries, as embedded in its pages, and their names in the queries.</summary>
        private static readonly KeyValuePair<string, Regex>[] WebAppTokenPatterns =
        {
            new KeyValuePair<string, Regex>("fb_dtsg", new Regex(@"""DTSGInitialData"",\[\],\{[^{}]*""token"":""([^""]+)""")),
            new KeyValuePair<string, Regex>("lsd", new Regex(@"""LSD"",\[\],\{[^{}]*""token"":""([^""]+)""")),
            new KeyValuePair<string, Regex>("av", new Regex(@"""IG_USER_EIMU"":""?(\d+)"))
        };

        private Dictionary<string, string> _webAppTokens;
        private Session _webAppTokensSession;

        /// <summary>
        /// Makes a logged-in doc_id query look like the web app's: with the session's request token (fb_dtsg), the
        /// query's friendly name and the Sec-Fetch-* headers of a script request. Since October 2026, Instagram
        /// answers logged-in queries without them with a redirect to its home page, for some sessions
        /// (Instaloader 4.15.4: "HTTP redirect from https://www.instagram.com/graphql/query to
        /// https://www.instagram.com/"). Instagram requires all three, but does not check the friendly name.
        /// </summary>
        private void AddWebAppQueryFields(Session tmpsession, JObject form, string docId, string friendlyName)
        {
            string name;
            if (friendlyName == null && DocIdFriendlyNames.TryGetValue(docId, out name))
                friendlyName = name;
            string fbDtsg;
            if (GetWebAppTokens().TryGetValue("fb_dtsg", out fbDtsg))
                form["fb_dtsg"] = fbDtsg;
            if (friendlyName != null)
            {
                form["fb_api_caller_class"] = "RelayModern";
                form["fb_api_req_friendly_name"] = friendlyName;
                tmpsession.Headers["X-FB-Friendly-Name"] = friendlyName;
            }
            tmpsession.Headers["Origin"] = "https://www.instagram.com";
            tmpsession.Headers["Sec-Fetch-Dest"] = "empty";
            tmpsession.Headers["Sec-Fetch-Mode"] = "cors";
            tmpsession.Headers["Sec-Fetch-Site"] = "same-origin";
        }

        /// <summary>
        /// Sends a query to the web app's api/graphql endpoint as the web app does (as Instaloader 4.16 does): with the
        /// tokens of its pages (fb_dtsg, lsd and the account ID av), lsd also as X-FB-LSD header, and the Sec-Fetch-*
        /// headers of a script request.
        /// </summary>
        private void AddWebApiFields(Session tmpsession, JObject form)
        {
            Dictionary<string, string> tokens = GetWebAppTokens();
            foreach (KeyValuePair<string, Regex> pattern in WebAppTokenPatterns)
            {
                string value;
                if (tokens.TryGetValue(pattern.Key, out value))
                    form[pattern.Key] = value;
                // Logged out, the start page has no fb_dtsg, and the web app sends its queries without.
                else if (pattern.Key != "fb_dtsg" || IsLoggedIn)
                    throw new BadResponseException(string.Format("The start page did not contain the token {0}.", pattern.Key));
            }
            tmpsession.Headers["X-FB-LSD"] = tokens["lsd"];
            tmpsession.Headers["Sec-Fetch-Dest"] = "empty";
            tmpsession.Headers["Sec-Fetch-Mode"] = "cors";
            tmpsession.Headers["Sec-Fetch-Site"] = "same-origin";
        }

        /// <summary>
        /// The tokens of the web app's queries (fb_dtsg, lsd, av) for the session, from Instagram's home page, loaded
        /// once per session. Tokens that the page does not contain are missing.
        /// </summary>
        private Dictionary<string, string> GetWebAppTokens()
        {
            if (_webAppTokens != null && ReferenceEquals(_webAppTokensSession, _session))
                return _webAppTokens;
            string html;
            try
            {
                html = GetPageText("", true);
            }
            catch (ConnectionException err)
            {
                Error(string.Format("Unable to get the request tokens from Instagram's home page: {0}", err.Message),
                      repeatAtEnd: false);
                return new Dictionary<string, string>();
            }
            var tokens = new Dictionary<string, string>();
            foreach (KeyValuePair<string, Regex> pattern in WebAppTokenPatterns)
            {
                Match match = pattern.Value.Match(html);
                if (match.Success)
                    tokens[pattern.Key] = match.Groups[1].Value;
            }
            _webAppTokens = tokens;
            _webAppTokensSession = _session;
            return tokens;
        }

        /// <summary>Retrieve a list of GraphQL nodes.</summary>
        [Obsolete("Use NodeIterator instead, which provides more functionality.")]
        public IEnumerable<JObject> GraphqlNodeList(string queryHash, JObject queryVariables, string queryReferer,
                                                    Func<JObject, JObject> edgeExtractor, string rhxGis = null,
                                                    JObject firstData = null)
        {
            Func<JObject> query = null;
            query = () =>
            {
                queryVariables["first"] = GraphqlPageLength;
                try
                {
                    return edgeExtractor(GraphqlQuery(queryHash, queryVariables, queryReferer));
                }
                catch (QueryReturnedBadRequestException)
                {
                    int newPageLength = GraphqlPageLength / 2;
                    if (newPageLength >= 12)
                    {
                        GraphqlPageLength = newPageLength;
                        Error("HTTP Error 400 (Bad Request) on GraphQL Query. Retrying with shorter page length.",
                              repeatAtEnd: false);
                        return query();
                    }
                    throw;
                }
            };
            JObject data = firstData != null && firstData.Truthy ? firstData : query();
            foreach (JToken edge in data["edges"].AsArray())
                yield return (JObject)edge["node"];
            while (data["page_info"]["has_next_page"].Truthy)
            {
                queryVariables["after"] = data["page_info"]["end_cursor"];
                data = query();
                foreach (JToken edge in data["edges"].AsArray())
                    yield return (JObject)edge["node"];
            }
        }

        /// <summary>JSON request to i.instagram.com.</summary>
        /// <param name="path">URL, relative to i.instagram.com/</param>
        /// <param name="parameters">GET parameters</param>
        /// <returns>Decoded response dictionary</returns>
        /// <param name="usePost">Send the parameters as form data with POST instead of GET.</param>
        public JObject GetIphoneJson(string path, JObject parameters, bool usePost = false)
        {
            return GetIphoneJsonCore(path, parameters, usePost);
        }

        private JObject GetIphoneJsonCore(string path, JObject parameters, bool usePost, int attempt = 1)
        {
            using (Session tempsession = CopySession(_session, RequestTimeout))
            {
                // Set headers to simulate an API request from iPad
                // Omitted rather than sent as "None" when the user is not known (as in Instaloader 4.16).
                if (!UserId.IsNone())
                    tempsession.Headers["ig-intended-user-id"] = PyStr.Str(UserId);
                tempsession.Headers["x-pigeon-rawclienttime"] =
                    PyDateTime.Now().Timestamp().ToString("F6", CultureInfo.InvariantCulture);

                // Add headers obtained from previous iPad request
                tempsession.Headers.Update(IphoneHeaders);

                // Most endpoints of the app interface (story tray, reels media, user info) require the app's bearer
                // token. Instagram hands one out (ig-set-authorization) only in answers of the few endpoints that
                // still accept requests without it, and refuses some of those for some accounts. The token is derived
                // from the session cookies, so construct it if none is known yet (as Instaloader 4.16 does).
                if (!tempsession.Headers.ContainsKey("authorization") && !UserId.IsNone())
                {
                    string sessionid = tempsession.Cookies.Get("sessionid");
                    if (!string.IsNullOrEmpty(sessionid))
                    {
                        string token = PyJson.Dumps(new JObject { { "ds_user_id", PyStr.Str(UserId) }, { "sessionid", sessionid } },
                                                    null, ",", ":");
                        tempsession.Headers["authorization"] = "Bearer IGT:2:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
                    }
                }

                // Extract key information from cookies if we haven't got it already from a previous request
                var headerCookiesMapping = new[]
                {
                    new KeyValuePair<string, string>("x-mid", "mid"),
                    new KeyValuePair<string, string>("ig-u-ds-user-id", "ds_user_id"),
                    new KeyValuePair<string, string>("x-ig-device-id", "ig_did"),
                    new KeyValuePair<string, string>("x-ig-family-device-id", "ig_did"),
                    new KeyValuePair<string, string>("family_device_id", "ig_did")
                };

                // Map the cookie value to the matching HTTP request header
                Dictionary<string, string> cookies = tempsession.Cookies.ToDictionary();
                foreach (var kv in headerCookiesMapping)
                {
                    if (cookies.ContainsKey(kv.Value))
                    {
                        if (!tempsession.Headers.ContainsKey(kv.Key))
                            tempsession.Headers[kv.Key] = cookies[kv.Value];
                        else
                            // Remove the cookie value if it's already specified as a header
                            tempsession.Cookies.Remove(kv.Value);
                    }
                }

                // Edge case for ig-u-rur header due to special string encoding in cookie
                if (cookies.ContainsKey("rur"))
                {
                    if (!tempsession.Headers.ContainsKey("ig-u-rur"))
                        tempsession.Headers["ig-u-rur"] = PyStr.DecodeEscapes(cookies["rur"].Trim('"'));
                    else
                        tempsession.Cookies.Remove("rur");
                }

                // Remove headers specific to Desktop version
                foreach (string header in new[] { "Host", "Origin", "X-Instagram-AJAX", "X-Requested-With", "Referer" })
                    tempsession.Headers.Remove(header);

                // No need for cookies if we have a bearer token
                if (tempsession.Headers.ContainsKey("authorization"))
                    tempsession.Cookies.Clear();

                var responseHeaders = new HttpHeaders();
                JObject response = GetJson(path, parameters, "i.instagram.com", tempsession, attempt, responseHeaders, usePost);

                // Extract the ig-set-* headers and use them in the next request
                foreach (var kv in responseHeaders)
                {
                    string key = kv.Key.ToLowerInvariant();
                    if (key.StartsWith("ig-set-"))
                        IphoneHeaders[key.Replace("ig-set-", "")] = kv.Value;
                    else if (key.StartsWith("x-ig-set-"))
                        IphoneHeaders[key.Replace("x-ig-set-", "x-ig-")] = kv.Value;
                }

                return response;
            }
        }

        private static readonly Regex ScriptJsonRegex =
            new Regex("<script type=\"application/json\"[^>]*>(.*?)</script>", RegexOptions.Singleline);

        /// <summary>
        /// Load a page of www.instagram.com as a logged-out visitor and return the data it embeds.
        ///
        /// Instagram renders public profile and post pages with the results of their GraphQL queries
        /// embedded in the HTML, even when it refuses anonymous API requests for the same data.
        /// </summary>
        /// <param name="path">URL, relative to www.instagram.com/</param>
        /// <returns>The data object of each query result that the page embeds, in page order.</returns>
        /// <exception cref="QueryReturnedNotFoundException">When the server responds with a 404.</exception>
        /// <exception cref="ConnectionException">When the request failed.</exception>
        public List<JObject> GetPageData(string path)
        {
            var data = new List<JObject>();
            foreach (JToken script in GetPageScripts(path, loggedIn: false))
                data.AddRange(EmbeddedQueryData(script));
            return data;
        }

        /// <summary>
        /// Load a page of www.instagram.com and return the JSON documents that it embeds in its
        /// &lt;script type="application/json"&gt; elements.
        /// </summary>
        /// <param name="path">URL, relative to www.instagram.com/</param>
        /// <param name="loggedIn">Use this context's session rather than a logged-out one.</param>
        /// <exception cref="QueryReturnedNotFoundException">When the server responds with a 404.</exception>
        /// <exception cref="ConnectionException">When the request failed.</exception>
        public List<JToken> GetPageScripts(string path, bool loggedIn)
        {
            var scripts = new List<JToken>();
            foreach (Match script in ScriptJsonRegex.Matches(GetPageText(path, loggedIn)))
            {
                try
                {
                    scripts.Add(PyJson.Loads(script.Groups[1].Value));
                }
                catch (JsonDecodeError)
                {
                }
            }
            return scripts;
        }

        private static readonly Regex ProfileIdRegex =
            new Regex(@"""page_logging"":\{""name"":""profilePage"",""params"":\{[^}]*?""profile_id"":""(\d+)""");

        /// <summary>
        /// The ID of the profile that a profile page names in its logging parameters, with the page loaded with this
        /// context's session, or null if the page names none.
        /// </summary>
        /// <param name="path">Path of the profile page, e.g. "instagram/".</param>
        public long? GetProfileIdFromPage(string path)
        {
            string html;
            try
            {
                html = GetPageText(path, true);
            }
            catch (QueryReturnedNotFoundException)
            {
                return null;
            }
            Match match = ProfileIdRegex.Match(html);
            long profileId;
            if (match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out profileId))
                return profileId;
            return null;
        }

        /// <summary>Loads a page of www.instagram.com as a browser's page navigation and returns its HTML.</summary>
        /// <param name="path">Path of the page, e.g. "" for the home page.</param>
        /// <param name="loggedIn">Whether to load the page with the logged-in session or as a logged-out visitor.</param>
        private string GetPageText(string path, bool loggedIn)
        {
            DoSleep();
            _rateController.WaitBeforeQuery("other");
            string url = string.Format("https://www.instagram.com/{0}", path);
            // Instagram only embeds the data when the request looks like a browser's page navigation.
            var headers = new HttpHeaders
            {
                { "Accept", "text/html" },
                { "Sec-Fetch-Dest", "document" },
                { "Sec-Fetch-Mode", "navigate" },
                { "Sec-Fetch-Site", "none" }
            };
            Response resp;
            try
            {
                if (loggedIn)
                {
                    resp = _session.Get(url, headers: headers, allowRedirects: false);
                }
                else
                {
                    using (Session anonymousSession = GetAnonymousSession())
                        resp = anonymousSession.Get(url, headers: headers, allowRedirects: false);
                }
            }
            catch (RequestException err)
            {
                throw new ConnectionException(string.Format("Page request to {0}: {1}", url, err.Message), err);
            }
            // Report a redirect to the login page as such, rather than as "302 Found" (as Instaloader 4.16 does).
            string location = resp.IsRedirect ? resp.Headers.Get("location") ?? "" : "";
            if (location.StartsWith("https://www.instagram.com/accounts/login") ||
                location.StartsWith("https://i.instagram.com/accounts/login"))
            {
                if (!loggedIn || !IsLoggedIn)
                    throw new LoginRequiredException("Redirected to login page. Use --login or --load-cookies.");
                throw new AbortDownloadException("Redirected to login page. You've been logged out, please wait " +
                                                 "some time, recreate the session and try again");
            }
            if (resp.StatusCode == 404)
                throw new QueryReturnedNotFoundException(ResponseError(resp));
            if (resp.StatusCode != 200)
                throw new ConnectionException(ResponseError(resp));
            return resp.Text;
        }

        /// <summary>Write raw response data into a file.</summary>
        public void WriteRaw(Response resp, string filename)
        {
            Log(filename, end: " ", flush: true);
            string temp = filename + ".temp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (Stream body = resp.GetBodyStream())
            {
                var buffer = new byte[64 * 1024];
                int n;
                while ((n = body.Read(buffer, 0, buffer.Length)) > 0)
                {
                    file.Write(buffer, 0, n);
                    Interrupt.Check();
                }
            }
            resp.Close();
            PyOs.Replace(temp, filename);
        }

        /// <summary>Write raw data into a file.</summary>
        public void WriteRaw(byte[] data, string filename)
        {
            Log(filename, end: " ", flush: true);
            string temp = filename + ".temp";
            File.WriteAllBytes(temp, data);
            PyOs.Replace(temp, filename);
        }

        /// <summary>Downloads a file anonymously.</summary>
        /// <exception cref="QueryReturnedNotFoundException">When the server responds with a 404.</exception>
        /// <exception cref="QueryReturnedForbiddenException">When the server responds with a 403.</exception>
        /// <exception cref="ConnectionException">When download failed.</exception>
        public Response GetRaw(string url, int attempt = 1)
        {
            Response resp;
            using (Session anonymousSession = GetAnonymousSession())
                resp = anonymousSession.Get(url, stream: true);
            if (resp.StatusCode == 200)
                return resp;
            if (resp.StatusCode == 403)
                // suspected invalid URL signature
                throw new QueryReturnedForbiddenException(ResponseError(resp));
            if (resp.StatusCode == 404)
                // 404 not worth retrying.
                throw new QueryReturnedNotFoundException(ResponseError(resp));
            throw new ConnectionException(ResponseError(resp));
        }

        /// <summary>Downloads and writes anonymously-requested raw data into a file.</summary>
        public void GetAndWriteRaw(string url, string filename)
        {
            WriteRaw(GetRaw(url), filename);
        }

        /// <summary>HEAD a URL anonymously.</summary>
        /// <exception cref="QueryReturnedNotFoundException">When the server responds with a 404.</exception>
        /// <exception cref="QueryReturnedForbiddenException">When the server responds with a 403.</exception>
        /// <exception cref="ConnectionException">When request failed.</exception>
        public Response Head(string url, bool allowRedirects = false)
        {
            Response resp;
            using (Session anonymousSession = GetAnonymousSession())
                resp = anonymousSession.Head(url, allowRedirects);
            if (resp.StatusCode == 200)
                return resp;
            if (resp.StatusCode == 403)
                throw new QueryReturnedForbiddenException(ResponseError(resp));
            if (resp.StatusCode == 404)
                throw new QueryReturnedNotFoundException(ResponseError(resp));
            throw new ConnectionException(ResponseError(resp));
        }
    }

    /// <summary>
    /// Class providing request tracking and rate controlling to stay within rate limits.
    ///
    /// It can be overridden to change Instaloader's behavior regarding rate limits, for example to raise a custom
    /// exception when the rate limit is hit:
    /// <code>
    /// class MyRateController : RateController
    /// {
    ///     public MyRateController(InstaloaderContext context) : base(context) { }
    ///     public override void Sleep(double secs) { throw new MyCustomException(); }
    /// }
    ///
    /// var L = new Instaloader(rateController: ctx => new MyRateController(ctx));
    /// </code>
    /// </summary>
    public class RateController
    {
        private static readonly Stopwatch Monotonic = Stopwatch.StartNew();

        /// <summary>Python's time.monotonic().</summary>
        protected static double MonotonicTime()
        {
            return Monotonic.Elapsed.TotalSeconds + 100000.0;
        }

        protected readonly InstaloaderContext Context;
        private readonly Dictionary<string, List<double>> _queryTimestamps = new Dictionary<string, List<double>>();
        private readonly List<string> _queryTypeOrder = new List<string>();
        private double _earliestNextRequestTime;
        private double _iphoneEarliestNextRequestTime;

        public RateController(InstaloaderContext context)
        {
            Context = context;
            _earliestNextRequestTime = 0.0;
            _iphoneEarliestNextRequestTime = 0.0;
        }

        /// <summary>Wait given number of seconds.</summary>
        public virtual void Sleep(double secs)
        {
            // Not static, to allow for the behavior of this method to depend on context-inherent properties, such as
            // whether we are logged in.
            Interrupt.Sleep(secs);
        }

        private void SetTimestamps(string queryType, List<double> times)
        {
            if (!_queryTimestamps.ContainsKey(queryType))
                _queryTypeOrder.Add(queryType);
            _queryTimestamps[queryType] = times;
        }

        private void DumpQueryTimestamps(double currentTime, string failedQueryType)
        {
            int[] windows = { 10, 11, 20, 22, 30, 60 };
            Context.Error(string.Format("Number of requests within last {0} minutes grouped by type:",
                                        string.Join("/", windows)),
                          repeatAtEnd: false);
            foreach (string queryType in _queryTypeOrder)
            {
                List<double> times = _queryTimestamps[queryType];
                var counts = new List<string>();
                foreach (int w in windows)
                {
                    int count = 0;
                    foreach (double t in times)
                        if (t > currentTime - w * 60)
                            count++;
                    counts.Add(count.ToString(CultureInfo.InvariantCulture).PadLeft(4));
                }
                Context.Error(string.Format(" {0} {1}: {2}", queryType == failedQueryType ? "*" : " ",
                                            queryType.PadLeft(32), string.Join(" ", counts)),
                              repeatAtEnd: false);
            }
        }

        /// <summary>
        /// Return how many requests of the given type can be done within a sliding window of 11 minutes.
        ///
        /// This is called by <see cref="QueryWaittime"/> and allows to simply customize wait times before queries
        /// at query_type granularity. Consider overriding <see cref="QueryWaittime"/> directly if you need more
        /// control.
        ///
        /// Logged in, half of Instaloader's 200 GraphQL queries per type are allowed: Instagram limits the requests
        /// of an account, and has flagged accounts (captcha, then feedback_required) that stayed within
        /// Instaloader's limits.
        /// </summary>
        public virtual int CountPerSlidingWindow(string queryType)
        {
            if (queryType == "other")
                return 75;
            return Context.IsLoggedIn ? 100 : 200;
        }

        /// <summary>
        /// Return how many GraphQL queries of all types together can be done within a sliding window of 10
        /// minutes: Instaloader's 275, or 150 when logged in (see <see cref="CountPerSlidingWindow"/>).
        /// </summary>
        public virtual int GraphqlCountPerSlidingWindow()
        {
            return Context.IsLoggedIn ? 150 : 275;
        }

        /// <summary>
        /// Return how many requests to Instagram's app interface can be done within a sliding window of 30
        /// minutes: Instaloader's 199, or 100 when logged in (see <see cref="CountPerSlidingWindow"/>).
        /// </summary>
        public virtual int IphoneCountPerSlidingWindow()
        {
            return Context.IsLoggedIn ? 100 : 199;
        }

        private List<double> ReqsInSlidingWindow(string queryType, double currentTime, double window)
        {
            var relevant = new List<double>();
            if (queryType != null)
            {
                // timestamps of type query_type
                relevant.AddRange(_queryTimestamps[queryType]);
            }
            else
            {
                // all GraphQL queries, i.e. not 'iphone' or 'other'
                foreach (string type in _queryTypeOrder)
                    if (type != "iphone" && type != "other")
                        relevant.AddRange(_queryTimestamps[type]);
            }
            return relevant.FindAll(t => t > currentTime - window);
        }

        private static double Min(List<double> values)
        {
            if (values.Count == 0)
                throw new PyValueError("min() iterable argument is empty");
            double m = values[0];
            foreach (double v in values)
                if (v < m)
                    m = v;
            return m;
        }

        /// <summary>Calculate time needed to wait before query can be executed.</summary>
        public virtual double QueryWaittime(string queryType, double currentTime, bool untrackedQueries = false)
        {
            const double perTypeSlidingWindow = 660;
            const double iphoneSlidingWindow = 1800;
            if (!_queryTimestamps.ContainsKey(queryType))
                SetTimestamps(queryType, new List<double>());
            SetTimestamps(queryType, _queryTimestamps[queryType].FindAll(t => t > currentTime - 60 * 60));

            Func<double> perTypeNextRequestTime = () =>
            {
                List<double> reqs = ReqsInSlidingWindow(queryType, currentTime, perTypeSlidingWindow);
                if (reqs.Count < CountPerSlidingWindow(queryType))
                    return 0.0;
                return Min(reqs) + perTypeSlidingWindow + 6;
            };

            Func<double> gqlAccumulatedNextRequestTime = () =>
            {
                if (queryType == "iphone" || queryType == "other")
                    return 0.0;
                const double gqlAccumulatedSlidingWindow = 600;
                int gqlAccumulatedMaxCount = GraphqlCountPerSlidingWindow();
                List<double> reqs = ReqsInSlidingWindow(null, currentTime, gqlAccumulatedSlidingWindow);
                if (reqs.Count < gqlAccumulatedMaxCount)
                    return 0.0;
                return Min(reqs) + gqlAccumulatedSlidingWindow;
            };

            Func<double> untrackedNextRequestTime = () =>
            {
                if (untrackedQueries)
                {
                    if (queryType == "iphone")
                    {
                        List<double> reqs = ReqsInSlidingWindow(queryType, currentTime, iphoneSlidingWindow);
                        _iphoneEarliestNextRequestTime = Min(reqs) + iphoneSlidingWindow + 18;
                    }
                    else
                    {
                        List<double> reqs = ReqsInSlidingWindow(queryType, currentTime, perTypeSlidingWindow);
                        _earliestNextRequestTime = Min(reqs) + perTypeSlidingWindow + 6;
                    }
                }
                return Math.Max(_iphoneEarliestNextRequestTime, _earliestNextRequestTime);
            };

            Func<double> iphoneNextRequest = () =>
            {
                if (queryType == "iphone")
                {
                    List<double> reqs = ReqsInSlidingWindow(queryType, currentTime, iphoneSlidingWindow);
                    if (reqs.Count >= IphoneCountPerSlidingWindow())
                        return Min(reqs) + iphoneSlidingWindow + 18;
                }
                return 0.0;
            };

            return Math.Max(0.0,
                            Math.Max(Math.Max(perTypeNextRequestTime(), gqlAccumulatedNextRequestTime()),
                                     Math.Max(untrackedNextRequestTime(), iphoneNextRequest())) - currentTime);
        }

        private static string FormatWaittime(double waittime)
        {
            return waittime <= 666
                ? string.Format("{0} seconds", Math.Round(waittime, MidpointRounding.ToEven))
                : string.Format("{0} minutes", Math.Round(waittime / 60, MidpointRounding.ToEven));
        }

        /// <summary>
        /// This method is called before a query to Instagram.
        ///
        /// It calls <see cref="QueryWaittime"/> to determine the time needed to wait and then calls
        /// <see cref="Sleep"/> to wait until the request can be made.
        /// </summary>
        public virtual void WaitBeforeQuery(string queryType)
        {
            double waittime = QueryWaittime(queryType, MonotonicTime(), false);
            Debug.Assert(waittime >= 0);
            if (waittime > 15)
            {
                Context.Log(string.Format("\nToo many queries in the last time. Need to wait {0}, until {1}.",
                                          FormatWaittime(waittime),
                                          DateTime.Now.AddSeconds(waittime).ToString("HH:mm", CultureInfo.InvariantCulture)));
            }
            if (waittime > 0)
                Sleep(waittime);
            if (!_queryTimestamps.ContainsKey(queryType))
                SetTimestamps(queryType, new List<double> { MonotonicTime() });
            else
                _queryTimestamps[queryType].Add(MonotonicTime());
        }

        /// <summary>
        /// This method is called to handle a 429 Too Many Requests response.
        ///
        /// It calls <see cref="QueryWaittime"/> to determine the time needed to wait and then calls
        /// <see cref="Sleep"/> to wait until we can repeat the same request.
        /// </summary>
        public virtual void Handle429(string queryType)
        {
            double currentTime = MonotonicTime();
            double waittime = QueryWaittime(queryType, currentTime, true);
            Debug.Assert(waittime >= 0);
            DumpQueryTimestamps(currentTime, queryType);
            const string textFor429 = "Instagram responded with HTTP error \"429 - Too Many Requests\". Please do not run multiple " +
                                      "instances of Instaloader in parallel or within short sequence. Also, do not use any Instagram " +
                                      "App while Instaloader is running.";
            Context.Error(TextWrap.Fill(textFor429), repeatAtEnd: false);
            if (waittime > 1.5)
            {
                Context.Error(string.Format("The request will be retried in {0}, at {1}.",
                                            FormatWaittime(waittime),
                                            DateTime.Now.AddSeconds(waittime).ToString("HH:mm", CultureInfo.InvariantCulture)),
                              repeatAtEnd: false);
            }
            if (waittime > 0)
                Sleep(waittime);
        }
    }
}
