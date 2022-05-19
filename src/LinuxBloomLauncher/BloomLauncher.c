/* This program displays a simple splash image and starts Bloom.  It quits when
 * Bloom signals it by deleting a flag file, or after 60 seconds, or when Bloom
 * dies prematurely.
 * ----------------------------------------------------------------------------
 * Copyright (c) 2017-2022 SIL International
 * This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
 */
#include <unistd.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <ctype.h>
#include <gtk/gtk.h>
#include <fcntl.h>

#ifndef PATH_MAX
#define PATH_MAX 2047
#endif

/* counter used by a timer to limit the duration of this program */
static int counter = 0;

/* directory this program is in: used for finding Bloom.exe and setting environment variables */
static gchar *programDirectory = NULL;

/* file created by this program, then deleted by Bloom proper to signal this program can quit. */
static char flagFile[] = "/tmp/BloomLaunching.now";

/* process id of the mono Bloom.exe process */
static GPid bloomPid;

/* forward declarations of methods found later in the file. */
static void DestroyWindow(GtkWidget*, gpointer);
static gboolean LimitSplashing(gpointer);
static gchar ** GetArgvForBloom(int, char **);
static gchar ** GetEnvpForBloom(void);
static int CreateFlagFile(void);
static void BloomDiedEarly(GPid pid, gint status, gpointer user_data);
static gboolean ShouldUseSystemMono();
static gboolean AreWeInsideFlatpak();
 
/* global variables set early on */
static gboolean useSystemMono = false;
static gboolean inFlatpak = false;
static FILE * fpLog = NULL;

/******************************************************************************
 * NAME
 *    main
 * DESCRIPTION
 *    main function of the program
 * RETURN VALUE
 *    0 if okay, 1 if error occured
 */
int main(int argc, char *argv[])
{
	const char * debugLauncher = g_getenv("DEBUG_LAUNCHER");
	if (debugLauncher != NULL)
	{
		char debug = tolower(debugLauncher[0]);
		if (debug == 't' || debug == 'y' || debug == '1')
			fpLog = fopen("/tmp/BloomLauncher.log", "w");
	}
	if (CreateFlagFile())
	{
		if (fpLog != NULL)
		{
			fprintf(fpLog, "Cannot create flag file\n");
			fclose(fpLog);
		}
		return 1;
	}

	/* initialize Gtk and create the main window (which is very bare bones) */
	gtk_init(&argc, &argv);
	GtkWidget *window = gtk_window_new(GTK_WINDOW_TOPLEVEL);
	gtk_container_set_border_width(GTK_CONTAINER(window), 0);
	gtk_widget_set_size_request(window, 411, 309);		/* exact size of BloomSplash.png */
	gtk_window_set_decorated(GTK_WINDOW(window), FALSE);
	gtk_window_set_position(GTK_WINDOW(window), GTK_WIN_POS_CENTER);
	gtk_window_set_resizable(GTK_WINDOW(window), FALSE);

	/* hide this program from being advertised */
	gtk_window_set_skip_taskbar_hint(GTK_WINDOW(window), TRUE);
	gtk_window_set_skip_pager_hint(GTK_WINDOW(window), TRUE);

	/* Connect the main window to the destroy signal. */
	g_signal_connect(G_OBJECT(window), "destroy", G_CALLBACK(DestroyWindow), NULL);

	/* create the image from the embedded resource and display it in the main window */
	GError *error = NULL;
	GdkPixbuf *pixbuf = gdk_pixbuf_new_from_resource("/sil/bloom/launch/../BloomSplash.png", &error);
	if (error != NULL)
	{
		// Report error to user, and free error
		fprintf(stderr, "Unable to read image resource: %s\n", error->message);
		if (fpLog != NULL)
		{
			fprintf(fpLog, "Unable to read image resource: %s\n", error->message);
			fclose(fpLog);
		}
		g_error_free(error);
		unlink(flagFile);
		return 1;
	}

	useSystemMono = ShouldUseSystemMono();
	inFlatpak = AreWeInsideFlatpak();
	if (fpLog != NULL)
	{
		if (inFlatpak)
			fprintf(fpLog, "Using system mono inside flatpak.\n");
		else if (useSystemMono)
			fprintf(fpLog, "Using system mono.\n");
		else
			fprintf(fpLog, "Using mono5-sil.\n");
	}
	
	/* start the real Bloom program */
	gchar ** argvBloom = GetArgvForBloom(argc, argv);
	if (argvBloom == NULL)
	{
		if (fpLog != NULL)
		{
			fprintf(fpLog, "GetArgvForBloom returned NULL\n");
			fclose(fpLog);
		}
		return 1;
	}
	gchar ** envpBloom = GetEnvpForBloom();
	if (fpLog != NULL)
	{
		fprintf(fpLog, "Environment for running Bloom:\n");
		for (int i = 0; envpBloom[i] != NULL; ++i)
			fprintf(fpLog, "    %s\n", envpBloom[i]);
	}

	gboolean okay = g_spawn_async(NULL, argvBloom, envpBloom, G_SPAWN_DO_NOT_REAP_CHILD,
		NULL, NULL, &bloomPid, &error);
	if (!okay || error != NULL)
	{
		if (error != NULL)
			fprintf(stderr, "Unable to start Bloom: %s\n", error->message);
		else
			fprintf(stderr, "Unable to start Bloom\n");
		if (fpLog != NULL)
		{
			if (error != NULL)
				fprintf(fpLog, "Unable to start Bloom: %s\n", error->message);
			else
				fprintf(fpLog, "Unable to start Bloom\n");
			fclose(fpLog);
		}
		g_error_free(error);
		unlink(flagFile);
		return 1;
	}
	g_child_watch_add(bloomPid, BloomDiedEarly, NULL);

	/* display the splash image, start a timer, and enter the event loop handler */
	GtkWidget *image = gtk_image_new_from_pixbuf(pixbuf);
	gtk_container_add(GTK_CONTAINER(window), image);
	gtk_widget_show_all(window);
	g_timeout_add_seconds(1, LimitSplashing, NULL);
	gtk_main();

	unlink(flagFile);	// just in case...  ignore any errors
	if (fpLog != NULL)
	{
		fprintf(fpLog, "Startup program finished successfully!\n");
		fclose(fpLog);
	}
	return 0;
}

