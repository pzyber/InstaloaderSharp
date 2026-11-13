Public profiles and single public posts can be downloaded without an Instagram account. For everything else, InstaloaderSharp logs in to Instagram like a web browser does. This page explains the two ways to log in, and how the login is remembered.

## Contents

- [What needs a login](#what-needs-a-login)
- [Logging in with your password](#logging-in-with-your-password)
- [Two-factor authentication](#two-factor-authentication)
- [Importing the login from your web browser](#importing-the-login-from-your-web-browser)
- [Session files](#session-files)
- [Several accounts](#several-accounts)
- [Logging in for scheduled tasks](#logging-in-for-scheduled-tasks)
- [Keeping your account safe](#keeping-your-account-safe)
- [Login problems](#login-problems)

## What needs a login

| Download | Without login | Logged in |
|---|---|---|
| Public profiles, their posts and profile pictures | Yes | Yes, in higher quality |
| Single public posts | Yes | Yes, in higher quality |
| Geotags of posts (`--geotags`) | Yes | Yes |
| Private profiles | No | If your account follows them |
| Stories (`--stories`, `:stories`) and highlights (`--highlights`) | No | Yes |
| Hashtags, locations, followees (`@profile`) | No | Yes |
| Your feed (`:feed`) and saved posts (`:saved`) | No | Yes |
| Comments (`--comments`) | No | Yes |

When you are not logged in, the program prints `Hint: Login to download higher-quality versions of pictures.` Logged in, it uses Instagram's app interface to get the largest versions of pictures and videos (unless `--no-iphone` is given).

> Instagram may restrict accounts that download a lot, for example by asking for a verification or by temporarily blocking requests. Consider using a secondary account for downloading, and see [Rate limits and connections](Rate-limits-and-connections).

## Logging in with your password

```
InstaloaderSharp.exe --login YOUR-USERNAME profilename
```

The first time, you are asked for your password:

```
Session file does not exist yet - Logging in.
Enter Instagram password for YOUR-USERNAME:
```

Nothing is shown while you type. If the password is wrong, you are asked again. After a successful login, the login is saved in a [session file](#session-files), and the next time `--login YOUR-USERNAME` loads it without asking for the password.

The password can also be given on the command line with `--password` (`-p`):

```
InstaloaderSharp.exe --login YOUR-USERNAME --password "your password" profilename
```

Avoid this where you can: the password then ends up in your command history and is visible to other programs that list running processes.

`--login` can be abbreviated to `-l`. The username must be your Instagram username, not your e-mail address or phone number.

## Two-factor authentication

If your account uses two-factor authentication, you are asked for the code after the password:

```
Enter 2FA verification code:
```

Enter the code from your authenticator app or text message. If the code is wrong, you are asked again. When the password is given with `--password`, a warning reminds you that two-factor login does not always work; if it fails, use the browser login below.

## Importing the login from your web browser

Instagram sometimes refuses logins from programs, or asks for a verification that can only be done in a browser. The most reliable way to log in is therefore to log in to instagram.com in your web browser and let InstaloaderSharp import the browser's login cookies:

```
InstaloaderSharp.exe --load-cookies firefox
```

```
Cookies loaded successfully from firefox
YOUR-USERNAME has been successfully logged in.
Next time use --login=YOUR-USERNAME to reuse the same session.
```

The program tells you which account it is logged in as, and saves a session file for that account. Afterwards you can use `--login YOUR-USERNAME` without the browser. You can also download in the same run:

```
InstaloaderSharp.exe --load-cookies firefox --stories profilename
```

`--load-cookies` can be abbreviated to `-b`. It cannot be combined with `--login`.

### Supported browsers

| Name | Browser | Cookies are read from |
|---|---|---|
| `firefox` | Mozilla Firefox | The default profile in `%APPDATA%\Mozilla\Firefox` |
| `librewolf` | LibreWolf | The default profile in `%APPDATA%\librewolf` |
| `chrome` | Google Chrome | `%LOCALAPPDATA%\Google\Chrome\User Data` |
| `chromium` | Chromium | `%LOCALAPPDATA%\Chromium\User Data` |
| `edge` | Microsoft Edge | `%LOCALAPPDATA%\Microsoft\Edge\User Data` |
| `brave` | Brave | `%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data` |
| `vivaldi` | Vivaldi | `%LOCALAPPDATA%\Vivaldi\User Data` |
| `opera` | Opera | `%APPDATA%\Opera Software\Opera Stable` |
| `opera_gx` | Opera GX | `%APPDATA%\Opera Software\Opera GX Stable` |

Firefox's default profile is the one Firefox itself starts with (from `profiles.ini`); for Chromium-based browsers it is the `Default` profile, then `Profile 1`, `Profile 2` and so on. Safari is accepted as a name but only exists on macOS.

**Firefox and LibreWolf work best.** Recent versions of Chrome protect their cookies with "app-bound encryption", which only Chrome itself can decrypt; InstaloaderSharp (like Instaloader) cannot read such cookies. Chromium-based browsers may also lock their cookie database while they run; close the browser if reading fails.

### Using another browser profile

To read the cookies of a browser profile other than the default one, give its cookie database with `--cookiefile` (`-B`) together with `--load-cookies`:

```
InstaloaderSharp.exe --load-cookies firefox --cookiefile "%APPDATA%\Mozilla\Firefox\Profiles\abcd1234.second\cookies.sqlite"
InstaloaderSharp.exe --load-cookies chrome --cookiefile "%LOCALAPPDATA%\Google\Chrome\User Data\Profile 2\Network\Cookies"
```

Firefox's profile folders are listed on its `about:profiles` page.

### The session belongs to the browser

An imported login is the same session as in your browser. If you log out of Instagram in the browser, the session ends for InstaloaderSharp too, and you need to log in and import the cookies again. While you are downloading, avoid using the same account heavily in the browser or the app.

## Session files

A session file stores the login cookies of an account, so that you do not have to log in every time. It is written after each run in which you were logged in (`Saved session to ...`).

- **Location:** `%LOCALAPPDATA%\Instaloader\session-YOUR-USERNAME` by default, for example `C:\Users\You\AppData\Local\Instaloader\session-myname`. This is the same location Instaloader uses, so both programs share their logins (see [Interoperability with Instaloader](Interoperability-with-Instaloader)).
- **Another location:** `--sessionfile FILE` (`-f`) loads and saves the session in `FILE` instead, together with `--login` or `--load-cookies`.
- **Contents:** the session cookies, in Python's pickle format (see [File formats](File-formats#session-files)). It does not contain your password.
- **Expiry:** sessions stay valid until you log out, change your password, or Instagram ends them. When a saved session no longer works, the program reports it and asks for your password again.
- **Logging out:** delete the session file. To also end the session on Instagram's side, log out of the device in Instagram's **Accounts Center → Password and security → Where you're logged in**.

## Several accounts

Each account has its own session file, so you can switch between accounts with `--login`:

```
InstaloaderSharp.exe --login account1 :saved
InstaloaderSharp.exe --login account2 :saved
```

With `--load-cookies`, the account that is logged in in the browser is used, and its session file is updated.

## Logging in for scheduled tasks

In a scheduled task nobody can type a password. Log in once interactively (or with `--load-cookies`), so that a session file exists, and then use `--login` together with `--quiet` in the task:

```
InstaloaderSharp.exe --quiet --login YOUR-USERNAME --fast-update --stories profilename
```

With `--quiet`, the program never asks questions: if the session is no longer valid, it fails with `Quiet mode requires given password or valid session file.` and exit code 3 instead of waiting for input. See [Keeping an archive up to date](Keeping-an-archive-up-to-date#scheduled-downloads).

## Keeping your account safe

- A session file, and the cookies in your browser, give full access to your Instagram account. Keep them private, and never post them or share them in bug reports.
- Prefer `--load-cookies` or the password prompt over `--password`.
- Use a secondary account for large downloads, so that a restriction does not affect your main account.
- InstaloaderSharp only reads from Instagram. It never likes, follows, comments or posts anything.

## Login problems

| Message | What to do |
|---|---|
| `Checkpoint required. Point your browser to https://www.instagram.com/... - follow the instructions, then retry.` | Instagram wants to confirm the login. Open the link in a browser where you are logged in, confirm, then try again, or use `--load-cookies`. |
| `Login error: Wrong password.` | Check the password; you are asked again. Make sure you gave your username, not your e-mail address. |
| `Login error: "fail" status, message "..."` or `Login error: Unexpected response, this might indicate a blocked IP.` | Instagram refused the login. Use `--load-cookies` instead. |
| `No cookies found for Instagram in firefox, Are you logged in successfully in firefox?` | Log in to instagram.com in that browser first, or use `--cookiefile` for a non-default profile. |
| `Not logged in. Are you logged in successfully in firefox?` | The cookies were found, but the browser is not logged in (anymore). Log in to instagram.com in the browser and try again. |
| `Instagram asks to verify the account: it redirected ... to its verification page` | The login is valid, but Instagram wants the account verified first. Open instagram.com in a browser logged in with the account, complete the verification there, then try again. |
| `Failed to find Chrome cookie` | The browser's cookie database was not found. Use `--cookiefile`, or another browser. |
| `--load-cookies and --login cannot be used together.` | Use one of them; after importing cookies once, `--login` is enough. |
| `Please wait a few minutes before you try again.` (401) | The account or connection is temporarily limited. Wait, use the account normally in a browser for a while, or import fresh cookies with `--load-cookies`. See [Troubleshooting and FAQ](Troubleshooting-and-FAQ#rate-limits-and-blocks). |

More problems and solutions are in [Troubleshooting and FAQ](Troubleshooting-and-FAQ#logging-in).
