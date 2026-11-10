## Contents

- [Installation and start-up](#installation-and-start-up)
- [Logging in](#logging-in)
- [Rate limits and blocks](#rate-limits-and-blocks)
- [Profiles and posts](#profiles-and-posts)
- [Stories, highlights, feed and saved posts](#stories-highlights-feed-and-saved-posts)
- [Files and folders](#files-and-folders)
- [Updates and resuming](#updates-and-resuming)
- [Filters and templates](#filters-and-templates)
- [General questions](#general-questions)
- [Getting help](#getting-help)

## Installation and start-up

**Nothing happens when I double-click `InstaloaderSharp.exe`, or a window flashes and closes.**
InstaloaderSharp is a command-line program. Open a command prompt and run it there; see [Getting started](Getting-started#your-first-download).

**Windows says that .NET Framework 4.8.1 is missing.**
Install .NET Framework 4.8.1 from Microsoft. It is a free runtime and is included in recent Windows 11 builds.

**Windows SmartScreen or my antivirus warns about the program.**
The program is not signed, which triggers SmartScreen. Click **More info → Run anyway** if you downloaded it from the [official releases page](https://github.com/pzyber/InstaloaderSharp/releases). You can also [build it yourself](Building-and-contributing#building-from-source).

**`'InstaloaderSharp.exe' is not recognized as an internal or external command`.**
The command prompt cannot find the program. Run it with its full path (`C:\Tools\InstaloaderSharp.exe instagram`), change to its folder first, or add its folder to your `PATH`.

**Unable to initialize WinHTTP, or HTTP/2 errors.**
The program needs Windows 10 version 1607 or later. Check that Windows is up to date.

## Logging in

**The password is not accepted, although it is correct.**
Make sure you give your username, not your e-mail address or phone number, to `--login`. If Instagram refuses the login anyway (`Login error: "fail" status ...`, `Checkpoint required`), log in to instagram.com in Firefox and use `--load-cookies firefox`; see [Logging in](Logging-in#importing-the-login-from-your-web-browser).

**`Checkpoint required. Point your browser to ...`**
Instagram wants to confirm that it is you. Open the link in a browser where you are logged in to the same account, follow the instructions, and run the command again. Or use `--load-cookies`.

**`--load-cookies chrome` finds no cookies, or reads them but is not logged in.**
Recent Chrome versions encrypt cookies so that only Chrome can read them. Use Firefox (or LibreWolf) for logging in to Instagram, and `--load-cookies firefox`. For other Chromium-based browsers, close the browser before importing.

**It imports the wrong account.**
`--load-cookies` uses the account that is logged in in the browser's default profile. Log in with the right account in the browser, or use `--cookiefile` for another browser profile. The program prints which account it logged in as.

**`Not logged in. Are you logged in successfully in firefox?` after `HTTP redirect from https://www.instagram.com/graphql/query to https://www.instagram.com/`.**
Older versions of InstaloaderSharp, like Instaloader, check the login with a query that Instagram redirects for some accounts, although the session is valid. Update to the [latest version](https://github.com/pzyber/InstaloaderSharp/releases), which checks the login through Instagram's app interface. With `--no-iphone`, the old check is used.

**The session stopped working.**
Sessions end when you log out in the browser you imported them from, change your password, or Instagram ends them. Log in again with `--login` (you are asked for the password) or import fresh cookies with `--load-cookies`.

**Two-factor authentication does not work.**
Instagram's two-factor login for programs is unreliable. Use `--load-cookies` instead.

## Rate limits and blocks

**`Too many queries in the last time. Need to wait ...`**
The program is pacing its requests to stay within Instagram's limits. This is normal for large downloads; it continues by itself. See [Rate limits and connections](Rate-limits-and-connections).

**`429 - Too Many Requests` or `Please wait a few minutes before you try again.` (401)**
Instagram limited the account or the connection. The program waits and retries. If it keeps happening:

- Do not run several downloads at the same time, and do not use the account heavily in the app or browser meanwhile.
- Stop for a while: hours rather than minutes.
- Use the account normally in a browser for a bit, then import fresh cookies with `--load-cookies`. A saved session that is throttled can stay throttled long after freshly imported cookies work again.
- Use a secondary account for downloading.

**`Download aborted: ... feedback_required` or `challenge_required`.**
Instagram has restricted the account and wants you to confirm something. Open Instagram in a browser or the app with that account and follow the instructions there. Then continue; resume information was saved.

**`Instagram answered ... with a web page instead of data, probably a verification (captcha).`**
Instagram has flagged the account and asks for a verification. Open Instagram in the web browser you are logged in with and complete it; it cannot be done by the program. Then let the account rest for a while, preferably a day, before downloading again; downloading right away usually leads to `feedback_required`. Resume information was saved. Older versions of InstaloaderSharp, like Instaloader, report this as `JSON Query to graphql/query: Expecting value: line 1 column 1 (char 0)`.

**Downloading the reels of a profile with many reels got the account flagged.**
Each reel is loaded with a request of its own, since the query of the website's reels tab returns them without their dates, captions and videos. Older versions of InstaloaderSharp, like Instaloader, also ask the app for each video's media info, two requests per reel; update to the [latest version](https://github.com/pzyber/InstaloaderSharp/releases), which needs one. The first `--reels` download of a profile with many reels still takes about one request per reel; with `--latest-stamps` or `--fast-update`, later downloads only load the new reels. Spread first downloads of large profiles over several days, one profile at a time.

**`Instagram keeps redirecting ...` or endless `HTTP redirect from ... to https://www.instagram.com/` lines.**
Instagram redirects every request of the account, usually because it is suspended, restricted or logged out. Open Instagram in a web browser with that account and follow the instructions there; a suspended account may have to be appealed. Older versions of InstaloaderSharp, like Instaloader, follow these redirects forever; press Ctrl+C to stop them.

**`Login required` or `Redirected to login page` while not logged in.**
Instagram refuses more and more requests without login. Log in; see [Logging in](Logging-in).

**`HTTP redirect from https://www.instagram.com/graphql/query to https://www.instagram.com/` and no posts are downloaded.**
This happens with Instaloader, and with older versions of InstaloaderSharp, when Instagram demands that queries look like its website's. Update to the [latest version](https://github.com/pzyber/InstaloaderSharp/releases) of InstaloaderSharp. If it happens with the latest version, your session may be invalid: import fresh cookies with `--load-cookies`.

## Profiles and posts

**`Profile ... does not exist.`**
Check the spelling: usernames contain only letters, digits, `.` and `_`. The profile may have been renamed (if you downloaded it before, the program finds it by its ID), deleted, or it may have blocked your account; logged in, the program then tries again without logging in. Restricted profiles, for example age-restricted ones, are only shown to logged-in users: log in to download them.

**`JSON Query to web/search/topsearch/: 404 Not Found` and `api/v1/users/.../info/: 429 Too Many Requests`.**
An older version of InstaloaderSharp could not find restricted profiles, and fell back to lookups that Instagram has retired or refuses. Update to the [latest version](https://github.com/pzyber/InstaloaderSharp/releases).

**`Login required.` or `Private but not followed.`**
The profile is private. You need to be logged in with an account that follows it.

**Posts are missing, or the download fails, without login.**
Instagram limits more and more what it shows to visitors who are not logged in, and may refuse the requests for a profile's posts. Log in to get all posts.

**The pictures have a low resolution.**
Log in. Logged in, the program gets the largest versions from Instagram's app interface, unless you gave `--no-iphone`.

**A single post: `unrecognized arguments: -DdwalmrkTSK`.**
Put `--` in front of the shortcode, after all options: `InstaloaderSharp.exe --no-videos -- -DdwalmrkTSK`.

**A hashtag is not downloaded, or `Invalid hashtag`.**
Put the hashtag in double quotes (`"#sunset"`), log in, and use only letters, digits and `_`.

**Downloading from an old metadata file fails with `403 Forbidden`.**
The download links in the file have expired. Download the post by its shortcode instead.

## Stories, highlights, feed and saved posts

**`--stories` downloads nothing.**
Stories need a login, and only exist for 24 hours. The profile may have no current story, or you may not be allowed to see it (private profile, close friends).

**`--stories` or `--highlights` fails with `JSON Query to graphql/query: Expecting value: line 1 column 1 (char 0)` after `HTTP redirect from https://www.instagram.com/graphql/query to https://www.instagram.com/`.**
Older versions of InstaloaderSharp, like Instaloader, load stories and highlights with queries that Instagram redirects for some accounts. Update to the [latest version](https://github.com/pzyber/InstaloaderSharp/releases), which loads them through Instagram's app interface. With `--no-iphone`, the old queries are used.

**`--reels` fails with `InvalidCastException: Unable to cast object of type 'InstaloaderSharp.Json.JValue' to type 'InstaloaderSharp.Json.JObject'`.**
An older version of InstaloaderSharp used a reels query that Instagram no longer answers. Update to the [latest version](https://github.com/pzyber/InstaloaderSharp/releases).

**`Instagram's response to the reels query contains no reels.`**
Instagram has changed its reels query again. Check for a newer version, or [report it](Building-and-contributing#reporting-bugs). The rest of the download continues.

**Story download fails with an error about a property.**
`--filename-pattern` is used for story items too. A placeholder that only posts have, such as `{likes}`, fails for story items. Use placeholders both have, such as `{date_utc}` and `{shortcode}`.

**`:feed` never ends.**
Your feed is practically endless. Use `--count`, or `--fast-update` to stop at the first post you already have.

**Where are the stories of `:stories`?**
In the folder `：stories` (with a full-width colon), in the current directory.

## Files and folders

**Folders have strange names like `：feed`.**
Windows does not allow `:` in names, so it is replaced with the similar-looking full-width colon `：`. The same is done for other characters that Windows does not allow; see [File and folder names](File-and-folder-names#invalid-characters).

**Files are saved in the wrong place, or in `C:\Windows\System32`.**
Downloads go to the current directory of the command prompt. In scheduled tasks, change to the right folder first (`cd /d D:\Instagram`) or use an absolute `--dirname-pattern`.

**The dates in Windows Explorer are not the download dates.**
That is intended: each file's modification time is the time the post was published, so sorting by date sorts by posting time.

**How do I open `.json.xz` files?**
They are compressed JSON. Open them with [7-Zip](https://www.7-zip.org/), or use `--no-compress-json` to get plain `.json` files. See [Downloaded files](Downloaded-files#metadata-files).

**`..._old_01.txt` files appear.**
The owner changed the post's caption; the old caption is kept. See [Downloaded files](Downloaded-files#captions).

**Paths that are too long.**
Windows limits paths to 260 characters unless long paths are enabled. Enable them (in **Local Group Policy Editor → Computer Configuration → Administrative Templates → System → Filesystem → Enable Win32 long paths**, or with the registry value `LongPathsEnabled`), or use shorter templates and folder names.

**`.temp` files are left behind.**
The program was stopped while downloading. The next download replaces them. You can delete them.

## Updates and resuming

**`--fast-update` stops too early.**
It stops at the first post that exists already. If you changed `--filename-pattern` or `--dirname-pattern`, the existing files are not found under the new names. Pinned posts at the top of a profile are handled. Without `--fast-update`, all posts are checked.

**`--fast-update` downloads everything again.**
The files are not where the current templates expect them; for example, you moved the folder or changed the patterns. Use `--latest-stamps` if you move downloaded files.

**`--latest-stamps` downloads everything again.**
The time in the stamps file could not be read; it must have exactly the format `2026-10-01T00:00:00.000000+0000`. Or the profile's section name differs from the username you gave. See [Keeping an archive up to date](Keeping-an-archive-up-to-date#latest-stamps).

**`--latest-stamps profilename` complains about a file or downloads nothing.**
`--latest-stamps` takes an optional file name, and took `profilename` as the file name. Write `--latest-stamps -- profilename`, or give the file name.

**A download does not resume.**
Resume files exist only for some kinds of lists (see [Interrupting and resuming](Interrupting-and-resuming#what-can-be-resumed)), are only used by the same target with the same login, and expire after 29 days.

## Filters and templates

**`Invalid filter: ... not a Post attribute.`**
A name in the filter is not a property. Check the spelling against the [Properties reference](Properties-reference). Functions such as `len()` are not available.

**`Invalid filter: invalid syntax`.**
The expression is not valid Python syntax. Check the quotes: double quotes around the whole filter, single quotes inside.

**`Filter evaluation failed` for some posts.**
The filter uses a property that some posts do not have, for example `video_duration` for pictures. Add a check: `video_duration is not None and video_duration > 60`.

**`can't compare offset-naive and offset-aware datetimes`.**
`date_local` has a time zone and cannot be compared with `datetime(...)`. Use `date_utc`, or `datetime(...).astimezone()`.

## General questions

**Can I download private profiles that I do not follow?**
No. InstaloaderSharp only downloads what your account can see on Instagram.

**Can InstaloaderSharp like, follow, comment or post?**
No. It only reads from Instagram.

**Does it work on Linux or macOS?**
No, it is a Windows program. Use [Instaloader](https://github.com/instaloader/instaloader) there; it uses the same files and commands.

**Is there a graphical interface?**
Yes, [InstaloaderSharp GUI](https://github.com/pzyber/InstaloaderSharpGUI).

**Do I need Python?**
No. InstaloaderSharp is a single .NET program.

## Getting help

1. Check the version (`InstaloaderSharp.exe --version`) and update to the [latest release](https://github.com/pzyber/InstaloaderSharp/releases): many problems are caused by changes on Instagram's side and are fixed in newer versions.
2. Search the [issues](https://github.com/pzyber/InstaloaderSharp/issues?q=is%3Aissue) for the error message.
3. Open a new issue with the command, the complete output and the version. Never post passwords, session files or cookies. See [Building and contributing](Building-and-contributing#reporting-bugs).
