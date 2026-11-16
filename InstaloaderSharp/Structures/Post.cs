using System;
using System.Collections.Generic;
using System.Numerics;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;

namespace InstaloaderSharp
{
    /// <summary>
    /// Structure containing information about an Instagram post.
    ///
    /// Created by methods <see cref="Profile.GetPosts"/>, <see cref="Instaloader.GetHashtagPosts"/>,
    /// <see cref="Instaloader.GetFeedPosts"/> and <see cref="Profile.GetSavedPosts"/>, which return iterators of
    /// Posts. Might also be created with <c>Post.FromShortcode(L.Context, SHORTCODE)</c>.
    ///
    /// This class unifies access to the properties associated with a post. It implements == and is hashable.
    /// </summary>
    public class Post : IJsonExportable, IPyObject
    {
        private readonly InstaloaderContext _context;
        private JObject _node;
        private Profile _ownerProfile;
        private JObject _fullMetadataDict;
        private PostLocation _location;
        private JObject _iphoneStruct;

        /// <param name="context">InstaloaderContext used for additional queries if necessary.</param>
        /// <param name="node">Node structure, as returned by Instagram.</param>
        /// <param name="ownerProfile">The Profile of the owner, if already known at creation.</param>
        public Post(InstaloaderContext context, JObject node, Profile ownerProfile = null)
        {
            if (!node.ContainsKey("shortcode") && !node.ContainsKey("code"))
                throw new ArgumentException("node must contain 'shortcode' or 'code'");
            _context = context;
            _node = node;
            _ownerProfile = ownerProfile;
            _fullMetadataDict = null;
            _location = null;
            _iphoneStruct = null;
            if (node.ContainsKey("iphone_struct"))
                // if loaded from JSON with LoadStructureFromFile()
                _iphoneStruct = node["iphone_struct"] as JObject;
        }

        public InstaloaderContext Context { get { return _context; } }

        /// <summary>The raw node structure.</summary>
        public JObject Node { get { return _node; } }

        /// <summary>Create a post object from a given shortcode.</summary>
        public static Post FromShortcode(InstaloaderContext context, string shortcode)
        {
            var post = new Post(context, new JObject { { "shortcode", shortcode } });
            post._node = post.FullMetadata;
            return post;
        }

        /// <summary>Create a post object from a given mediaid.</summary>
        public static Post FromMediaid(InstaloaderContext context, BigInteger mediaid)
        {
            return FromShortcode(context, MediaidToShortcode(mediaid));
        }

        /// <summary>Create a post from a given iphone_struct.</summary>
        public static Post FromIphoneStruct(InstaloaderContext context, JObject media)
        {
            string typename = StructureHelpers.MediaTypeName(media["media_type"], true);
            JToken caption = media.Get("caption");
            var fakeNode = new JObject
            {
                { "shortcode", media["code"] },
                { "id", media["pk"] },
                { "__typename", typename },
                { "is_video", typename == "GraphVideo" },
                { "date", media["taken_at"] },
                { "caption", !caption.IsNone() ? caption.Get("text") ?? JValue.Null : JValue.Null },
                { "title", media.Get("title") ?? JValue.Null },
                { "viewer_has_liked", media["has_liked"] },
                { "edge_media_preview_like", new JObject { { "count", media["like_count"] } } },
                { "accessibility_caption", media.Get("accessibility_caption") ?? JValue.Null },
                { "comments", media.Get("comment_count") ?? JValue.Null },
                { "iphone_struct", media }
            };
            try
            {
                fakeNode["display_url"] = media["image_versions2"]["candidates"][0]["url"];
            }
            catch (PyKeyError)
            {
            }
            try
            {
                fakeNode["video_url"] = media["video_versions"][-1]["url"];
                fakeNode["video_duration"] = media["video_duration"];
                fakeNode["video_view_count"] = media["view_count"];
            }
            catch (Exception ex) when (ex is PyKeyError || ex is PyTypeError)
            {
            }
            try
            {
                var edges = new JArray();
                foreach (JToken node in media["carousel_media"].AsIterable())
                    edges.Add(new JObject { { "node", ConvertIphoneCarousel((JObject)node) } });
                fakeNode["edge_sidecar_to_children"] = new JObject { { "edges", edges } };
            }
            catch (Exception ex) when (ex is PyKeyError || ex is PyTypeError)
            {
            }
            return new Post(context, fakeNode,
                            media.ContainsKey("user") ? InstaloaderSharp.Profile.FromIphoneStruct(context, (JObject)media["user"]) : null);
        }

        private static JObject ConvertIphoneCarousel(JObject iphoneNode)
        {
            var fakeNode = new JObject
            {
                { "display_url", iphoneNode["image_versions2"]["candidates"][0]["url"] },
                { "is_video", StructureHelpers.MediaTypeName(iphoneNode["media_type"], true) == "GraphVideo" }
            };
            if (iphoneNode.ContainsKey("video_versions") && !iphoneNode["video_versions"].IsNull)
                fakeNode["video_url"] = iphoneNode["video_versions"][0]["url"];
            return fakeNode;
        }

