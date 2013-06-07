using System;
using System.Reflection;
using Autofac;
using Bloom.CollectionChoosing;
using Bloom.Properties;
using Bloom.ToPalaso;
using System.Linq;
using L10NSharp;
using NetSparkle;


namespace Bloom
{
		/// <summary>
		/// This is sortof a wrapper around the DI container. I'm not thrilled with the name I've
		/// used (jh).
		/// </summary>
		public class ApplicationContainer : IDisposable
		{
			private IContainer _container;

			public ApplicationContainer()
			{
				var builder = new ContainerBuilder();
				//builder.RegisterModule<WhiteboxProfilingModule>();

				//default to InstancePerDependency, i.e., they it will make a new
				//one each time someone asks for one
				builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly());

				builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
					.Where(t => t.GetInterfaces().Contains(typeof(ICommand))).InstancePerLifetimeScope();

				builder.Register<Sparkle>(c =>
											  {
												  var s = new Sparkle(@"http://build.palaso.org/guestAuth/repository/download/bt78/.lastSuccessful/appcast.xml", Resources.Bloom);
												  s.CustomInstallerArguments = "/qb";
												  s.DoLaunchAfterUpdate = false;
												  return s;
											  }).InstancePerLifetimeScope();

				builder.Register(c => LocalizationManager).SingleInstance();

				if (Settings.Default.MruProjects==null)
				{
					Settings.Default.MruProjects = new MostRecentPathsList();
				}
				builder.RegisterInstance(Settings.Default.MruProjects).SingleInstance();

				_container = builder.Build();
			}

			public OpenAndCreateCollectionDialog OpenAndCreateCollectionDialog()
			{
				return _container.Resolve<OpenAndCreateCollectionDialog>();
			}

			public Sparkle ApplicationUpdator
			{
				get { return _container.Resolve<Sparkle>(); }
			}

			public LocalizationManager LocalizationManager;

			public void Dispose()
			{
				_container.Dispose();
				_container = null;
			}

			public ProjectContext CreateProjectContext(string projectPath)
			{
				return new ProjectContext(projectPath, _container);
			}
		}
	}
