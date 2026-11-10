# InstaloaderSharp manual

Welcome to the InstaloaderSharp manual. InstaloaderSharp is a free command-line program for Windows that downloads pictures and videos from Instagram, along with their captions and other metadata: whole profiles, single posts, hashtags, locations, your feed, your saved posts, stories, highlights and reels. It is a C# / .NET Framework version of [Instaloader](https://github.com/instaloader/instaloader), with the same command line and file formats, so the two programs can work on the same downloads.

No previous knowledge of Instaloader is needed. If you are new, start with [Getting started](Getting-started), then read [Targets](Targets) and [Logging in](Logging-in). If you prefer a graphical user interface, have a look at [InstaloaderSharp GUI](https://github.com/pzyber/InstaloaderSharpGUI).

## Getting going

| Page | What it covers |
|---|---|
| [Getting started](Getting-started) | Requirements, getting the program, the first download, updating a download, getting help |
| [Targets](Targets) | Profiles, single posts, hashtags, locations, feed, saved posts, stories, followees and metadata files |
| [Logging in](Logging-in) | Logging in with a password or two-factor authentication, importing a browser's login, session files |
| [Downloaded files](Downloaded-files) | Where files go, what each file contains, renamed profiles, changed captions |

## Downloading

| Page | What it covers |
|---|---|
| [Choosing what to download](Choosing-what-to-download) | Pictures, videos, captions, metadata, comments, geotags, stories, highlights, tagged posts, reels |
| [Keeping an archive up to date](Keeping-an-archive-up-to-date) | `--fast-update`, `--latest-stamps`, `--count` and scheduled downloads |
| [Filtering posts and stories](Filtering-posts-and-stories) | `--post-filter` and `--storyitem-filter` expressions |
| [File and folder names](File-and-folder-names) | Name templates, format specifications, caption file templates |
| [Interrupting and resuming](Interrupting-and-resuming) | Ctrl+C, resume files, skipping a single item |
| [Rate limits and connections](Rate-limits-and-connections) | How the program paces its requests, retries, timeouts, `--abort-on`, the user agent |

## Reference

| Page | What it covers |
|---|---|
| [Command-line options](Command-line-options) | Every option, abbreviations, option files and exit codes |
| [Properties reference](Properties-reference) | Every property of posts, story items, profiles and other items, for filters and templates |
| [File formats](File-formats) | Session files, metadata, resume files, the latest-stamps file, comments and geotags |
| [Interoperability with Instaloader](Interoperability-with-Instaloader) | Using InstaloaderSharp and Instaloader on the same downloads |
| [Differences from Instaloader](Differences-from-Instaloader) | Where InstaloaderSharp deliberately behaves differently, and why |
| [Library use](Library-use) | Using InstaloaderSharp from your own C# or .NET program |
| [Troubleshooting and FAQ](Troubleshooting-and-FAQ) | Common problems and their solutions |
| [Glossary](Glossary) | Instagram and InstaloaderSharp terms used in this manual |

## Project

| Page | What it covers |
|---|---|
| [Building and contributing](Building-and-contributing) | Building from source, unit tests, reporting bugs, contributing code |
| [Releases](https://github.com/pzyber/InstaloaderSharp/releases) | Downloads of each version, and what changed in it |

## Conventions in this manual

- Commands are shown as they are typed in a Windows command prompt (`cmd.exe`), with the program named `InstaloaderSharp.exe`. If you renamed it, for example to `instaloader.exe`, use that name instead.
- `YOUR-USERNAME` stands for your Instagram username, `profilename` for the username of the profile you want to download.
- Options are shown in their long form (`--fast-update`); the short form, where there is one, is given in brackets (`-F`).
- Times in file names are in UTC unless the text says otherwise.

> InstaloaderSharp is in no way affiliated with, authorized, maintained or endorsed by Instagram or any of its affiliates or subsidiaries. This is an independent and unofficial project. Use at your own risk, and respect the privacy and copyright of the people whose posts you download.
