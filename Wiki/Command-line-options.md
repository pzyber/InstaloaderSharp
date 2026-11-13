This page lists every option of InstaloaderSharp, explains how the command line is read, and lists the exit codes. The options and their meaning are the same as in Instaloader.

## Contents

- [Synopsis](#synopsis)
- [What to download of each post](#what-to-download-of-each-post)
- [What to download of each profile](#what-to-download-of-each-profile)
- [Which posts to download](#which-posts-to-download)
- [Login](#login)
- [How to download](#how-to-download)
- [Miscellaneous](#miscellaneous)
- [How the command line is read](#how-the-command-line-is-read)
- [Reading options from a file](#reading-options-from-a-file)
- [Exit codes](#exit-codes)

## Synopsis

```
InstaloaderSharp.exe [options] target [target ...]
InstaloaderSharp.exe [options] -- -SHORTCODE [...]
InstaloaderSharp.exe +args.txt
InstaloaderSharp.exe --help
```

Targets are described in [Targets](Targets). Without targets, the program only logs in (if asked to) and prints `No targets were specified, thus nothing has been downloaded.`

## What to download of each post

| Option | Effect | Details |
|---|---|---|
| `--slide SLIDE` | Download only some pictures of posts with several: `1`, `last`, `2-4`, `2-last` | [Choosing what to download](Choosing-what-to-download#parts-of-posts-with-several-pictures) |
| `--no-pictures` | Do not download pictures. Implies `--no-video-thumbnails`, not `--no-videos`. Cannot be combined with `--fast-update` | |
| `-V`, `--no-videos` | Do not download videos | |
| `--no-video-thumbnails` | Do not download the thumbnails of videos | |
| `-G`, `--geotags` | Save the location of each post as `_location.txt`, with a Google Maps link | [Downloaded files](Downloaded-files#geotags) |
| `-C`, `--comments` | Save and update the comments of each post as `_comments.json`. Requires login | [Downloaded files](Downloaded-files#comments) |
| `--no-captions` | Do not create caption `.txt` files | |
| `--post-metadata-txt TEMPLATE` | Template for the `.txt` file of each post (default `{caption}`). Can be given several times, one line each | [File and folder names](File-and-folder-names#caption-file-templates) |
| `--storyitem-metadata-txt TEMPLATE` | Template for a `.txt` file for each story item (default: none). Can be given several times | |
| `--no-metadata-json` | Do not create a JSON file with the metadata of each post | |
| `--no-compress-json` | Write pretty-printed `.json` files instead of xz-compressed `.json.xz` files | |

## What to download of each profile

| Option | Effect | Details |
|---|---|---|
| `--no-posts` | Do not download the profile's regular posts | |
| `--no-profile-pic` | Do not download the profile picture | |
| `-s`, `--stories` | Also download the profile's stories. Requires login | [Choosing what to download](Choosing-what-to-download#stories-highlights-tagged-posts-and-reels) |
| `--highlights` | Also download the profile's highlights. Requires login | |
| `--tagged` | Also download the posts in which the profile is tagged | |
| `--reels` | Also download the profile's reels | |
| `--igtv` | Accepted for compatibility; IGTV is retired and the option only prints a note | |

## Which posts to download

| Option | Effect | Details |
|---|---|---|
| `-F`, `--fast-update` | For each target, stop at the first post that was downloaded before | [Keeping an archive up to date](Keeping-an-archive-up-to-date#fast-updates) |
| `--latest-stamps [STAMPSFILE]` | Remember the time of the newest downloaded items of each profile in `STAMPSFILE` (default `%LOCALAPPDATA%\Instaloader\latest-stamps.ini`) and only download newer ones | [Keeping an archive up to date](Keeping-an-archive-up-to-date#latest-stamps) |
| `--post-filter FILTER`, `--only-if FILTER` | Only download posts for which the expression is true | [Filtering posts and stories](Filtering-posts-and-stories) |
| `--storyitem-filter FILTER` | Only download story items for which the expression is true | |
| `-c`, `--count COUNT` | Download at most COUNT posts of each hashtag, location, `:feed` and `:saved` | [Keeping an archive up to date](Keeping-an-archive-up-to-date#limiting-the-number-of-posts) |

## Login

| Option | Effect | Details |
|---|---|---|
| `-l`, `--login YOUR-USERNAME` | Log in as this user: load the session file, or ask for the password | [Logging in](Logging-in) |
| `-p`, `--password YOUR-PASSWORD` | The password, instead of asking for it | |
| `-b`, `--load-cookies BROWSER-NAME` | Import the login from a browser: `firefox`, `librewolf`, `chrome`, `chromium`, `edge`, `brave`, `vivaldi`, `opera`, `opera_gx` | [Logging in](Logging-in#importing-the-login-from-your-web-browser) |
| `-B`, `--cookiefile COOKIE-FILE` | The browser's cookie database, for a profile other than the default one | |
| `-f`, `--sessionfile SESSIONFILE` | Load and save the session in this file instead of `%LOCALAPPDATA%\Instaloader\session-YOUR-USERNAME` | [Logging in](Logging-in#session-files) |

`--login` and `--load-cookies` cannot be combined.

## How to download

| Option | Effect | Default |
|---|---|---|
| `--dirname-pattern PATTERN` | Folder of the downloads | `{target}` |
| `--filename-pattern PATTERN` | File names of posts and story items | `{date_utc}_UTC` |
| `--title-pattern PATTERN` | File names of profile pictures, hashtag pictures and highlight covers | `{date_utc}_UTC_{typename}`, or `{target}_{date_utc}_UTC_{typename}` if `--dirname-pattern` contains neither `{target}` nor `{profile}` |
| `--resume-prefix PREFIX` | Prefix of resume file names | `iterator` |
| `--no-resume` | Do not use or create resume files | |
| `--sanitize-paths` | Make names valid on Windows and other systems; always the case on Windows | |
| `--user-agent USER_AGENT` | User agent of the requests | Chrome 142 on Linux |
| `--max-connection-attempts N` | Attempts per request; `0` retries forever | `3` |
| `--request-timeout N` | Seconds to wait for an answer | `300` |
| `--abort-on STATUS_CODES` | Comma-separated HTTP status codes that stop the download immediately | |
| `--no-iphone` | Do not use Instagram's app interface for higher-quality pictures and videos | |

Patterns are explained in [File and folder names](File-and-folder-names), resume files in [Interrupting and resuming](Interrupting-and-resuming), and the connection options in [Rate limits and connections](Rate-limits-and-connections).

## Miscellaneous

| Option | Effect |
|---|---|
| `-q`, `--quiet` | Print only errors and never ask for input; fail if a password would be needed. Suitable for scheduled tasks |
| `-h`, `--help` | Print a summary of all options and exit |
| `--version` | Print the version number and exit |

## How the command line is read

The command line is read like Instaloader's (Python's `argparse`):

- **Options come before targets**, but they may also be mixed with them: `InstaloaderSharp.exe profile1 --stories profile2` works.
- **Values** follow the option, separated by a space or `=`: `--count 50` and `--count=50` are the same.
- **Abbreviations:** long options can be shortened as long as they stay unique: `--fast` for `--fast-update`, `--no-vid` is ambiguous (`--no-videos` or `--no-video-thumbnails`) and gives an error.
- **Short options** can be combined: `-sF` is `-s -F`. A value can follow directly: `-c50`.
- **`--`** ends the options: everything after it is a target, even if it starts with `-`. This is how single posts (`-- -SHORTCODE`) are given.
- **Optional values:** `--latest-stamps` takes an optional file name. A target right after it would be taken as the file name, so write `--latest-stamps -- profilename`, or put another option in between.
- **Quoting:** values with spaces, and hashtags, must be quoted: `--dirname-pattern "D:\My Instagram\{target}"`, `"#cats"`.

Errors in the command line are reported with a short usage text, and the program exits with code 2:

```
usage: InstaloaderSharp [--comments] [--geotags] ...
InstaloaderSharp: error: unrecognized arguments: --foo
```

## Reading options from a file

Arguments can be put into a text file and passed with `+` in front of its name:

```
InstaloaderSharp.exe +args.txt
```

The file contains one argument per line: options with their values joined by `=`, and targets:

```
--login=YOUR-USERNAME
--fast-update
--stories
--post-filter=not is_video
profile1
profile2
```

- Each line is exactly one argument, so do not quote values, and write `--count=50` rather than `--count 50`.
- Empty lines and comments are not allowed; every line counts as an argument.
- Files and normal arguments can be mixed, and several files can be given: `InstaloaderSharp.exe --quiet +login.txt +profiles.txt`.
- The file is read as UTF-8 text; a file that is not valid UTF-8 is read in the Windows code page instead.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Finished, but some errors occurred. They were printed when they happened and are repeated at the end under `Errors or warnings occurred:` |
| `2` | Invalid command line or option values, or an invalid filter |
| `3` | Login failed, or Instagram asks to verify the account while the login is checked |
| `4` | Download aborted: Instagram demanded a verification, or a status code from `--abort-on` was received |
| `5` | Interrupted by the user (Ctrl+C) |
| `99` | Unexpected error; a technical error report (traceback) is printed. Please [report it](Building-and-contributing#reporting-bugs) |

In a batch file, the exit code is in `%errorlevel%`:

```bat
InstaloaderSharp.exe --quiet --login YOUR-USERNAME --fast-update +profiles.txt
if %errorlevel% equ 3 echo Log in again: the session has expired or the login failed.
if %errorlevel% equ 4 echo Instagram stopped the download; check the account in a browser.
```

`if errorlevel N` is true for all codes of N and above, so prefer `if %errorlevel% equ N` to test for one code.
