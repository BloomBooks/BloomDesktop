using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Book;
using Bloom.SafeXml;

namespace Bloom.Publish
{
    /// <summary>
    /// Replaces inline QR markers on xMatter pages during publishing.
    /// Authors use this by typing text such as <c>QR: example.com</c> or
    /// <c>qr: person@example.com</c> in xMatter content. Publish code should call
    /// <see cref="ReplaceOnXmatterPages"/> on the pages being emitted; content pages are
    /// left alone, and the marker text is replaced with a small QR image wrapped in a plain link.
    /// </summary>
    internal static class InlineQrCodeReplacer
    {
        private const string kInlineQrCodeClass = "bloom-inline-qrcode";
        private const string kInlineQrCodeDisplaySize = "72";
        private static readonly Regex kInlineQrMarkerRegex = new Regex(
            @"qr:\s*(?<payload>\S+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
        private static readonly char[] kInlineQrTrailingPunctuation =
        {
            '.',
            ',',
            ';',
            ':',
            '!',
            '?',
            ')',
        };
        private static readonly Regex kUriSchemeRegex = new Regex(
            @"^[a-z][a-z0-9+.-]*:",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
        private static readonly Regex kEmailAddressRegex = new Regex(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        /// <summary>
        /// Normalizes the payload that will be encoded into the QR code and used for the link target.
        /// Existing URI schemes are preserved, email addresses gain <c>mailto:</c>, and everything
        /// else defaults to <c>https://</c>.
        /// </summary>
        internal static string NormalizePayload(string rawPayload)
        {
            var trimmedPayload = rawPayload?.Trim();
            if (string.IsNullOrEmpty(trimmedPayload))
                return null;

            if (kUriSchemeRegex.IsMatch(trimmedPayload))
                return trimmedPayload;

            if (trimmedPayload.StartsWith("//"))
                return "https:" + trimmedPayload;

            if (kEmailAddressRegex.IsMatch(trimmedPayload))
                return "mailto:" + trimmedPayload;

            return "https://" + trimmedPayload;
        }

        /// <summary>
        /// Replaces inline QR markers on the supplied xMatter pages.
        /// Call this on the publish DOM after the target pages have been selected and before the
        /// final output is generated.
        /// </summary>
        internal static void ReplaceOnXmatterPages(
            IEnumerable<SafeXmlElement> pageElts,
            string bookFolderPath
        )
        {
            foreach (var page in pageElts)
            {
                if (string.IsNullOrWhiteSpace(page.GetAttribute("data-xmatter-page")))
                    continue;

                ReplaceOnXmatterPage(page, bookFolderPath);
            }
        }

        /// <summary>
        /// Replaces inline QR markers on a single xMatter page.
        /// This is mainly useful for focused tests or callers that already know they have exactly
        /// one xMatter page to transform.
        /// </summary>
        internal static void ReplaceOnXmatterPage(SafeXmlElement page, string bookFolderPath)
        {
            var textNodes = page.SafeSelectNodes(
                    ".//text()[not(ancestor::script) and not(ancestor::style)]"
                )
                .ToArray();
            foreach (var textNode in textNodes)
            {
                ReplaceInTextNode(textNode, bookFolderPath);
            }
        }

        private static void ReplaceInTextNode(SafeXmlNode textNode, string bookFolderPath)
        {
            var originalText = textNode.InnerText;
            var matches = kInlineQrMarkerRegex.Matches(originalText).Cast<Match>().ToArray();
            if (matches.Length == 0)
                return;

            var parent = textNode.ParentNode;
            if (parent == null)
                return;

            var currentIndex = 0;
            foreach (var match in matches)
            {
                InsertTextFragment(
                    parent,
                    textNode,
                    originalText.Substring(currentIndex, match.Index - currentIndex)
                );

                var payloadAndSuffix = SplitPayloadAndSuffix(match.Groups["payload"].Value);
                var normalizedPayload = NormalizePayload(payloadAndSuffix.payload);
                if (normalizedPayload == null)
                {
                    InsertTextFragment(parent, textNode, match.Value);
                }
                else
                {
                    parent.InsertBefore(
                        CreateInlineQrLink(
                            textNode.OwnerDocument,
                            normalizedPayload,
                            bookFolderPath
                        ),
                        textNode
                    );
                    InsertTextFragment(parent, textNode, payloadAndSuffix.suffix);
                }

                currentIndex = match.Index + match.Length;
            }

            InsertTextFragment(parent, textNode, originalText.Substring(currentIndex));
            parent.RemoveChild(textNode);
        }

        private static void InsertTextFragment(
            SafeXmlNode parent,
            SafeXmlNode referenceNode,
            string textFragment
        )
        {
            if (string.IsNullOrEmpty(textFragment))
                return;

            parent.InsertBefore(
                referenceNode.OwnerDocument.CreateTextNode(textFragment),
                referenceNode
            );
        }

        private static (string payload, string suffix) SplitPayloadAndSuffix(string payload)
        {
            var suffixStart = payload.Length;
            while (
                suffixStart > 0 && kInlineQrTrailingPunctuation.Contains(payload[suffixStart - 1])
            )
            {
                suffixStart--;
            }

            return (payload.Substring(0, suffixStart), payload.Substring(suffixStart));
        }

        private static SafeXmlElement CreateInlineQrLink(
            SafeXmlDocument ownerDocument,
            string normalizedPayload,
            string bookFolderPath
        )
        {
            var link = ownerDocument.CreateElement("a");
            link.SetAttribute("href", normalizedPayload);
            link.SetAttribute(
                "style",
                "display:inline-block; margin-top:1em; text-decoration:none; color:inherit;"
            );

            var img = ownerDocument.CreateElement("img");
            img.SetAttribute("class", kInlineQrCodeClass);
            img.SetAttribute(
                "src",
                BookStorage.GeneratePlainQrCodeImage(bookFolderPath, normalizedPayload)
            );
            img.SetAttribute("alt", $"QR code for {normalizedPayload}");
            img.SetAttribute("style", "display:block; width:72px; height:72px;");
            img.SetAttribute("width", kInlineQrCodeDisplaySize);
            img.SetAttribute("height", kInlineQrCodeDisplaySize);
            link.AppendChild(img);

            return link;
        }
    }
}
