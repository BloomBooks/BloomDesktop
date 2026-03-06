using System;
using System.Drawing;
using System.Threading.Tasks;
using Bloom;
using Bloom.Api;
using Newtonsoft.Json;

namespace Bloom.web
{
    /// <summary>
    /// Loads a React bundle into an existing iframe element in the current top-level browser document.
    /// It creates stable in-memory HTML for the requested iframe id, then sets iframe.src (with a cache-busting
    /// version query) once the target iframe is present, retrying briefly because the host document may still be rendering.
    /// It borrows the logic from ReactControl that allows the relevant code to be hot-loaded from vite dev
    /// or read normally in production.
    /// </summary>
    public class IframeReactControl : IDisposable
    {
        public async Task Load(
            Browser browser,
            string javascriptBundleName,
            object props,
            string iframeId,
            Color? backColor = null
        )
        {
            if (browser == null)
                throw new ArgumentNullException(nameof(browser));
            if (string.IsNullOrEmpty(javascriptBundleName))
                throw new ArgumentNullException(nameof(javascriptBundleName));
            if (string.IsNullOrEmpty(iframeId))
                throw new ArgumentNullException(nameof(iframeId));

            var html = ReactControl.GetHtmlForReactBundle(
                javascriptBundleName,
                props,
                backColor ?? Color.White,
                hideVerticalOverflow: false
            );

            var baseUrl = BloomServer.PutFixedSimulatedHtmlForId(
                iframeId,
                html,
                InMemoryHtmlFileSource.Frame
            );
            var srcWithVersion = baseUrl + "?v=" + Guid.NewGuid();

            var iframeIdJson = JsonConvert.SerializeObject(iframeId);
            var srcJson = JsonConvert.SerializeObject(srcWithVersion);
            await browser.RunJavascriptAsync(
                $@"(() => {{
                    const targetId = {iframeIdJson};
                    const src = {srcJson};
                    let attempts = 0;
                    const maxAttempts = 40;
                    const setSrcWhenReady = () => {{
                        const iframe = document.getElementById(targetId);
                        if (iframe) {{
                            iframe.src = src;
                            return;
                        }}

                        attempts += 1;
                        if (attempts < maxAttempts) {{
                            window.setTimeout(setSrcWhenReady, 50);
                        }}
                    }};

                    setSrcWhenReady();
                }})()"
            );
        }

        public void Dispose() { }
    }
}
