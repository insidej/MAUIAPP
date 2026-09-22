namespace MauiApp;

public partial class MainPage : ContentPage
{
	private readonly Random random = new();
	private readonly FingerBoardDrawable boardDrawable = new();
	private CancellationTokenSource? roundCancellation;
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

		UpdateTouches(e.Touches);
		if (!roundStarted && activeTouches.Count > 0)
		{
			roundStarted = true;
			PromptLabel.Text = "Hold steady";
			ResultLabel.Text = "The circle is choosing...";
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
		if (!hasWinner)
			UpdateTouches(e.Touches);
	}

	private void UpdateTouches(IReadOnlyList<PointF> touches)
	{
		activeTouches = touches;
		boardDrawable.SetTouches(activeTouches, null);
		ParticipantLabel.Text = $"{activeTouches.Count} {(activeTouches.Count == 1 ? "finger" : "fingers")} on screen";
		TouchBoard.Invalidate();
	}

	private async Task PickWinnerAfterDelayAsync()
	{
		roundCancellation?.Cancel();
		roundCancellation = new CancellationTokenSource();
		var token = roundCancellation.Token;

		try
		{
			for (var seconds = 10; seconds > 0; seconds--)
			{
				CountdownLabel.Text = $"00:{seconds:00}";
				await Task.Delay(1000, token);
			}

			if (token.IsCancellationRequested || activeTouches.Count == 0)
			{
				ResultLabel.Text = "Keep a finger on the board to be picked";
				return;
			}

			hasWinner = true;
			var winner = activeTouches[random.Next(activeTouches.Count)];
			boardDrawable.SetTouches(activeTouches, winner);
			CountdownLabel.Text = "PICKED";
			PromptLabel.Text = "The circle picked one";
			ResultLabel.Text = "Winner selected at random";
			TouchBoard.Invalidate();
			SemanticScreenReader.Announce("A finger has been selected");
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void OnResetClicked(object? sender, EventArgs e)
	{
		roundCancellation?.Cancel();
		roundStarted = false;
		hasWinner = false;
		activeTouches = Array.Empty<PointF>();
		boardDrawable.SetTouches(activeTouches, null);
		CountdownLabel.Text = "READY";
		PromptLabel.Text = "Place your fingers";
		ParticipantLabel.Text = "0 fingers on screen";
		ResultLabel.Text = "First finger starts the 10 second countdown";
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
	}

	private static float Distance(PointF first, PointF second)
	{
		var x = first.X - second.X;
		var y = first.Y - second.Y;
		return MathF.Sqrt(x * x + y * y);
	}
}
