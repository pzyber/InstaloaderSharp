using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using InstaloaderSharp.Compression;
using InstaloaderSharp.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class StructureTests
    {
        private const char FullwidthColon = (char)0xFF1A;
        private const char DivisionSlash = (char)0x2215;
        private const char OneDotLeader = (char)0x2024;
        private const char Ellipsis = (char)0x2026;
        private const char EAcute = (char)0xE9;

        private static Post LoadPost()
        {
            return (Post)Structures.LoadStructureFromFile(TestData.Context(), TestData.Path("post.json"));
        }

        [TestMethod]
        public void PostPropertiesFromNode()
        {
            // Expected values from Instaloader for the post in post.json
            Post post = LoadPost();
            Assert.AreEqual("CqXy_12-Ab", post.Shortcode);
            Assert.AreEqual(3082211244414448123L, post.Mediaid);
            Assert.AreEqual("GraphSidecar", post.Typename);
            Assert.AreEqual(2, post.Mediacount);
            CollectionAssert.AreEqual(new List<bool> { false, true }, post.GetIsVideos());
            Assert.AreEqual(new DateTime(2021, 1, 1, 12, 0, 0, DateTimeKind.Utc), post.DateUtc);
            Assert.AreEqual(150L, post.Likes);
            Assert.AreEqual(7L, post.Comments);
            Assert.AreEqual("someuser", post.OwnerUsername);
            Assert.AreEqual(25025320L, post.OwnerId);
            Assert.AreEqual("A cat", post.AccessibilityCaption);
            Assert.IsNull(post.Title);
            Assert.IsFalse(post.IsVideo);
            Assert.IsNull(post.VideoUrl);
            Assert.AreEqual("https://scontent.cdninstagram.com/v/t51/abc_n.jpg?stp=1&se=7&x=2", post.Url);
            Assert.AreEqual("<Post CqXy_12-Ab>", post.ToString());
        }

        [TestMethod]
        public void CaptionDerivedProperties()
        {
            Post post = LoadPost();
            CollectionAssert.AreEqual(new List<string> { "cat", "dogs_2" }, post.CaptionHashtags);
            CollectionAssert.AreEqual(new List<string> { "some.user", "other__x" }, post.CaptionMentions);
            CollectionAssert.AreEqual(new List<string> { "tagged.one" }, post.TaggedUsers);
            Assert.AreEqual("Hello World! #Cat #dogs_2 " + EAcute + "t" + EAcute + " " + Ellipsis, post.Pcaption);
        }

        [TestMethod]
        public void SidecarNodes()
        {
            List<PostSidecarNode> nodes = LoadPost().GetSidecarNodes().ToList();
            Assert.AreEqual(2, nodes.Count);
            Assert.IsFalse(nodes[0].IsVideo);
            Assert.AreEqual("https://example.com/1.jpg", nodes[0].DisplayUrl);
            Assert.IsNull(nodes[0].VideoUrl);
            Assert.AreEqual("https://example.com/2.mp4", nodes[1].VideoUrl);
            Assert.AreEqual(1, LoadPost().GetSidecarNodes(1, 1).Count());
        }

        [TestMethod]
        public void ShortcodesAndMediaids()
        {
            // Values of Python's Post.shortcode_to_mediaid
            Assert.AreEqual(new BigInteger(47955405633740827L), Post.ShortcodeToMediaid("CqXy_12-Ab"));
            Assert.AreEqual(new BigInteger(3994809802840683658L), Post.ShortcodeToMediaid("DdwalmrkTSK"));
            Assert.AreEqual(BigInteger.One, Post.ShortcodeToMediaid("B"));
            Assert.AreEqual("CqXy_12-Ab", Post.MediaidToShortcode(47955405633740827L));
            Assert.AreEqual("DdwalmrkTSK", Post.MediaidToShortcode(3994809802840683658L));
            Assert.AreEqual("B", Post.MediaidToShortcode(1));
        }

        [TestMethod]
        public void FilenamePatterns()
        {
            Post post = LoadPost();
            Func<string, string> path = pattern => new PostPathFormatter(post, true).Format(pattern, "my:target", true);
            Func<string, string> text = pattern => new ArbitraryItemFormatter(post).Format(pattern, "my:target", true);
            Assert.AreEqual("2021-01-01_12-00-00_UTC", path("{date_utc}_UTC"));
            Assert.AreEqual("my" + FullwidthColon + "target/CqXy_12-Ab_3082211244414448123", path("{target}/{shortcode}_{mediaid}"));
            Assert.AreEqual("my:target/CqXy_12-Ab_3082211244414448123", text("{target}/{shortcode}_{mediaid}"));
            Assert.AreEqual("someuser_00150_2021-01", path("{profile}_{likes:05d}_{date_utc:%Y-%m}"));
            Assert.AreEqual("['cat', 'dogs_2']", path("{caption_hashtags}"));
            Assert.AreEqual("|'GraphSidecar'", path("{title}|{typename!r}"));
            Assert.AreEqual("{filename}_x", path("{filename}_x"));
        }

        [TestMethod]
        public void FormattedValuesInPathsAreSanitized()
        {
            // Instaloader leaves the colons of "%c" in file names, which fails on Windows ("Invalid argument").
            Post post = LoadPost();
            Func<string, string> path = pattern => new PostPathFormatter(post, true).Format(pattern);
            Assert.AreEqual("Fri Jan  1 12" + FullwidthColon + "00" + FullwidthColon + "00 2021", path("{date_utc:%c}"));
            Assert.AreEqual("Friday, January 01, 2021", path("{date_utc:%#x}"));
            Assert.AreEqual("2021/01", path("{date_utc:%Y/%m}"), "path separators from formats are kept");
            Assert.AreEqual("Fri Jan  1 12:00:00 2021", new ArbitraryItemFormatter(post).Format("{date_utc:%c}"));
        }

        [TestMethod]
        public void SanitizePath()
        {
            Assert.AreEqual(FullwidthColon + "feed" + DivisionSlash + "x", PostPathFormatter.SanitizePath(":feed/x", true));
            Assert.AreEqual("CON_", PostPathFormatter.SanitizePath("CON", true));
            Assert.AreEqual("lpt1_.txt", PostPathFormatter.SanitizePath("lpt1.txt", true));
            Assert.AreEqual(OneDotLeader + "hidden", PostPathFormatter.SanitizePath(".hidden", true));
            Assert.AreEqual("x" + OneDotLeader, PostPathFormatter.SanitizePath("x.", true));
            string expected = "a" + (char)0xFE64 + "b" + (char)0xFE65 + "c" + (char)0xFF02 + "d" + (char)0xFE68 + "e" +
                              (char)0xFF5C + "f" + (char)0xFE16 + "g" + (char)0xFF0A + "h";
            Assert.AreEqual(expected, PostPathFormatter.SanitizePath("a<b>c\"d\\e|f?g*h", true));
        }

        [TestMethod]
        public void SavedPostIsIdenticalToPythonsFile()
        {
            using (TempDirectory dir = TestData.TempDirectory())
            {
                // Like Python, loading a file and saving it again in the same format gives the same file
                // (pretty-printed files have sorted keys, compressed ones keep the order of the node).
                Structures.SaveStructureToFile(LoadPost(), dir.File("post.json"));
                TestData.AssertSameBytes(TestData.Bytes("post.json"), File.ReadAllBytes(dir.File("post.json")));
                IJsonExportable compressed = Structures.LoadStructureFromFile(TestData.Context(), TestData.Path("post.json.xz"));
                Structures.SaveStructureToFile(compressed, dir.File("post.json.xz"));
                TestData.AssertSameBytes(Xz.DecompressFile(TestData.Path("post.json.xz")),
                                         Xz.DecompressFile(dir.File("post.json.xz")));
            }
        }

        [TestMethod]
        public void LoadsCompressedPostWrittenByPython()
        {
            var post = (Post)Structures.LoadStructureFromFile(TestData.Context(), TestData.Path("post.json.xz"));
            Assert.AreEqual("CqXy_12-Ab", post.Shortcode);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidArgumentException))]
        public void LoadingNonInstaloaderJsonFails()
        {
            Structures.LoadStructure(TestData.Context(), new JObject { { "x", 1 } });
        }

        [TestMethod]
        public void ProfileAsDictRemovesPostConnections()
        {
            var profile = new Profile(TestData.Context(), new JObject
            {
                { "id", "25025320" },
                { "username", "SomeUser" },
                { "edge_owner_to_timeline_media", new JObject { { "count", 5 } } },
                { "is_private", false }
            });
            Assert.AreEqual(25025320L, profile.Userid);
            Assert.AreEqual("someuser", profile.Username);
            Assert.IsFalse(profile.IsPrivate);
            Assert.IsFalse(profile.AsDict().ContainsKey("edge_owner_to_timeline_media"));
            Assert.AreEqual("<Profile someuser (25025320)>", profile.ToString());
        }

        [TestMethod]
        public void StoryFromIphoneReel()
        {
            // A reel as the app's reels_media returns it: the items' users have no username.
            JObject reel = PyJson.Loads(
                "{\"id\":\"25025320\",\"latest_reel_media\":1793325578,\"seen\":0,\"expiring_at\":1793411978," +
                "\"user\":{\"id\":\"25025320\",\"pk\":25025320,\"username\":\"SomeUser\"},\"items\":[" +
                "{\"pk\":4019028500106552572,\"taken_at\":1793325553,\"expiring_at\":1793411953,\"media_type\":1," +
                "\"user\":{\"pk\":25025320},\"image_versions2\":{\"candidates\":[{\"url\":\"https://x/a.jpg\"}]}}," +
                "{\"pk\":4019028705006752700,\"taken_at\":1793325578,\"expiring_at\":1793411978,\"media_type\":2," +
                "\"user\":{\"pk\":25025320},\"caption\":{\"text\":\"Hi #There\"}," +
                "\"image_versions2\":{\"candidates\":[{\"url\":\"https://x/b.jpg\"}]}," +
                "\"video_versions\":[{\"url\":\"https://x/b.mp4\"}]}]}").AsObject();
            Story story = Story.FromIphoneReel(TestData.Context(), reel);
            Assert.AreEqual("someuser", story.OwnerUsername);
            Assert.AreEqual(25025320L, story.OwnerId);
            Assert.AreEqual(2, story.Itemcount);
            Assert.IsNull(story.LastSeenUtc);
            Assert.AreEqual(new DateTime(2026, 10, 30, 1, 59, 38, DateTimeKind.Utc), story.LatestMediaUtc);
            // Newest first, as with the GraphQL items.
            List<StoryItem> items = story.GetItems().ToList();
            Assert.AreEqual(4019028705006752700L, items[0].Mediaid);
            Assert.IsTrue(items[0].IsVideo);
            Assert.AreEqual("GraphStoryVideo", items[0].Typename);
            Assert.AreEqual("Hi #There", items[0].Caption);
            CollectionAssert.AreEqual(new List<string> { "there" }, items[0].CaptionHashtags);
            Assert.AreEqual(4019028500106552572L, items[1].Mediaid);
            Assert.AreEqual("https://x/a.jpg", items[1].Url);
            Assert.AreEqual(new DateTime(2026, 10, 31, 1, 59, 13, DateTimeKind.Utc), items[1].ExpiringUtc);
            Assert.AreEqual("someuser", items[1].OwnerUsername);
            JObject dict = items[1].AsDict();
            Assert.AreEqual("SomeUser", (string)dict["owner"]["username"]);
            Assert.IsTrue(dict.ContainsKey("iphone_struct"));
        }

        [TestMethod]
        public void VideoUrlUsesTheVideoVersionsOfTheNode()
        {
            // Logged in, a video whose node has its video versions (as from the post details) is not looked up in
            // the app's media info: that would be a request, which fails here and is logged as an error.
            InstaloaderContext context = TestData.Context();
            context.Username = "someuser";
            JObject node = PyJson.Loads(
                "{\"shortcode\":\"DeraQlgorHA\",\"id\":\"3700000000000000001\",\"__typename\":\"GraphVideo\"," +
                "\"is_video\":true,\"video_url\":\"https://x/a.mp4\",\"video_versions\":[{\"url\":\"https://x/a.mp4\"}]}").AsObject();
            var post = new Post(context, node);
            Assert.AreEqual("https://x/a.mp4", post.VideoUrl);
            Assert.AreEqual(0, context.ErrorLog.Count);
        }

        [TestMethod]
        public void ImageUrlsUseTheImageVersionsOfTheNode()
        {
            // Logged in, images whose node has their image versions (as from the post details) are not looked up in
            // the app's media info either.
            InstaloaderContext context = TestData.Context();
            context.Username = "someuser";
            JObject image = PyJson.Loads(
                "{\"shortcode\":\"DeraQlgorHA\",\"id\":\"3700000000000000001\",\"__typename\":\"GraphImage\"," +
                "\"is_video\":false,\"display_url\":\"https://x/small.jpg\"," +
                "\"image_versions2\":{\"candidates\":[{\"url\":\"https://x/a.jpg?stp=1&se=7\"}]}}").AsObject();
            Assert.AreEqual("https://x/a.jpg?stp=1", new Post(context, image).Url);
            JObject sidecar = PyJson.Loads(
                "{\"shortcode\":\"DeraQlgorHB\",\"id\":\"3700000000000000002\",\"__typename\":\"GraphSidecar\"," +
                "\"is_video\":false,\"edge_sidecar_to_children\":{\"edges\":[" +
                "{\"node\":{\"is_video\":false,\"display_url\":\"https://x/small1.jpg\"}}," +
                "{\"node\":{\"is_video\":true,\"display_url\":\"https://x/small2.jpg\",\"video_url\":\"https://x/2.mp4\"}}]}," +
                "\"carousel_media\":[{\"media_type\":1,\"image_versions2\":{\"candidates\":[{\"url\":\"https://x/1.jpg\"}]}}," +
                "{\"media_type\":2,\"image_versions2\":{\"candidates\":[{\"url\":\"https://x/2.jpg\"}]}}]}").AsObject();
            List<PostSidecarNode> nodes = new Post(context, sidecar).GetSidecarNodes().ToList();
            Assert.AreEqual("https://x/1.jpg", nodes[0].DisplayUrl);
            Assert.AreEqual("https://x/small2.jpg", nodes[1].DisplayUrl);
            Assert.AreEqual("https://x/2.mp4", nodes[1].VideoUrl);
            Assert.AreEqual(0, context.ErrorLog.Count);
        }

        [TestMethod]
        public void HighlightFromIphoneTrayItem()
        {
            // An item of the app's highlights tray.
            JObject trayItem = PyJson.Loads(
                "{\"id\":\"highlight:17926879011426551\",\"title\":\"Me\",\"media_count\":1," +
                "\"latest_reel_media\":1778020554,\"cover_media\":{\"cropped_image_version\":" +
                "{\"url\":\"https://x/cover150.jpg\"},\"full_image_version\":null}," +
                "\"user\":{\"id\":\"25025320\",\"pk\":25025320,\"username\":\"someuser\"," +
                "\"profile_pic_url\":\"https://x/pic.jpg\"}}").AsObject();
            Highlight highlight = Highlight.FromIphoneTrayItem(TestData.Context(), trayItem, null);
            Assert.AreEqual(17926879011426551L, highlight.HighlightId);
            Assert.AreEqual(17926879011426551L, highlight.UniqueId);
            Assert.AreEqual("Me", highlight.Title);
            Assert.AreEqual("https://x/cover150.jpg", highlight.CoverUrl);
            Assert.AreEqual("https://x/cover150.jpg", highlight.CoverCroppedUrl);
            Assert.AreEqual("someuser", highlight.OwnerUsername);
            Assert.AreEqual(25025320L, highlight.OwnerId);
            Assert.AreEqual("<Highlight by someuser: Me>", highlight.ToString());
        }
    }
}
