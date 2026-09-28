using Plugin.Maui.Audio;

namespace MauiApp;

public partial class MainPage : ContentPage
{
	private readonly Random random = new();
	private readonly FingerBoardDrawable boardDrawable = new();
	private CancellationTokenSource? roundCancellation;
	private IAudioPlayer? cuePlayer;
	private Stream? cueStream;
	private IReadOnlyList<PointF> activeTouches = Array.Empty<PointF>();
	private bool roundStarted;
	private bool hasWinner;

	public MainPage()
	{
		InitializeComponent();
		TouchBoard.Drawable = boardDrawable;
	}

	private void OnTouchStart(object? sender, TouchEventArgs e)
	{
		if (hasWinner)
			return;

		HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
		UpdateTouches(e.Touches);
		if (!roundStarted && activeTouches.Count > 0)
		{
			roundStarted = true;
			PromptLabel.Text = "Hold steady";
			ResultLabel.Text = "Stay still. The pick is closing in.";
			_ = PickWinnerAfterDelayAsync();
		}
	}

	private void OnTouchDrag(object? sender, TouchEventArgs e)
	{
		if (!hasWinner)
			UpdateTouches(e.Touches);
	}

	private void OnTouchEnd(object? sender, TouchEventArgs e)
	{
		if (hasWinner)
			return;

		var releasedTouch = activeTouches
			.Where(previousTouch => !e.Touches.Any(currentTouch => TouchDistance(previousTouch, currentTouch) < 24))
			.Select(touch => (PointF?)touch)
			.FirstOrDefault();
		releasedTouch ??= e.Touches.Select(touch => (PointF?)touch).FirstOrDefault();

		if (roundStarted && releasedTouch is PointF winner)
		{
			roundCancellation?.Cancel();
			cuePlayer?.Stop();
			CompleteRound(winner, "First finger lifted wins");
			return;
		}

		UpdateTouches(e.Touches);
	}

	private void UpdateTouches(IReadOnlyList<PointF> touches)
	{
		activeTouches = touches;
		boardDrawable.SetTouches(activeTouches, null);
		ParticipantLabel.Text = $"{activeTouches.Count} {(activeTouches.Count == 1 ? "finger" : "fingers")} on screen";
		TouchBoard.Invalidate();
	}

	private static float TouchDistance(PointF first, PointF second)
	{
		var x = first.X - second.X;
		var y = first.Y - second.Y;
		return MathF.Sqrt(x * x + y * y);
	}