        /// <summary>Fetch play_count for a reel via the clips connection endpoint as a fallback.</summary>
        private static JToken FetchPlayCountFromClips(InstaloaderContext context, string userId, string shortcode)
        {
            try
            {
                // The web app's reels tab query; sent with the web app's request fields, the older query of
                // Instaloader 4.15.4, 27234427476213202, is answered without reels.
                JObject resp = context.DocIdGraphqlQuery(InstaloaderSharp.Profile.ReelsDocId,
                                                         InstaloaderSharp.Profile.ReelsQueryVariables(userId));
                JToken edges = (JToken)InstaloaderSharp.Profile.ReelsConnection(resp) ?? new JObject();
                foreach (JToken edge in edges.Get("edges").Or(new JArray()).AsIterable())
                {
                    JToken media = edge.Get("node").Or(new JObject()).Get("media").Or(new JObject());
                    JToken code = media.Get("code");
                    if (code != null && code.Type == JTokenType.String && (string)code == shortcode)
                        return media.Get("play_count") ?? JValue.Null;
                }
            }
            catch (Exception ex) when (ex is ConnectionException || ex is BadResponseException)
            {
            }
            return JValue.Null;
        }

        /// <summary>Normalize a Polaris media item to a legacy-compatible node, preserving the source structure.</summary>
        private static JObject NormalizePostData(JObject media, InstaloaderContext context)
        {
            JToken mediaType = media.Get("media_type");
            string typename = null;
            if (mediaType != null && (mediaType.Type == JTokenType.Integer || mediaType.Type == JTokenType.Boolean))
                typename = StructureHelpers.MediaTypeName(mediaType, false);
            if (typename == null)
                throw new BadResponseException(string.Format("Unknown media_type in metadata: {0}.", Python.PyStr.Str(mediaType)));
            bool isVideoType = mediaType.Type == JTokenType.Integer && mediaType.ToBigInteger() == 2;
            JObject picJson = media.Copy();
            picJson["shortcode"] = media["code"];
            picJson["id"] = media["pk"];
            picJson["__typename"] = typename;
            picJson["is_video"] = isVideoType;
            picJson["taken_at_timestamp"] = media["taken_at"];
            JToken user = media["user"];
            picJson["owner"] = new JObject
            {
                { "id", user["pk"] },
                { "username", user.Get("username", "") },
                { "full_name", user.Get("full_name", "") }
            };
            JToken candidates = media.Get("image_versions2").Or(new JObject()).Get("candidates").Or(new JArray());
            picJson["display_url"] = candidates.Truthy ? candidates[0]["url"] : JValue.Null;
            JToken videoVersions = media.Get("video_versions").Or(new JArray());
            picJson["video_url"] = videoVersions.Truthy ? videoVersions[0]["url"] : JValue.Null;
            picJson["video_duration"] = media.Get("video_duration") ?? JValue.Null;
            picJson["video_view_count"] = media.Get("view_count") ?? JValue.Null;
            picJson["video_play_count"] = media.Get("play_count") ?? JValue.Null;
            JToken caption = media.Get("caption");
            JToken captionText = caption is JObject ? caption.Get("text") : null;
            picJson["edge_media_to_caption"] = !captionText.IsNone()
                ? new JObject { { "edges", new JArray { new JObject { { "node", new JObject { { "text", captionText } } } } } } }
                : new JObject { { "edges", new JArray() } };
            picJson["edge_media_preview_like"] = new JObject { { "count", media.Get("like_count").Or(0) } };
            picJson["edge_media_to_parent_comment"] = new JObject
            {
                { "count", media.Get("comment_count").Or(0) },
                { "edges", new JArray() }
            };
            if (!media.Get("has_liked").IsNone())
                picJson["viewer_has_liked"] = media["has_liked"];
            JToken carousel = media.Get("carousel_media").Or(new JArray());
            if (carousel.Truthy)
            {
                var carouselNodes = new JArray();
                foreach (JToken item in carousel.AsIterable())
                {
                    JToken itemType = item.Get("media_type", 1);
                    var node = new JObject
                    {
                        { "shortcode", item.Get("code", "") },
                        { "__typename", StructureHelpers.MediaTypeName(itemType, false) ?? "GraphImage" },
                        { "is_video", itemType.Type == JTokenType.Integer && itemType.ToBigInteger() == 2 }
                    };
                    JToken itemCandidates = item.Get("image_versions2").Or(new JObject()).Get("candidates").Or(new JArray());
                    node["display_url"] = itemCandidates.Truthy ? itemCandidates[0]["url"] : "";
                    JToken itemVideos = item.Get("video_versions").Or(new JArray());
                    node["video_url"] = itemVideos.Truthy ? itemVideos[0]["url"] : JValue.Null;
                    if (!item.Get("accessibility_caption").IsNone())
                        node["accessibility_caption"] = item["accessibility_caption"];
                    carouselNodes.Add(new JObject { { "node", node } });
                }
                picJson["edge_sidecar_to_children"] = new JObject { { "edges", carouselNodes } };
            }
            JToken tagged = media.Get("usertags").Or(new JObject()).Get("in").Or(new JArray());
            if (tagged.Truthy)
            {
                var edges = new JArray();
                foreach (JToken t in tagged.AsIterable())
                {
                    if (t.Get("user").Or(new JObject()).Get("username").Or(null) != null)
                        edges.Add(new JObject
                        {
                            { "node", new JObject { { "user", new JObject { { "username", Python.PyStr.Lower((string)t["user"]["username"]) } } } } }
                        });
                }
                picJson["edge_media_to_tagged_user"] = new JObject { { "edges", edges } };
            }
            return picJson;
        }

