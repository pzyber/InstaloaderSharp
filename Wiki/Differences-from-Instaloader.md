InstaloaderSharp follows Instaloader closely, but it puts working correctly before reproducing Instaloader's behavior. Where Instaloader fails because Instagram changed, or because of a bug, InstaloaderSharp is fixed and differs deliberately. This page lists every such difference.

## Contents

- [Features that work again](#features-that-work-again)
- [How requests are sent](#how-requests-are-sent)
- [File names](#file-names)
- [Logging in](#logging-in)
- [Error handling](#error-handling)
- [Platform](#platform)

## Features that work again

| Feature | Instaloader | InstaloaderSharp |
|---|---|---|
| `:feed` | Pages through the app's timeline (`api/v1/feed/timeline/`), which returned about two posts per request in a test, between suggestions | Loads the feed the way Instagram's website does: the first page from the home page, further pages with the query the home page declares, which returns the same posts with fewer requests. Instagram's suggested posts from accounts you do not follow are skipped |
| `:stories` | Uses a query for the list of accounts with stories that Instagram no longer answers | Gets the list from the app's story tray (`api/v1/feed/reels_tray/`) |
| Followers and followees (`@profile`, `Profile.GetFollowers()`, `GetFollowees()`) | Its query returns no profiles | Uses the friendships interface of Instagram's web app |
| Geotags (`--geotags`) | Fails with `KeyError: 'id'`: Instagram now names the location ID `pk` and no longer provides `slug` and `has_public_page`; the fallback lookup returns no data either | Works, also without logging in. In saved metadata, a post's location has no `slug` |
| `--igtv` | Fails with `KeyError: 'edges'` | Reports that Instagram has retired IGTV; former IGTV videos are downloaded as posts and reels. Lists that Instagram returns without items are treated as empty instead of failing |
| `Profile.GetFollowedHashtags()` | Fails with a `TypeError` | Returns an empty list, since Instagram removed following hashtags in December 2024 |
| Restricted profiles (for example age-restricted ones) | Reports `Profile ... does not exist.`: it reads profiles from their page as a logged-out visitor sees it, and Instagram does not show restricted profiles to logged-out visitors | Found when logged in: the profile's ID is read from the profile page as the session sees it, and the profile is loaded by its ID |
| Tagged posts (`--tagged`) | Downloads only the first few tagged posts: its query returns a first page of a few posts, followed by empty pages | Downloads all tagged posts, with the query of the website's tagged tab (`PolarisProfileTaggedTabContentQuery_connection`). That query returns the posts without their dates and videos, so each tagged post is loaded with an extra request |
| Searching Instagram (`TopSearchResults` in library use) | Uses the website's top search, which Instagram has retired (404) | Uses the search of Instagram's app (`api/v1/fbsearch/topsearch_flat/`, and `api/v1/fbsearch/places/` for places). Requires login; throws `LoginRequiredException` otherwise |
| Suggestions of similar profile names | When a profile cannot be loaded, tries the website's top search, which Instagram has retired (404), several times | Not made; the error is `Profile ... does not exist.` |
| Stories and highlights (`--stories`, `--highlights`, `:stories`) | For some accounts, Instagram answers its queries with a redirect to its home page, and no stories or highlights are downloaded | Works; see below |
| Highlights whose items Instagram refuses to list (400 `invalid request`) | Stops downloading the profile's highlights | Takes the items from the app's `reels_media` answer; if a highlight still fails, the other highlights are downloaded |
| Single posts, reels and profile details, logged in | For some accounts, Instagram answers these queries with a redirect to its home page (`HTTP redirect from https://www.instagram.com/graphql/query to https://www.instagram.com/`), and they cannot be loaded | Works; see below |

## How requests are sent

- **Logged-in queries are sent like the website's.** Since October 2026, Instagram redirects logged-in GraphQL queries to its home page, for some accounts, unless they carry the session's request token (`fb_dtsg`, from Instagram's home page), the query's name and the browser's `Sec-Fetch-*` headers. InstaloaderSharp sends them for queries of reels, tagged posts, post details, profile details and further pages of `:feed`. Instaloader sends profile posts the website's way too, through the website's `api/graphql` endpoint, but its other logged-in queries without these fields. Reels are loaded with the query of the website's reels tab (`PolarisProfileReelsTabContentQuery_connection`), since Instagram answers Instaloader's older reels query without reels when it is sent this way.
- **The login check asks the app interface.** Whether a session is logged in, and as whom, is checked with Instagram's app interface (`api/v1/accounts/current_user/`). Instaloader uses a GraphQL query that Instagram redirects to its home page for some accounts, so that a valid session is reported as `Not logged in. Are you logged in successfully in firefox?`, after three `HTTP redirect from https://www.instagram.com/graphql/query to https://www.instagram.com/` messages. That query is still used as a fallback, and with `--no-iphone`.
- **Stories and highlights come from the app interface.** The stories and the highlights' items are loaded from Instagram's app interface (`api/v1/feed/reels_media/`), the list of highlights from `api/v1/highlights/<ID>/highlights_tray/`. Instaloader uses GraphQL queries that Instagram redirects to its home page for some accounts, so that `--stories` and `--highlights` end with `Download stories: JSON Query to graphql/query: Expecting value: line 1 column 1 (char 0)`. Instaloader's queries are still used when the app does not answer, and with `--no-iphone`. The saved metadata of story items then has fewer GraphQL fields; the app's complete item is in `iphone_struct`, as before.
- **No extra request for the video versions.** Logged in, a video whose details already contain its video versions (as those of reels, tagged posts and single posts do) is downloaded without asking Instagram's app for its media info (`api/v1/media/<ID>/info/`). Instaloader asks for it for every video, for the best version; the versions in it were the same sizes and files in all 956 videos compared, so the same file is downloaded. Each reel takes one request instead of two: the first `--reels` download of a profile with 1,200 reels took about 2,500 requests, enough for Instagram to flag the account (a captcha, then `feedback_required`). The saved metadata of these videos has no `iphone_struct`.
- **Lower limits when logged in.** Logged in, the program allows half as many requests as Instaloader within the same time; see [Rate limits and connections](Rate-limits-and-connections#how-requests-are-paced). Logged out, Instaloader's limits apply.
- **A verification page stops the download.** When Instagram answers a logged-in query with a web page instead of data, usually a verification (captcha), the download stops (exit code 4) instead of retrying; see [Rate limits and connections](Rate-limits-and-connections#stopping-on-certain-responses). Instaloader retries, and goes on with the next targets, which get the same page.
- **A redirect to the verification page stops at once.** When Instagram redirects a query to its verification page (`/challenge/`), the program stops with `Instagram asks to verify the account: it redirected ... to its verification page`: while the login is checked with exit code 3, during a download with exit code 4. Instaloader retries the login check three times and then reports `Not logged in. Are you logged in successfully in firefox?`, although the session is valid.
- **No endless redirects.** For a suspended or restricted account, Instagram redirects every request to its home page, and the home page to itself. Instaloader then follows the redirects forever, printing `HTTP redirect from ... to https://www.instagram.com/` again and again. InstaloaderSharp follows at most 10 redirects and never the same one twice; logged in, it then stops the download (exit code 4) with the message `Instagram keeps redirecting ... The account may be suspended, restricted or logged out; check it in a web browser.`

## File names

- **Formatted values are cleaned.** Values with a format specification in `--dirname-pattern` and `--filename-pattern`, such as `{date_utc:%c}` or `{date_utc:%H:%M}`, get the same replacements of characters that are invalid in Windows file names as other values (`:` becomes `：`). Instaloader leaves them unchanged and fails with `OSError: [Errno 22] Invalid argument`. Path separators from formats are kept, so `{date_utc:%Y/%m}` still creates subfolders.

## Logging in

- **`--load-cookies` is built in.** Instaloader needs the optional `browser_cookie3` package. InstaloaderSharp supports Firefox, LibreWolf and the Chromium-based browsers on Windows itself. Cookies with Chrome's app-bound encryption (`v20`) cannot be decrypted, as with `browser_cookie3`, and Chrome may need to be closed so that its cookie database can be read. Safari only exists on macOS.

## Error handling

- **Syntax errors in filters** are reported as an error message, with exit code 2, instead of a Python traceback.
- **Several profiles** are downloaded in the order they were given, instead of in the order of Python's set.

## Platform

InstaloaderSharp is a Windows program for .NET Framework. It has no Python API; its [library](Library-use) is a .NET API with the same structure as Instaloader's Python API.
