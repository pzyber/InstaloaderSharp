InstaloaderSharp is open source under the MIT License. Everything about the project (bug reports, feature requests, questions and code) is handled in the GitHub repository [pzyber/InstaloaderSharp](https://github.com/pzyber/InstaloaderSharp). The full guidelines are in [CONTRIBUTING.md](https://github.com/pzyber/InstaloaderSharp/blob/main/CONTRIBUTING.md); this page summarizes them.

## Contents

- [Building from source](#building-from-source)
- [Running the unit tests](#running-the-unit-tests)
- [Repository layout](#repository-layout)
- [Reporting bugs](#reporting-bugs)
- [Reporting security issues](#reporting-security-issues)
- [Suggesting features and asking questions](#suggesting-features-and-asking-questions)
- [Contributing code](#contributing-code)
- [Editing this wiki](#editing-this-wiki)

## Building from source

You need:

- Windows 10 version 1607 or later.
- Visual Studio 2015 or later, or the MSBuild 14 build tools.
- The .NET Framework 4.8.1 developer pack (reference assemblies).

No NuGet packages or other dependencies are needed.

```
git clone https://github.com/pzyber/InstaloaderSharp.git
cd InstaloaderSharp
"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" InstaloaderSharp.sln /p:Configuration=Release
```

The result is `InstaloaderSharp\bin\Release\InstaloaderSharp.exe`, a single file without further dependencies. Without Visual Studio, build only the program, since the unit tests need Visual Studio's MSTest framework:

```
"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" InstaloaderSharp\InstaloaderSharp.csproj /p:Configuration=Release
```

The code must compile with the C# 6 compiler of Visual Studio 2015 / MSBuild 14, so newer language features such as tuples, `out var`, local functions and pattern matching are not used.

## Running the unit tests

The solution contains a unit test project, `InstaloaderSharp.Tests` (MSTest). The tests run offline, without an Instagram account, and check the parts that must stay compatible with Instaloader against reference files that Instaloader created: JSON, pickle session files, xz, metadata and resume files, `latest-stamps.ini`, `str.format`, `strftime`, filter expressions, file name patterns, the command-line parser, cookies, Firefox cookie databases, AES-GCM and the HTTP client (against a local server).

In Visual Studio, build the solution, open **Test Explorer** from the **Test** menu and choose **Run All**. On the command line:

```
"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" InstaloaderSharp.sln /p:Configuration=Debug
"C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" InstaloaderSharp.Tests\bin\Debug\InstaloaderSharp.Tests.dll
```

Use the `vstest.console.exe` of your Visual Studio version; for newer versions it is under `Common7\IDE\Extensions\TestPlatform`. If the repository is on a network drive, the test runner may refuse to load the test assembly (error `0x80131515`); copy `InstaloaderSharp.Tests\bin\Debug` to a local folder and run the tests there.

## Repository layout

| Path | Contents |
|---|---|
| `InstaloaderSharp/Instaloader.cs`, `InstaloaderContext.cs`, `NodeIterator.cs`, `LatestStamps.cs` | The core: downloading, requests to Instagram, resumable lists, `--latest-stamps` |
| `InstaloaderSharp/Structures/` | `Post`, `Profile`, `StoryItem`, `Story`, `Highlight`, `Hashtag`, and saving and loading them as JSON |
| `InstaloaderSharp/Program.cs`, `Cli/` | The command line (an argparse-compatible parser) and `--load-cookies` (browser cookie databases) |
| `InstaloaderSharp/Http/` | The HTTP/2 client on top of WinHTTP, the cookie jar and a `requests`-like session |
| `InstaloaderSharp/Json/`, `Compression/`, `Python/` | Python-compatible JSON, xz/LZMA, pickle, `str.format`, `strftime` and the filter expression evaluator |
| `InstaloaderSharp.Tests/` | Unit tests, and in `TestData/` reference files created by Instaloader |
| `Wiki/` | The pages of this wiki |

## Reporting bugs

Report bugs in the [issue tracker](https://github.com/pzyber/InstaloaderSharp/issues):

1. **Search first** in the [open and closed issues](https://github.com/pzyber/InstaloaderSharp/issues?q=is%3Aissue), and add new details to an existing issue instead of opening a duplicate.
2. **Use the latest version.** Many problems are caused by changes on Instagram's side and may be fixed already.
3. **Open an issue** with a descriptive title, and include:
   - the version (`InstaloaderSharp.exe --version`) and your Windows version;
   - the complete command line and the complete output, including the `Errors or warnings occurred` summary and the exit code;
   - what you expected and what happened;
   - whether you were logged in, and how (`--login`, or `--load-cookies` with which browser);
   - whether it happens every time, and with every profile or post;
   - if you can, whether [Instaloader](https://github.com/instaloader/instaloader) has the same problem. Both cases are worth reporting: InstaloaderSharp aims to work correctly even where Instaloader does not.

> Never post passwords, session files, cookies or two-factor codes. Remove anything private from the output before posting it. A public profile or post that shows the problem is the most helpful example.

Messages about rate limits, `Checkpoint required` and restricted accounts usually come from Instagram, not from a bug; see [Troubleshooting and FAQ](Troubleshooting-and-FAQ#rate-limits-and-blocks) first.

## Reporting security issues

Do not open a public issue for security problems, for example in how session files, cookies or credentials are handled. Report them privately through the repository's [Security tab](https://github.com/pzyber/InstaloaderSharp/security) (**Report a vulnerability**).

## Suggesting features and asking questions

Feature requests and questions are welcome as [issues](https://github.com/pzyber/InstaloaderSharp/issues/new). Describe what you would like to do and why, and mention whether Instaloader has the feature. InstaloaderSharp only downloads; features that act on Instagram (liking, following, posting) are out of scope.

## Contributing code

Bug fixes, adaptations to Instagram changes, documentation improvements and new features are all welcome. For larger changes, open an issue first to discuss the approach.

1. Fork the repository and create a branch from `main`.
2. Make your change, following the project guidelines below.
3. Build Debug and Release without errors and warnings, run the unit tests, and test the change against Instagram with small downloads (`--count`, single posts, a secondary account).
4. Open a pull request against `main`, describing what the change does, why, which issue it fixes, and how you tested it.

Project guidelines, in short:

- **C# 6 and .NET Framework 4.8.1, no third-party dependencies.** The program is a single executable without NuGet packages.
- **Correct behavior first.** Where Instaloader fails or has bugs, fix them in InstaloaderSharp, and document every deliberate difference in [Differences from Instaloader](Differences-from-Instaloader) and the README.
- **Keep files and the command line compatible with Instaloader.** Session files, metadata, resume files, `latest-stamps.ini`, file names and modification times must stay readable by and consistent with Instaloader. Do not rename or remove options.
- **Follow the existing code style**, with PascalCase names that mirror the Python names (`download_post` becomes `DownloadPost`).
- **Add tests** for fixes and features where possible, with expected values verified against Instaloader. The repository contains only C# code.
- **Never commit** session files, cookies, downloads, metadata of private accounts or credentials.

## Editing this wiki

The pages of this wiki are kept in the repository's [`Wiki` folder](https://github.com/pzyber/InstaloaderSharp/tree/main/Wiki), one Markdown file per page, with `_Sidebar.md` as the navigation. Improvements are welcome as pull requests like any other change. Links between pages use the page name without `.md`, for example `[Logging in](Logging-in)`.