        public static BigInteger ShortcodeToMediaid(string code)
        {
            if (code.Length > 11)
                throw new InvalidArgumentException(string.Format("Wrong shortcode \"{0}\", unable to convert to mediaid.", code));
            code = new string('A', 12 - code.Length) + code;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(code.Replace('-', '+').Replace('_', '/'));
            }
            catch (FormatException)
            {
                throw new PyValueError("Incorrect padding");
            }
            var unsigned = new byte[bytes.Length + 1];
            for (int i = 0; i < bytes.Length; i++)
                unsigned[i] = bytes[bytes.Length - 1 - i];
            return new BigInteger(unsigned);
        }

        public static string MediaidToShortcode(BigInteger mediaid)
        {
            if (mediaid.Sign < 0 || mediaid >= BigInteger.One << 64)
                throw new InvalidArgumentException(string.Format("Wrong mediaid {0}, unable to convert to shortcode", mediaid));
            byte[] little = mediaid.ToByteArray();
            var big = new byte[9];
            for (int i = 0; i < little.Length && i < 9; i++)
                big[8 - i] = little[i];
            string b64 = Convert.ToBase64String(big).Replace('+', '-').Replace('/', '_');
            return b64.Replace('A', ' ').TrimStart(' ').Replace(' ', 'A');
        }

        /// <summary>The values of __typename fields that the Post class can handle.</summary>
        public static List<string> SupportedGraphqlTypes()
        {
            return new List<string> { "GraphImage", "GraphVideo", "GraphSidecar" };
        }

        public string NodeType { get { return "Post"; } }

        public JObject AsDict()
        {
            JObject node = _node;
            if (_fullMetadataDict != null && _fullMetadataDict.Truthy)
                node.Update(_fullMetadataDict);
            if (_ownerProfile != null)
                node["owner"] = OwnerProfile.AsDict();
            if (_location != null)
                node["location"] = _location.AsDict();
            if (_iphoneStruct != null && _iphoneStruct.Truthy)
                node["iphone_struct"] = _iphoneStruct;
            return node;
        }

        /// <summary>Media shortcode. URL of the post is instagram.com/p/&lt;shortcode&gt;/.</summary>
        public string Shortcode
        {
            get { return (string)(_node.ContainsKey("shortcode") ? _node["shortcode"] : _node["code"]); }
        }

        /// <summary>The mediaid is a decimal representation of the media shortcode.</summary>
        public long Mediaid { get { return _node["id"].ToInt64(); } }

        /// <summary>Title of post</summary>
        public string Title
        {
            get
            {
                try
                {
                    return (string)Field("title");
                }
                catch (PyKeyError)
                {
                    return null;
                }
            }
        }

        public string PyStr() { return PyRepr(); }

        public string PyRepr() { return string.Format("<Post {0}>", Shortcode); }

        public override string ToString() { return PyRepr(); }

        public override bool Equals(object obj)
        {
            var other = obj as Post;
            return other != null && Shortcode == other.Shortcode;
        }

        public override int GetHashCode()
        {
            return Shortcode.GetHashCode();
        }

        private void ObtainMetadata()
        {
            if (_fullMetadataDict == null || !_fullMetadataDict.Truthy)
            {
                JObject resp = _context.DocIdGraphqlQuery(
                    "27128499623469141",
                    new JObject
                    {
                        { "shortcode", Shortcode },
                        { "__relay_internal__pv__PolarisAIGMMediaWebLabelEnabledrelayprovider", false }
                    },
                    // As the logged-out web app does (Instaloader 4.16).
                    webApi: !_context.IsLoggedIn);
                JToken webInfo = resp.Get("data").Or(new JObject()).Get("xdt_api__v1__media__shortcode__web_info").Or(new JObject());
                JToken items = webInfo.Get("items");
                if (items == null || !items.Truthy)
                    throw new BadResponseException("Fetching Post metadata failed.");
                _fullMetadataDict = NormalizePostData((JObject)items[0], _context);
                if (Shortcode != (string)_fullMetadataDict["shortcode"])
                {
                    _node.Update(_fullMetadataDict);
                    throw new PostChangedException();
                }
            }
        }

