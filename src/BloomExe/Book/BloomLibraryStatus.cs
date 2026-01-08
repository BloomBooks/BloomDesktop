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
        public readonly string BloomLibraryBookUrl;

        /// <summary>
        /// Record a summarized status of a book in BloomLibrary.
        /// </summary>
        public BloomLibraryStatus(
            bool draft,
            bool notInCirculation,
            HarvesterState harvesterState,
            string bloomLibraryBookUrl
        )
        {
            Draft = draft;
            NotInCirculation = notInCirculation;
            HarvesterState = harvesterState;
            BloomLibraryBookUrl = bloomLibraryBookUrl;
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
            var url = BloomLibraryUrls.BloomLibraryDetailPageUrlFromBookId(
                bookState.id.ToString(),
                forceUseProductionData: forceUseProductionData
            );
            return new BloomLibraryStatus(draft, !inCirculation, harvesterState, url);
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
                && this.BloomLibraryBookUrl == that.BloomLibraryBookUrl;
        }

        // GetHashCode operator override is required by C# if Equals is overridden.
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override string ToString()
        {
            return $"BlorgStatus: Draft={Draft}, NotInCirculation={NotInCirculation}, HarvesterState={HarvesterState}, BloomLibraryBookUrl={BloomLibraryBookUrl}";
        }
    }
}
