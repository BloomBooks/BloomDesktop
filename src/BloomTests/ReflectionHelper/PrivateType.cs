using System;
using System.Globalization;
using System.Reflection;

namespace BloomTests.ReflectionHelper
{
	internal class PrivateType
	{
		private Type m_type;

		public PrivateType(Type type)
		{
			if (type == null)
				throw new ArgumentNullException("type");
			this.m_type = type;
		}

		internal object InvokeStatic(string name, params object[] args)
		{
			return m_type.InvokeMember(name,
				BindingFlags.InvokeMethod| BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy,
				null, null, args, CultureInfo.InvariantCulture);
		}
	}
}
