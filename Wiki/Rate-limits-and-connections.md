Instagram limits how many requests an account or a connection may make in a given time. InstaloaderSharp paces its requests to stay within these limits, retries failed requests, and stops when Instagram demands it. This page explains how, and which options change it.

## Contents

- [How requests are paced](#how-requests-are-paced)
- [When Instagram says "too many requests"](#when-instagram-says-too-many-requests)
- [Retries](#retries)
- [Stopping on certain responses](#stopping-on-certain-responses)
- [Timeouts](#timeouts)
- [The user agent](#the-user-agent)
- [High-quality media and the app interface](#high-quality-media-and-the-app-interface)
- [Proxies](#proxies)
- [Avoiding restrictions](#avoiding-restrictions)

## How requests are paced

- **A short pause before every request**, of random length (on average under two seconds, at most 15 seconds), so that the program does not send requests in a rigid rhythm.
- **Limits per kind of request.** The program counts its requests to Instagram by kind (each kind of query, the app interface, other pages) and waits when one kind reaches its limit within a sliding window of about 10 minutes. Logged in: 100 requests of each query kind, 150 queries in total, and 75 other requests; requests to Instagram's app interface are limited separately, to 100 per 30 minutes. Logged out, the limits are Instaloader's: 200 of each query kind, 275 queries in total, 75 other requests, and 199 app requests per 30 minutes.
- **Downloads of pictures and videos are not limited**, since they come from Instagram's content servers.

When a limit is reached, the program says how long it waits:

```
Too many queries in the last time. Need to wait 7 minutes, until 14:32.
```

Waiting is normal for large downloads and needs no action. Logged in, the limits are half of Instaloader's: Instagram limits the requests of an account, and has asked accounts that stayed within Instaloader's limits for a captcha, then answered them with `feedback_required`. Instagram does not publish its limits, so these values are a cautious guess. In [library use](Library-use), a custom `RateController` can change them (`CountPerSlidingWindow`, `GraphqlCountPerSlidingWindow`, `IphoneCountPerSlidingWindow`).

## When Instagram says "too many requests"

If Instagram still answers with HTTP 429 ("Too Many Requests"), the program prints how many requests of each kind it made in the last 10 to 60 minutes, waits until the limit window has passed, and retries:

```
Instagram responded with HTTP error "429 - Too Many Requests". Please do not run multiple
instances of Instaloader in parallel or within short sequence. Also, do not use any Instagram
App while Instaloader is running.
The request will be retried in 9 minutes, at 14:41.
```

Instagram also answers with HTTP 401 and the message `Please wait a few minutes before you try again.` when an account or connection has made too many requests. The program retries these too. If they continue for a long time, see [Troubleshooting and FAQ](Troubleshooting-and-FAQ#rate-limits-and-blocks).

## Retries

A request that fails because of a connection problem or a temporary error is retried, with a pause in between:

```
JSON Query to graphql/query: HTTP error code 500. [retrying; skip with ^C]
```

| Option | Effect | Default |
|---|---|---|
| `--max-connection-attempts N` | How many times a request is tried before it is given up. `0` retries forever. | `3` |

When a request has failed this many times, the item is skipped with an error (`... skipped`), the download continues with the next item, and the error is repeated at the end. Pressing Ctrl+C during the `[retrying; skip with ^C]` message skips the item immediately; see [Interrupting and resuming](Interrupting-and-resuming#skipping-a-single-item).

## Stopping on certain responses

Some answers from Instagram mean that continuing would only make things worse. When Instagram demands a verification (`checkpoint_required`, `challenge_required`) or reports that the account is restricted (`feedback_required`), the program stops the whole download:

```
Download aborted: ... "checkpoint_required" ...
```

and exits with code 4. Resume information is saved, so you can continue after solving the problem (usually by logging in to Instagram in a browser and confirming the request).

The download also stops when Instagram answers a logged-in query with a web page instead of data, usually a verification (captcha):

```
Instagram answered https://www.instagram.com/graphql/query with a web page instead of data, probably a verification (captcha). Open https://www.instagram.com/ in a web browser logged in with the account, complete the verification there and run InstaloaderSharp again.
```

The verification only goes away when it is completed in a web browser, so retrying would only send more requests that Instagram counts against the account. Instaloader retries, and goes on with the next targets, until Instagram answers with `feedback_required`.

The same applies when Instagram redirects a query to its verification page (`/challenge/`):

```
Instagram asks to verify the account: it redirected https://www.instagram.com/graphql/query to its verification page (https://www.instagram.com/challenge/?next=...). Open https://www.instagram.com/ in a web browser logged in with the account, complete the verification there and run InstaloaderSharp again.
```

During a download it stops with exit code 4. When it happens while the login is checked (at the start, with `--load-cookies` or `--login`), the program ends with exit code 3. Instaloader retries the login check three times, and then reports that the session is not logged in.

`--abort-on` stops the download in the same way when Instagram answers with one of the given HTTP status codes, without retrying:

```
InstaloaderSharp.exe --abort-on 302,401,429 --login YOUR-USERNAME profilename
```

| Code | Usual meaning on Instagram |
|---|---|
| `302` | Redirected to the login page or elsewhere: the session is not valid, or the request was refused |
| `400` | The request was refused |
| `401` | Not logged in, or "please wait a few minutes" |
| `403` | Forbidden |
| `429` | Too many requests |

This is useful in scheduled tasks: rather than waiting and retrying for a long time, the program stops, and the next scheduled run continues where it stopped.

## Timeouts

| Option | Effect | Default |
|---|---|---|
| `--request-timeout N` | Seconds to wait for Instagram to answer a request before it counts as failed | `300` |

## The user agent

Every request tells Instagram which browser it comes from, by its *user agent*. The default is that of Chrome on Linux:

```
Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36
```

`--user-agent TEXT` sends another one. When you [import your browser's login](Logging-in#importing-the-login-from-your-web-browser), it helps to use the user agent of that browser, so that the session looks the same to Instagram as in your browser. You can see your browser's user agent by searching for "my user agent" in it. For example, for Firefox:

```
InstaloaderSharp.exe --load-cookies firefox --user-agent "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:144.0) Gecko/20100101 Firefox/144.0" profilename
```

Requests to Instagram's app interface always use the user agent of Instagram's iPad app.

## High-quality media and the app interface

When you are logged in, the program asks Instagram's app interface (`i.instagram.com`) for the largest versions of pictures and videos, which the website does not offer. This costs extra requests, which are limited separately (see above).

| Option | Effect |
|---|---|
| `--no-iphone` | Do not use the app interface. Pictures and videos are downloaded in the quality the website offers, and fewer requests are made. |

Logged in, queries to the website are sent with the same tokens and headers as Instagram's website sends them; see [Differences from Instaloader](Differences-from-Instaloader).

## Proxies

InstaloaderSharp connects through Windows' WinHTTP, over HTTP/2 to Instagram's servers. To use a proxy, set the environment variable `HTTPS_PROXY` (or `ALL_PROXY`) before running the program, for example:

```
set HTTPS_PROXY=http://proxy.example.com:8080
InstaloaderSharp.exe profilename
```

Hosts listed in `NO_PROXY` (separated by commas) are reached without the proxy. Without these variables, the proxy settings of Windows are used.

## Avoiding restrictions

- Run only one download per account at a time, and do not run several downloads from the same computer in quick succession.
- Avoid using Instagram's app or website with the same account heavily while a download runs.
- Use a secondary account for large downloads.
- Prefer `--fast-update` or `--latest-stamps` for updates, so that only new posts are requested.
- Leave out what you do not need: `--comments` and `--geotags` cost extra requests per post.
- If Instagram restricts the account, stop for a while (hours rather than minutes), use the account normally in a browser, and import fresh cookies with `--load-cookies` before trying again.
