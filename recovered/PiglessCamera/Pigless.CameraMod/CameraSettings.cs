using BepInEx.Configuration;

namespace Pigless.CameraMod;

internal sealed class CameraSettings
{
	internal readonly ConfigEntry<bool> Enabled;

	internal readonly ConfigEntry<bool> DesktopOutput;

	internal readonly ConfigEntry<bool> ShowModel;

	internal readonly ConfigEntry<bool> ShowInterface;

	internal readonly ConfigEntry<bool> HorizonLock;

	internal readonly ConfigEntry<bool> CollisionAvoidance;

	internal readonly ConfigEntry<bool> Mirror;

	internal readonly ConfigEntry<bool> Guides;

	internal readonly ConfigEntry<bool> DollyLoop;

	internal readonly ConfigEntry<bool> RollLock;

	internal readonly ConfigEntry<bool> Flipped;

	internal readonly ConfigEntry<CameraMode> Mode;

	internal readonly ConfigEntry<float> Fov;

	internal readonly ConfigEntry<float> Distance;

	internal readonly ConfigEntry<float> Height;

	internal readonly ConfigEntry<float> Shoulder;

	internal readonly ConfigEntry<float> Smoothing;

	internal readonly ConfigEntry<float> OrbitSpeed;

	internal readonly ConfigEntry<float> Roll;

	internal readonly ConfigEntry<float> DollyDuration;

	internal readonly ConfigEntry<int> ResolutionPreset;

	internal readonly ConfigEntry<int> AspectPreset;

	internal CameraSettings(ConfigFile file)
	{
		Enabled = file.Bind("Output", "Enabled", defaultValue: false, "Enable the separate filming camera. The headset camera is left alone.");
		DesktopOutput = file.Bind("Output", "DesktopOutput", defaultValue: true, "Show the filming view on the desktop game window.");
		ShowModel = file.Bind("Output", "ShowCameraModel", defaultValue: true, "Show the local camera body and live viewfinder in VR.");
		ShowInterface = file.Bind("Output", "ShowInterface", defaultValue: false, "Include layer-5 UI in the camera feed. The camera body/viewfinder remain excluded.");
		Mirror = file.Bind("Output", "Mirror", defaultValue: false, "Mirror the desktop feed and saved photos horizontally.");
		Guides = file.Bind("Output", "CompositionGuides", defaultValue: false, "Draw white rule-of-thirds guides over the desktop view. Guides are not saved into photos.");
		ResolutionPreset = file.Bind("Output", "ResolutionPreset", 0, "0 = 720p base, 1 = 1080p base, 2 = 1440p base.");
		AspectPreset = file.Bind("Output", "AspectPreset", 0, "0 = 16:9, 1 = 9:16, 2 = square, 3 = 2.35:1 cinema.");
		Mode = file.Bind("View", "Mode", CameraMode.Follow, "FirstPerson, Follow, Selfie, Orbit, Tripod, Handheld, Dolly, GoPro or Track. The tablet directly selects its six main modes.");
		Fov = file.Bind("View", "FieldOfView", 70f, "Vertical camera FOV, clamped to 30–110 degrees.");
		Distance = file.Bind("View", "Distance", 2.2f, "Follow/selfie/orbit distance, clamped to 0.6–6 metres.");
		Height = file.Bind("View", "Height", 0.25f, "Follow/selfie/orbit height offset, clamped to -1–3 metres.");
		Shoulder = file.Bind("View", "Shoulder", 0.35f, "Horizontal follow/selfie offset, clamped to -0.8–0.8 metres.");
		Flipped = file.Bind("View", "Flipped", defaultValue: false, "Reverse the filming direction. First person and GoPro become an outside selfie view in front of your gorilla.");
		Smoothing = file.Bind("Motion", "Responsiveness", 10f, "0 disables smoothing; positive response coefficients 1–30. Lower positive values produce more smoothing/lag.");
		RollLock = file.Bind("Motion", "RollLock", defaultValue: true, "Keep the filming horizon level. Turn off to retain physical head/tablet roll. First person and GoPro never lag behind the headset.");
		HorizonLock = file.Bind("Motion", "HorizonLock", defaultValue: true, "Legacy setting retained for older configs. Use the tablet's Roll Lock control instead.");
		CollisionAvoidance = file.Bind("Motion", "CollisionAvoidance", defaultValue: true, "Keep follow/selfie/orbit cameras out of nearby world geometry.");
		OrbitSpeed = file.Bind("Motion", "OrbitDegreesPerSecond", 20f, "Orbit angular speed, clamped to -90–90 degrees/second. Zero pauses orbit.");
		Roll = file.Bind("Motion", "Roll", 0f, "Legacy setting retained for older configs. The camera ignores this hidden roll offset; use the tablet's Roll Lock control.");
		DollyDuration = file.Bind("Dolly", "Duration", 8f, "Seconds for each A-to-B move, clamped to 1–60.");
		DollyLoop = file.Bind("Dolly", "PingPongLoop", defaultValue: true, "Repeat the camera path from A to B and back while playing.");
	}

	internal void ResetView()
	{
		Mode.Value = CameraMode.Follow;
		Fov.Value = 70f;
		Distance.Value = 2.2f;
		Height.Value = 0.25f;
		Shoulder.Value = 0.35f;
		Smoothing.Value = 10f;
		OrbitSpeed.Value = 20f;
		Roll.Value = 0f;
		HorizonLock.Value = true;
		RollLock.Value = true;
		Flipped.Value = false;
		CollisionAvoidance.Value = true;
		Mirror.Value = false;
	}
}
