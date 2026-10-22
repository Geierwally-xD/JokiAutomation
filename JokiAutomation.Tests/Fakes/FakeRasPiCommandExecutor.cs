using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JokiAutomation;

namespace JokiAutomation.Tests.Fakes
{
    internal sealed class FakeRasPiCommandExecutor : IRasPiCommandExecutor
    {
        public sealed class Invocation
        {
            public int SequenceNumber { get; set; }
            public string Host { get; set; }
            public int Port { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string Command { get; set; }
            public TimeSpan Timeout { get; set; }
            public bool Completed { get; set; }
            public int? ExitCode { get; set; }
            public Exception Exception { get; set; }

            /// <summary>
            /// Completes when ExecuteAsync for this invocation has started executing.
            /// </summary>
            public Task Started => StartedSource.Task;

            /// <summary>
            /// Completes when ExecuteAsync for this invocation has returned or thrown.
            /// </summary>
            public Task Completion => CompletionSource.Task;

            internal TaskCompletionSource<object> StartedSource { get; } =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal TaskCompletionSource<object> CompletionSource { get; } =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private int _sequenceNumber;

        public List<Invocation> Invocations { get; } = new List<Invocation>();

        public RasPiCommandExecutionResult Result { get; set; } = new RasPiCommandExecutionResult
        {
            ExitCode = 0,
            StandardOutput = string.Empty,
            StandardError = string.Empty
        };

        public Exception ExceptionToThrow { get; set; }

        public TimeSpan Delay { get; set; }

        /// <summary>
        /// When set, ExecuteAsync will wait for this gate to complete before proceeding,
        /// allowing deterministic blocking of the background audio continuation in tests.
        /// </summary>
        public TaskCompletionSource<object> AllowCompletionGate { get; set; }

        public async Task<RasPiCommandExecutionResult> ExecuteAsync(
            string host,
            int port,
            string username,
            string password,
            string command,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var invocation = new Invocation
            {
                SequenceNumber = Interlocked.Increment(ref _sequenceNumber),
                Host = host,
                Port = port,
                Username = username,
                Password = password,
                Command = command,
                Timeout = timeout
            };

            lock (Invocations)
            {
                Invocations.Add(invocation);
            }

            invocation.StartedSource.TrySetResult(null);

            try
            {
                if (AllowCompletionGate != null)
                {
                    using (cancellationToken.Register(() => AllowCompletionGate.TrySetCanceled(cancellationToken)))
                    {
                        await AllowCompletionGate.Task.ConfigureAwait(false);
                    }
                }

                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (ExceptionToThrow != null)
                {
                    throw ExceptionToThrow;
                }

                invocation.ExitCode = Result?.ExitCode;
                invocation.Completed = true;
                invocation.CompletionSource.TrySetResult(null);
                return Result;
            }
            catch (Exception ex)
            {
                invocation.Exception = ex;
                invocation.CompletionSource.TrySetException(ex);
                throw;
            }
        }
    }
}
