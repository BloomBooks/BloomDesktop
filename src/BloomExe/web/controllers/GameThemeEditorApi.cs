using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Endpoints for the game theme editor (the self-contained editor in src/gameThemeEditor,
    /// embedded by Bloom). It can save a theme to the current book (customBookStyles2.css),
    /// collection-wide (customCollectionStyles.css), and—when Bloom is running from source—into
    /// the factory theme source (gamesThemes.less).
    /// </summary>
    public class GameThemeEditorApi
    {
        private readonly CollectionSettings _collectionSettings;
        private readonly BookSelection _bookSelection;

        /// <summary>A theme as sent from the editor: a slug, a display name, and the CSS custom
        /// properties it sets explicitly.</summary>
        private class ThemeDto
        {
            public string slug;
            public string displayName;
            public Dictionary<string, string> variables;

            /// <summary>When the user renamed a custom theme, the slug it had before, so we can
            /// remove the old rule (a rename, not a duplicate). Empty/null when creating.</summary>
            public string renameFrom;
        }

        // Called by Autofac, which creates the one instance and registers it with the server.
        public GameThemeEditorApi(
            CollectionSettings collectionSettings,
            BookSelection bookSelection
        )
        {
            _collectionSettings = collectionSettings;
            _bookSelection = bookSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "gameThemeEditor/saveToBook",
                HandleSaveToBook,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "gameThemeEditor/saveToCollection",
                HandleSaveToCollection,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "gameThemeEditor/saveToFactorySource",
                HandleSaveToFactorySource,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "gameThemeEditor/canSaveToFactorySource",
                HandleCanSaveToFactorySource,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "gameThemeEditor/deleteFromCollection",
                HandleDeleteFromCollection,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "gameThemeEditor/deleteFromBook",
                HandleDeleteFromBook,
                true
            );
        }

        /// <summary>The current book's own custom stylesheet. customBookStyles2.css is the
        /// appearance-system custom CSS file; when present in the book folder it is linked
        /// automatically on load (for non-legacy themes, which game books use).</summary>
        private string CustomBookStylesPath =>
            Path.Combine(_bookSelection.CurrentSelection.FolderPath, "customBookStyles2.css");

        /// <summary>The collection-wide custom stylesheet, which lives in the collection folder
        /// and is loaded by every book in the collection.</summary>
        private string CustomCollectionStylesPath =>
            Path.Combine(_collectionSettings.FolderPath, "customCollectionStyles.css");

        /// <summary>The factory theme source file in the developer's source tree, or null when not
        /// running from source (in which case the file does not exist beside the running app).</summary>
        private static string FactoryThemesSourcePathOrNull()
        {
            var path = Path.Combine(
                FileLocationUtilities.DirectoryOfApplicationOrSolution,
                "src",
                "content",
                "templates",
                "template books",
                "Games",
                "gamesThemes.less"
            );
            return RobustFile.Exists(path) ? path : null;
        }

        private void HandleSaveToBook(ApiRequest request)
        {
            var theme = request.RequiredPostObject<ThemeDto>();
            try
            {
                WriteThemeToFile(CustomBookStylesPath, theme);
            }
            catch (Exception e)
            {
                request.Failed(HttpStatusCode.InternalServerError, e.Message);
                return;
            }
            request.PostSucceeded();
        }

        private void HandleSaveToCollection(ApiRequest request)
        {
            var theme = request.RequiredPostObject<ThemeDto>();
            try
            {
                WriteThemeToFile(CustomCollectionStylesPath, theme);
            }
            catch (Exception e)
            {
                request.Failed(HttpStatusCode.InternalServerError, e.Message);
                return;
            }
            request.PostSucceeded();
        }

        private void HandleSaveToFactorySource(ApiRequest request)
        {
            var theme = request.RequiredPostObject<ThemeDto>();
            var path = FactoryThemesSourcePathOrNull();
            if (path == null)
            {
                request.Failed(
                    HttpStatusCode.BadRequest,
                    "The factory theme source is only available when running Bloom from source."
                );
                return;
            }
            try
            {
                WriteThemeToFile(path, theme);
                // The .less source is what we just edited, but the live page does not load it: it
                // loads the book folder's own copy of the compiled Games.css (placed there by
                // BookStorage.UpdateSupportFiles when the book was opened). The LESS watcher will
                // recompile output/.../Games.css, but that copy is only pulled into the book folder
                // on the next book load, so without this the change vanishes the moment we navigate
                // to another page. Upsert the same rule directly into the book's served copy so the
                // change survives navigation. The next book load overwrites this with an identical
                // recompiled-from-source version, so the two stay consistent.
                var bookGamesCss = Path.Combine(
                    _bookSelection.CurrentSelection.FolderPath,
                    "Games.css"
                );
                if (RobustFile.Exists(bookGamesCss))
                    WriteThemeToFile(bookGamesCss, theme);
            }
            catch (Exception e)
            {
                request.Failed(HttpStatusCode.InternalServerError, e.Message);
                return;
            }
            request.PostSucceeded();
        }

        /// <summary>Upsert the theme's rule into the file at <paramref name="path"/>, and—when the
        /// theme was renamed—remove the rule for its previous slug so a rename does not leave a
        /// duplicate behind.</summary>
        private static void WriteThemeToFile(string path, ThemeDto theme)
        {
            var existing = RobustFile.Exists(path) ? RobustFile.ReadAllText(path) : "";
            var updated = UpsertThemeRule(existing, theme);
            if (!string.IsNullOrEmpty(theme.renameFrom) && theme.renameFrom != theme.slug)
                updated = RemoveThemeRule(updated, theme.renameFrom);
            RobustFile.WriteAllText(path, updated);
        }

        private void HandleCanSaveToFactorySource(ApiRequest request)
        {
            request.ReplyWithJson(FactoryThemesSourcePathOrNull() != null);
        }

        private void HandleDeleteFromCollection(ApiRequest request)
        {
            DeleteThemeFromFile(request, CustomCollectionStylesPath);
        }

        private void HandleDeleteFromBook(ApiRequest request)
        {
            DeleteThemeFromFile(request, CustomBookStylesPath);
        }

        /// <summary>Remove the posted theme's rule from the file at <paramref name="path"/>, if present.</summary>
        private void DeleteThemeFromFile(ApiRequest request, string path)
        {
            var theme = request.RequiredPostObject<ThemeDto>();
            try
            {
                if (RobustFile.Exists(path))
                {
                    var updated = RemoveThemeRule(RobustFile.ReadAllText(path), theme.slug);
                    RobustFile.WriteAllText(path, updated);
                }
            }
            catch (Exception e)
            {
                request.Failed(HttpStatusCode.InternalServerError, e.Message);
                return;
            }
            request.PostSucceeded();
        }

        /// <summary>
        /// Replace the existing rule block for this theme's slug, or append a new one if absent.
        /// The selector is the same one Bloom's theme system uses (.bloom-page.game-theme-&lt;slug&gt;),
        /// which both customCollectionStyles.css and gamesThemes.less use, so this works for both.
        /// INVARIANT: a game-theme rule block contains only flat declarations (no nested braces).
        /// The "[^}]*" matcher relies on this; both editor-generated rules and the factory themes
        /// honor it. A hand-edited rule containing "{"/"}" would not match correctly.
        /// </summary>
        private static string UpsertThemeRule(string css, ThemeDto theme)
        {
            var rule = GenerateThemeRule(theme);
            // Match ".bloom-page.game-theme-<slug> { ... }" with a slug boundary so e.g. "blue" does
            // not match "blue-on-white". Theme blocks contain only declarations (no nested braces).
            var pattern =
                @"\.bloom-page\.game-theme-" + Regex.Escape(theme.slug) + @"(?![\w-])\s*\{[^}]*\}";
            var regex = new Regex(pattern);
            if (regex.IsMatch(css))
                return regex.Replace(css, rule, 1);

            var trimmed = css.TrimEnd();
            return trimmed.Length == 0 ? rule + "\n" : trimmed + "\n\n" + rule + "\n";
        }

        /// <summary>Remove the rule block for the given theme slug (used when a theme is renamed).</summary>
        private static string RemoveThemeRule(string css, string slug)
        {
            var pattern =
                @"\.bloom-page\.game-theme-" + Regex.Escape(slug) + @"(?![\w-])\s*\{[^}]*\}";
            var withoutRule = Regex.Replace(css, pattern, "");
            // Collapse the blank gap the removal may leave behind.
            var tidied = Regex.Replace(withoutRule, @"\n{3,}", "\n\n").TrimEnd();
            return tidied.Length == 0 ? "" : tidied + "\n";
        }

        private static string GenerateThemeRule(ThemeDto theme)
        {
            var sb = new StringBuilder();
            sb.Append(".bloom-page.game-theme-").Append(theme.slug).Append(" {\n");
            if (theme.variables != null)
            {
                foreach (var name in theme.variables.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    sb.Append("    ")
                        .Append(name)
                        .Append(": ")
                        .Append(theme.variables[name])
                        .Append(";\n");
            }
            sb.Append("}");
            return sb.ToString();
        }
    }
}
