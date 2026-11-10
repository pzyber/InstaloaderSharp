# Contributing to InstaloaderSharp

Thank you for your interest in InstaloaderSharp! Everything about the project, including bug reports, feature
requests, questions and code contributions, is handled through the GitHub repository:

**https://github.com/pzyber/InstaloaderSharp**

* [Reporting a bug](#reporting-a-bug)
* [Reporting a security issue](#reporting-a-security-issue)
* [Suggesting a feature](#suggesting-a-feature)
* [Asking a question](#asking-a-question)
* [Contributing code](#contributing-code)
* [Project guidelines](#project-guidelines)
* [Testing your changes](#testing-your-changes)
* [Submitting a pull request](#submitting-a-pull-request)
* [License](#license)

## Reporting a bug

Report bugs in the [issue tracker](https://github.com/pzyber/InstaloaderSharp/issues).

1. **Search first.** Check the [open and closed issues](https://github.com/pzyber/InstaloaderSharp/issues?q=is%3Aissue)
   for the same problem. If you find one, add any new details as a comment instead of opening a duplicate.
2. **Make sure you use the latest version.** Many problems are caused by changes on Instagram's side and may
   already be fixed in a newer release.
3. **Open a new issue** with a short, descriptive title, e.g. "`--stories` fails with KeyError 'reels'"
   rather than "It doesn't work".

Please include:

* The InstaloaderSharp version (`InstaloaderSharp.exe --version`) and your Windows version.
* The full command line you ran.
* The complete output, including all error messages and the "Errors or warnings occurred" summary at the end,
  and the exit code.
* What you expected to happen and what happened instead.
* Whether you were logged in, and how (`--login` or `--load-cookies` with which browser).
* Whether the problem is reproducible, and if it happens with every profile/post or only with specific ones.
* If you can, whether [Instaloader](https://github.com/instaloader/instaloader) shows the same problem.
  That helps tell bugs in InstaloaderSharp apart from changes on Instagram's side. Both are worth reporting:
  InstaloaderSharp aims to work correctly even where Instaloader does not.

> **Never post passwords, session files, cookie values or two-factor codes.** Session files
> (`%LOCALAPPDATA%\Instaloader\session-*`) and browser cookies give full access to your Instagram account.
> Before posting output, remove or replace anything private, such as the usernames of private accounts or
> the contents of `.json`/`.json.xz` metadata files of private posts. A public profile or post that shows the
> problem is the most helpful example.

### Rate limits and blocked accounts

Messages like *"Please wait a few minutes before you try again"*, HTTP 429 or 401, *"Checkpoint required"*
or redirects to `/terms/unblock/` come from Instagram restricting the account or IP address, not from a bug.
Before reporting them, see "Tips and troubleshooting" in the [README](README.md#tips-and-troubleshooting).
Waiting, using the account normally in a browser for a while, or re-importing fresh cookies with
`--load-cookies` usually helps.

## Reporting a security issue

If you find a security problem, for example in how session files, cookies or credentials are handled, **do not
open a public issue.** Report it privately through the repository's
[Security tab](https://github.com/pzyber/InstaloaderSharp/security) ("Report a vulnerability"),
so that it can be fixed before it is disclosed.

## Suggesting a feature

Feature requests are welcome as [issues](https://github.com/pzyber/InstaloaderSharp/issues/new).
Describe what you would like to do and why (the use case), not only the proposed solution, and mention if
Instaloader already has the feature. Please keep in mind that InstaloaderSharp only downloads data;
features that act on Instagram (liking, following, posting, ...) are out of scope.

## Asking a question

For questions about using InstaloaderSharp, first check the [README](README.md), which covers all options. If
it does not answer your question, open an [issue](https://github.com/pzyber/InstaloaderSharp/issues)
and describe what you are trying to do.

## Contributing code

Bug fixes, adaptations to Instagram changes, documentation improvements and new features are all welcome.
For larger changes, please open an issue first to discuss the approach, so that no work is wasted.

### Setting up

1. Fork the repository on GitHub and clone your fork:

   ```
   git clone https://github.com/YOUR-USERNAME/InstaloaderSharp.git
   ```

2. Create a branch for your change from `main`, with a descriptive name:

   ```
   git checkout -b fix-stories-keyerror
   ```

3. Build the solution, either in Visual Studio (2015 or later) or with:

   ```
   "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" InstaloaderSharp.sln /p:Configuration=Release
   ```

   You need the .NET Framework 4.8.1 developer pack (reference assemblies). The build must finish without
   errors and warnings. The unit test project needs Visual Studio's MSTest framework (see
   [Unit tests](#unit-tests)).

### Repository layout

| Path | Contents |
| --- | --- |
| `InstaloaderSharp/Instaloader.cs`, `InstaloaderContext.cs`, `NodeIterator.cs`, `LatestStamps.cs` | The core: downloading, Instagram requests, resumable iteration, `--latest-stamps` |
| `InstaloaderSharp/Structures/` | `Post`, `Profile`, `StoryItem`, `Story`, `Highlight`, `Hashtag` and saving/loading them as JSON |
| `InstaloaderSharp/Program.cs`, `Cli/` | Command line (argparse-compatible parser), `--load-cookies` (browser cookie databases) |
| `InstaloaderSharp/Http/` | HTTP/2 client on top of WinHTTP, cookie jar, `requests`-like session |
| `InstaloaderSharp/Json/`, `Compression/`, `Python/` | Python-compatible JSON, xz/LZMA, pickle, `str.format`, `strftime` and the expression evaluator for `--post-filter` |
| `InstaloaderSharp.Tests/` | Unit tests, and in `TestData/` reference files created by Instaloader |

## Project guidelines

* **C# 6 and .NET Framework 4.8.1.** The code must compile with the C# 6 compiler of Visual Studio 2015 /
  MSBuild 14. Do not use newer language features such as tuples, `out var`, local functions, pattern
  matching (`is T x`), `switch` expressions, `??=` or `default` literals.
* **No third-party dependencies.** InstaloaderSharp is a single executable without NuGet packages. Use the .NET
  Framework and Windows APIs only.
* **Add new source files to `InstaloaderSharp/InstaloaderSharp.csproj`.** The project lists its files explicitly.
  Visual Studio does this automatically; if you use another editor, add a `<Compile Include="..." />` entry.
* **Correct behavior first.** InstaloaderSharp started as a C# / .NET Framework version of Instaloader, but it should work
  correctly rather than reproduce Instaloader's bugs or failures caused by Instagram changes. Fix them, and
  document every deliberate difference from Instaloader in the "Differences from Instaloader" section of
  the [README](README.md).
* **Keep files compatible with Instaloader.** Users may use both programs on the same downloads.
  Session files (pickle), metadata and resume files (`.json`/`.json.xz`), `latest-stamps.ini`, file names and
  modification times should stay readable and consistent with Instaloader.
* **Keep the command line compatible.** Do not rename or remove existing options or change their meaning.
  New options should follow the existing naming style. Update the help text and the README when you change
  options or behavior.
* **Follow the existing code style.** Match the surrounding code: four-space indentation, braces on their own
  lines, PascalCase names that mirror the Python names (`download_post` → `DownloadPost`), and XML
  documentation comments on public members. Explain *why* in comments where the code works around Instagram
  behavior.
* **Do not commit downloads or personal data.** Never commit session files, cookies, downloaded media,
  metadata of private accounts or credentials, also not in tests or examples.

## Testing your changes

### Unit tests

The solution contains a unit test project, `InstaloaderSharp.Tests` (MSTest). The tests run offline. They
make no requests to Instagram and need no account. They cover the parts that must stay compatible with
Instaloader, comparing against reference files that Instaloader created:

* JSON output (`ensure_ascii`, float formatting, key order), pickle session files, xz/LZMA and the
  `.json`/`.json.xz` metadata files.
* Resume files and their "magic" hash, and `latest-stamps.ini`.
* `str.format`, `strftime`, `--post-filter` expressions, file name patterns and path handling.
* The command line parser, the cookie jar, Firefox cookie databases (SQLite with write-ahead log), AES-GCM
  for Chromium cookies, and the HTTP client, which is tested against a local server on `127.0.0.1`.

The test project needs Visual Studio's MSTest framework, which is included with Visual Studio 2015 or later.
To run the tests, build the solution in Visual Studio, open **Test Explorer** from the **Test** menu and choose
**Run All**. On the command line, run:

```
"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" InstaloaderSharp.sln /p:Configuration=Debug
"C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" InstaloaderSharp.Tests\bin\Debug\InstaloaderSharp.Tests.dll
```

Use the `vstest.console.exe` of your Visual Studio version (for newer versions, it is under
`Common7\IDE\Extensions\TestPlatform`).

All tests must pass before you submit a pull request. When you fix a bug or add a feature, add a test for it
where possible, preferably with expected values verified against Instaloader. The reference files in
`InstaloaderSharp.Tests\TestData` were created with Instaloader. If a test needs a new reference
file, create it with Instaloader, add it to `TestData`, and describe in the test how it was created. The
repository contains only C# code, so do not add scripts in other languages.

Add new test files to `InstaloaderSharp.Tests/InstaloaderSharp.Tests.csproj` like source files. The
reference files in `TestData` are included automatically.

### Testing with Instagram

Unit tests cannot detect changes on Instagram's side, so please also test your change against Instagram and
describe how in the pull request:

* Build both Debug and Release without warnings.
* Run the commands affected by your change. For example, download a public profile or a single post
  (`InstaloaderSharp.exe -- -SHORTCODE`). For features that need it, use a logged-in session.
* If your change touches file formats, check that Instaloader (`pip install instaloader`) can still
  read the files InstaloaderSharp writes, and the other way round.
* Keep live tests small (`--count`, single posts, `--no-posts`, `--no-videos`), and do not run several
  instances in parallel. Instagram quickly rate-limits or restricts accounts that make many requests. Use a
  secondary account for logged-in tests.

## Submitting a pull request

1. Keep the pull request focused on one change; split unrelated changes into separate pull requests.
2. Write clear commit messages: a short summary line in the imperative ("Fix KeyError in story download"),
   followed by a blank line and an explanation of what and why, if needed.
3. Make sure your branch is up to date with `main`, builds without errors and warnings, and passes all
   unit tests.
4. Push the branch to your fork and open a pull request against `main` of
   [pzyber/InstaloaderSharp](https://github.com/pzyber/InstaloaderSharp/pulls).
5. In the description, explain what the change does, link the related issue (e.g. "Fixes #12"), and describe
   how you tested it.
6. Be ready to answer questions and to update your pull request after review.

## License

InstaloaderSharp is licensed under the [MIT License](LICENSE). By contributing, you agree that your
contributions are licensed under the same license.
