# InstaloaderSharp

A C# / .NET Framework version of [Instaloader](https://instaloader.github.io/).
It downloads pictures and videos along with their captions and other metadata from Instagram: whole
profiles, single posts, hashtags, locations, your feed, your saved posts, stories and highlights.

This version of InstaloaderSharp, targets compatibility with Instaloader 4.16 but might be compatible with later versions.

No previous knowledge of Instaloader is needed to use it; this document covers everything from a first
download to the advanced options.

If you prefer a graphical user interface, have a look at [InstaloaderSharp GUI](https://github.com/pzyber/InstaloaderSharpGUI).

The full manual is in the [wiki](https://github.com/pzyber/InstaloaderSharp/wiki/Home).

* [Requirements and building](#requirements-and-building)
* [Quick start](#quick-start)
* [Targets: what to download](#targets-what-to-download)
* [Where files go and what they contain](#where-files-go-and-what-they-contain)
* [Logging in](#logging-in)
* [Choosing what to download](#choosing-what-to-download)
* [Keeping an archive up to date](#keeping-an-archive-up-to-date)
* [Filtering posts](#filtering-posts)
* [Customizing file and directory names](#customizing-file-and-directory-names)
* [Interrupting and resuming](#interrupting-and-resuming)
* [Other options](#other-options)
* [Reading options from a file](#reading-options-from-a-file)
* [Exit codes](#exit-codes)
* [Tips and troubleshooting](#tips-and-troubleshooting)
* [Interoperability with Instaloader](#interoperability-with-instaloader)
* [Library use](#library-use)
* [Differences from Instaloader](#differences-from-instaloader)
* [Disclaimer](#disclaimer)

## Requirements and building

* Windows 10 version 1607 or later (requires HTTP/2, which the program gets from Windows' built-in
  WinHTTP).
* .NET Framework 4.8.1.

Open `InstaloaderSharp.sln` in Visual Studio (2015 or later) and build, or run:

```
"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" InstaloaderSharp.sln /p:Configuration=Release
```

The result is a single file, `InstaloaderSharp\bin\Release\InstaloaderSharp.exe`, without other
dependencies. Copy it wherever you like. You may rename it, for example to `instaloader.exe`; the examples
below use `InstaloaderSharp.exe`.

The solution also contains the unit tests (`InstaloaderSharp.Tests`), which need Visual Studio's MSTest
framework; see [CONTRIBUTING.md](CONTRIBUTING.md#unit-tests). Without Visual Studio, build only the program
with `InstaloaderSharp\InstaloaderSharp.csproj` instead of `InstaloaderSharp.sln` in the command above.

## Quick start

Open a command prompt in the folder where the downloads should be saved and run:

```
InstaloaderSharp.exe instagram
```

This downloads all posts of the public profile `instagram` (pictures, videos, captions and metadata) and
its profile picture into a new folder `instagram`. Progress is printed as it goes:

```
[1/1] Downloading profile instagram
Retrieving posts from profile instagram.
[   1/8123] instagram\2026-10-08_17-10-31_UTC.jpg [fur real, every single one of…] instagram\2026-10-08_17-10-31_UTC.mp4 json
[   2/8123] ...
```

Run the same command again later and only new posts are downloaded, while existing files are skipped
(`... exists`). Add `--fast-update` (`-F`) to stop as soon as the first already-downloaded post is reached,
which makes updates much faster:

```
InstaloaderSharp.exe --fast-update instagram
```

Press **Ctrl+C** at any time to stop. The download can be continued later; see
[Interrupting and resuming](#interrupting-and-resuming).

Show all options with:

```
InstaloaderSharp.exe --help
```

## Targets: what to download

Any number of targets can be given on one command line, e.g. `InstaloaderSharp.exe profile1 profile2 "#cats"`.

| Target | Downloads | Login required |
| --- | --- | --- |
| `profilename` | All posts and the profile picture of a profile. Private profiles require that you are logged in and follow them. | Only for private profiles |
| `@profilename` | All profiles that `profilename` follows (its "followees"). | Yes |
| `"#hashtag"` | Posts with that hashtag. Quote it, since `#` may be special in some shells. | Yes |
| `%location_id` | Posts tagged with a location, given by its numeric Instagram ID (the number in `instagram.com/explore/locations/ID/`). | Yes |
| `:feed` | Posts of your own feed (posts of accounts you follow; posts that Instagram suggests from other accounts are skipped). | Yes |
| `:stories` | Current stories of all profiles you follow. | Yes |
| `:saved` | Posts you marked as saved. | Yes |
| `-- -SHORTCODE` | A single post. The shortcode is the part after `/p/` or `/reel/` in a post's URL, e.g. `https://www.instagram.com/p/DdwalmrkTSK/` → `-- -DdwalmrkTSK`. The `--` is needed because the target starts with `-`; put all options before it. | No (unless the post is private) |
| `file.json` / `file.json.xz` | Re-downloads a post or story item from a metadata file that was saved earlier. | Depends |

Examples:

```
InstaloaderSharp.exe -- -DdwalmrkTSK
InstaloaderSharp.exe --login myname --count 50 "#sunset"
InstaloaderSharp.exe --login myname :stories :saved
InstaloaderSharp.exe instagram\2026-09-26_16-01-00_UTC.json.xz
```

A trailing `/` on a target is ignored, so shell auto-completion of a downloaded folder name works.

## Where files go and what they contain

By default, every target gets its own folder (named after the target) in the current directory. For a
profile, the folder looks like this:

```
instagram\
    2025-09-20_00-00-36_UTC_profile_pic.jpg   profile picture (date = when it was uploaded)
    id                                        the profile's numeric ID (used to detect renames)
    instagram_25025320.json.xz                profile metadata
    2026-10-08_17-10-31_UTC.jpg               picture, or thumbnail of a video
    2026-10-08_17-10-31_UTC.mp4               video
    2026-10-08_17-10-31_UTC.txt               caption
    2026-10-08_17-10-31_UTC.json.xz           post metadata (likes, comments count, location, ...)
    2026-09-26_16-01-00_UTC_1.jpg             a post with several pictures ("sidecar")
    2026-09-26_16-01-00_UTC_2.jpg               gets one numbered file per picture/video
    2026-09-26_16-01-00_UTC_3.jpg
```

* File names are the post's date and time in UTC. Each file's modification time is set to the time the post
  was created.
* `.json.xz` files are xz-compressed JSON. Use `--no-compress-json` to get plain, readable `.json` files
  instead, or `--no-metadata-json` to not save them at all.
* Characters that are not allowed in Windows file names are replaced by similar-looking Unicode characters.
  For example, the `:feed` target is saved in a folder named `：feed` (with a full-width colon).
* If a profile is renamed on Instagram, its stored `id` lets the program find it again, and the folder is
  renamed accordingly.
* If a caption changes on Instagram, the old `.txt` file is kept as `..._old_01.txt`.

## Logging in

Public profiles and single public posts can be downloaded without an account. Logging in is needed for
private profiles you follow, stories, highlights, hashtags, locations, your feed and saved posts, and
comments. Logged in, you also get higher-quality pictures and videos.

> **Note:** Downloading in large amounts can get an account temporarily restricted by Instagram. Consider
> using a secondary account.

There are two ways to log in.

### Option 1: with your username and password

```
InstaloaderSharp.exe --login YOUR-USERNAME profilename
```

You are asked for your password (it is not shown while typing). If your account uses two-factor
authentication, you are asked for the code as well. Alternatively the password can be given with
`--password` (`-p`), but it may then end up in your command history.

After a successful login, a **session file** is saved, so you do not need to enter your password again:
next time, `--login YOUR-USERNAME` just loads the session. Session files are stored in
`%LOCALAPPDATA%\Instaloader\session-YOUR-USERNAME`; use `--sessionfile FILE` (`-f`) to choose another
location. The session file contains the session cookie (not your password), but it still grants access to
your account, so keep it private.

### Option 2: import the login from your web browser

If logging in with a password does not work (Instagram sometimes asks for extra verification), log in to
instagram.com in a web browser and import its cookies:

```
InstaloaderSharp.exe --load-cookies firefox
```

Supported browsers: `chrome`, `chromium`, `edge`, `brave`, `opera`, `opera_gx`, `vivaldi`, `firefox` and
`librewolf`. The program prints which account it is logged in as and saves a session file, so afterwards
you can simply use `--login YOUR-USERNAME`. To read cookies of a specific browser profile, pass its cookie
database with `--cookiefile FILE` (`-B`). Chromium-based browsers may need to be closed while cookies are
read. Firefox usually works best, because recent Chrome versions encrypt cookies in a way other programs
cannot decrypt.

`--login` and `--load-cookies` cannot be used together.

## Choosing what to download

For every post (by default pictures, videos, video thumbnails, captions and metadata are downloaded):

| Option | Effect |
| --- | --- |
| `--no-pictures` | Do not download pictures (and video thumbnails). Cannot be combined with `--fast-update`. |
| `--no-videos`, `-V` | Do not download videos. |
| `--no-video-thumbnails` | Do not download the thumbnail pictures of videos. |
| `--no-captions` | Do not create caption `.txt` files. |
| `--no-metadata-json` | Do not create `.json.xz` metadata files. |
| `--no-compress-json` | Create readable `.json` files instead of compressed `.json.xz` files. |
| `--geotags`, `-G` | Also save the location of each post, with a Google Maps link, as `..._location.txt`. |
| `--comments`, `-C` | Also save the comments of each post as `..._comments.json`; existing files are updated (login required). |
| `--slide N` | For posts with several pictures, download only some of them: `--slide 2` (the second), `--slide last`, `--slide 2-4`, `--slide 3-last`. |
| `--post-metadata-txt TEMPLATE` | What to write into the caption `.txt` file, e.g. `--post-metadata-txt "{likes} likes, {comments} comments"`. Can be given several times (one line each). See [templates](#customizing-file-and-directory-names). |
| `--storyitem-metadata-txt TEMPLATE` | Like `--post-metadata-txt`, for stories (no file is written by default). |

For every profile:

| Option | Effect |
| --- | --- |
| `--no-posts` | Do not download the profile's posts (e.g. only the profile picture and metadata). |
| `--no-profile-pic` | Do not download the profile picture. |
| `--stories`, `-s` | Also download the profile's current stories (login required). |
| `--highlights` | Also download the profile's story highlights; each highlight gets a subfolder (login required). |
| `--tagged` | Also download posts in which the profile is tagged (into a `：tagged` subfolder, with a full-width colon). |
| `--reels` | Also download the profile's reels. |
| `--igtv` | No effect: Instagram has retired IGTV. Former IGTV videos are regular posts and reels now, downloaded with the posts and with `--reels`. Accepted for compatibility. |

Example: download stories and highlights of two profiles, but no posts:

```
InstaloaderSharp.exe --login myname --no-posts --stories --highlights profile1 profile2
```

## Keeping an archive up to date

* `--fast-update` (`-F`): for every target, stop at the first post that was already downloaded. Up to three
  pinned posts at the top of a profile are taken into account.
* `--latest-stamps [FILE]`: remember the date of the newest downloaded post, story, reel etc. of every
  profile in a small database file (by default
  `%LOCALAPPDATA%\Instaloader\latest-stamps.ini`), and only download newer items next time. Unlike
  `--fast-update`, this works even if you delete or move the downloaded files.
* `--count N` (`-c`): download at most N posts. Applies to hashtags, locations, `:feed` and `:saved`.

```
InstaloaderSharp.exe --fast-update --login myname --stories profile1 profile2
InstaloaderSharp.exe --latest-stamps --login myname --stories profile1 profile2
```

## Filtering posts

`--post-filter EXPRESSION` downloads only the posts for which the expression is true. Use the post
properties listed below as variables. The syntax is that of Python expressions: comparison operators,
`and`, `or`, `not`, `in`, arithmetic, strings in quotes, and lists. `datetime(year, month, day)` creates a
date to compare with.

```
InstaloaderSharp.exe --post-filter="likes > 1000" profilename
InstaloaderSharp.exe --post-filter="not is_video" profilename
InstaloaderSharp.exe --post-filter="date_utc >= datetime(2024, 1, 1)" profilename
InstaloaderSharp.exe --post-filter="'travel' in caption_hashtags and likes > 100" profilename
InstaloaderSharp.exe --login myname --post-filter="viewer_has_liked" :feed
```

Wrap the expression in double quotes and use single quotes for strings inside it. Posts that do not match
are reported as `skipped`. If the expression cannot be evaluated for a post (for example because a property
is missing), the post is skipped with an error message.

Useful post properties:

| Property | Meaning |
| --- | --- |
| `date_utc`, `date_local` | When the post was created (UTC, or your local time zone). Compare `date_utc` with `datetime(...)`; `date_local` includes a time zone and cannot be compared with `datetime(...)`. |
| `likes`, `comments` | Number of likes and comments. |
| `is_video`, `typename` | Whether it is a video; the kind of post: `'GraphImage'`, `'GraphVideo'` or `'GraphSidecar'` (several pictures). |
| `mediacount` | Number of pictures/videos in the post. |
| `caption`, `caption_hashtags`, `caption_mentions` | The caption text; lists of the hashtags (lowercase, without `#`) and mentioned users (without `@`). |
| `tagged_users` | List of users tagged in the post. |
| `owner_username`, `owner_id` | The account that posted it. |
| `video_duration`, `video_view_count` | For videos: length in seconds and number of views. |
| `viewer_has_liked` | Whether you liked the post (when logged in). |
| `is_sponsored`, `title`, `accessibility_caption` | Sponsored post; title (of videos); Instagram's automatic description. |
| `shortcode`, `mediaid`, `url`, `video_url` | Identifiers and download URLs. |

Strings have methods like Python's, e.g. `caption.lower()`, `caption.startswith('...')`; dates have
`date_utc.year`, `date_utc.month`, `date_utc.weekday()` etc.

`--storyitem-filter EXPRESSION` works the same for stories, with these properties: `date_utc`,
`date_local`, `expiring_utc`, `is_video`, `typename`, `caption`, `caption_hashtags`, `caption_mentions`,
`owner_username`, `owner_id`, `shortcode`, `mediaid`.

## Customizing file and directory names

Folder and file names are built from templates in which `{name}` is replaced by a property of the post:

| Option | Default | Used for |
| --- | --- | --- |
| `--dirname-pattern` | `{target}` | The folder for posts. `{target}` is the target as given on the command line (profile name, `#hashtag`, `:feed`, ...); `{profile}` is the post owner's username. |
| `--filename-pattern` | `{date_utc}_UTC` | File names of posts and stories, without extension. |
| `--title-pattern` | `{date_utc}_UTC_{typename}` | File names of profile pictures and highlight covers. |

All [post properties](#filtering-posts) can be used, plus `{filename}` (the original file name on
Instagram's server), `{profile}` and `{target}`. Format specifications can follow a colon:

* dates: `{date_utc:%Y-%m-%d}` gives `2026-10-08`; codes are `%Y` year, `%m` month, `%d` day, `%H` hour,
  `%M` minute, `%S` second, `%A` weekday name, `%B` month name (default: `%Y-%m-%d_%H-%M-%S`);
* numbers: `{likes:06d}` gives zero-padded `000150`;
* text: `{caption:.40}` cuts the caption after 40 characters. `{pcaption}` is a shortened single-line
  version of the caption, suitable for file names.

A `/` or `\` in the folder template creates subfolders. Characters that are invalid in file names are
replaced automatically. Use `--sanitize-paths` to also create names that are valid on Linux/macOS.

Examples:

```
InstaloaderSharp.exe --dirname-pattern "D:\Instagram\{profile}" profilename
InstaloaderSharp.exe --filename-pattern "{date_utc:%Y-%m-%d}_{shortcode}" profilename
InstaloaderSharp.exe --dirname-pattern "{target}\{date_utc:%Y}" --filename-pattern "{date_utc:%m-%d_%H%M}_{pcaption}" profilename
```

The same template syntax is used by `--post-metadata-txt` (for the content of the caption files), e.g.
`--post-metadata-txt "{caption}" --post-metadata-txt "Posted on {date_local} - {likes} likes"`.

## Interrupting and resuming

When a download of many posts is interrupted (by Ctrl+C, or because Instagram aborts it), the position is
saved in a file named `iterator_XXXXXXXX.json.xz` in the target's folder:

```
Saved resume information to instagram\iterator_s8YiB9OL.json.xz.
```

Running the same command again continues from that position (`Resuming from ...`), and the file is
deleted when the download completes. Resume files expire after 29 days. Use `--no-resume` to neither use
nor create them, and `--resume-prefix PREFIX` to name them differently.

While a request is being retried after a network error (`[retrying; skip with ^C]`), Ctrl+C skips just
that item instead of stopping everything.

## Other options

| Option | Effect |
| --- | --- |
| `--quiet`, `-q` | Print only errors, never ask questions (fails if a password would be needed). Suitable for scheduled tasks. |
| `--max-connection-attempts N` | Number of attempts for failing requests (default 3; 0 retries forever). |
| `--request-timeout N` | Seconds to wait for a response (default 300). |
| `--abort-on CODES` | Stop immediately if Instagram answers with one of these HTTP status codes, e.g. `--abort-on 302,400,429`. |
| `--no-iphone` | Do not use Instagram's mobile API for higher-quality media when logged in. |
| `--user-agent TEXT` | Browser identification sent to Instagram. |
| `--version` | Print the version of InstaloaderSharp. |
| `--help`, `-h` | Print all options. |

Options can be abbreviated as long as they are unique (`--fast` for `--fast-update`), and given as
`--option=value`. Short options can be combined (`-sF` is `-s -F`).

## Reading options from a file

Arguments can be put into a text file, one argument per line, and passed with `+` before the file name:

```
InstaloaderSharp.exe +args.txt
```

with `args.txt` containing for example:

```
--login=myname
--fast-update
--stories
profile1
profile2
```

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success. |
| 1 | Finished, but errors occurred (they are repeated at the end of the output). |
| 2 | Invalid command line, or nothing to do. |
| 3 | Login failed. |
| 4 | Download aborted (e.g. by `--abort-on`, or because Instagram requires verification). |
| 5 | Interrupted by the user (Ctrl+C). |
| 99 | Unexpected error. |

## Tips and troubleshooting

* **"Too many queries" / "Please wait a few minutes" / HTTP 429 or 401:** Instagram limits how many
  requests can be made. The program waits automatically between requests; when a limit is hit it waits
  longer and retries. Do not run several instances at the same time, and avoid using Instagram apps with
  the same account while downloading.
* **"Login required" / "Redirected to login page":** Instagram refused the request anonymously. Log in
  (see [Logging in](#logging-in)). If you get this while logged in, your session has expired; log in again
  or re-import your browser cookies.
* **"Checkpoint required":** open the given link in a browser, confirm the login there, then try again,
  or use `--load-cookies`.
* **"Instagram answered ... with a web page instead of data, probably a verification (captcha)":** open
  Instagram in the web browser you are logged in with, complete the verification, and let the account rest for a
  while before downloading again.
* **"Profile ... does not exist":** check the spelling; the profile may have been renamed, deleted, or it
  blocked you. When logged in, the program tries again anonymously in that case.
* **Errors during a run** are printed when they occur and repeated at the end; the program continues with
  the next item.

## Interoperability with Instaloader

InstaloaderSharp and Instaloader can be used on the same download folders, in either order:

| Data | Format |
| --- | --- |
| Session files (`%LOCALAPPDATA%\Instaloader\session-USER`) | Python pickle (read: all protocols; write: protocol 4) |
| Metadata (`*.json.xz`, `*.json`) | Should be the same JSON as Python's `json` module (key order, escaping, float formatting, CRLF); xz/LZMA2 |
| Resume files (`iterator_MAGIC.json.xz`) | Should have the same content and the same BLAKE2b "magic" file name, so an interrupted download can be resumed by either program |
| `--latest-stamps` file | Should use the same INI dialect as Python's `configparser` |
| Media, captions, `id` files | Should be byte-identical, with identical file names and modification times |
| Comments (`*_comments.json`), geotags (`*_location.txt`) | Should use the same format and merge logic as Python |

The command line should behave the same too (options, abbreviations, help output, error messages and exit codes).

JSON metadata and resume files record the Instaloader version they were written with. InstaloaderSharp
 writes `"version": "4.16"` there (the Instaloader version it targets), not its own
version number, so its files should be identical to those of Instaloader.

## Library use

The program can also be referenced as a library:

```csharp
var L = new InstaloaderSharp.Instaloader();
var profile = InstaloaderSharp.Profile.FromUsername(L.Context, "instagram");
foreach (var post in profile.GetPosts())
    L.DownloadPost(post, profile.Username);
```

Python names map to PascalCase (`download_post` → `DownloadPost`, `date_utc` → `DateUtc`). Python's
`KeyboardInterrupt` is `KeyboardInterruptException` (^C is turned into it at the next interruption point,
so resume files get saved), and the `with` context managers are methods taking a delegate
(`Context.ErrorCatcher(...)`, `NodeIteration.ResumableIteration(...)`).

## Differences from Instaloader

* `--load-cookies` is built in (Instaloader needs the optional `browser_cookie3` package). Chromium-based
  browsers and Firefox/LibreWolf are supported on Windows; cookies using Chrome's newer app-bound
  encryption (`v20`) cannot be decrypted (browser_cookie3 has the same limitation), and Chrome may need to be
  closed so that its cookie database can be read. Safari is macOS-only.
* Redirects of Instagram's interfaces are followed at most 10 times, and not in a loop. For a suspended or
  restricted account, Instagram redirects every request to its home page and the home page to itself;
  Instaloader then follows the redirects forever. InstaloaderSharp stops the download (exit code 4) with a
  message to check the account in a web browser.
* Syntax errors in filters are reported as an error message (exit code 2) instead of a Python traceback.
* Sets of profiles are processed in the order they were given rather than Instaloader's set order.
* Geotags (`--geotags`) work again, also without logging in. Instagram now names the location ID `pk` and
  no longer provides `slug` and `has_public_page`, which Instaloader fails on (`KeyError: 'id'`); its
  fallback lookup (`explore/locations/`) no longer returns data either. In saved metadata, a post's location
  therefore has no `slug`.
* `--igtv` only reports that Instagram has retired IGTV (former IGTV videos are downloaded as posts and
  reels); Instaloader fails with `KeyError: 'edges'`. Connections that Instagram returns without
  any items are treated as empty instead of failing.
* `:feed` is loaded the way Instagram's website does: the first page from the home page, further pages with the
  query the home page declares. Instagram's suggested posts from accounts you do not follow are skipped.
  Instaloader pages through the app's timeline (`api/v1/feed/timeline/`) instead, which returned fewer posts per
  request in a test (about two, between suggestions).
* `:stories` (the stories of all accounts you follow) works again. The list of accounts with stories comes
  from the app's story tray (`api/v1/feed/reels_tray/`) instead of a query that Instagram no longer answers.
* Followers and followees (`@profile`, `Profile.GetFollowers()`/`GetFollowees()`) are retrieved with the
  friendships API of Instagram's web app; Instaloader's query returns no profiles anymore.
* `Profile.GetFollowedHashtags()` returns an empty list, since Instagram removed following hashtags in
  December 2024; Instaloader fails with a `TypeError`.
* Formatted values in `--dirname-pattern` and `--filename-pattern`, such as `{date_utc:%c}` or
  `{date_utc:%H:%M}`, get the same replacements for characters that are invalid in Windows file names as
  other values (e.g. `:` becomes `：`). Instaloader leaves them unchanged and fails with
  `OSError: [Errno 22] Invalid argument`. Path separators from formats are kept, so `{date_utc:%Y/%m}`
  still creates subdirectories.
* Logged-in GraphQL queries (reels, tagged posts, post details, profile details, further pages of `:feed`) are
  sent the way Instagram's website sends them: with the session's request token (`fb_dtsg`), the query's name
  and the browser's `Sec-Fetch-*` headers. Since October 2026, Instagram redirects queries without them to its
  home page for some accounts (*"HTTP redirect from https://www.instagram.com/graphql/query to
  https://www.instagram.com/"*). Instaloader sends profile posts the website's way too (through `api/graphql`),
  but its other logged-in queries without these fields, so that it cannot load single posts, reels or profile
  details for such accounts.
* The login is checked by asking Instagram's app interface which user the session belongs to
  (`api/v1/accounts/current_user/`). Instaloader checks it with a GraphQL query that Instagram redirects to its
  home page for some accounts, so that a valid session is reported as *"Not logged in. Are you logged in
  successfully in firefox?"*. That query is still used as a fallback, and with `--no-iphone`.
* Reels (`--reels`) are loaded with the query of the website's reels tab, since Instagram answers Instaloader's
  older reels query without reels when it is sent the website's way.
* Logged in, a video whose details already contain its video versions (as those of reels, tagged posts and single
  posts do) is downloaded without asking Instagram's app for its media info
  (`api/v1/media/<ID>/info/`). Instaloader asks for it for every video, for the best version; the versions in it
  were the same sizes and files in all 956 videos compared, so the same file is downloaded. Each reel takes one
  request instead of two: the first `--reels` download of a profile with 1,200 reels took about 2,500 requests,
  enough for Instagram to flag the account (a captcha, then `feedback_required`). The saved metadata of these videos
  has no `iphone_struct`.
* Logged in, the program allows half as many requests as Instaloader within the same time: 100 of each kind of
  query and 150 queries in total per 10 minutes, and 100 requests to the app interface per 30 minutes. Instagram
  limits the requests of an account, and flagged accounts that stayed within Instaloader's limits. Logged out,
  Instaloader's limits apply. Large downloads take longer.
* When Instagram answers a logged-in query with a web page instead of data, usually a verification (captcha), the
  download stops with *"Instagram answered ... with a web page instead of data, probably a verification
  (captcha)"* (exit code 4, resume information is saved). Instaloader retries each query and goes on with the
  next targets, which get the same page and count against the account, until Instagram answers with
  `feedback_required`. The verification has to be completed in a web browser; it cannot be bypassed.
* Tagged posts (`--tagged`) are loaded with the query of the website's tagged tab. Instaloader's query only
  returns the first few tagged posts (5 in a test) and then empty pages, so it stops there. The tagged tab's query
  returns the posts without their dates and videos, so each tagged post is loaded with an extra request.
* Stories and highlights (`--stories`, `--highlights`, `:stories`) are loaded through Instagram's app interface:
  the stories and the highlights' items from `api/v1/feed/reels_media/`, the list of highlights from
  `api/v1/highlights/<ID>/highlights_tray/`. Instaloader uses GraphQL queries that Instagram redirects to its home
  page for some accounts (*"HTTP redirect from https://www.instagram.com/graphql/query to
  https://www.instagram.com/"*), so that it downloads no stories or highlights for them. Instaloader's queries are
  still used when the app does not answer, and with `--no-iphone`. Since the items come from the app, their saved
  metadata has fewer GraphQL fields; the app's complete item is in `iphone_struct`, as before.
* Highlights whose items Instagram refuses to list with Instaloader's query (400 "invalid request") are
  downloaded too. If retrieving one highlight fails, the other highlights are downloaded; Instaloader stops
  downloading the profile's highlights then.
* Restricted profiles (for example age-restricted ones), which Instagram does not show to logged-out
  visitors, are found when logged in: the program reads the profile's ID from the profile page as the session
  sees it, and loads the profile by its ID. Instaloader reports that such profiles do not exist. No similar
  profile names are suggested when a profile cannot be loaded; Instaloader tries the website's top search for
  that, which Instagram has retired (404).
* `TopSearchResults` (searching Instagram for profiles, hashtags and places, in library use) uses the search
  of Instagram's app (`api/v1/fbsearch/topsearch_flat/`, and `api/v1/fbsearch/places/` for places) instead
  of the website's top search, which Instagram has retired (404). It requires login and throws
  `LoginRequiredException` otherwise.

## Disclaimer

InstaloaderSharp is in no way affiliated with, authorized, maintained or endorsed by Instagram or any of its affiliates or subsidiaries. This is an independent and unofficial project. Use at your own risk.
