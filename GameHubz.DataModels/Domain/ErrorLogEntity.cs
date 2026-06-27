using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    /// <summary>
    /// One persisted server-side error. Captures who hit it, when, what failed and
    /// enough request context to reproduce and fix the bug. <see cref="BaseEntity.Id"/>
    /// is the reference shown to the user, and <see cref="BaseEntity.CreatedOn"/> is when
    /// it happened.
    /// </summary>
    public class ErrorLogEntity : BaseEntity
    {
        /// <summary>Authenticated user that triggered the error, if any.</summary>
        public Guid? UserId { get; set; }

        /// <summary>Exception category bucket (Unhandled, Core, Data, ...).</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>CLR type name of the exception, e.g. NullReferenceException.</summary>
        public string ExceptionType { get; set; } = string.Empty;

        /// <summary>Exception.Message.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Full exception text including stack trace and inner exceptions.</summary>
        public string? StackTrace { get; set; }

        /// <summary>Exception.Source (assembly that threw).</summary>
        public string? Source { get; set; }

        /// <summary>HTTP status code returned to the client.</summary>
        public int StatusCode { get; set; }

        public string RequestMethod { get; set; } = string.Empty;

        public string RequestPath { get; set; } = string.Empty;

        public string? QueryString { get; set; }

        /// <summary>Request body (JSON only, truncated). Null for uploads / empty bodies.</summary>
        public string? RequestBody { get; set; }

        public string? UserAgent { get; set; }

        /// <summary>Client app version (X-App-Version header) to correlate with releases.</summary>
        public string? AppVersion { get; set; }

        /// <summary>Client platform (X-Platform header), e.g. ios / android.</summary>
        public string? Platform { get; set; }

        public string? IpAddress { get; set; }

        /// <summary>Triage flag — flip to true once the underlying bug is fixed.</summary>
        public bool IsResolved { get; set; }

        /// <summary>Optional note about the fix / root cause when resolved.</summary>
        public string? ResolutionNotes { get; set; }
    }
}
