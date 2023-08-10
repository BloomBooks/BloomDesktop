using System;
using System.Globalization;
using System.Reflection;

namespace BloomTests.ReflectionHelper
{
	internal class PrivateObject
	{
		private object m_target;
		private Type m_type;

		public PrivateObject(object obj)
		{
			if (obj == null)
				throw new ArgumentNullException("obj");
			this.m_target = obj;
			m_type = obj.GetType();
		}

		internal object Invoke(string name, params object[] args)
		{
			return m_type.InvokeMember(name,
				BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null, m_target, args, CultureInfo.InvariantCulture);
		}

		internal void SetFieldOrProperty(string name, object value)
		{
			m_type.InvokeMember(name,
				BindingFlags.SetField | BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null, m_target, new object[] { value }, CultureInfo.InvariantCulture);
		}
	}
}
