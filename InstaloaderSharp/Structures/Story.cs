using System;
using System.Collections.Generic;
using System.Globalization;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;

namespace InstaloaderSharp
{
    /// <summary>
    /// Structure containing information about a user story item i.e. image or video.
    ///
    /// Created by method <see cref="Story.GetItems"/>. This class implements == and is hashable.
    /// </summary>
    public class StoryItem : IJsonExportable, IPyObject
    {
        private readonly InstaloaderContext _context;
        private readonly JObject _node;
        private InstaloaderSharp.Profile _ownerProfile;

        /// <summary>The iPhone API struct: null if not yet fetched, empty if not available.</summary>
        internal JObject IphoneStructRaw;

        /// <param name="context">InstaloaderContext instance used for additional queries if necessary.</param>
        /// <param name="node">Dictionary containing the available information of the story item.</param>
        /// <param name="ownerProfile">Profile instance representing the story owner.</param>
        public StoryItem(InstaloaderContext context, JObject node, InstaloaderSharp.Profile ownerProfile = null)
        {
            _context = context;
            _node = node;
            _ownerProfile = ownerProfile;
            IphoneStructRaw = null;
            if (node.ContainsKey("iphone_struct"))
                // if loaded from JSON with LoadStructureFromFile()
                IphoneStructRaw = node["iphone_struct"] as JObject;
        }

        public InstaloaderContext Context { get { return _context; } }

        /// <summary>The raw node structure.</summary>
        public JObject Node { get { return _node; } }

        public string NodeType { get { return "StoryItem"; } }

        public JObject AsDict()
        {
            JObject node = _node;
            if (_ownerProfile != null)
                node["owner"] = _ownerProfile.AsDict();
            if (IphoneStructRaw != null && IphoneStructRaw.Truthy)
                node["iphone_struct"] = IphoneStructRaw;
            return node;
        }

        /// <summary>The mediaid is a decimal representation of the media shortcode.</summary>
        public long Mediaid { get { return _node["id"].ToInt64(); } }

        /// <summary>
        /// Convert <see cref="Mediaid"/> to a shortcode-like string, allowing {shortcode} to be used with
        /// --filename-pattern.
        /// </summary>
        public string Shortcode { get { return Post.MediaidToShortcode(Mediaid); } }

        public string PyStr() { return PyRepr(); }

        public string PyRepr() { return string.Format("<StoryItem {0}>", Mediaid); }

        public override string ToString() { return PyRepr(); }

        public override bool Equals(object obj)
        {
            var other = obj as StoryItem;
            return other != null && Mediaid == other.Mediaid;
        }

        public override int GetHashCode()
        {
            return Mediaid.GetHashCode();
        }

        /// <summary>Create a StoryItem object from a given mediaid.</summary>
        public static StoryItem FromMediaid(InstaloaderContext context, long mediaid)
        {
            JObject picJson = context.GraphqlQuery(
                "2b0673e0dc4580674a88d426fe00ea90",
                new JObject { { "shortcode", Post.MediaidToShortcode(mediaid) } });
            JToken shortcodeMedia = picJson["data"]["shortcode_media"];
            if (shortcodeMedia.IsNull)
                throw new BadResponseException("Fetching StoryItem metadata failed.");
            return new StoryItem(context, shortcodeMedia.AsObject());
        }

        private JObject IphoneStruct
        {
            get
            {
                if (!_context.IphoneSupport)
                    throw new IPhoneSupportDisabledException("iPhone support is disabled.");
                if (!_context.IsLoggedIn)
                    throw new LoginRequiredException("Login required to access iPhone media info endpoint.");
                if (IphoneStructRaw == null || !IphoneStructRaw.Truthy)
                {
                    JObject data = _context.GetIphoneJson(
                        string.Format("api/v1/feed/reels_media/?reel_ids={0}", OwnerId), new JObject());
                    IphoneStructRaw = new JObject();
                    foreach (JToken item in data["reels"][OwnerId.ToString(CultureInfo.InvariantCulture)]["items"].AsIterable())
                    {
                        if (JToken.DeepEquals(item["pk"], Mediaid))
                        {
                            IphoneStructRaw = (JObject)item;
                            break;
                        }
                    }
                }
                return IphoneStructRaw;
            }
        }

