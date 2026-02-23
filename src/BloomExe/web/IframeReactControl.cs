using System;
using System.Drawing;
using System.Threading.Tasks;
using Bloom;
using Bloom.Api;
using Newtonsoft.Json;

namespace Bloom.web
{
    public class IframeReactControl : IDisposable
    {
        public async Task Load(
            Browser browser,
            string javascriptBundleName,
            object props,
            string iframeId
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
                Color.White,
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
