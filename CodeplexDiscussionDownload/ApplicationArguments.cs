using System.Collections.Generic;

namespace CodeplexDiscussionDownload
{
    /// <summary>
    /// Argument values that can be passed to the console application.
    /// </summary>
    internal class ApplicationArguments
    {
        /// <summary>
        /// Gets or sets the CodePlex Forums to process.
        /// </summary>
        internal List<string> CodePlexForums { get; set; }

        /// <summary>
        /// Gets or sets the number of posts on an HTML page.
        /// </summary>
        internal int PageSize { get; set; }

        /// <summary>
        /// Gets or sets the output directory where the generated files are written.
        /// </summary>
        internal string OutputDirectory { get; set; }

        /// <summary>
        /// Gets or sets the name of the log file.
        /// </summary>
        internal string Logfile { get; set; }
    }
}