        private JObject FullMetadata
        {
            get
            {
                ObtainMetadata();
                return _fullMetadataDict;
            }
        }

        private JObject IphoneStruct
        {
            get
            {
                if (!_context.IphoneSupport)
                    throw new IPhoneSupportDisabledException("iPhone support is disabled.");
                if (!_context.IsLoggedIn)
                    throw new LoginRequiredException("Login required to access iPhone media info endpoint.");
                if (_iphoneStruct == null || !_iphoneStruct.Truthy)
                {
                    JObject data = _context.GetIphoneJson(string.Format("api/v1/media/{0}/info/", Mediaid), new JObject());
                    _iphoneStruct = (JObject)data["items"][0];
                }
                return _iphoneStruct;
            }
        }

        /// <summary>URL of the largest image version of a media in the app's format, or null if it has none.</summary>
        private static string FirstCandidateUrl(JToken media)
        {
            JToken candidates = media.Get("image_versions2").Or(new JObject()).Get("candidates").Or(new JArray());
            return candidates is JArray && candidates.Truthy ? (string)candidates[0]["url"] : null;
        }

        /// <summary>Lookups given fields in _node, and if not found in _full_metadata. Raises KeyError if not found anywhere.</summary>
        private JToken Field(params string[] keys)
        {
            try
            {
                JToken d = _node;
                foreach (string key in keys)
                    d = d[key];
                return d;
            }
            catch (PyKeyError)
            {
                JToken d = FullMetadata;
                foreach (string key in keys)
                    d = d[key];
                return d;
            }
        }

        /// <summary>Profile instance of the Post's owner.</summary>
        public Profile OwnerProfile
        {
            get
            {
                if (_ownerProfile == null)
                {
                    JObject ownerStruct;
                    if (_node["owner"].ContainsKey("username"))
                    {
                        ownerStruct = (JObject)_node["owner"];
                    }
                    else
                    {
                        // Sometimes, the 'owner' structure does not contain the username, only the user's ID. In that
                        // case, this call triggers downloading of the complete Post metadata struct, where the owner
                        // username is contained.
                        // Note that we cannot use Profile.FromId() here since that would lead us into a recursion.
                        ownerStruct = (JObject)FullMetadata["owner"];
                    }
                    _ownerProfile = new Profile(_context, ownerStruct);
                }
                return _ownerProfile;
            }
        }

        /// <summary>The Post's lowercase owner name.</summary>
        public string OwnerUsername { get { return OwnerProfile.Username; } }

        /// <summary>The ID of the Post's owner.</summary>
        public long OwnerId
        {
            get
            {
                // The ID may already be available, e.g. if the post instance was created
                // from an `hashtag.get_posts()` iterator, so no need to make another
                // http request.
                if (_node.ContainsKey("owner") && _node["owner"].ContainsKey("id"))
                    return _node["owner"]["id"].ToInt64();
                return OwnerProfile.Userid;
            }
        }

        /// <summary>Timestamp when the post was created (local time zone).</summary>
        public DateTimeOffset DateLocal
        {
            get { return PyDateTime.LocalFromTimestamp(GetTimestampDateCreated()); }
        }

        /// <summary>Timestamp when the post was created (UTC).</summary>
        public DateTime DateUtc
        {
            get { return DateTime.SpecifyKind(PyDateTime.UtcFromTimestamp(GetTimestampDateCreated()).Wall, DateTimeKind.Utc); }
        }

        /// <summary>Synonym to <see cref="DateUtc"/></summary>
        public DateTime Date { get { return DateUtc; } }

        /// <summary>Synonym to <see cref="OwnerUsername"/></summary>
        public string Profile { get { return OwnerUsername; } }

        /// <summary>URL of the picture / video thumbnail of the post</summary>
        public string Url
        {
            get
            {
                if (Typename == "GraphImage" && _context.IphoneSupport && _context.IsLoggedIn)
                {
                    try
                    {
                        // The post details (posts loaded by their shortcode, as tagged posts and reels) contain the
                        // same image versions as the app's media info, which Instaloader requests for every image:
                        // the same files and sizes in all 366 images compared. Using them halves the requests per
                        // post, which got accounts asked to verify (challenge_required) on the media info.
                        string origUrl = FirstCandidateUrl(_node) ??
                                         (string)IphoneStruct["image_versions2"]["candidates"][0]["url"];
                        return StructureHelpers.StripSeParam(origUrl);
                    }
                    catch (Exception err) when (err is InstaloaderException || err is PyKeyError || err is PyIndexError)
                    {
                        _context.Error(string.Format("Unable to fetch high quality image version of {0}: {1}", this, err.Message));
                    }
                }
                return (string)(_node.ContainsKey("display_url") ? _node["display_url"] : _node["display_src"]);
            }
        }

