using Bloom.Api;
using Bloom.Collection;
using Bloom.CollectionCreating;

namespace Bloom.web.controllers
{
    public class NewCollectionWizardApi
    {
        public static NewCollectionWizard CurrentWizard;

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "newCollection/selectLanguage",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                        return; // Should be a post
                    var data = DynamicJson.Parse(request.RequiredPostJson());

                    if (CurrentWizard != null)
                    {
                        CurrentWizard.SelectLanguage(
                            data.LanguageTag,
                            data.DesiredName,
                            data.DefaultName
                        );
                        request.PostSucceeded();
                    }
                },
                true // TODO
            );
        }
    }
}