/******************************************************************************
 * NAME
 *    DestroyWindow
 * DESCRIPTION
 *    stop the program in response to the window being destroyed by Alt-F4
 * RETURN VALUE
 *    none
 */
static void DestroyWindow(GtkWidget *window, gpointer data)
{
	gtk_main_quit();
}

/******************************************************************************
 * NAME
 *    LimitSplashing
 * DESCRIPTION
 *    stop the program after 60 seconds, or when the flag file no longer exists
 * RETURN VALUE
 *    TRUE to continue the timer
 */
static gboolean LimitSplashing(gpointer data)
{
	++counter;
	// access returns 0 if file exists, -1 if it has been deleted (by Bloom)
	if (counter >= 60 || access(flagFile, F_OK) != 0)
	{
		gtk_main_quit();
		return FALSE;
	}
	return TRUE;
}

/******************************************************************************
 * NAME
 *    GetArgvForBloom
 * DESCRIPTION
 *    Get the argument array to execute Bloom side by side with this program.
 * RETURN VALUE
 *    argument array, or NULL if Bloom cannot be found
 */
static gchar ** GetArgvForBloom(int argcOrig, char ** argvOrig)
{
	gchar ** argvNew = g_new(gchar *, argcOrig + 2);	// 1 new arg + terminating NULL

	// On Linux, mono is the program that is running: Bloom.exe is just data for the program
	// Bloom uses the debugged version of Mono 5.16.0.1 packaged by SIL/LSDev on older versions
	// of Linux.  From Ubuntu 22.04 on (or in flatpak packages), Bloom must use the standard
	// system mono.

	argvNew[0] = g_strdup(inFlatpak ? "/app/bin/mono" : (useSystemMono ? "/usr/bin/mono" : "/opt/mono5-sil/bin/mono"));
	// Sacrifice line numbers in stack dumps for speed -- often don't see them anyway.
	// If we do reinstate the "--debug", then the "+ 2" and "1 new" above need to be
	// incremented, and the "[1]", "[i+1]", and "[argcOrig+1]" below need to be incremented.
	//argvNew[1] = g_strdup("--debug");

	// Get the full path to the executing program, and verify that it is readable
	// and is marked executable.
	char * progpath = (char *)malloc(PATH_MAX+1);
	if (progpath == NULL)
	{
		fprintf(stderr, "insufficient memory\n");
		if (fpLog != NULL)
			fprintf(fpLog, "insufficient memory\n");
		return NULL;
	}
	ssize_t len = readlink("/proc/self/exe", progpath, PATH_MAX);
	if (len < 0)
	{
		perror("readlink failed");
		if (fpLog != NULL)
			fprintf(fpLog, "readlink failed\n");
		free(progpath);
		return NULL;
	}
	progpath[len] = '\0';
	char * enddirs = strrchr(progpath, '/');
	if (enddirs == NULL)
	{
		if (fpLog != NULL)
			fprintf(fpLog, "enddirs == NULL?  progpath='%s'\n", progpath);
		free(progpath);
		return NULL;
	}
	programDirectory = g_strndup(progpath, enddirs - progpath);
	++enddirs;
	if (enddirs + strlen("Bloom.exe") > progpath + PATH_MAX)
	{
		if (fpLog != NULL)
			fprintf(fpLog, "path too long: progpath='%s'\n", progpath);
		free(progpath);
		return NULL;
	}
	strcpy(enddirs, "Bloom.exe");
	// access returns 0 if file can be read and executed, -1 if either is not true.
	if (access(progpath, R_OK|X_OK) != 0)
	{
		if (fpLog != NULL)
			fprintf(fpLog, "Bloom.exe not accessible: progpath='%s'\n", progpath);
		free(progpath);
		return NULL;
	}
	argvNew[1] = progpath;

	for (int i = 1; i < argcOrig; ++i)
		argvNew[i+1] = g_strdup(argvOrig[i]);
	argvNew[argcOrig+1] = NULL;

	if (fpLog != NULL)
	{
		for (int i = 0; argvNew[i] != NULL; ++i)
			fprintf(fpLog, "DEBUG BloomLauncher spawn: argvNew[%d] = '%s'\n", i, argvNew[i]);
	}
	return argvNew;
}