        /// <summary>Type of post, GraphImage, GraphVideo or GraphSidecar</summary>
        public string Typename { get { return (string)Field("__typename"); } }

        /// <summary>The number of media in a sidecar Post, or 1 if the Post it not a sidecar.</summary>
        public int Mediacount
        {
            get
            {
                if (Typename == "GraphSidecar")
                    return Field("edge_sidecar_to_children", "edges").Count;
                return 1;
            }
        }

        private double GetTimestampDateCreated()
        {
            return (_node.ContainsKey("date") ? _node["date"] : _node["taken_at_timestamp"]).ToDouble();
        }

        /// <summary>Return a list containing the is_video property for each media in the post.</summary>
        public List<bool> GetIsVideos()
        {
            if (Typename == "GraphSidecar")
            {
                var result = new List<bool>();
                foreach (JToken edge in Field("edge_sidecar_to_children", "edges").AsIterable())
                    result.Add((bool)edge["node"]["is_video"]);
                return result;
            }
            return new List<bool> { IsVideo };
        }

        /// <summary>Sidecar nodes of a Post with typename==GraphSidecar.</summary>
        /// <param name="start">Index of the first sidecar node to return.</param>
        /// <param name="end">Index of the last sidecar node to return (-1 for the last one).</param>
        public IEnumerable<PostSidecarNode> GetSidecarNodes(int start = 0, int end = -1)
        {
            if (Typename == "GraphSidecar")
            {
                JArray edges = Field("edge_sidecar_to_children", "edges").AsArray();
                if (end < 0)
                    end = edges.Count - 1;
                if (start < 0)
                    start = edges.Count - 1;
                bool needFull = false;
                foreach (JToken edge in edges.Slice(start, end + 1))
                    if (edge["node"]["is_video"].Truthy && !edge["node"].ContainsKey("video_url"))
                        needFull = true;
                if (needFull)
                    // video_url is only present in full metadata, issue #558.
                    edges = FullMetadata["edge_sidecar_to_children"]["edges"].AsArray();
                for (int idx = 0; idx < edges.Count; idx++)
                {
                    if (start <= idx && idx <= end)
                    {
                        JToken node = edges[idx]["node"];
                        bool isVideo = node["is_video"].Truthy;
                        string displayUrl = (string)node["display_url"];
                        if (!isVideo && _context.IphoneSupport && _context.IsLoggedIn)
                        {
                            try
                            {
                                // As for single images, see Url.
                                JToken nodeCarousel = _node.Get("carousel_media");
                                string origUrl = nodeCarousel is JArray && idx < nodeCarousel.Count
                                                     ? FirstCandidateUrl(nodeCarousel[idx])
                                                     : null;
                                if (origUrl == null)
                                {
                                    JToken carouselMedia = IphoneStruct["carousel_media"];
                                    origUrl = (string)carouselMedia[idx]["image_versions2"]["candidates"][0]["url"];
                                }
                                displayUrl = StructureHelpers.StripSeParam(origUrl);
                            }
                            catch (Exception err) when (err is InstaloaderException || err is PyKeyError || err is PyIndexError)
                            {
                                _context.Error(string.Format("Unable to fetch high quality image version of {0}: {1}", this, err.Message));
                            }
                        }
                        yield return new PostSidecarNode(isVideo, displayUrl, isVideo ? (string)node["video_url"] : null);
                    }
                }
            }
        }

        /// <summary>Caption.</summary>
        public string Caption
        {
            get
            {
                if (_node.ContainsKey("edge_media_to_caption") && _node["edge_media_to_caption"]["edges"].Truthy)
                    return StructureHelpers.OptionalNormalize((string)_node["edge_media_to_caption"]["edges"][0]["node"]["text"]);
                if (_node.ContainsKey("caption"))
                    return StructureHelpers.OptionalNormalize((string)_node["caption"]);
                return null;
            }
        }

        /// <summary>List of all lowercased hashtags (without preceding #) that occur in the Post's caption.</summary>
        public List<string> CaptionHashtags
        {
            get
            {
                string caption = Caption;
                if (string.IsNullOrEmpty(caption))
                    return new List<string>();
                return StructureHelpers.FindAll(StructureHelpers.HashtagRegex, Python.PyStr.Lower(caption));
            }
        }

        /// <summary>List of all lowercased profiles that are mentioned in the Post's caption, without preceding @.</summary>
        public List<string> CaptionMentions
        {
            get
            {
                string caption = Caption;
                if (string.IsNullOrEmpty(caption))
                    return new List<string>();
                return StructureHelpers.FindAll(StructureHelpers.MentionRegex, Python.PyStr.Lower(caption));
            }
        }

        /// <summary>Printable caption, useful as a format specifier for --filename-pattern.</summary>
        public string Pcaption
        {
            get
            {
                string caption = Caption;
                return string.IsNullOrEmpty(caption) ? "" : StructureHelpers.Elliptify(caption);
            }
        }