        /// <summary>Profile instance of the story item's owner.</summary>
        public InstaloaderSharp.Profile OwnerProfile
        {
            get
            {
                if (_ownerProfile == null)
                    _ownerProfile = InstaloaderSharp.Profile.FromId(_context, _node["owner"]["id"].ToInt64());
                return _ownerProfile;
            }
        }

        /// <summary>The StoryItem owner's lowercase name.</summary>
        public string OwnerUsername { get { return OwnerProfile.Username; } }

        /// <summary>The ID of the StoryItem owner.</summary>
        public long OwnerId { get { return OwnerProfile.Userid; } }

        /// <summary>Timestamp when the StoryItem was created (local time zone).</summary>
        public DateTimeOffset DateLocal
        {
            get { return StructureHelpers.LocalFromTimestamp(_node["taken_at_timestamp"]); }
        }

        /// <summary>Timestamp when the StoryItem was created (UTC).</summary>
        public DateTime DateUtc
        {
            get { return StructureHelpers.UtcFromTimestamp(_node["taken_at_timestamp"]); }
        }

        /// <summary>Synonym to <see cref="DateUtc"/></summary>
        public DateTime Date { get { return DateUtc; } }

        /// <summary>Synonym to <see cref="OwnerUsername"/></summary>
        public string Profile { get { return OwnerUsername; } }

        /// <summary>Timestamp when the StoryItem will get unavailable (local time zone).</summary>
        public DateTime ExpiringLocal
        {
            get { return StructureHelpers.NaiveLocalFromTimestamp(_node["expiring_at_timestamp"]); }
        }

        /// <summary>Timestamp when the StoryItem will get unavailable (UTC).</summary>
        public DateTime ExpiringUtc
        {
            get { return StructureHelpers.UtcFromTimestamp(_node["expiring_at_timestamp"]); }
        }

        private bool IphoneStructIsNotEmpty
        {
            // Python: self._iphone_struct_ != {}
            get { return IphoneStructRaw == null || IphoneStructRaw.Count > 0; }
        }

        /// <summary>URL of the picture / video thumbnail of the StoryItem</summary>
        public string Url
        {
            get
            {
                if ((Typename == "GraphStoryImage" || Typename == "StoryImage") &&
                    _context.IphoneSupport && _context.IsLoggedIn && IphoneStructIsNotEmpty)
                {
                    try
                    {
                        string origUrl = (string)IphoneStruct["image_versions2"]["candidates"][0]["url"];
                        return StructureHelpers.StripSeParam(origUrl);
                    }
                    catch (Exception err) when (err is InstaloaderException || err is PyKeyError || err is PyIndexError)
                    {
                        _context.Error(string.Format("Unable to fetch high quality image version of {0}: {1}", this, err.Message));
                    }
                }
                return (string)_node["display_resources"][-1]["src"];
            }
        }

        /// <summary>Type of post, GraphStoryImage or GraphStoryVideo</summary>
        public string Typename { get { return (string)_node["__typename"]; } }

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

        /// <summary>List of all lowercased hashtags (without preceding #) that occur in the StoryItem's caption.</summary>
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

        /// <summary>List of all lowercased profiles that are mentioned in the StoryItem's caption, without preceding @.</summary>
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

