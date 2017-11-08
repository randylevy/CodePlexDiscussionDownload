using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Fclp;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace CodeplexDiscussionDownload
{
    internal class Program
    {
        private static string logFileName;

        private static void Main(string[] args)
        {
            var parser = CreateCommandLineParser();
            var result = parser.Parse(args);
            if (result.HasErrors)
            {
                parser.HelpOption.ShowHelp(parser.Options);
                Console.WriteLine(result.ErrorText);

                return;
            }

            if (result.HelpCalled)
            {
                return;
            }

            logFileName = Path.Combine(parser.Object.OutputDirectory, parser.Object.Logfile);
            DownloadForums(parser.Object);
        }

        private static FluentCommandLineParser<ApplicationArguments> CreateCommandLineParser()
        {
            var parser = new FluentCommandLineParser<ApplicationArguments>();

            parser.Setup(arg => arg.CodePlexForums)
                .As('f', "forums")
                .WithDescription("List of CodePlex Forums to download.")
                .Required();

            parser.Setup(arg => arg.OutputDirectory)
                .As('o', "outputDirectory")
                .SetDefault(Path.Combine(Environment.CurrentDirectory, "output"))
                .WithDescription("Output directory where files are written. Default value is 'output'.");

            parser.Setup(arg => arg.Logfile)
                .As('l', "logfile")
                .SetDefault("codeplex_discussion_download.log")
                .WithDescription("Name of the logfile.  Default value is 'codeplex_discussion_download.log'.");

            parser.Setup(arg => arg.PageSize)
                .As('p', "pageSize")
                .SetDefault(100)
                .WithDescription("The number of posts on a generated HTML page.  Default value is 100.");

            parser.SetupHelp("?", "h", "help")
                .Callback(text => Console.WriteLine(text))
                .WithHeader("Usage: CodePlexDiscussionDownload: ");

            return parser;
        }

        private static void DownloadForums(ApplicationArguments appArgs)
        {
            foreach (var forumName in appArgs.CodePlexForums)
            {
                var forumOutputFolder = Path.Combine(appArgs.OutputDirectory, forumName);
                if (!Directory.Exists(forumOutputFolder))
                {
                    Directory.CreateDirectory(forumOutputFolder);
                }

                DownloadForum(forumName, appArgs.PageSize, forumOutputFolder);
            }
        }

        private static void DownloadForum(string forumName, int pageSize, string outputFolder)
        {
            const int startPage = 0;

            var discussionThreads = new List<DiscussionThread>();
            DownloadDiscussionThreads(forumName, startPage, pageSize, discussionThreads, outputFolder);

            WriteDiscussionThreadData(discussionThreads, forumName, outputFolder);
        }

        private static void DownloadDiscussionThreads(
            string forumName,
            int pageNumber,
            int pageSize,
            List<DiscussionThread> discussionThreads,
            string outputFolder)
        {
            var discussionThreadDocument = GetDiscussionThreadDocument(forumName, pageNumber, pageSize, outputFolder);

            discussionThreads.AddRange(
                discussionThreadDocument.DocumentNode.SelectNodes("//div[@class='post_info']")
                .Select(threadNode => GetDiscussionThread(threadNode, outputFolder)));

            if (IsLastPage(discussionThreadDocument))
            {
                return;
            }

            pageNumber++;
            DownloadDiscussionThreads(forumName, pageNumber, pageSize, discussionThreads, outputFolder);
        }

        private static DiscussionThread GetDiscussionThread(HtmlNode threadNode, string outputFolder)
        {
            var threadAnchor = threadNode.ParentNode.SelectSingleNode("div[@class='post_content']/h3/a");

            var title = threadAnchor.InnerText;
            var threadUrl = threadAnchor.Attributes["href"].Value;
            var id = threadUrl.Split('/').Last();

            var date = threadNode.SelectSingleNode("p/span[@class='smartDate dateOnly']")?.InnerText;
            var time = threadNode.SelectSingleNode("p/span[@class='smartDate timeOnly']")?.InnerText;
            var username = threadNode.ParentNode.SelectSingleNode("div[@class='post_content']/p/span[@class='author']")?.InnerText;

            var discussionThread = new DiscussionThread()
            {
                Id = id,
                AuthorUsername = username,
                DiscussionDate = date,
                Time = time,
                Title = title
            };

            var posts = GetDiscussionThreadPosts(threadUrl, outputFolder);
            discussionThread.Posts = posts;

            return discussionThread;
        }

        private static HtmlDocument GetDiscussionThreadDocument(string forumName, int pageNumber, int pageSize, string outputFolder)
        {
            var threadListUri = $"http://{forumName}.codeplex.com/discussions?searchText=&size={pageSize}&page={pageNumber}";

            var threadListHtml = DownloadUriWithRetry(() =>
            {
                using (var webClient = new WebClient())
                {
                    return webClient.DownloadString(new Uri(threadListUri));
                }
            }, threadListUri, 10);

            if (threadListHtml == null)
            {
                var message = "Could not get thread list for uri: " + threadListUri;
                Log(message);
                throw new Exception(message);
            }

            WriteThreadListPage(outputFolder, pageNumber, threadListHtml);

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(threadListHtml);
            return htmlDoc;
        }

        private static Post[] GetDiscussionThreadPosts(string threadUrl, string outputFolder)
        {
            var posts = new List<Post>();
            var discussionThreadId = threadUrl.Split('/').Last();

            var detailPageHtml = GetThreadDetailPageHtml(threadUrl);
            if (detailPageHtml == null)
            {
                return posts.ToArray();
            }

            WriteThreadDetailPage(outputFolder, discussionThreadId, detailPageHtml);

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(detailPageHtml);

            foreach (var postNode in htmlDoc.DocumentNode.SelectNodes("(//tr[@id='PostPanel'])"))
            {
                var postId = postNode.SelectSingleNode("td/div/a")?.Attributes["name"].Value?.Replace("post", string.Empty);

                if (string.IsNullOrEmpty(postId))
                {
                    Log("No postId found for PostPanel tr: " + postNode.InnerText + " and threadUrl: " + threadUrl + ".  Skipping Post.");
                    continue;
                }

                var post = GetPost(postNode, postId, discussionThreadId);
                posts.Add(post);
            }

            return posts.ToArray();
        }

        private static Post GetPost(HtmlNode postNode, string postId, string discussionThreadId)
        {
            var date = postNode.SelectSingleNode("td/div/div/span[@class='smartDate']").Attributes["title"].Value;
            var username = postNode.SelectSingleNode("td/div/div/a[@class='UserProfileLink']").InnerText;
            var content = postNode.SelectSingleNode("td[position()>1]")?.InnerHtml;

            return new Post()
            {
                Id = postId,
                Content = content,
                DateTime = date,
                DiscussionThreadId = discussionThreadId,
                Username = username
            };
        }

        private static string GetThreadDetailPageHtml(string threadUrl)
        {
            return DownloadUriWithRetry(() =>
            {
                using (var webClient = new WebClient())
                {
                    return webClient.DownloadString(new Uri(threadUrl));
                }
            }, threadUrl, 10);
        }

        private static string DownloadUriWithRetry(Func<string> downloadString, string uri, int maxNumberOfRetries)
        {
            string htmlString = null;
            var retryCount = 0;
            Exception lastException = null;
            while (htmlString == null && retryCount++ <= maxNumberOfRetries)
            {
                try
                {
                    htmlString = downloadString();
                }
                catch (WebException e)
                {
                    lastException = e;
                    Thread.Sleep(100);
                }
            }

            if (htmlString == null)
            {
                Log($"Could not get HTML data for {uri}.  Skipping.  Last Exception: {lastException}");
            }

            return htmlString;
        }

        private static bool IsLastPage(HtmlDocument htmlDoc)
        {
            var lastItem = htmlDoc.DocumentNode.SelectNodes("//li[@class='last']").Last();
            var isLastPage = lastItem.InnerHtml == "Next";
            return isLastPage;
        }

        private static void WriteDiscussionThreadData(List<DiscussionThread> discussionThreads, string forumName, string outputFolder)
        {
            var serializedDiscussionThreads = JsonConvert.SerializeObject(discussionThreads);
            var outputFile = Path.Combine(outputFolder, forumName + ".json");
            File.WriteAllText(outputFile, serializedDiscussionThreads);
        }

        private static void WriteThreadListPage(string outputFolder, int pageNumber, string threadListHtml)
        {
            File.WriteAllText(Path.Combine(outputFolder, "index_" + pageNumber + ".html"), threadListHtml);
        }

        private static void WriteThreadDetailPage(string outputFolder, string discussionThreadId, string detailPageHtml)
        {
            var postDirectory = Path.Combine(outputFolder, discussionThreadId);
            Directory.CreateDirectory(postDirectory);
            File.WriteAllText(Path.Combine(postDirectory, "index.html"), detailPageHtml);
        }

        private static void Log(string message)
        {
            if (!string.IsNullOrWhiteSpace(logFileName))
            {
                File.AppendAllText(logFileName, message + Environment.NewLine);
            }

            Console.WriteLine(message);
        }
    }
}