using System;

namespace InstaloaderSharp
{
    /// <summary>Base exception for this script.</summary>
    /// <remarks>This exception should not be raised directly.</remarks>
    public class InstaloaderException : Exception
    {
        public InstaloaderException() : base("") { }
        public InstaloaderException(string message) : base(message ?? "") { }
        public InstaloaderException(string message, Exception inner) : base(message ?? "", inner) { }
    }

    public class QueryReturnedBadRequestException : InstaloaderException
    {
        public QueryReturnedBadRequestException() { }
        public QueryReturnedBadRequestException(string message) : base(message) { }
        public QueryReturnedBadRequestException(string message, Exception inner) : base(message, inner) { }
    }

    public class QueryReturnedForbiddenException : InstaloaderException
    {
        public QueryReturnedForbiddenException() { }
        public QueryReturnedForbiddenException(string message) : base(message) { }
        public QueryReturnedForbiddenException(string message, Exception inner) : base(message, inner) { }
    }

    public class ProfileNotExistsException : InstaloaderException
    {
        public ProfileNotExistsException() { }
        public ProfileNotExistsException(string message) : base(message) { }
        public ProfileNotExistsException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Deprecated since 4.2.2: Not raised anymore.</summary>
    public class ProfileHasNoPicsException : InstaloaderException
    {
        public ProfileHasNoPicsException() { }
        public ProfileHasNoPicsException(string message) : base(message) { }
    }

    public class PrivateProfileNotFollowedException : InstaloaderException
    {
        public PrivateProfileNotFollowedException() { }
        public PrivateProfileNotFollowedException(string message) : base(message) { }
    }

    public class LoginRequiredException : InstaloaderException
    {
        public LoginRequiredException() { }
        public LoginRequiredException(string message) : base(message) { }
    }

    public class LoginException : InstaloaderException
    {
        public LoginException() { }
        public LoginException(string message) : base(message) { }
        public LoginException(string message, Exception inner) : base(message, inner) { }
    }

    public class TwoFactorAuthRequiredException : LoginException
    {
        public TwoFactorAuthRequiredException() { }
        public TwoFactorAuthRequiredException(string message) : base(message) { }
    }

    public class InvalidArgumentException : InstaloaderException
    {
        public InvalidArgumentException() { }
        public InvalidArgumentException(string message) : base(message) { }
        public InvalidArgumentException(string message, Exception inner) : base(message, inner) { }
    }

    public class BadResponseException : InstaloaderException
    {
        public BadResponseException() { }
        public BadResponseException(string message) : base(message) { }
    }

    public class BadCredentialsException : LoginException
    {
        public BadCredentialsException() { }
        public BadCredentialsException(string message) : base(message) { }
    }

    public class ConnectionException : InstaloaderException
    {
        public ConnectionException() { }
        public ConnectionException(string message) : base(message) { }
        public ConnectionException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>.. versionadded:: 4.2.2</summary>
    public class PostChangedException : InstaloaderException
    {
        public PostChangedException() { }
        public PostChangedException(string message) : base(message) { }
    }

    public class QueryReturnedNotFoundException : ConnectionException
    {
        public QueryReturnedNotFoundException() { }
        public QueryReturnedNotFoundException(string message) : base(message) { }
        public QueryReturnedNotFoundException(string message, Exception inner) : base(message, inner) { }
    }

    public class TooManyRequestsException : ConnectionException
    {
        public TooManyRequestsException() { }
        public TooManyRequestsException(string message) : base(message) { }
    }

    public class IPhoneSupportDisabledException : InstaloaderException
    {
        public IPhoneSupportDisabledException() { }
        public IPhoneSupportDisabledException(string message) : base(message) { }
    }

    /// <summary>
    /// Exception that is not catched in the error catchers inside the download loop and so aborts the
    /// download loop.
    ///
    /// This exception is not a subclass of <see cref="InstaloaderException"/>.
    /// </summary>
    public class AbortDownloadException : Exception
    {
        public AbortDownloadException() : base("") { }
        public AbortDownloadException(string message) : base(message ?? "") { }
        public AbortDownloadException(string message, Exception inner) : base(message ?? "", inner) { }
    }

    /// <summary>
    /// Instagram asks to verify the account (a challenge, checkpoint or captcha), which can only be completed in a
    /// web browser. Aborts the download like <see cref="AbortDownloadException"/>; the login check raises a
    /// <see cref="LoginException"/> with the same message instead.
    /// </summary>
    public class VerificationRequiredException : AbortDownloadException
    {
        public VerificationRequiredException(string message) : base(message) { }
    }

    /// <summary>
    /// Counterpart of Python's KeyboardInterrupt: raised at the next interruption point after the user hit ^C.
    /// Not a subclass of <see cref="InstaloaderException"/>.
    /// </summary>
    public class KeyboardInterruptException : Exception
    {
        public KeyboardInterruptException() : base("") { }
    }
}