        /// <summary>Accessibility caption of the post, if available.</summary>
        public string AccessibilityCaption
        {
            get
            {
                try
                {
                    return (string)Field("accessibility_caption");
                }
                catch (PyKeyError)
                {
                    return null;
                }
            }
        }

        /// <summary>List of all lowercased users that are tagged in the Post.</summary>
        public List<string> TaggedUsers
        {
            get
            {
                try
                {
                    var result = new List<string>();
                    foreach (JToken edge in Field("edge_media_to_tagged_user", "edges").AsIterable())
                        result.Add(Python.PyStr.Lower((string)edge["node"]["user"]["username"]));
                    return result;
                }
                catch (PyKeyError)
                {
                    return new List<string>();
                }
            }
        }

        /// <summary>True if the Post is a video.</summary>
        public bool IsVideo { get { return (bool)_node["is_video"]; } }

        /// <summary>URL of the video, or null.</summary>
        public string VideoUrl
        {
            get
            {
                if (!IsVideo)
                    return null;
                var versionUrls = new List<string>();
                try
                {
                    versionUrls.Add((string)Field("video_url"));
                }
                catch (Exception err) when (err is InstaloaderException || err is PyKeyError || err is PyIndexError)
                {
                    _context.Error(string.Format("Warning: Unable to fetch video from graphql of {0}: {1}", this, err.Message));
                }
                if (_context.IphoneSupport && _context.IsLoggedIn)
                {
                    try
                    {
                        // The post details contain the same video versions as the app's
                        // media info, which Instaloader requests for every video: the same sizes and files in all
                        // 956 videos compared. Using them saves a request per video, which for profiles with many
                        // reels got accounts flagged (a captcha, then feedback_required).
                        JToken nodeVersions = _node.Get("video_versions");
                        var urls = new List<string>();
                        foreach (JToken version in (nodeVersions is JArray && nodeVersions.Truthy
                                                        ? nodeVersions
                                                        : IphoneStruct["video_versions"]).AsIterable())
                            urls.Add((string)version["url"]);
                        versionUrls.AddRange(urls);
                    }
                    catch (Exception err) when (err is InstaloaderException || err is PyKeyError || err is PyIndexError)
                    {
                        _context.Error(string.Format("Unable to fetch high-quality video version of {0}: {1}", this, err.Message));
                    }
                }
                return StructureHelpers.BestVideoUrl(_context, versionUrls, this);
            }
        }

        /// <summary>View count of the video, or null.</summary>
        public long? VideoViewCount
        {
            get { return IsVideo ? (long?)Field("video_view_count") : null; }
        }

        /// <summary>Play count of the video, or null.</summary>
        public long? VideoPlayCount
        {
            get
            {
                if (!IsVideo)
                    return null;
                var playCount = (long?)Field("video_play_count");
                // Fetched only when needed, as in Instaloader 4.16, since it takes a request of its own. Not logged in,
                // the clips connection is null.
                if (playCount == null && VideoViewCount == null && _context.IsLoggedIn)
                {
                    JToken fetched = FetchPlayCountFromClips(_context, OwnerId.ToString(System.Globalization.CultureInfo.InvariantCulture), Shortcode);
                    if (!fetched.IsNone())
                        playCount = fetched.ToInt64();
                }
                return playCount;
            }
        }

        /// <summary>Duration of the video in seconds, or null.</summary>
        public double? VideoDuration
        {
            get { return IsVideo ? (double?)Field("video_duration") : null; }
        }

        /// <summary>Whether the viewer has liked the post, or null if not logged in.</summary>
        public bool? ViewerHasLiked
        {
            get
            {
                if (!_context.IsLoggedIn)
                    return null;
                if (_node.ContainsKey("likes") && _node["likes"].ContainsKey("viewer_has_liked"))
                    return (bool?)_node["likes"]["viewer_has_liked"];
                return (bool?)Field("viewer_has_liked");
            }
        }

        /// <summary>Likes count</summary>
        public long? Likes { get { return (long?)Field("edge_media_preview_like", "count"); } }

        /// <summary>Comment count including answers</summary>
        public long? Comments
        {
            get
            {
                // If the count is already present in `_node`, do not use `Field` which could trigger fetching the
                // full metadata dict.
                JToken comments = _node.Get("edge_media_to_comment");
                if (comments != null && comments.Truthy && comments.ContainsKey("count"))
                    return (long?)comments["count"];
                try
                {
                    return (long?)Field("edge_media_to_parent_comment", "count");
                }
                catch (PyKeyError)
                {
                    return (long?)Field("edge_media_to_comment", "count");
                }
            }
        }

