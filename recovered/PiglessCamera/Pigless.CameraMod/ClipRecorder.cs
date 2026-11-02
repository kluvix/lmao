using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace Pigless.CameraMod;

internal sealed class ClipRecorder : IDisposable
{
	private sealed class SaveResult
	{
		internal string Path;

		internal Exception Error;

		internal double Seconds;
	}

	private const int FramesPerSecond = 15;

	private const int LongEdge = 640;

	private const int JpegQuality = 75;

	private readonly ManualLogSource log;

	private RollingClipBuffer buffer;

	private RenderTexture capture;

	private RenderTexture sourceSeen;

	private Texture2D readable;

	private MethodInfo encodeJpg;

	private readonly object[] encodeArgs = new object[2];

	private Task<SaveResult> saveTask;

	private bool disposed;

	private bool failed;

	private bool formatKnown;

	private bool lastMirror;

	private float nextCapture;

	private float nextStatus;

	private float noticeUntil;

	private float lastTick = float.NaN;

	private string status = "CLIPS BUFFER WHEN CAMERA IS ON";

	internal string Status => status;

	internal bool IsSaving => saveTask != null;

	internal double BufferedSeconds => buffer?.BufferedSeconds ?? 0.0;

	internal string LastClipPath { get; private set; } = "";

	internal ClipRecorder(ManualLogSource logger)
	{
		log = logger ?? throw new ArgumentNullException("logger");
	}

