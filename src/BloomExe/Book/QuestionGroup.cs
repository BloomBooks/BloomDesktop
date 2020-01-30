using Newtonsoft.Json;

namespace Bloom.Book
{
	/// <summary>
	/// These classes exist to support serializing a BloomReader QuestionGroup into json.
	/// A QuestionGroup corresponds to one bloom-editable div on the questions page:
	/// It contains one or more questions in a language.
	/// </summary>
	public class QuestionGroup
	{
		public Question[] questions { get; set; }
		public string lang { get; set; }
		public bool onlyForBloomReader1 { get; set; }

		public string GetJson()
		{
			return JsonConvert.SerializeObject(this);
		}

		public static QuestionGroup[] FromJson(string json)
		{
			return JsonConvert.DeserializeObject<QuestionGroup[]>(json);
		}
	}

	/// <summary>
	/// One question (with its answers)
	/// </summary>
	public class Question
	{
		public string question { get; set; }
		public Answer[] answers { get; set; }
	}

	/// <summary>
	/// An answer, with correctness designated.
	/// </summary>
	public class Answer
	{
		public string text { get; set; }
		public bool correct { get; set; }
	}
}