	private async Task PickWinnerAfterDelayAsync()
	{
		roundCancellation?.Cancel();
		roundCancellation = new CancellationTokenSource();
		var token = roundCancellation.Token;

		try
		{
			var drumSequence = PlayDrumSequenceAsync(token);
			for (var seconds = 5; seconds > 0; seconds--)
			{
				CountdownLabel.Text = $"00:{seconds:00}";
				await PulseForSecondAsync(seconds, token);
			}
			await drumSequence;

			if (token.IsCancellationRequested || activeTouches.Count == 0)
			{
				ResultLabel.Text = "Keep a finger on the board to be picked";
				return;
			}

			var winner = activeTouches[random.Next(activeTouches.Count)];
			CompleteRound(winner, "Winner selected at random");
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void CompleteRound(PointF winner, string result)
	{
		hasWinner = true;
		PulseOverlay.CancelAnimations();
		PulseOverlay.Opacity = 0;
		BoardPrompt.IsVisible = false;
		boardDrawable.SetTouches(activeTouches, winner);
		CountdownLabel.Text = "PICKED";
		PromptLabel.Text = "The circle picked one";
		ResultLabel.Text = result;
		TouchBoard.Invalidate();
		HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
		SemanticScreenReader.Announce("A finger has been selected");
	}

	private async Task PulseForSecondAsync(int secondsRemaining, CancellationToken token)
	{
		var intervalMilliseconds = 760 - ((5 - secondsRemaining) * 90);
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		while (stopwatch.ElapsedMilliseconds < 1000)
		{
			token.ThrowIfCancellationRequested();
			var remainingMilliseconds = (int)Math.Min(intervalMilliseconds, 1000 - stopwatch.ElapsedMilliseconds);
			var fadeMilliseconds = Math.Max(1, remainingMilliseconds / 3);
			await PulseOverlay.FadeToAsync(0.18, (uint)fadeMilliseconds, Easing.SinInOut);
			var timeLeft = 1000 - (int)stopwatch.ElapsedMilliseconds;
			if (timeLeft <= 0)
				break;

			await PulseOverlay.FadeToAsync(0, (uint)Math.Min(timeLeft, Math.Max(1, remainingMilliseconds - fadeMilliseconds)), Easing.SinInOut);
		}

		var secondRemainder = 1000 - (int)stopwatch.ElapsedMilliseconds;
		if (secondRemainder > 0)
			await Task.Delay(secondRemainder, token);
	}

	private async Task PlayDrumSequenceAsync(CancellationToken token)
	{
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		var hitIndex = 0;

		while (stopwatch.ElapsedMilliseconds < 5000)
		{
			token.ThrowIfCancellationRequested();
			var progress = Math.Clamp((float)stopwatch.ElapsedMilliseconds / 5000, 0, 1);
			PlayDrumHit(hitIndex++, progress);
			var beatInterval = (int)(900 - (progress * 650));
			var remainingMilliseconds = 5000 - (int)stopwatch.ElapsedMilliseconds;
			if (remainingMilliseconds > 0)
				await Task.Delay(Math.Min(beatInterval, remainingMilliseconds), token);
		}
	}

	private void PlayDrumHit(int hitIndex, float progress)
	{
		try
		{
			cuePlayer?.Stop();
			cuePlayer?.Dispose();
			cueStream?.Dispose();

			cueStream = CreateDrumWave(hitIndex, progress);
			cuePlayer = AudioManager.Current.CreatePlayer(cueStream);
			cuePlayer.Volume = 1;
			cuePlayer.Play();
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Could not play suspense cue: {exception}");
			cuePlayer?.Dispose();
			cueStream?.Dispose();
			cuePlayer = null;
			cueStream = null;
		}
	}

	private static MemoryStream CreateDrumWave(int hitIndex, float progress)
	{
		const int sampleRate = 22050;
		const double durationSeconds = 0.24;
		var sampleCount = (int)(sampleRate * durationSeconds);
		var dataSize = sampleCount * sizeof(short);
		var stream = new MemoryStream(44 + dataSize);
		var noise = new Random(hitIndex + Environment.TickCount);

		using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
		{
			writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
			writer.Write(36 + dataSize);
			writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
			writer.Write(16);
			writer.Write((short)1);
			writer.Write((short)1);
			writer.Write(sampleRate);
			writer.Write(sampleRate * sizeof(short));
			writer.Write((short)sizeof(short));
			writer.Write((short)16);
			writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
			writer.Write(dataSize);

			for (var index = 0; index < sampleCount; index++)
			{
				var time = (double)index / sampleRate;
				var baseFrequency = 100 + (progress * 12);
				var pitchDecay = Math.Exp(-time * 18);
				var phase = 2 * Math.PI * (baseFrequency * time + (100 * (1 - pitchDecay) / 18));
				var body = Math.Sin(phase) * Math.Exp(-time * 13);
			var lowResonance = Math.Sin(2 * Math.PI * 76 * time) * Math.Exp(-time * 20) * 0.3;
			var attack = (noise.NextDouble() * 2 - 1) * Math.Exp(-time * 110) * 0.4;
				var sampleValue = Math.Clamp((body + lowResonance + attack) * 0.82, -1, 1);
				var sample = (short)(sampleValue * 26000);
				writer.Write(sample);
			}
		}

		stream.Position = 0;
		return stream;
	}

	private void OnResetClicked(object? sender, EventArgs e)
	{
		roundCancellation?.Cancel();
		PulseOverlay.CancelAnimations();
		PulseOverlay.Opacity = 0;
		cuePlayer?.Stop();
		roundStarted = false;
		hasWinner = false;
		activeTouches = Array.Empty<PointF>();
		boardDrawable.SetTouches(activeTouches, null);
		CountdownLabel.Text = "READY";
		PromptLabel.Text = "Place your fingers";
		BoardPrompt.IsVisible = true;
		ParticipantLabel.Text = "0 fingers on screen";
		ResultLabel.Text = "First finger starts the 5 second countdown";
		TouchBoard.Invalidate();
	}
}

internal sealed class FingerBoardDrawable : IDrawable
{
	private IReadOnlyList<PointF> touches = Array.Empty<PointF>();
	private PointF? winner;

	public void SetTouches(IReadOnlyList<PointF> newTouches, PointF? selectedWinner)
	{
		touches = newTouches;
		winner = selectedWinner;
	}

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		foreach (var touch in touches)
		{
			var isWinner = winner.HasValue && Distance(touch, winner.Value) < 1;
			var radius = isWinner ? 38f : 28f;
			canvas.FillColor = isWinner ? Color.FromArgb("#FFB454") : Color.FromArgb("#8FF0C2");
			canvas.FillCircle(touch, radius);
			canvas.StrokeColor = isWinner ? Color.FromArgb("#FFE1A8") : Color.FromArgb("#D9FFF0");
			canvas.StrokeSize = isWinner ? 4 : 2;
			canvas.DrawCircle(touch, radius);
		}

		if (winner is PointF selectedTouch)
		{
			var direction = FindArrowDirection(selectedTouch, dirtyRect);
			DrawWinnerArrow(canvas, selectedTouch, direction);
		}
	}

	private PointF FindArrowDirection(PointF target, RectF bounds)
	{
		var diagonal = 1 / MathF.Sqrt(2);
		PointF[] directions =
		[
			new(0, -1), new(diagonal, -diagonal), new(1, 0), new(diagonal, diagonal),
			new(0, 1), new(-diagonal, diagonal), new(-1, 0), new(-diagonal, -diagonal)
		];
		var bestDirection = directions[0];
		var bestScore = float.NegativeInfinity;

		foreach (var direction in directions)
		{
			var tail = Offset(target, direction, 112);
			if (tail.X < bounds.Left + 18 || tail.X > bounds.Right - 18 ||
				tail.Y < bounds.Top + 18 || tail.Y > bounds.Bottom - 18)
				continue;

			var arrowCenter = Offset(target, direction, 78);
			var nearestFinger = touches
				.Where(touch => Distance(touch, target) > 1)
				.Select(touch => Distance(touch, arrowCenter))
				.DefaultIfEmpty(180)
				.Min();
			var score = Math.Min(nearestFinger, 180) + Math.Min(EdgeClearance(tail, bounds), 100) * 0.25f;
			if (score > bestScore)
			{
				bestScore = score;
				bestDirection = direction;
			}
		}

		return bestDirection;
	}

	private static void DrawWinnerArrow(ICanvas canvas, PointF target, PointF direction)
	{
		var start = Offset(target, direction, 112);
		var tip = Offset(target, direction, 42);
		var arrowDirection = new PointF(-direction.X, -direction.Y);
		var perpendicular = new PointF(-direction.Y, direction.X);
		var wingBase = Offset(tip, direction, 20);
		var firstWing = Offset(wingBase, perpendicular, 15);
		var secondWing = Offset(wingBase, perpendicular, -15);

		canvas.StrokeLineCap = LineCap.Round;
		canvas.StrokeColor = Color.FromArgb("#FFF4DD");
		canvas.StrokeSize = 12;
		canvas.DrawLine(start, tip);
		canvas.DrawLine(tip, firstWing);
		canvas.DrawLine(tip, secondWing);
		canvas.StrokeColor = Color.FromArgb("#FFB454");
		canvas.StrokeSize = 7;
		canvas.DrawLine(start, tip);
		canvas.DrawLine(tip, firstWing);
		canvas.DrawLine(tip, secondWing);
	}

	private static PointF Offset(PointF point, PointF direction, float distance) =>
		new(point.X + direction.X * distance, point.Y + direction.Y * distance);

	private static float EdgeClearance(PointF point, RectF bounds) =>
		Math.Min(Math.Min(point.X - bounds.Left, bounds.Right - point.X), Math.Min(point.Y - bounds.Top, bounds.Bottom - point.Y));

	private static float Distance(PointF first, PointF second)
	{
		var x = first.X - second.X;
		var y = first.Y - second.Y;
		return MathF.Sqrt(x * x + y * y);
	}
}