	internal void Tick(RenderTexture source, bool enabled, bool mirror, float now)
	{
		if (disposed || float.IsNaN(now) || float.IsInfinity(now))
		{
			return;
		}
		if (!float.IsNaN(lastTick) && now < lastTick)
		{
			buffer?.Clear();
			nextCapture = 0f;
			sourceSeen = null;
			noticeUntil = 0f;
		}
		lastTick = now;
		PollSave(now);
		if (!enabled || source == null || !source.IsCreated())
		{
			if (buffer != null)
			{
				buffer.Clear();
			}
			sourceSeen = null;
			formatKnown = false;
			nextCapture = 0f;
			failed = false;
			ReleaseCapture();
			if (saveTask == null && now >= noticeUntil)
			{
				status = "CLIPS PAUSED - CAMERA OFF";
			}
		}
		else
		{
			if (failed)
			{
				return;
			}
			if (sourceSeen != source)
			{
				sourceSeen = source;
				nextCapture = now + 1f / 15f;
				if (buffer == null && saveTask == null && now >= noticeUntil)
				{
					status = "BUFFERING VIDEO...";
				}
			}
			else
			{
				if (now < nextCapture)
				{
					return;
				}
				nextCapture += 1f / 15f;
				if (nextCapture < now - 1f / 15f)
				{
					nextCapture = now + 1f / 15f;
				}
				RenderTexture active = RenderTexture.active;
				bool sRGBWrite = GL.sRGBWrite;
				try
				{
					int num = Math.Max(source.width, source.height);
					if (num <= 0)
					{
						return;
					}
					double num2 = Math.Min(1.0, 640.0 / (double)num);
					int num3 = Math.Max(2, (int)Math.Round((double)source.width * num2) & -2);
					int num4 = Math.Max(2, (int)Math.Round((double)source.height * num2) & -2);
					if (!formatKnown || capture == null || capture.width != num3 || capture.height != num4 || lastMirror != mirror)
					{
						ReleaseCapture();
						buffer = new RollingClipBuffer(num3, num4);
						capture = new RenderTexture(num3, num4, 0, RenderTextureFormat.ARGB32)
						{
							name = "Pigless clip / capture",
							filterMode = FilterMode.Bilinear,
							antiAliasing = 1,
							useMipMap = false,
							autoGenerateMips = false
						};
						if (!capture.Create())
						{
							throw new InvalidOperationException("Video capture render size is unsupported.");
						}
						readable = new Texture2D(num3, num4, TextureFormat.RGB24, mipChain: false, linear: false)
						{
							name = "Pigless clip / reusable frame"
						};
						lastMirror = mirror;
						formatKnown = true;
						log.LogInfo("Video clip buffer ready: " + num3 + "x" + num4 + " at " + 15 + " fps, up to 15 seconds; silent MJPEG AVI.");
					}
					GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
					Graphics.Blit(source, capture, new Vector2(mirror ? (-1f) : 1f, 1f), new Vector2(mirror ? 1f : 0f, 0f));
					RenderTexture.active = capture;
					readable.ReadPixels(new Rect(0f, 0f, num3, num4), 0, 0, recalculateMipMaps: false);
					readable.Apply(updateMipmaps: false, makeNoLongerReadable: false);
					if (encodeJpg == null)
					{
						encodeJpg = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", throwOnError: false)?.GetMethod("EncodeToJPG", BindingFlags.Public | BindingFlags.Static, null, new Type[2]
						{
							typeof(Texture2D),
							typeof(int)
						}, null);
						if (encodeJpg == null)
						{
							throw new MissingMethodException("The game's JPEG video-frame encoder is unavailable.");
						}
					}
					encodeArgs[0] = readable;
					encodeArgs[1] = 75;
					if (!(encodeJpg.Invoke(null, encodeArgs) is byte[] array) || array.Length < 4 || array[0] != byte.MaxValue || array[1] != 216)
					{
						throw new InvalidOperationException("The game did not return a valid JPEG video frame.");
					}
					if (!buffer.Add(now, array))
					{
						throw new InvalidOperationException("A video frame exceeded the bounded clip buffer.");
					}
					if (saveTask == null && now >= noticeUntil && now >= nextStatus)
					{
						nextStatus = now + 0.5f;
						status = "CLIP READY: " + BufferedSeconds.ToString("0") + "s / 15s";
					}
				}
				catch (Exception ex)
				{
					failed = true;
					buffer?.Clear();
					status = "CLIP FAILED - CHECK LOG";
					log.LogError("Video clip capture failed: " + (ex.InnerException ?? ex));
				}
				finally
				{
					GL.sRGBWrite = sRGBWrite;
					RenderTexture.active = active;
					encodeArgs[0] = null;
				}
			}
		}
	}

	internal void SaveClip()
	{
		if (disposed)
		{
			return;
		}
		float unscaledTime = Time.unscaledTime;
		PollSave(unscaledTime);
		if (saveTask != null)
		{
			status = "SAVING CLIP...";
			return;
		}
		if (failed)
		{
			failed = false;
			buffer?.Clear();
			sourceSeen = null;
			formatKnown = false;
			nextCapture = 0f;
			ReleaseCapture();
			status = "RESTARTING CLIP BUFFER";
			noticeUntil = unscaledTime + 2f;
			log.LogInfo("Retrying video clip capture after an explicit CLIP button press.");
			return;
		}
		if (buffer == null || buffer.Count < 2 || BufferedSeconds < 0.5)
		{
			status = (failed ? "CLIP FAILED - CHECK LOG" : "BUFFERING - WAIT A SECOND");
			noticeUntil = unscaledTime + 2f;
			return;
		}
		ClipSnapshot snapshot = buffer.Snapshot();
		string text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
		if (string.IsNullOrEmpty(text))
		{
			text = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
		}
		if (string.IsNullOrEmpty(text))
		{
			text = Paths.ConfigPath;
		}
		string folder = Path.Combine(text, "PiglessCamera");
		string destination = Path.Combine(folder, "Pigless-Clip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".avi");
		status = "SAVING CLIP...";
		saveTask = Task.Run(() => Save(snapshot, folder, destination));
	}

	private static SaveResult Save(ClipSnapshot snapshot, string folder, string destination)
	{
		bool flag = false;
		try
		{
			Directory.CreateDirectory(folder);
			using (FileStream destination2 = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.SequentialScan))
			{
				flag = true;
				AviWriter.Write(destination2, snapshot.Width, snapshot.Height, snapshot.FramesPerSecond, snapshot.Frames);
			}
			return new SaveResult
			{
				Path = destination,
				Seconds = snapshot.Seconds
			};
		}
		catch (Exception error)
		{
			if (flag)
			{
				try
				{
					File.Delete(destination);
				}
				catch
				{
				}
			}
			return new SaveResult
			{
				Error = error
			};
		}
	}

	private void PollSave(float now)
	{
		if (saveTask != null && saveTask.IsCompleted)
		{
			SaveResult saveResult;
			try
			{
				saveResult = saveTask.GetAwaiter().GetResult();
			}
			catch (Exception error)
			{
				saveResult = new SaveResult
				{
					Error = error
				};
			}
			saveTask = null;
			noticeUntil = now + 5f;
			if (saveResult.Error != null)
			{
				status = "CLIP FAILED - CHECK LOG";
				log.LogError("Video clip save failed: " + saveResult.Error);
				return;
			}
			LastClipPath = saveResult.Path;
			status = "CLIP SAVED: " + saveResult.Seconds.ToString("0.0") + "s";
			log.LogInfo("Video clip saved: " + saveResult.Path + " (" + saveResult.Seconds.ToString("0.0") + " seconds, silent MJPEG AVI).");
		}
	}

	private void ReleaseCapture()
	{
		if (capture != null)
		{
			if (RenderTexture.active == capture)
			{
				RenderTexture.active = null;
			}
			capture.Release();
			UnityEngine.Object.Destroy(capture);
			capture = null;
		}
		if (readable != null)
		{
			UnityEngine.Object.Destroy(readable);
			readable = null;
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			ReleaseCapture();
			buffer?.Clear();
			sourceSeen = null;
			encodeArgs[0] = null;
		}
	}
}
