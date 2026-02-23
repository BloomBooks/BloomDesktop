using System;
using System.Drawing;
using System.Threading.Tasks;
using Bloom;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.web
{
    public class IframeReactControl : IDisposable
    {
        private TempFile _tempFile;

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

            _tempFile?.Dispose();
            _tempFile = ReactControl.MakeTempFileForReactBundle(
                javascriptBundleName,
                props,
                Color.White,
                hideVerticalOverflow: false,
                detach: false
            );

            var iframeIdJson = JsonConvert.SerializeObject(iframeId);
            var srcJson = JsonConvert.SerializeObject(_tempFile.Path.ToLocalhost());
            await browser.RunJavascriptAsync(
                $"(() => {{ const iframe = document.getElementById({iframeIdJson}); if (!iframe) return; iframe.src = {srcJson}; }})()"
            );
        }

        public void Dispose()
        {
            _tempFile?.Dispose();
            _tempFile = null;
        }
    }
}
