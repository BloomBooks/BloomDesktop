using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Collection;

namespace Bloom.web.controllers
{
	public class TalkingBookApi
	{
		public const string kApiUrlPart = "talkingBook/";

		// These options must match the ones used in audioRecording.ts
		public enum AudioRecordingMode
		{
			Unknown,
			Sentence,
			TextBox,
			Custom
		}

		private readonly CollectionSettings _collectionSettings;

		public TalkingBookApi(CollectionSettings collectionSettings)
		{
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
			//_collectionSettings.Load();
			return _collectionSettings.AudioRecordingMode;
		}

		public void HandlePost(AudioRecordingMode newDefaultAudioRecordingMode)
		{
			_collectionSettings.AudioRecordingMode = newDefaultAudioRecordingMode;
			//_collectionSettings.Save();
		}
	}
}
