using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Collection;

namespace Bloom.web.controllers
{
	// API Handler to handle updates to the collection settings from the Talking Book Tool
	public class TalkingBookApi
	{
		public const string kApiUrlPart = "talkingBook/";

		// These options must match the ones used in audioRecording.ts
		public enum AudioRecordingMode
		{
			Unknown,
			Sentence,
			TextBox
		}

		private readonly CollectionSettings _collectionSettings;

		public TalkingBookApi(CollectionSettings collectionSettings)
		{
			// Assumes collectionSettings.Load() has already been called.
			_collectionSettings = collectionSettings;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			
			bool requiresSync = false;
			apiHandler.RegisterEnumEndpointHandler(kApiUrlPart + "defaultAudioRecordingMode",
				request => HandleGet(),
				(request, newDefaultAudioRecordingMode) => HandlePost(newDefaultAudioRecordingMode), requiresSync);
		}

		public AudioRecordingMode HandleGet()
		{
			return _collectionSettings.AudioRecordingMode;
		}

		public void HandlePost(AudioRecordingMode newDefaultAudioRecordingMode)
		{
			_collectionSettings.AudioRecordingMode = newDefaultAudioRecordingMode;
			_collectionSettings.Save();
		}
	}
}
