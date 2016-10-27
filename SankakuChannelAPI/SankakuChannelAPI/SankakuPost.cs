using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SankakuChannelAPI
{
    [Serializable]
    public class SankakuPost
    {
        public int PostID { get; }

        /// <summary>
        /// Link to post
        /// </summary>
        public string PostReference { get; }

        /// <summary>
        /// Link to image in low preview quality.
        /// </summary>
        public string PreviewReference { get; }
        public string[] Tags { get; }
        public SankakuChannelUser AssociatedUser { get; }
        public SankakuPost(int postID, string postReference, string previewReference, string[] tags, SankakuChannelUser associatedUser)
        {
            PostID = postID;
            PostReference = postReference;
            PreviewReference = previewReference;
            this.AssociatedUser = associatedUser;
            Tags = tags;
        }

        /// <summary>
        /// Get link to image in full quality
        /// </summary>
        /// <param name="user">SankakuChannel user to search the post as</param>
        /// <returns>Link to image in full quality</returns>
        public string GetFullImageLink() => SankakuHttpHandler.GetImageLink(AssociatedUser, this.PostReference);
        public byte[] DownloadFullImage(out string imageLink, out bool wasRedirected)
        {
            imageLink = GetFullImageLink();
            return SankakuHttpHandler.DownloadImage(AssociatedUser, imageLink, out wasRedirected, true, 0);
        }
        public byte[] DownloadFullImage(out bool wasRedirected) => SankakuHttpHandler.DownloadImage(AssociatedUser, GetFullImageLink(), out wasRedirected, true, 0);

        public byte[] DownloadFullImage(string imageLink, out bool wasRedirected, bool containsVideo, double sizeLimitMB) => SankakuHttpHandler.DownloadImage(AssociatedUser, GetFullImageLink(), out wasRedirected, containsVideo, sizeLimitMB);
    }
}
