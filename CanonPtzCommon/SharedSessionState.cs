using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace CanonPtzCommon
{
    /// <summary>
    /// Shares XC Protocol Session ID between multiple application instances
    /// Uses Memory-Mapped File for inter-process communication
    /// </summary>
    public static class SharedSessionState
    {
        private const string SessionMapName = "CanonXcProtocolSession";
        private const int MaxSessionIdLength = 256;
        private static readonly Mutex _mutex = new Mutex(false, "CanonXcProtocolSessionMutex");

        /// <summary>
        /// Get the shared session ID, or null if none exists
        /// </summary>
        public static string GetSessionId()
        {
            try
            {
                _mutex.WaitOne();
                try
                {
                    using (var mmf = MemoryMappedFile.OpenExisting(SessionMapName))
                    using (var accessor = mmf.CreateViewAccessor())
                    {
                        byte[] buffer = new byte[MaxSessionIdLength];
                        accessor.ReadArray(0, buffer, 0, MaxSessionIdLength);

                        string sessionId = Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                        return string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
                    }
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Set the shared session ID
        /// </summary>
        public static void SetSessionId(string sessionId)
        {
            try
            {
                _mutex.WaitOne();
                try
                {
                    MemoryMappedFile mmf;
                    try
                    {
                        mmf = MemoryMappedFile.OpenExisting(SessionMapName);
                    }
                    catch (FileNotFoundException)
                    {
                        mmf = MemoryMappedFile.CreateNew(SessionMapName, MaxSessionIdLength);
                    }

                    using (mmf)
                    using (var accessor = mmf.CreateViewAccessor())
                    {
                        byte[] buffer = new byte[MaxSessionIdLength];
                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            byte[] sessionBytes = Encoding.UTF8.GetBytes(sessionId);
                            Array.Copy(sessionBytes, buffer, Math.Min(sessionBytes.Length, MaxSessionIdLength));
                        }
                        accessor.WriteArray(0, buffer, 0, MaxSessionIdLength);
                    }
                }
                finally
                {
                    _mutex.ReleaseMutex();
                }
            }
            catch
            {
                // Ignore errors in shared state
            }
        }

        /// <summary>
        /// Clear the shared session ID
        /// </summary>
        public static void ClearSessionId()
        {
            SetSessionId(null);
        }
    }
}