namespace CodeplexDiscussionDownload
{
    public class DiscussionThread
    {
        public string Id { get; set; }
        public string DiscussionDate { get; set; }
        public string Time { get; set; }
        public string Title { get; set; }
        public string AuthorUsername { get; set; }
        public Post[] Posts { get; set; }
    }
}