        /// <summary>True if the StoryItem is a video.</summary>
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
                    versionUrls.Add((string)_node["video_resources"][-1]["src"]);
                }
                catch (Exception err) when (err is InstaloaderException || err is PyKeyError || err is PyIndexError)
                {
                    _context.Error(string.Format("Warning: Unable to fetch video from graphql of {0}: {1}", this, err.Message));
                }
                if (_context.IphoneSupport && _context.IsLoggedIn && IphoneStructIsNotEmpty)
                {
                    try
                    {
                        var urls = new List<string>();
                        foreach (JToken version in IphoneStruct["video_versions"].AsIterable())
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
    }

    /// <summary>
    /// Structure representing a user story with its associated items.
    ///
    /// Provides methods for accessing story properties, as well as <see cref="GetItems"/> to request associated
    /// StoryItem nodes. Stories are returned by <see cref="Instaloader.GetStories"/>.
    ///
    /// This class implements == and is hashable.
    /// </summary>
    public class Story : IPyObject
    {
        protected readonly InstaloaderContext ContextField;
        protected readonly JObject NodeField;
        private string _uniqueId;
        protected Profile OwnerProfileField;
        protected JObject IphoneStructField;

        public Story(InstaloaderContext context, JObject node)
        {
            ContextField = context;
            NodeField = node;
            _uniqueId = null;
            OwnerProfileField = null;
            IphoneStructField = null;
        }

        /// <summary>
        /// Creates a Story from a reel of the app's reels_media, in the form of the GraphQL story reels. The reel is
        /// kept as the items' iPhone struct, so that it is not requested again.
        /// </summary>
        internal static Story FromIphoneReel(InstaloaderContext context, JObject reel)
        {
            JToken user = reel["user"];
            var items = new JArray();
            foreach (JToken item in reel.Get("items", new JArray()).AsIterable())
                items.Add(StoryItemNodeFromIphoneItem((JObject)item, user));
            var node = new JObject
            {
                { "id", Python.PyStr.Str(reel["id"]) },
                { "latest_reel_media", reel.Get("latest_reel_media", JValue.Null) },
                { "seen", reel.Get("seen").Or(JValue.Null) },
                { "expiring_at", reel.Get("expiring_at", JValue.Null) },
                { "user", user },
                { "items", items }
            };
            return new Story(context, node) { IphoneStructField = reel };
        }

        /// <summary>Converts an item of the app's reels_media into the form of the GraphQL story items.</summary>
        /// <param name="item">The app's item.</param>
        /// <param name="owner">The app's user of the reel, for items whose user has no username.</param>
        internal static JObject StoryItemNodeFromIphoneItem(JObject item, JToken owner = null)
        {
            JToken mediaType = item.Get("media_type");
            bool isVideo = mediaType != null && mediaType.Type == JTokenType.Integer && mediaType.ToInt64() == 2;
            JToken takenAt = item["taken_at"];
            JToken user = item.Get("user").Or(new JObject());
            if (user.Get("username").IsNone() && owner is JObject)
                user = owner;
            var node = new JObject
            {
                { "__typename", isVideo ? "GraphStoryVideo" : "GraphStoryImage" },
                { "id", Python.PyStr.Str(item["pk"]) },
                { "is_video", isVideo },
                { "taken_at_timestamp", takenAt },
                // The app's highlight items have no expiry; as in the GraphQL items, it is 24 hours after posting.
                { "expiring_at_timestamp", item.Get("expiring_at").Or(null) ?? (JToken)(takenAt.ToInt64() + 24 * 60 * 60) },
                { "owner", new JObject { { "id", Python.PyStr.Str(user.Get("pk")) }, { "username", user.Get("username").Or("") } } }
            };
            JToken candidates = item.Get("image_versions2").Or(new JObject()).Get("candidates").Or(new JArray());
            node["display_resources"] = candidates.Truthy
                ? new JArray { new JObject { { "src", candidates[0]["url"] } } }
                : new JArray();
            JToken videos = item.Get("video_versions").Or(new JArray());
            if (isVideo && videos.Truthy)
                node["video_resources"] = new JArray { new JObject { { "src", videos[0]["url"] } } };
            JToken caption = item.Get("caption");
            JToken captionText = caption is JObject ? caption.Get("text") : null;
            if (!captionText.IsNone())
                node["edge_media_to_caption"] = new JObject
                {
                    { "edges", new JArray { new JObject { { "node", new JObject { { "text", captionText } } } } } }
                };
            node["iphone_struct"] = item;
            return node;
        }

        public InstaloaderContext Context { get { return ContextField; } }

        /// <summary>The raw node structure.</summary>
        public JObject Node { get { return NodeField; } }

        public virtual string PyStr() { return PyRepr(); }

        public virtual string PyRepr()
        {
            return string.Format("<Story by {0} changed {1}>", OwnerUsername,
                                 PyDateTime.FromObject(LatestMediaUtc).Strftime("%Y-%m-%d_%H-%M-%S_UTC"));
        }

        public override string ToString() { return PyRepr(); }

        public override bool Equals(object obj)
        {
            var other = obj as Story;
            return other != null && Equals(UniqueId, other.UniqueId);
        }

        public override int GetHashCode()
        {
            return UniqueId.GetHashCode();
        }

        /// <summary>
        /// This ID only equals amongst Story instances which have the same owner and the same set of StoryItem.
        /// For all other Story instances this ID is different.
        /// </summary>
        public virtual object UniqueId
        {
            get
            {
                if (string.IsNullOrEmpty(_uniqueId))
                {
                    var idList = new List<long>();
                    foreach (StoryItem item in GetItems())
                        idList.Add(item.Mediaid);
                    idList.Sort();
                    var parts = new List<string> { OwnerId.ToString(CultureInfo.InvariantCulture) };
                    foreach (long id in idList)
                        parts.Add(id.ToString(CultureInfo.InvariantCulture));
                    _uniqueId = string.Join("", parts);
                }
                return _uniqueId;
            }
        }

        /// <summary>Timestamp of the most recent StoryItem that has been watched or null (local time zone).</summary>
        public DateTime? LastSeenLocal
        {
            get
            {
                if (NodeField["seen"].Truthy)
                    return StructureHelpers.NaiveLocalFromTimestamp(NodeField["seen"]);
                return null;
            }
        }

        /// <summary>Timestamp of the most recent StoryItem that has been watched or null (UTC).</summary>
        public DateTime? LastSeenUtc
        {
            get
            {
                if (NodeField["seen"].Truthy)
                    return StructureHelpers.UtcFromTimestamp(NodeField["seen"]);
                return null;
            }
        }

        /// <summary>Timestamp when the last item of the story was created (local time zone).</summary>
        public DateTime LatestMediaLocal
        {
            get { return StructureHelpers.NaiveLocalFromTimestamp(NodeField["latest_reel_media"]); }
        }

        /// <summary>Timestamp when the last item of the story was created (UTC).</summary>
        public DateTime LatestMediaUtc
        {
            get { return StructureHelpers.UtcFromTimestamp(NodeField["latest_reel_media"]); }
        }

        /// <summary>Count of items associated with the Story instance.</summary>
        public virtual int Itemcount { get { return NodeField["items"].Count; } }

        /// <summary>Profile instance of the story owner.</summary>
        public virtual Profile OwnerProfile
        {
            get
            {
                if (OwnerProfileField == null)
                    OwnerProfileField = new Profile(ContextField, (JObject)NodeField["user"]);
                return OwnerProfileField;
            }
        }

        /// <summary>The story owner's lowercase username.</summary>
        public string OwnerUsername { get { return OwnerProfile.Username; } }

        /// <summary>The story owner's ID.</summary>
        public long OwnerId { get { return OwnerProfile.Userid; } }

        protected virtual void FetchIphoneStruct()
        {
            if (ContextField.IphoneSupport && ContextField.IsLoggedIn && (IphoneStructField == null || !IphoneStructField.Truthy))
            {
                JObject data = ContextField.GetIphoneJson(
                    string.Format("api/v1/feed/reels_media/?reel_ids={0}", OwnerId), new JObject());
                // The iPhone API sometimes leaves the reel out; the GraphQL items are used then.
                JToken reel = data.Get("reels", new JObject()).Get(OwnerId.ToString(CultureInfo.InvariantCulture));
                IphoneStructField = reel.IsNone() ? null : reel.AsObject();
            }
        }

        protected StoryItem MakeStoryItem(JObject item)
        {
            if (IphoneStructField != null)
            {
                foreach (JToken iphoneStructItem in IphoneStructField["items"].AsIterable())
                {
                    if (JToken.DeepEquals(iphoneStructItem["pk"], item["id"].ToBigInteger()))
                    {
                        item["iphone_struct"] = iphoneStructItem;
                        break;
                    }
                }
            }
            var storyItem = new StoryItem(ContextField, item, OwnerProfile);
            if (storyItem.IphoneStructRaw == null)
                // StoryItem would repeat the lookup that just came back without this item; an
                // empty struct makes it use the GraphQL URLs instead.
                storyItem.IphoneStructRaw = new JObject();
            return storyItem;
        }

        /// <summary>Retrieve all items from a story.</summary>
        public virtual IEnumerable<StoryItem> GetItems()
        {
            FetchIphoneStruct();
            var items = new List<JToken>(NodeField["items"].AsIterable());
            items.Reverse();
            foreach (JToken item in items)
                yield return MakeStoryItem((JObject)item);
        }
    }

    /// <summary>
    /// Structure representing a user's highlight with its associated story items.
    ///
    /// Provides methods for accessing highlight properties, as well as <see cref="GetItems"/> to request associated
    /// StoryItem nodes. Highlights are returned by <see cref="Instaloader.GetHighlights(Profile)"/>.
    ///
    /// This class implements == and is hashable.
    /// </summary>
    public class Highlight : Story
    {
        private JArray _items;

        /// <param name="context">InstaloaderContext instance used for additional queries if necessary.</param>
        /// <param name="node">Dictionary containing the available information of the highlight as returned by Instagram.</param>
        /// <param name="owner">Profile instance representing the owner profile of the highlight.</param>
        public Highlight(InstaloaderContext context, JObject node, Profile owner = null)
            : base(context, node)
        {
            OwnerProfileField = owner;
            _items = null;
            IphoneStructField = null;
        }

        /// <summary>
        /// Creates a Highlight from an item of the app's highlights tray, in the form of the GraphQL highlight nodes.
        /// </summary>
        internal static Highlight FromIphoneTrayItem(InstaloaderContext context, JObject trayItem, Profile owner)
        {
            string id = Python.PyStr.Str(trayItem["id"]);
            if (id.StartsWith("highlight:", StringComparison.Ordinal))
                id = id.Substring("highlight:".Length);
            JToken cover = trayItem.Get("cover_media").Or(new JObject());
            JToken cropped = cover.Get("cropped_image_version").Or(new JObject()).Get("url", JValue.Null);
            JToken full = cover.Get("full_image_version").Or(new JObject()).Get("url").Or(cropped);
            JToken user = trayItem.Get("user").Or(new JObject());
            var node = new JObject
            {
                { "__typename", "GraphHighlightReel" },
                { "id", id },
                { "title", trayItem.Get("title", JValue.Null) },
                { "cover_media", new JObject { { "thumbnail_src", full } } },
                { "cover_media_cropped_thumbnail", new JObject { { "url", cropped } } },
                { "latest_reel_media", trayItem.Get("latest_reel_media", JValue.Null) },
                { "highlight_reel_type", trayItem.Get("highlight_reel_type", JValue.Null) },
                { "media_count", trayItem.Get("media_count", JValue.Null) },
                {
                    "owner", new JObject
                    {
                        { "id", Python.PyStr.Str(user.Get("pk") ?? user.Get("id")) },
                        { "profile_pic_url", user.Get("profile_pic_url", JValue.Null) },
                        { "username", user.Get("username", JValue.Null) }
                    }
                }
            };
            return new Highlight(context, node, owner);
        }

        public override string PyRepr()
        {
            return string.Format("<Highlight by {0}: {1}>", OwnerUsername, Title);
        }

        /// <summary>A unique ID identifying this set of highlights.</summary>
        public override object UniqueId { get { return NodeField["id"].ToInt64(); } }

        /// <summary>A unique ID identifying this set of highlights.</summary>
        public long HighlightId { get { return NodeField["id"].ToInt64(); } }

        /// <summary>Profile instance of the highlights' owner.</summary>
        public override Profile OwnerProfile
        {
            get
            {
                if (OwnerProfileField == null)
                    OwnerProfileField = new Profile(ContextField, (JObject)NodeField["owner"]);
                return OwnerProfileField;
            }
        }

        /// <summary>The title of these highlights.</summary>
        public string Title { get { return (string)NodeField["title"]; } }

        /// <summary>URL of the highlights' cover, or null if Instagram has not given one.</summary>
        public string CoverUrl
        {
            get { return (string)NodeField.Get("cover_media").Or(new JObject()).Get("thumbnail_src", JValue.Null); }
        }

        /// <summary>URL of the cropped version of the cover, or null if Instagram has not given one.</summary>
        public string CoverCroppedUrl
        {
            get { return (string)NodeField.Get("cover_media_cropped_thumbnail").Or(new JObject()).Get("url", JValue.Null); }
        }

        /// <summary>Whether these highlights are only for the subscribers of their owner.</summary>
        public bool IsSubscribersOnly
        {
            get { return (string)NodeField.Get("highlight_reel_type", JValue.Null) == "FAN_CLUB"; }
        }

        /// <summary>
        /// Whether these highlights are only for subscribers and Instagram has given no items of them, as for
        /// accounts that are not subscribed: no cover and a count of 0 items.
        /// </summary>
        internal bool IsLockedForSubscribers
        {
            get
            {
                JToken mediaCount = NodeField.Get("media_count", JValue.Null);
                return IsSubscribersOnly && (mediaCount.IsNone() || mediaCount.ToInt64() == 0);
            }
        }

        private void FetchItems()
        {
            if (_items == null || !_items.Truthy)
            {
                // The app's reels_media, which is requested for the items' best versions anyway, contains the items
                // too. Instaloader's GraphQL query is redirected to Instagram's home page for some accounts, and
                // refused with 400 "invalid request" for some highlights (also in Instaloader 4.16); it is used when
                // the app does not answer with the highlight, and with --no-iphone.
                if (ContextField.IphoneSupport && ContextField.IsLoggedIn)
                {
                    try
                    {
                        FetchIphoneStruct();
                    }
                    catch (Exception ex) when (ex is QueryReturnedBadRequestException || ex is ConnectionException)
                    {
                        ContextField.Error(string.Format("Unable to fetch the items of {0} from the app: {1}", this, ex.Message));
                    }
                    if (IphoneStructField != null)
                    {
                        var items = new JArray();
                        foreach (JToken item in IphoneStructField.Get("items", new JArray()).AsIterable())
                            items.Add(StoryItemNodeFromIphoneItem((JObject)item, IphoneStructField.Get("user")));
                        _items = items;
                        return;
                    }
                }
                _items = ContextField.GraphqlQuery(
                    "45246d3fe16ccc6577e0bd297a5db1ab",
                    new JObject
                    {
                        { "reel_ids", new JArray() },
                        { "tag_names", new JArray() },
                        { "location_ids", new JArray() },
                        { "highlight_reel_ids", new JArray { HighlightId.ToString(CultureInfo.InvariantCulture) } },
                        { "precomposed_overlay", false }
                    })["data"]["reels_media"][0]["items"].AsArray();
            }
        }

        protected override void FetchIphoneStruct()
        {
            if (ContextField.IphoneSupport && ContextField.IsLoggedIn && (IphoneStructField == null || !IphoneStructField.Truthy))
            {
                JObject data = ContextField.GetIphoneJson(
                    string.Format("api/v1/feed/reels_media/?reel_ids=highlight:{0}", HighlightId), new JObject());
                // The iPhone API sometimes leaves the highlight out; the GraphQL items are used then.
                JToken reel = data.Get("reels", new JObject()).Get(string.Format("highlight:{0}", HighlightId));
                IphoneStructField = reel.IsNone() ? null : reel.AsObject();
            }
        }

        /// <summary>Count of items associated with the Highlight instance.</summary>
        public override int Itemcount
        {
            get
            {
                FetchItems();
                return _items.Count;
            }
        }

        /// <summary>Retrieve all associated highlight items.</summary>
        public override IEnumerable<StoryItem> GetItems()
        {
            FetchItems();
            FetchIphoneStruct();
            foreach (JToken item in new List<JToken>(_items))
                // StoryItem would look the item up in the owner's current story, which never
                // holds highlight items; an empty struct makes it use the GraphQL URLs instead.
                yield return MakeStoryItem((JObject)item);
        }
    }
}
