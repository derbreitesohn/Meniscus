using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Meniscus.EditorTools
{
	/// Batch-mode entry point for the browser build.
	/// Invoked as: Unity -quit -batchmode -executeMethod Meniscus.EditorTools.WebGLBuilder.Build
	public static class WebGLBuilder
	{
		const string AudioFolder = "Assets/Resources/WwiseAudio";

		[MenuItem("Meniscus/Build WebGL")]
		public static void Build()
		{
			var output = ArgValue("-buildOutput") ?? Path.Combine(Directory.GetCurrentDirectory(), "Build", "WebGL");

			ConfigureAudio();
			ConfigurePlayer();

			var scenes = EditorBuildSettings.scenes
				.Where(s => s.enabled)
				.Select(s => s.path)
				.ToArray();

			if (scenes.Length == 0)
				Fail("No enabled scenes in the build settings.");

			Debug.Log($"[Build] WebGL -> {output}\n[Build] scenes: {string.Join(", ", scenes)}");

			var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
			{
				scenes = scenes,
				locationPathName = output,
				target = BuildTarget.WebGL,
				targetGroup = BuildTargetGroup.WebGL,
				options = BuildOptions.None,
			});

			var summary = report.summary;
			Debug.Log($"[Build] {summary.result} — {summary.totalSize / (1024 * 1024)} MB in {summary.totalTime}");

			foreach (var step in report.steps)
				foreach (var msg in step.messages)
					if (msg.type == LogType.Error || msg.type == LogType.Exception)
						Debug.Log($"[Build][{msg.type}] {msg.content}");

			if (summary.result != BuildResult.Succeeded)
				Fail($"Build failed: {summary.result} ({summary.totalErrors} errors)");

			EditorApplication.Exit(0);
		}

		/// The recovered .wav sources are shipped raw; encode them so the browser
		/// download stays sane. WebGL decodes through the browser, hence AAC.
		static void ConfigureAudio()
		{
			if (!Directory.Exists(AudioFolder))
				return;

			var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder });
			var changed = 0;

			foreach (var guid in guids)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
					continue;

				var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
				// Long beds stay compressed in memory; one-shots decode up front so
				// they fire without a hitch.
				var longClip = clip && clip.length > 10f;

				var settings = importer.defaultSampleSettings;
				settings.loadType = longClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
				settings.compressionFormat = AudioCompressionFormat.Vorbis;
				settings.quality = 0.5f;
				settings.preloadAudioData = !longClip;
				importer.defaultSampleSettings = settings;

				var webgl = settings;
				webgl.compressionFormat = AudioCompressionFormat.AAC;
				importer.SetOverrideSampleSettings("WebGL", webgl);

				importer.forceToMono = false;

				EditorUtility.SetDirty(importer);
				importer.SaveAndReimport();
				changed++;
			}

			Debug.Log($"[Build] audio import settings applied to {changed} clips");
		}

		static void ConfigurePlayer()
		{
			// Brotli keeps the payload small; the JS fallback means the build does not
			// depend on the host sending Content-Encoding for .br files.
			PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
			PlayerSettings.WebGL.decompressionFallback = true;
			PlayerSettings.WebGL.dataCaching = true;
			PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
			PlayerSettings.WebGL.template = "APPLICATION:Default";
			PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Release);
			PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
			PlayerSettings.runInBackground = true;
		}

		static string ArgValue(string flag)
		{
			var args = Environment.GetCommandLineArgs();
			var i = Array.IndexOf(args, flag);
			return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
		}

		static void Fail(string message)
		{
			Debug.LogError("[Build] " + message);
			EditorApplication.Exit(1);
		}
	}
}
