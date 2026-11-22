using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using InstaloaderSharp.Http;
using InstaloaderSharp.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    /// <summary>
    /// Posts that Instagram lists without their pictures, and highlights listed without a cover or items, answered
    /// by a local server in place of Instagram (no internet access needed).
    /// </summary>
    [TestClass]
    public class PostDetailsFallbackTests
    {
        private static HttpListener _listener;
        private static string _base;
        private static readonly List<string> Requests = new List<string>();

        /// <summary>Answers a request, given its path (host first, as in "/www.instagram.com/graphql/query").</summary>
        private static Func<string, string> _answer;

        [ClassInitialize]
        public static void StartServer(TestContext context)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _base = "http://127.0.0.1:" + port + "/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(_base);
            _listener.Start();
            new Thread(Serve) { IsBackground = true }.Start();
        }

        [ClassCleanup]
        public static void StopServer()
        {
            _listener.Close();
        }

        private static void Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (Exception)
                {
                    return;
                }
                string path = ctx.Request.Url.AbsolutePath;
                lock (Requests)
                    Requests.Add(path);
                string body = _answer(path);
                ctx.Response.ContentType = body.StartsWith("<") ? "text/html" : "application/json";
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
        }

        [TestInitialize]
        public void SendToLocalServer()
        {
            lock (Requests)
                Requests.Clear();
            Session.SendToForTests = url => _base + url.Substring("https://".Length);
        }

        [TestCleanup]
        public void SendToInstagram()
        {
            Session.SendToForTests = null;
        }

        private static InstaloaderContext LoggedInContext()
        {
            var context = new InstaloaderContext(sleep: false, quiet: true, maxConnectionAttempts: 1);
            context.UpdateCookies(new[]
            {
                new KeyValuePair<string, string>("sessionid", "1234%3Aabc%3A1"),
                new KeyValuePair<string, string>("ds_user_id", "1234"),
                new KeyValuePair<string, string>("csrftoken", "token")
            });
            context.Username = "someuser";
            return context;
        }

        /// <summary>A carousel of two pictures, with or without their image versions.</summary>
        private static string Carousel(bool withPictures)
        {
            Func<string, string> versions = name => withPictures
                ? "{\"candidates\":[{\"url\":\"https://x/" + name + ".jpg?stp=1&se=7\",\"width\":1440,\"height\":1800}]}"
                : "{\"candidates\":[]}";
            return "{\"pk\":\"1825668768391764808\",\"id\":\"1825668768391764808_25025320\",\"code\":\"BlWFBJxhBtI\"," +
                   "\"media_type\":8,\"taken_at\":1531856698,\"has_liked\":false,\"like_count\":1,\"user\":{\"pk\":\"25025320\",\"username\":\"someuser\",\"is_private\":false," +
                   "\"full_name\":\"Some User\",\"profile_pic_url\":\"https://x/pic.jpg\"}," +
                   "\"image_versions2\":" + versions("1") + ",\"carousel_media\":[" +
                   "{\"pk\":\"1825668459523250497\",\"code\":\"BlWE8qHhJlB\",\"media_type\":1,\"image_versions2\":" + versions("1") + "}," +
                   "{\"pk\":\"1825668459523250498\",\"code\":\"BlWE8qHhJlC\",\"media_type\":1,\"image_versions2\":" + versions("2") + "}]}";
        }

        [TestMethod]
        public void CarouselListedWithoutPicturesIsLoadedFromThePostDetails()
        {
            // Instagram listed a post of a profile with a carousel without image versions (candidates = []), on which
            // Instaloader fails with IndexError: list index out of range. Its post details contain the pictures.
            _answer = path => path == "/www.instagram.com/"
                ? "<html><script>[\"DTSGInitialData\",[],{\"token\":\"dtsg\"}]</script></html>"
                : "{\"data\":{\"xdt_api__v1__media__shortcode__web_info\":{\"items\":[" + Carousel(true) + "]}},\"status\":\"ok\"}";
            InstaloaderContext context = LoggedInContext();
            Post post = Post.FromIphoneStruct(context, PyJson.Loads(Carousel(false)).AsObject());
            Assert.AreEqual("GraphSidecar", post.Typename);
            Assert.AreEqual(0, RequestsMade().Count, "the post is listed without a request");

            List<PostSidecarNode> nodes = post.GetSidecarNodes().ToList();
            Assert.AreEqual(2, post.Mediacount);
            CollectionAssert.AreEqual(new List<string> { "https://x/1.jpg?stp=1", "https://x/2.jpg?stp=1" },
                                      nodes.Select(n => n.DisplayUrl).ToList());
            CollectionAssert.AreEqual(new List<string> { "/www.instagram.com/", "/www.instagram.com/graphql/query" },
                                      RequestsMade(), "only the post details, once");
            Assert.AreEqual(0, context.ErrorLog.Count, string.Join(", ", context.ErrorLog));
        }

        [TestMethod]
        public void CarouselWithoutPicturesInItsDetailsFailsWithItsName()
        {
            _answer = path => path == "/www.instagram.com/"
                ? "<html><script>[\"DTSGInitialData\",[],{\"token\":\"dtsg\"}]</script></html>"
                : "{\"data\":{\"xdt_api__v1__media__shortcode__web_info\":{\"items\":[" + Carousel(false) + "]}},\"status\":\"ok\"}";
            Post post = Post.FromIphoneStruct(LoggedInContext(), PyJson.Loads(Carousel(false)).AsObject());
            try
            {
                post.GetSidecarNodes().ToList();
                Assert.Fail("BadResponseException expected");
            }
            catch (BadResponseException err)
            {
                Assert.AreEqual("Instagram returned no picture of item 1 of <Post BlWFBJxhBtI>.", err.Message);
            }
        }

        [TestMethod]
        public void HighlightsOnlyForSubscribersAreSkipped()
        {
            // Instagram lists the highlights only for subscribers of a profile (FAN_CLUB) to accounts that are not
            // subscribed, but without a cover (cover_media null) and with a count of 0 items.
            const string trayPath = "/i.instagram.com/api/v1/highlights/25025320/highlights_tray/";
            _answer = path => path == trayPath
                ? "{\"tray\":[{\"id\":\"highlight:17990134595293637\",\"title\":\"Exclusive\",\"media_count\":0," +
                  "\"highlight_reel_type\":\"FAN_CLUB\",\"cover_media\":null,\"latest_reel_media\":null," +
                  "\"user\":{\"pk\":25025320,\"username\":\"someuser\",\"profile_pic_url\":\"https://x/pic.jpg\"}}]," +
                  "\"status\":\"ok\"}"
                : "{}";
            var loader = new Instaloader(sleep: false, quiet: true, maxConnectionAttempts: 1);
            loader.Context.UpdateCookies(new[]
            {
                new KeyValuePair<string, string>("sessionid", "1234%3Aabc%3A1"),
                new KeyValuePair<string, string>("ds_user_id", "1234"),
                new KeyValuePair<string, string>("csrftoken", "token")
            });
            loader.Context.Username = "someuser";

            Highlight highlight = loader.GetHighlights(25025320L).Single();
            Assert.IsTrue(highlight.IsSubscribersOnly);
            Assert.IsNull(highlight.CoverUrl);
            Assert.IsNull(highlight.CoverCroppedUrl);

            loader.DownloadHighlights(25025320L);
            CollectionAssert.AreEqual(new List<string> { trayPath, trayPath }, RequestsMade(),
                                      "the tray only, once for each call: no cover or items requested");
            Assert.AreEqual(0, loader.Context.ErrorLog.Count, string.Join(", ", loader.Context.ErrorLog));
        }

        private static List<string> RequestsMade()
        {
            lock (Requests)
                return new List<string>(Requests);
        }
    }
}
