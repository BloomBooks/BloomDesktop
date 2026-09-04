using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Publish;

namespace Bloom.Workspace
{
    public enum WorkspaceTab
    {
        collection,
        edit,
        publish,
    }

    /// <summary>
    /// An AutoFac-created object can obtain the one instance of this by requesting one in its
    /// constructor if it needs to know which tab is currently active in the Workspace.
    /// Enhance: if necessary we can add a change event.
    /// </summary>
    public class WorkspaceTabSelection
    {
        private WorkspaceTab _activeTab;

        /// <summary>
        /// The tab the Workspace is currently showing. This is the authoritative answer to
        /// "which tab are we on"; everything else that needs to know should either read this or
        /// be kept in step by this setter.
        /// </summary>
        public WorkspaceTab ActiveTab
        {
            get => _activeTab;
            set
            {
                _activeTab = value;
                // PublishHelper refuses to stage a book while we are not in the Publish tab, but it
                // is reached from static code that has no way to get hold of this object, so it needs
                // a static mirror of this value. We update it here, as part of the same assignment,
                // precisely so the two cannot disagree. PublishView used to own it instead, setting it
                // from its SelectedTabChangedEvent subscriber -- which runs later, after ActiveTab has
                // already changed and after however many other subscribers. Any gap or hiccup in
                // between (e.g. an activation that got skipped) left ActiveTab saying "publish" while
                // the flag still said "not publish", and then an ordinary switch-to-Publish-and-make-a-
                // BloomPUB-preview died with "Should not be creating bloom book while not in publish
                // tab". See BL-16174.
                PublishHelper.InPublishTab = value == WorkspaceTab.publish;
            }
        }
    }
}