/******************************************************************************
 * NAME
 *    GetEnvpForBloom
 * DESCRIPTION
 *    Get the environment array to pass to Bloom when we run it.
 * RETURN VALUE
 *    array of environment variable name/value pairs
 */
static gchar ** GetEnvpForBloom()
{
	gchar ** envp = g_get_environ();

	/* If XULRUNNER is set, assume the environment is already okay. */
	const gchar * xulrunner = g_environ_getenv(envp, "XULRUNNER");
	if (xulrunner != NULL && strlen(xulrunner) > 0)
		return envp;

	/* Emulate environ-xulrunner and set XULRUNNER, LD_LIBRARY_PATH, and LD_PRELOAD */
	const char * xulpath = g_strconcat(programDirectory, "/Firefox/libxul.so", NULL);
	envp = g_environ_setenv(envp, "XULRUNNER", xulpath, true);
	const char * preload = g_strconcat(programDirectory, "/Firefox/libgeckofix.so", NULL);
	envp = g_environ_setenv(envp, "LD_PRELOAD", preload, true);
	const char * libpathOld = g_environ_getenv(envp, "LD_LIBRARY_PATH");
	const char * libpath;
	if (libpathOld == NULL || strlen(libpathOld) == 0)
		libpath = g_strconcat(programDirectory, "/Firefox", NULL);
	else
		libpath = g_strconcat(programDirectory, "/Firefox:", libpathOld, NULL);
	envp = g_environ_setenv(envp, "LD_LIBRARY_PATH", libpath, true);

	/* also set MONO_PREFIX, other MONO related values, and PATH */
	envp = g_environ_setenv(envp, "MONO_PREFIX",
							g_strdup(inFlatpak ? "/app" : (useSystemMono ? "/usr" : "/opt/mono5-sil")), true);
	envp = g_environ_setenv(envp, "MONO_RUNTIME", g_strdup("v4.0.30319"), true);
	envp = g_environ_setenv(envp, "MONO_DEBUG", g_strdup("explicit-null-checks"), true);
	envp = g_environ_setenv(envp, "MONO_ENV_OPTIONS", g_strdup("-O=-gshared"), true);
	envp = g_environ_setenv(envp, "MONO_TRACE_LISTENER", g_strdup("Console.Out"), true);
	envp = g_environ_setenv(envp, "MONO_MWF_SCALING", g_strdup("disable"), true);
	envp = g_environ_setenv(envp, "MONO_PATH", g_strconcat(programDirectory, ":/usr/lib/cli/gdk-sharp-2.0", NULL), true);
	envp = g_environ_setenv(envp, "MONO_GAC_PREFIX",
							g_strdup(inFlatpak ? "/app" : (useSystemMono ? "/usr" : "/opt/mono5-sil:/usr")), true);
	const char * pathOld = g_environ_getenv(envp, "PATH");
	const char * path;
	if (pathOld == NULL || strlen(pathOld) == 0)
		path = g_strdup(programDirectory);
	else
		path = g_strconcat(programDirectory, ":", pathOld, NULL);
	envp = g_environ_setenv(envp, "PATH", path, true);

	return envp;
}

/******************************************************************************
 * NAME
 *    CreateFlagFile
 * DESCRIPTION
 *    create the flag file if it doesn't already exist
 * RETURN VALUE
 *    0 if successful, non-zero if the file already exists or can't be created
 */
static int CreateFlagFile()
{
	int fd = open(flagFile, O_CREAT|O_EXCL, 0644);
	if (fd < 0)
	{
		/* The file can't be created (O_CREAT) or already exists (O_EXCL). */
		return 1;
	}
	close(fd);
	return 0;
}

/******************************************************************************
 * NAME
 *    BloomDiedEarly
 * DESCRIPTION
 *    Handle the case of Bloom crashing on startup.
 * RETURN VALUE
 *    none
 */
static void BloomDiedEarly(GPid pid, gint status, gpointer user_data)
{
	gtk_main_quit();
}


/******************************************************************************
 * NAME
 *    ShouldUseSystemMono
 * DESCRIPTION
 *    Check whether or not the system mono should be used instead our mono5-sil.
 * RETURN VALUE
 *    true if the system mono should be used
 */
static gboolean ShouldUseSystemMono()
{
	return access("/opt/mono5-sil/bin/mono", F_OK) != 0;
}

/******************************************************************************
 * NAME
 *    AreWeInsideFlatpak
 * DESCRIPTION
 *    Check whether or not the program is running inside a flatpak environment.
 * RETURN VALUE
 *    true if running inside a flatpak environment for Bloom
 */
static gboolean AreWeInsideFlatpak()
{
	const char * flatpakId = g_getenv("FLATPAK_ID");
	return flatpakId != NULL && strncmp(flatpakId, "org.sil.Bloom", 13) == 0;
}
