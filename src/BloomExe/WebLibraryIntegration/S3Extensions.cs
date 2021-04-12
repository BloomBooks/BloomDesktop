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
		/// <param name="request">The request to list objects for. Note that request may be modified by this function (specifically, its Marker property may change)</param>
		/// <returns>All matching objects as a single concatenated (non-lazy) list of S3Object</returns>
		static internal List<S3Object> ListAllObjects(this IAmazonS3 s3, ListObjectsRequest request)
		{
			Debug.Assert(s3 != null, "s3 must not be null");

			var allMatchingItems = new List<S3Object>();
			ListObjectsResponse matchingItemsResponse;

			// Note: S3 only allows a max of 1,000 objects in one go.
			do
			{
				matchingItemsResponse = s3.ListObjects(request);
				allMatchingItems.AddRange(matchingItemsResponse.S3Objects);
				request.Marker = matchingItemsResponse.NextMarker;	// NextMarker indicates where the next request should start
			}
			while (matchingItemsResponse.IsTruncated);	// IsTruncated returns true if it's not at the end

			return allMatchingItems;
		}
	}
}
