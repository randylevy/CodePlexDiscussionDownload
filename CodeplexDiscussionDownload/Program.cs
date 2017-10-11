using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

using HtmlAgilityPack;
using Newtonsoft.Json;

namespace CodeplexDiscussionDownload
{
    internal class Program
    {
        private static string logFileName;

        private static void Main()
        {
            var configuration = LoadConfiguration();

            logFileName = Path.Combine(configuration.OutputFolder, configuration.LogFileName);

            DownloadForums(configuration);
        }

        private static Configuration LoadConfiguration()
        {
            var codePlexForums = ConfigurationManager.AppSettings["codePlexForums"];
            var logFilename = ConfigurationManager.AppSettings["logFileName"];
            var pageSize = ConfigurationManager.AppSettings["pageSize"] ?? "100";
            var outputFolder = ConfigurationManager.AppSettings["outputFolder"] ?? "output";

            return new Configuration(codePlexForums, logFilename, pageSize, outputFolder);
        }

        private static void DownloadForums(Configuration config)
        {
            foreach (var forumName in config.ForumNames)
            {
                var forumOutputFolder = Path.Combine(config.OutputFolder, forumName);
                if (!Directory.Exists(forumOutputFolder))
                {
                    Directory.CreateDirectory(forumOutputFolder);
                }

                DownloadForum(forumName, config.PageSize, forumOutputFolder);
            }
        }

        private static void DownloadForum(string forumName, string pageSize, string outputFolder)
        {
            const int startPage = 0;

            var discussionThreads = new List<DiscussionThread>();
            DownloadDiscussionThreads(forumName, startPage, pageSize, discussionThreads, outputFolder);

            WriteDiscussionThreadData(discussionThreads, forumName, outputFolder);
        }

        private static void DownloadDiscussionThreads(
            string forumName,
            int pageNumber,
            string pageSize,
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

        private static HtmlDocument GetDiscussionThreadDocument(string forumName, int pageNumber, string pageSize, string outputFolder)
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