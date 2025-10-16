using System;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.web;

namespace BloomTests
{
    internal static class ApiTestRuntime
    {
        private const string HostEnvironmentVariable = "BLOOM_API_TEST_HOST";
        private static bool? _useKestrel;

        public static bool UseKestrel
        {
            get
            {
                if (!_useKestrel.HasValue)
                {
                    var value = Environment.GetEnvironmentVariable(HostEnvironmentVariable);
                    _useKestrel = string.Equals(
                        value,
                        "KESTREL",
                        StringComparison.OrdinalIgnoreCase
                    );
                }

                return _useKestrel.Value;
            }
        }
    }

    public interface IApiTestServer : IDisposable
    {
        BloomApiHandler ApiHandler { get; }
        void EnsureListening();
        string ServerUrlWithBloomPrefixEndingInSlash { get; }
        void SetCollectionSettingsDuringInitialization(CollectionSettings collectionSettings);
    }

    internal static class ApiTestServerFactory
    {
        public static IApiTestServer Create(BookSelection bookSelection, bool? useKestrel = null)
        {
            bookSelection ??= new BookSelection();
            var hostFlag = useKestrel ?? ApiTestRuntime.UseKestrel;

            if (hostFlag)
            {
                var apiHandler = new BloomApiHandler(bookSelection);
                var imageProcessor = new RuntimeImageProcessor(new BookRenamedEvent());
                var collectionSettings = new CollectionSettings();
                var locator = BuildFileLocator(collectionSettings);
                var server = new KestrelBloomServer(
                    imageProcessor,
                    bookSelection,
                    locator,
                    apiHandler
                );
                return new KestrelApiTestServerAdapter(server, apiHandler);
            }

            var legacyServer = new BloomServer(bookSelection);
            return new BloomApiTestServerAdapter(legacyServer);
        }

        private static BloomFileLocator BuildFileLocator(CollectionSettings collectionSettings)
        {
            var xmatterFinder = new XMatterPackFinder(Array.Empty<string>());
            return new BloomFileLocator(
                collectionSettings,
                xmatterFinder,
                Array.Empty<string>(),
                Array.Empty<string>()
            );
        }
    }

    internal sealed class BloomApiTestServerAdapter : IApiTestServer
    {
        private readonly BloomServer _server;

        public BloomApiTestServerAdapter(BloomServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
        }

        public BloomApiHandler ApiHandler => _server.ApiHandler;

        public void EnsureListening()
        {
            _server.EnsureListening();
        }

        public string ServerUrlWithBloomPrefixEndingInSlash =>
            BloomServer.ServerUrlWithBloomPrefixEndingInSlash;

        public void SetCollectionSettingsDuringInitialization(CollectionSettings collectionSettings)
        {
            _server.SetCollectionSettingsDuringInitialization(collectionSettings);
        }

        public void Dispose()
        {
            _server.Dispose();
        }
    }

    internal sealed class KestrelApiTestServerAdapter : IApiTestServer
    {
        private readonly KestrelBloomServer _server;
        private readonly BloomApiHandler _apiHandler;

        public KestrelApiTestServerAdapter(KestrelBloomServer server, BloomApiHandler apiHandler)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _apiHandler = apiHandler ?? throw new ArgumentNullException(nameof(apiHandler));
        }

        public BloomApiHandler ApiHandler => _apiHandler;

        public void EnsureListening()
        {
            _server.EnsureListening();
        }

        public string ServerUrlWithBloomPrefixEndingInSlash =>
            KestrelBloomServer.ServerUrlWithBloomPrefixEndingInSlash;

        public void SetCollectionSettingsDuringInitialization(CollectionSettings collectionSettings)
        {
            _server.SetCollectionSettingsDuringInitialization(collectionSettings);
        }

        public void Dispose()
        {
            _server.Dispose();
        }
    }
}
