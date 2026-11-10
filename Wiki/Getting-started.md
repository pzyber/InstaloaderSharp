This page takes you from nothing to your first download, and shows how to update a download later.

## Contents

- [Requirements](#requirements)
- [Getting the program](#getting-the-program)
- [Your first download](#your-first-download)
- [Updating a download](#updating-a-download)
- [Downloading more than public profiles](#downloading-more-than-public-profiles)
- [Getting help](#getting-help)
- [Next steps](#next-steps)

## Requirements

- Windows 10 version 1607 or later, or Windows 11. InstaloaderSharp talks to Instagram over HTTP/2, which it gets from Windows' built-in WinHTTP.
- .NET Framework 4.8.1. It is included in recent Windows 11 builds; on Windows 10 it is a free download from Microsoft.

Nothing else is needed: no Python, no other runtime, no installer.

## Getting the program

Download the latest `InstaloaderSharp-<version>-win.zip` from the [releases page](https://github.com/pzyber/InstaloaderSharp/releases) and extract `InstaloaderSharp.exe` from it. The program is a single file: put it in any folder you like. If you want to run it from any folder, put it in a folder that is in your `PATH`.

You may rename the file, for example to `instaloader.exe`, so that commands written for Instaloader work unchanged. This manual uses `InstaloaderSharp.exe`.

To build the program yourself instead, see [Building and contributing](Building-and-contributing).

> Windows SmartScreen may warn about the downloaded program because it is not signed. Click **More info → Run anyway** if you downloaded it from the official releases page.

## Your first download

InstaloaderSharp is a command-line program. Open a command prompt (press **Win+R**, type `cmd`, press **Enter**), go to the folder where the downloads should be saved, and run the program with the name of a public profile:

```
cd /d D:\Instagram
InstaloaderSharp.exe instagram
```

This downloads all posts of the public profile `instagram` (pictures, videos, captions and metadata) and its profile picture into a new folder `D:\Instagram\instagram`. Progress is printed as it goes:

```
[1/1] Downloading profile instagram
instagram\2025-09-20_00-00-36_UTC_profile_pic.jpg
Retrieving posts from profile instagram.
[   1/8123] instagram\2026-10-08_17-10-31_UTC.jpg [fur real, every single one of…] instagram\2026-10-08_17-10-31_UTC.mp4 json
[   2/8123] instagram\2026-10-07_18-17-32_UTC.jpg [Who IS she? 🤩 …] json
...
```

Each line is one post: its number and the total, the files that were saved, a short version of the caption in square brackets, and `json` for the metadata file. A large profile takes a long time, because the program waits between requests so that Instagram does not block it (see [Rate limits and connections](Rate-limits-and-connections)).

Press **Ctrl+C** at any time to stop. The download can be continued later; see [Interrupting and resuming](Interrupting-and-resuming).

A single post is downloaded by its shortcode, the part after `/p/` or `/reel/` in its URL. For `https://www.instagram.com/p/DdwalmrkTSK/`:

```
InstaloaderSharp.exe -- -DdwalmrkTSK
```

The `--` is needed because the shortcode is written with a leading `-`. See [Targets](Targets) for everything else you can download.

## Updating a download

Run the same command again later and only new posts are downloaded; files that already exist are skipped (`... exists`). Add `--fast-update` (`-F`) to stop as soon as the first already-downloaded post is reached, which makes updates much faster:

```
InstaloaderSharp.exe --fast-update instagram
```

See [Keeping an archive up to date](Keeping-an-archive-up-to-date) for more ways to update downloads, including scheduled updates.

## Downloading more than public profiles

Without an account you can download public profiles and single public posts. Stories, highlights, private profiles you follow, hashtags, locations, your feed and your saved posts need a login:

```
InstaloaderSharp.exe --login YOUR-USERNAME --stories --highlights profilename
```

You are asked for your password once; afterwards the login is reused. If logging in with a password does not work, import the login from your web browser instead:

```
InstaloaderSharp.exe --load-cookies firefox
```

See [Logging in](Logging-in) for details.

> Downloading in large amounts can get an account temporarily restricted by Instagram. Consider using a secondary account.

## Getting help

```
InstaloaderSharp.exe --help
```

prints all options with a short description; `--version` prints the program's version. Every option is explained in [Command-line options](Command-line-options), and common problems in [Troubleshooting and FAQ](Troubleshooting-and-FAQ).

## Next steps

- [Choosing what to download](Choosing-what-to-download): leave out videos, add comments or geotags, download stories and highlights.
- [File and folder names](File-and-folder-names): organize downloads in your own folder structure.
- [Filtering posts and stories](Filtering-posts-and-stories): download only posts that match a condition, such as a date range or a hashtag.