        /// <summary>Iterate over all comments of the post via an iPhone endpoint (fallback for issue #2125).</summary>
        private IEnumerable<PostComment> GetCommentsViaIphoneEndpoint()
        {
            Func<string, JObject> query = minId =>
            {
                var parameters = new JObject { { "can_support_threading", "true" }, { "permalink_enabled", "false" } };
                if (minId != null)
                    parameters["min_id"] = minId;
                return _context.GetIphoneJson(string.Format("api/v1/media/{0}/comments/", Mediaid), parameters);
            };
            return PaginatedComments(query, query(null));
        }

        private PostCommentAnswer IphoneAnswer(JToken childComment)
        {
            return new PostCommentAnswer(
                childComment["pk"].ToInt64(),
                StructureHelpers.UtcFromTimestamp(childComment["created_at"]),
                (string)childComment["text"],
                InstaloaderSharp.Profile.FromIphoneStruct(_context, (JObject)childComment["user"]),
                childComment["comment_like_count"].ToInt64());
        }

        private IEnumerable<PostCommentAnswer> IphoneAnswers(JObject commentNode)
        {
            JToken childCommentCount = commentNode["child_comment_count"];
            if (childCommentCount.ToBigInteger() == 0)
                yield break;
            JToken previewChildComments = commentNode["preview_child_comments"];
            if (childCommentCount.ToBigInteger() == previewChildComments.Count)
            {
                foreach (JToken childComment in previewChildComments.AsIterable())
                    yield return IphoneAnswer(childComment);
                yield break;
            }
            JToken pk = commentNode["pk"];
            JObject answersJson = _context.GetIphoneJson(
                string.Format("api/v1/media/{0}/comments/{1}/child_comments/", Mediaid, Python.PyStr.Str(pk)),
                new JObject { { "max_id", "" } });
            foreach (JToken childComment in answersJson["child_comments"].AsIterable())
                yield return IphoneAnswer(childComment);
        }

        private IEnumerable<PostComment> PaginatedComments(Func<string, JObject> query, JObject commentsJson)
        {
            while (true)
            {
                foreach (JToken commentNode in commentsJson.Get("comments", new JArray()).AsIterable())
                    yield return PostComment.FromIphoneStruct(_context, (JObject)commentNode, IphoneAnswers((JObject)commentNode), this);
                JToken nextMinId = commentsJson.Get("next_min_id");
                if (nextMinId == null || !nextMinId.Truthy)
                    yield break;
                commentsJson = query((string)nextMinId);
            }
        }

        private PostCommentAnswer GraphqlAnswer(JObject node)
        {
            return new PostCommentAnswer(
                node["id"].ToInt64(),
                StructureHelpers.UtcFromTimestamp(node["created_at"]),
                (string)node["text"],
                new Profile(_context, (JObject)node["owner"]),
                node.Get("edge_liked_by", new JObject()).Get("count", 0).ToInt64());
        }

        private IEnumerable<PostCommentAnswer> GraphqlAnswers(JObject node)
        {
            if (!node.ContainsKey("edge_threaded_comments"))
                yield break;
            JToken answerCount = node["edge_threaded_comments"]["count"];
            if (answerCount.ToBigInteger() == 0)
                // Avoid doing additional requests if there are no comment answers
                yield break;
            JToken answerEdges = node["edge_threaded_comments"]["edges"];
            if (answerCount.ToBigInteger() == answerEdges.Count)
            {
                // If the answer's metadata already contains all comments, don't do GraphQL requests to obtain them
                foreach (JToken comment in answerEdges.AsIterable())
                    yield return GraphqlAnswer((JObject)comment["node"]);
                yield break;
            }
            foreach (PostCommentAnswer answer in new NodeIterator<PostCommentAnswer>(
                _context,
                "51fdd02b67508306ad4484ff574a0b62",
                d => (JObject)d["data"]["comment"]["edge_threaded_comments"],
                GraphqlAnswer,
                new JObject { { "comment_id", node["id"] } },
                string.Format("https://www.instagram.com/p/{0}/", Shortcode)))
                yield return answer;
        }

        private PostComment GraphqlComment(JObject node)
        {
            return new PostComment(_context, node, GraphqlAnswers(node), this);
        }

        /// <summary>Iterate over all comments of the post.</summary>
        public IEnumerable<PostComment> GetComments()
        {
            if (!_context.IsLoggedIn)
                throw new LoginRequiredException("Login required to access comments of a post.");

            if (Comments == 0)
                // Avoid doing additional requests if there are no comments
                return new List<PostComment>();

            JToken commentEdges;
            try
            {
                commentEdges = Field("edge_media_to_parent_comment", "edges");
            }
            catch (PyKeyError)
            {
                commentEdges = Field("edge_media_to_comment", "edges");
            }

            BigInteger answersCount = 0;
            foreach (JToken edge in commentEdges.AsIterable())
                answersCount += edge["node"].Get("edge_threaded_comments", new JObject()).Get("count", 0).ToBigInteger();

            if (Comments == commentEdges.Count + answersCount)
            {
                // If the Post's metadata already contains all parent comments, don't do GraphQL requests to obtain them
                var result = new List<PostComment>();
                foreach (JToken comment in commentEdges.AsIterable())
                    result.Add(GraphqlComment((JObject)comment["node"]));
                return result;
            }

            if (Comments > NodeIterator<PostComment>.PageLength())
                // comments pagination via our graphql query does not work reliably anymore (issue #2125), fallback to an
                // iphone endpoint if needed.
                return GetCommentsViaIphoneEndpoint();

            return new NodeIterator<PostComment>(
                _context,
                "97b41c52301f77ce508f55e66d17620e",
                d => (JObject)d["data"]["shortcode_media"]["edge_media_to_parent_comment"],
                GraphqlComment,
                new JObject { { "shortcode", Shortcode } },
                string.Format("https://www.instagram.com/p/{0}/", Shortcode));
        }

