using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace JokiAutomation
{
    partial class RasPi
    {
        /// <summary>
        /// Runs the existing RasPi execution path and waits for the worker thread
        /// to finish, with timeout and cancellation support.
        /// </summary>
        public async Task RasPiExecuteAsync(
            int command,
            int id,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                _rasPiForm?._logDat?.sendInfoMessage(
                    $"RasPiExecuteAsync START: command={command}, id={id}, timeout={effectiveTimeout.TotalSeconds:F0}s");

                cancellationToken.ThrowIfCancellationRequested();

                rasPiExecute(command, id);

                Thread worker = _RasPiThread;
                if (worker == null)
                {
                    _rasPiForm?._logDat?.sendInfoMessage("RasPiExecuteAsync: no worker thread was started");
                    return;
                }

                while (worker.IsAlive)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (stopwatch.Elapsed > effectiveTimeout)
                    {
                        throw new TimeoutException(
                            $"RasPi command {command}/{id} timed out after {effectiveTimeout.TotalSeconds:F0}s");
                    }

                    await Task.Delay(100, cancellationToken);
                }

                _rasPiForm?._logDat?.sendInfoMessage(
                    $"RasPiExecuteAsync END: command={command}, id={id}, elapsed={stopwatch.ElapsedMilliseconds}ms");
            }
            catch (OperationCanceledException)
            {
                _rasPiForm?._logDat?.sendInfoMessage(
                    $"RasPiExecuteAsync CANCELED: command={command}, id={id}, elapsed={stopwatch.ElapsedMilliseconds}ms");
                throw;
            }
            catch (TimeoutException tex)
            {
                _rasPiForm?._logDat?.sendInfoMessage(
                    $"RasPiExecuteAsync TIMEOUT: {tex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _rasPiForm?._logDat?.sendInfoMessage(
                    $"RasPiExecuteAsync ERROR: {ex.GetType().Name} - {ex.Message}");
                throw;
            }
        }
    }
}
