using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeplexDiscussionDownload
{
    public class Configuration
    {
        public readonly string LogFileName;
        public readonly List<string> ForumNames;
        public readonly string PageSize;
        public readonly string OutputFolder;

        public Configuration(string forumNames, string logFileName, string pageSize, string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(forumNames))
            {
                throw new ArgumentNullException(nameof(forumNames));
            }

            if (string.IsNullOrWhiteSpace(pageSize))
            {
                throw new ArgumentNullException(nameof(pageSize));
            }

            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                throw new ArgumentNullException(nameof(outputFolder));
            }

            this.ForumNames = forumNames.Split(',').ToList();
            this.OutputFolder = outputFolder;
            this.PageSize = pageSize;
            this.LogFileName = logFileName;
        }
    }
}
