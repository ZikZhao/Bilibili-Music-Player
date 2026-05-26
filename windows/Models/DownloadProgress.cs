namespace bilibili_music_player_windows.Models
{
    /// <summary>
    /// Represents the download progress of an audio cache operation.
    /// Mirrors the Flutter <c>DownloadProgress</c> class from <c>cache_manager.dart</c>.
    /// </summary>
    public class DownloadProgress
    {
        /// <summary>
        /// The BVID of the video being downloaded (used as cache file key).
        /// </summary>
        public string Bvid { get; }

        /// <summary>
        /// Number of bytes received so far.
        /// </summary>
        public long ReceivedBytes { get; }

        /// <summary>
        /// Total number of bytes expected. 0 if unknown.
        /// </summary>
        public long TotalBytes { get; }

        /// <summary>
        /// Whether the download has completed successfully.
        /// </summary>
        public bool IsComplete { get; }

        /// <summary>
        /// An error message if the download failed; otherwise <c>null</c>.
        /// </summary>
        public string? Error { get; }

        /// <summary>
        /// Download progress as a fraction between 0.0 and 1.0.
        /// Returns 0.0 when <see cref="TotalBytes"/> is 0 or negative.
        /// </summary>
        public double Progress
        {
            get
            {
                if (TotalBytes <= 0)
                {
                    return 0.0;
                }

                var ratio = (double)ReceivedBytes / TotalBytes;
                return ratio < 0.0 ? 0.0 : ratio > 1.0 ? 1.0 : ratio;
            }
        }

        /// <summary>
        /// Whether this progress represents an error.
        /// </summary>
        public bool HasError => Error is not null;

        public DownloadProgress(string bvid, long receivedBytes, long totalBytes, bool isComplete = false, string? error = null)
        {
            Bvid = bvid;
            ReceivedBytes = receivedBytes;
            TotalBytes = totalBytes;
            IsComplete = isComplete;
            Error = error;
        }

        /// <summary>
        /// Creates a completed progress snapshot for a cached file (100% with size).
        /// </summary>
        public static DownloadProgress Completed(string bvid, long fileSize)
        {
            return new DownloadProgress(bvid, fileSize, fileSize, isComplete: true);
        }

        /// <summary>
        /// Creates an error progress snapshot.
        /// </summary>
        public static DownloadProgress Failed(string bvid, string error)
        {
            return new DownloadProgress(bvid, 0, 0, error: error);
        }
    }
}
