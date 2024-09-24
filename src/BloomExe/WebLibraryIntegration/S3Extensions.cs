using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Bloom.WebLibraryIntegration
{
    static class S3Extensions
    {
        /// <summary>
        /// Similar to the S3 API's ListObjects, but instead of returning only the first 1,000 objects, returns all of them.
        /// (Multiple calls to ListObjects may be made internally if there are many matching objects)
        /// </summary>
        /// <param name="request">The request to list objects for. Note that request may be modified by this function (specifically, its ContinuationToken property may change)</param>
        /// <returns>All matching objects as a single concatenated (non-lazy) list of S3Object</returns>
        static internal List<S3Object> ListAllObjects(
            this IAmazonS3 s3,
            ListObjectsV2Request request
        )
        {
            Debug.Assert(s3 != null, "s3 must not be null");

            var allMatchingItems = new List<S3Object>();
            ListObjectsV2Response matchingItemsResponse;

            // Note: S3 only allows a max of 1,000 objects in one go.
            do
            {
                matchingItemsResponse = s3.ListObjectsV2(request);
                allMatchingItems.AddRange(matchingItemsResponse.S3Objects);
                // matchingItemsResponse.ContinuationToken indicates where the request that generated the response started
                // matchingItemsResponse.NextContinuationToken indicates where the next request (if needed) should start
                // request.ContinuationToken indicates where the request starts the next time it is used
                request.ContinuationToken = matchingItemsResponse.NextContinuationToken;
            } while (matchingItemsResponse.IsTruncated); // IsTruncated returns true if it's not at the end

            return allMatchingItems;
        }
    }
}
