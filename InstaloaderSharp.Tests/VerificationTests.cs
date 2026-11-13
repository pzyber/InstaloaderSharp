using System;
using System.Collections.Generic;
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
    /// Instagram asking to verify the account, answered by a local server in place of Instagram (no internet access
    /// needed).
    /// </summary>
    [TestClass]
    public class VerificationTests
    {
        private const string ChallengeUrl = "https://www.instagram.com/challenge/?next=/graphql/query";

        private static HttpListener _listener;
        private static string _base;
        private static readonly List<string> Requests = new List<string>();

        /// <summary>Answers a request, given its path (host first, as in "/www.instagram.com/graphql/query").</summary>
        private static Action<string, HttpListenerResponse> _answer;

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
                _answer(path, ctx.Response);
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

        private static void RedirectToChallenge(string path, HttpListenerResponse response)
        {
            response.StatusCode = 302;
            response.RedirectLocation = ChallengeUrl;
        }

        private static InstaloaderContext ContextWithSessionCookies()
        {
            var context = new InstaloaderContext(sleep: false, quiet: true, maxConnectionAttempts: 1);
            context.UpdateCookies(new[]
            {
                new KeyValuePair<string, string>("sessionid", "1234%3Aabc%3A1"),
                new KeyValuePair<string, string>("ds_user_id", "1234"),
                new KeyValuePair<string, string>("csrftoken", "token")
            });
            return context;
        }

        private static List<string> RequestsMade()
        {
            lock (Requests)
                return new List<string>(Requests);
        }

        [TestMethod]
        public void LoginCheckRedirectedToVerificationFailsWithAdvice()
        {
            _answer = RedirectToChallenge;
            InstaloaderContext context = ContextWithSessionCookies();
            try
            {
                context.TestLogin();
                Assert.Fail("LoginException expected");
            }
            catch (LoginException err)
            {
                StringAssert.Contains(err.Message, "Instagram asks to verify the account");
                StringAssert.Contains(err.Message, ChallengeUrl);
                StringAssert.Contains(err.Message, "complete the verification");
            }
            CollectionAssert.AreEqual(new List<string> { "/i.instagram.com/api/v1/accounts/current_user/" }, RequestsMade(),
                                      "no further check and no retries: " + string.Join(", ", RequestsMade()));
        }

        [TestMethod]
        public void LoginCheckAnsweredWithChallengeRequiredFailsWithAdvice()
        {
            _answer = (path, response) =>
            {
                response.StatusCode = 400;
                response.ContentType = "application/json";
                byte[] body = Encoding.UTF8.GetBytes("{\"message\": \"challenge_required\", \"status\": \"fail\"}");
                response.OutputStream.Write(body, 0, body.Length);
            };
            InstaloaderContext context = ContextWithSessionCookies();
            try
            {
                context.TestLogin();
                Assert.Fail("LoginException expected");
            }
            catch (LoginException err)
            {
                StringAssert.Contains(err.Message, "challenge_required");
                StringAssert.Contains(err.Message, "complete the verification");
            }
            Assert.AreEqual(1, RequestsMade().Count);
        }

        [TestMethod]
        public void QueryRedirectedToVerificationAbortsDownload()
        {
            _answer = RedirectToChallenge;
            InstaloaderContext context = ContextWithSessionCookies();
            context.Username = "someuser";
            try
            {
                context.GraphqlQuery("d6f4427fbe92d846298cf93df0b937d3", new JObject());
                Assert.Fail("VerificationRequiredException expected");
            }
            catch (VerificationRequiredException err)
            {
                StringAssert.Contains(err.Message, "complete the verification");
            }
            Assert.AreEqual(1, RequestsMade().Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ConnectionException))]
        public void QueryWithoutSessionRedirectedToVerificationIsAConnectionError()
        {
            _answer = RedirectToChallenge;
            new InstaloaderContext(sleep: false, quiet: true, maxConnectionAttempts: 1)
                .GraphqlQuery("d6f4427fbe92d846298cf93df0b937d3", new JObject());
        }
    }
}
