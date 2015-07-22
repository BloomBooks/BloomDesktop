/*
 * Based on code posted at https://bitbucket.org/geckofx/geckofx-2.0/wiki/GeckofxHelp
 * See the FAQ called "How do I get a stack trace on javascript error?"
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gecko;

namespace Bloom
{
	internal class JavaScriptErrorHandler : jsdIErrorHook
	{
		private readonly List<string> _errorsToIgnore;

		public string ErrorMessage { get; set; }

		public JavaScriptErrorHandler(List<string> errorsToIgnore)
		{
			_errorsToIgnore = errorsToIgnore;
		}

		// Returning true suppresses the generation of a call stack by JavaScriptCallHook.OnExecute()
		public bool OnError(nsAUTF8StringBase message, nsAUTF8StringBase fileName, uint line, uint pos, uint flags, uint errnum, jsdIValue exc)
		{
			// the message to show or log
			ErrorMessage = string.Format(@"There was a JScript error in {0} at line {1}: {2}", fileName, line, message);

			// we are completely ignoring messages in _errorsToIgnore
			return _errorsToIgnore.Any(matchString => ErrorMessage.Contains(matchString));
		}
	}

	internal class JavaScriptCallHook : jsdIExecutionHook
	{
		private readonly JavaScriptErrorHandler _jsErrorHandler;

		public event EventHandler<JavaScriptErrorArgs> JavaScriptError;

		public JavaScriptCallHook(JavaScriptErrorHandler jsErrorHandler)
		{
			// we need this in order to get the original error message
			_jsErrorHandler = jsErrorHandler;
		}

		public uint OnExecute(jsdIStackFrame frame, uint type, ref jsdIValue val)
		{
			using (nsAUTF8String filename = new nsAUTF8String("unknown"))
			using (nsAUTF8String functionName = new nsAUTF8String())
			{
				var depth = 0;
				var callStack = new StringBuilder();

				while (frame != null)
				{
					frame.GetFunctionNameAttribute(functionName);
					var line = frame.GetLineAttribute();
					var script = frame.GetScriptAttribute();
					if (script != null)
						script.GetFileNameAttribute(filename);

					callStack.AppendLine(string.Format(@"{0}: {1}, line: {2}, method: {3}().", depth++, filename, line, functionName));

					frame = frame.GetCallingFrameAttribute();
				}

				callStack.AppendLine();

				var handler = JavaScriptError;
				if (handler != null)
				{
					handler(this, new JavaScriptErrorArgs(_jsErrorHandler.ErrorMessage, callStack.ToString()));
				}

				_jsErrorHandler.ErrorMessage = string.Empty;
			}
			return 0;
		}
	}

	internal class JavaScriptErrorArgs : EventArgs
	{
		public string Message { get; private set; }
		public string CallStack { get; private set; }

		public JavaScriptErrorArgs(string message, string callStack)
		{
			Message = message;
			CallStack = callStack;
		}
	}
}
