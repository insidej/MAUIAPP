using Microsoft.Extensions.Logging;

namespace MauiApp;

public static class MauiProgram
{
	public static global::Microsoft.Maui.Hosting.MauiApp CreateMauiApp()
	{
		var builder = global::Microsoft.Maui.Hosting.MauiApp.CreateBuilder();
		ConfigureAudioSession();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});
		Microsoft.Maui.Handlers.GraphicsViewHandler.Mapper.AppendToMapping("FingerPickMultiTouch", (handler, view) =>
		{
#if IOS
			handler.PlatformView.MultipleTouchEnabled = true;
#endif
		});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	private static void ConfigureAudioSession()
	{
#if IOS
		var audioSession = AVFoundation.AVAudioSession.SharedInstance();
		var categoryError = audioSession.SetCategory(AVFoundation.AVAudioSessionCategory.Playback, AVFoundation.AVAudioSessionCategoryOptions.MixWithOthers);
		if (categoryError is not null)
			System.Diagnostics.Debug.WriteLine($"Could not configure iOS audio playback: {categoryError}");

		if (!audioSession.SetActive(true, out var activationError))
			System.Diagnostics.Debug.WriteLine($"Could not activate iOS audio playback: {activationError}");
#endif
	}
}
