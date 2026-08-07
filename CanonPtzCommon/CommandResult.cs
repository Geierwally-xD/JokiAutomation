using System;
using System.Net;

namespace CanonPtzCommon
{
    public sealed class CommandResult
    {
        public bool Success { get; private set; }
        public string Command { get; private set; }
        public string Message { get; private set; }
        public HttpStatusCode? StatusCode { get; private set; }
        public string ResponseBody { get; private set; }
        public Exception Exception { get; private set; }

        public static CommandResult Ok(string command, string message = null, HttpStatusCode? statusCode = null, string responseBody = null)
        {
            return new CommandResult
            {
                Success = true,
                Command = command,
                Message = message ?? "OK",
                StatusCode = statusCode,
                ResponseBody = responseBody
            };
        }

        public static CommandResult Fail(string command, string message, HttpStatusCode? statusCode = null, string responseBody = null, Exception exception = null)
        {
            return new CommandResult
            {
                Success = false,
                Command = command,
                Message = message,
                StatusCode = statusCode,
                ResponseBody = responseBody,
                Exception = exception
            };
        }

        public override string ToString()
        {
            if (Success)
            {
                return $"{Command}: OK - {Message}";
            }

            string status = StatusCode.HasValue ? $" HTTP={(int)StatusCode.Value}" : string.Empty;
            string ex = Exception != null ? $" Exception={Exception.GetType().Name}: {Exception.Message}" : string.Empty;
            return $"{Command}: FEHLER - {Message}{status}{ex}";
        }
    }
}
