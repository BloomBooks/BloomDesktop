using System;
using Bloom.web;

namespace Bloom.Book
{
    public enum HarvesterState
    {
        Done,
        InProgress, // includes New or Updated harvestStates in book record
        Failed,
        FailedIndefinitely, // marked by staff as not worth trying again
        Multiple, // multiple books with this id, who knows what the harvester state of this is
    }

    public class BloomLibraryStatus
    {
        public readonly bool Draft;
        public readonly bool NotInCirculation;
        public readonly HarvesterState HarvesterState;

        // The id of the book's record on Bloom Library (not the book instance id).
        // Null when HarvesterState is Multiple, since there is then more than one record.
        private readonly string _bloomLibraryBookId;
        private readonly string _bookInstanceId;
        private readonly bool _forceUseProductionData;

        /// <summary>
        /// Record a summarized status of a book in BloomLibrary.
        /// We keep the ids rather than a URL because building the URL needs the Bloom Library
        /// URL prefix, which may require a server lookup. Statuses are created while a collection
        /// loads, often during startup, so we defer that until someone actually wants the URL.
        /// </summary>
        public BloomLibraryStatus(
            bool draft,
            bool notInCirculation,
            HarvesterState harvesterState,
            string bloomLibraryBookId,
            string bookInstanceId,
            bool forceUseProductionData
        )
        {
            Draft = draft;
            NotInCirculation = notInCirculation;
            HarvesterState = harvesterState;
            _bloomLibraryBookId = bloomLibraryBookId;
            _bookInstanceId = bookInstanceId;
            _forceUseProductionData = forceUseProductionData;
        }

        /// <summary>
        /// Status for a book instance id that has more than one upload on Bloom Library.
        /// (The other fields are meaningless in that case.)
        /// </summary>
        public static BloomLibraryStatus ForMultipleUploads(
            string bookInstanceId,
            bool forceUseProductionData
        )
        {
            return new BloomLibraryStatus(
                false,
                false,
                HarvesterState.Multiple,
                null,
                bookInstanceId,
                forceUseProductionData
            );
        }

        /// <summary>
        /// The Bloom Library page for this book: its detail page, or, if it has been uploaded
        /// more than once, a listing of all the books with its instance id.
        /// Don't call this during startup: it may need to look up the Bloom Library URL prefix.
        /// </summary>
        public string GetBloomLibraryBookUrl()
        {
            if (HarvesterState == HarvesterState.Multiple)
                return BloomLibraryUrls.BloomLibraryBooksWithMatchingIdListingUrl(
                    _bookInstanceId,
                    _forceUseProductionData
                );
            return BloomLibraryUrls.BloomLibraryDetailPageUrlFromBookId(
                _bloomLibraryBookId,
                forceUseProductionData: _forceUseProductionData
            );
        }

        public static BloomLibraryStatus FromDynamicJson(
            dynamic bookState,
            bool forceUseProductionData
        )
        {
            //Debug.WriteLine($"DEBUG draft={bookState.draft}, inCirculation={bookState.inCirculation}, harvestState={bookState.harvestState}, objectId={bookState.objectId}");
            HarvesterState harvesterState = HarvesterState.Failed;
            switch (bookState.harvestState?.ToString().ToLowerInvariant())
            {
                case "done":
                    harvesterState = HarvesterState.Done;
                    break;
                case "new":
                case "updated":
                case "inprogress":
                    harvesterState = HarvesterState.InProgress;
                    break;
                case "failed":
                    harvesterState = HarvesterState.Failed;
                    break;
                case "failedindefinitely":
                    harvesterState = HarvesterState.FailedIndefinitely;
                    break;
                default:
                    // undefined or unrecognized value will just leave it as Failed
                    break;
            }
            // Draft only if explicitly marked as such.  Undefined means not draft.
            var draft =
                (bookState.draft != null)
                && bookState.draft.ToString().ToLowerInvariant() == "true";
            // In circulation unless explicitly marked false.  Undefined means in circulation.
            var inCirculation =
                (bookState.inCirculation == null)
                || bookState.inCirculation.ToString().ToLowerInvariant() == "true";
            return new BloomLibraryStatus(
                draft,
                !inCirculation,
                harvesterState,
                (string)bookState.id.ToString(),
                (string)bookState.instanceId?.ToString(),
                forceUseProductionData
            );
        }

        /// <summary>
        /// We consider two BloomLibraryStatus objects to be equal if they have the same values for all fields.
        /// The default equality operator for a class compares references, which is not what we want.
        /// </summary>
        public static bool operator ==(BloomLibraryStatus a, BloomLibraryStatus b)
        {
            if (Object.ReferenceEquals(a, null))
                return Object.ReferenceEquals(b, null);
            return a.Equals(b);
        }

        // matching operator is required by C#.
        public static bool operator !=(BloomLibraryStatus a, BloomLibraryStatus b)
        {
            return !(a == b);
        }

        /// <summary>
        /// We consider two BloomLibraryStatus objects to be equal if they have the same values for all fields.
        /// The default equality operator for a class compares references, which is not what we want.
        /// </summary>
        public override bool Equals(object obj)
        {
            var that = obj as BloomLibraryStatus;
            if (that == null)
                return false;
            return this.Draft == that.Draft
                && this.NotInCirculation == that.NotInCirculation
                && this.HarvesterState == that.HarvesterState
                && this._bloomLibraryBookId == that._bloomLibraryBookId
                && this._bookInstanceId == that._bookInstanceId
                && this._forceUseProductionData == that._forceUseProductionData;
        }

        // GetHashCode operator override is required by C# if Equals is overridden.
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override string ToString()
        {
            return $"BlorgStatus: Draft={Draft}, NotInCirculation={NotInCirculation}, HarvesterState={HarvesterState}, BloomLibraryBookId={_bloomLibraryBookId}, BookInstanceId={_bookInstanceId}";
        }
    }
}
