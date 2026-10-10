using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StreamAlive
{
    namespace Google.Apis.YouTube.Samples
    {
        /// <summary>
        /// YouTube Data API v3 sample: search by keyword.
        /// Relies on the Google APIs Client Library for .NET, v1.7.0 or higher.
        /// See https://developers.google.com/api-client-library/dotnet/get_started
        /// https://github.com/youtube/api-samples/blob/07263305b59a7c3275bc7e925f9ce6cabf774022/dotnet/Search.cs
        ///
        /// Set ApiKey to the API key value from the APIs & auth > Registered apps tab of
        ///   https://cloud.google.com/console
        /// Please ensure that you have enabled the YouTube Data API for your project.
        /// </summary>
        class Search
        {

            public bool streamAlive = false;
            public bool youtubeException = false;

            public async Task Run()
            {
                var youtubeService = new YouTubeService(new BaseClientService.Initializer()
                {
                    ApiKey = "AIzaSyAhAc8ke9HZbMzTEdnKS-oUZIdNnPlg7rw",
                    ApplicationName = "EventTimer"
                });

                var searchListRequest = youtubeService.Search.List("snippet");
                searchListRequest.Type = "video";
                searchListRequest.Q = "Johanneskirche Hersbruck"; /*searchString;*/ // search term.
                                                                                    // searchListRequest.ChannelId = searchString;
                searchListRequest.MaxResults = 100;

                // Call the search.list method to retrieve results matching the specified query term.
                var searchListResponse = await searchListRequest.ExecuteAsync();

                List<string> videos = new List<string>();
                // List<string> channels = new List<string>();
                // List<string> playlists = new List<string>();

                // Add each result to the appropriate list, and then display the lists of
                // matching videos, channels, and playlists.
                foreach (var searchResult in searchListResponse.Items)
                {
                    switch (searchResult.Id.Kind)
                    {
                        case "youtube#video":
                            videos.Add(String.Format("{0} ({1})", searchResult.Snippet.LiveBroadcastContent, searchResult.Id.VideoId));
                            break;

                            /*case "youtube#channel":
                                channels.Add(String.Format("{0} ({1})", searchResult.Snippet.LiveBroadcastContent, searchResult.Id.ChannelId));
                                break;

                            case "youtube#playlist":
                                playlists.Add(String.Format("{0} ({1})", searchResult.Snippet.Title, searchResult.Id.PlaylistId));
                                break;*/
                    }
                }
                if (videos.Count > 0)
                {
                    streamAlive = false;
                    for (int count = 0; count < videos.Count; count++)
                    {
                        if (videos[count].Substring(0, 4) == "live") // otherwise "none" or "upcoming"
                        {
                            streamAlive = true;
                            Console.WriteLine(videos[count]);
                        }
                    }
                }
                //Console.WriteLine(String.Format("Videos:\n{0}\n", string.Join("\n", videos)));
                //Console.WriteLine(String.Format("Channels:\n{0}\n", string.Join("\n", channels)));
                //Console.WriteLine(String.Format("Playlists:\n{0}\n", string.Join("\n", playlists)));
            }
        }
    }
}
