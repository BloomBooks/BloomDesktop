using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public WorkspaceTab ActiveTab;
    }
}