        /// <summary>Iterate over all likes of the post. A Profile instance of each likee is yielded.</summary>
        public IEnumerable<Profile> GetLikes()
        {
            if (!_context.IsLoggedIn)
                throw new LoginRequiredException("Login required to access likes of a post.");
            return GetLikesImpl();
        }

        private IEnumerable<Profile> GetLikesImpl()
        {
            if (Likes == 0)
                // Avoid doing additional requests if there are no comments
                yield break;
            // Up to 99 likes, as in Instaloader 4.15.5; its previous query is no longer answered.
            JObject data = _context.GetJson(string.Format(System.Globalization.CultureInfo.InvariantCulture, "api/v1/media/{0}/likers/", Mediaid),
                                            new JObject());
            foreach (JToken user in data.Get("users", new JArray()).AsIterable())
                yield return new Profile(_context, (JObject)user);
        }

        /// <summary>Whether Post is a sponsored post, equivalent to non-empty <see cref="SponsorUsers"/>.</summary>
        public bool IsSponsored
        {
            get
            {
                JToken sponsorEdges;
                try
                {
                    sponsorEdges = Field("edge_media_to_sponsor_user", "edges");
                }
                catch (PyKeyError)
                {
                    return false;
                }
                return sponsorEdges.Truthy;
            }
        }

        /// <summary>The Post's sponsors.</summary>
        public List<Profile> SponsorUsers
        {
            get
            {
                var result = new List<Profile>();
                if (!IsSponsored)
                    return result;
                foreach (JToken edge in Field("edge_media_to_sponsor_user", "edges").AsIterable())
                    result.Add(new Profile(_context, (JObject)edge["node"]["sponsor"]));
                return result;
            }
        }

        /// <summary>
        /// If the Post has a location, returns PostLocation with fields 'id', 'lat' and 'lng' and 'name'
        /// ('slug' and 'has_public_page' are null, as Instagram does not provide them anymore).
        /// </summary>
        public PostLocation Location
        {
            get
            {
                JToken loc = Field("location");
                if (_location != null || !loc.Truthy)
                    return _location;
                // Instagram's current metadata calls the location ID "pk" (formerly "id") and no longer includes
                // "slug" and "has_public_page"; the name and coordinates, which geotags need, are included, also
                // for anonymous sessions. Instaloader 4.15.4 fails here with KeyError: 'id'.
                JToken id = loc.Get("id").Or(null) ?? loc.Get("pk");
                long locationId = id.IsNone() ? 0 : id.ToInt64();
                bool missing = false;
                foreach (string k in new[] { "name", "lat", "lng" })
                    if (!loc.ContainsKey(k))
                        missing = true;
                if (missing && locationId != 0 && _context.IsLoggedIn)
                {
                    // Fallback for incomplete location data. Instagram has stopped answering this endpoint with
                    // data, so a failure only costs the missing fields instead of aborting the download.
                    try
                    {
                        JToken info = _context.GetJson(string.Format("explore/locations/{0}/", locationId),
                                                       new JObject { { "__a", 1 }, { "__d", "dis" } })
                            ["native_location_data"]["location_info"];
                        ((JObject)loc).Update(info.AsObject());
                    }
                    catch (Exception err) when (err is ConnectionException || err is PyKeyError || err is PyTypeError)
                    {
                        _context.Error(string.Format("Unable to fetch details of the location of {0}: {1}", this, err.Message));
                    }
                }
                if (loc.Get("name").IsNone())
                    return null;
                _location = new PostLocation(locationId, (string)loc["name"], (string)(loc.Get("slug") ?? JValue.Null),
                                             (bool?)loc.Get("has_public_page"), loc.Get("lat") ?? JValue.Null,
                                             loc.Get("lng") ?? JValue.Null);
                return _location;
            }
        }

        /// <summary>
        /// Deprecated: This information is not returned by IG anymore.
        /// Used to return true if this Post has been pinned by at least one user, now likely returns always false.
        /// </summary>
        public bool IsPinned
        {
            get { return _node.ContainsKey("pinned_for_users") && _node["pinned_for_users"].Truthy; }
        }
    }
}
