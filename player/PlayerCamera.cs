using Godot;

public partial class PlayerCamera : Camera2D
{
	private const float PixelScale = 3.0f;
	private const float Fps = 60.0f;

	private static float Pixels(float pixels) => pixels * PixelScale;
	private static float PerFrame(float pixelsPerFrame) => pixelsPerFrame * Fps * PixelScale;

	// How far the player can drift from the camera before it starts scrolling.
	private static readonly float DeadZoneX = Pixels(8.0f);

	// While moving, the camera aims ahead of the player to show more of what is
	// coming. The faster they go, the further ahead it looks.
	private static readonly float LookAheadMin = Pixels(12.0f);
	private static readonly float LookAhead = Pixels(28.0f);
	private static readonly float LookAheadRate = PerFrame(2.0f);

	// Below this fraction of top speed, don't look ahead at all, so a small nudge
	// doesn't swing the view around.
	private const float LookAheadStartRatio = 0.1f;

	// How far below centre the player sits, leaving more room to see overhead.
	private static readonly float RestingLift = Pixels(12.0f);

	// Vertically the camera tracks the last ground height instead of the player,
	// so jumping doesn't move the view.
	private static readonly float GroundFollowSpeed = PerFrame(2.0f);
	private static readonly float DeadZoneY = Pixels(4.0f);

	// The player can still only get so far from centre. Past these limits the
	// camera gives up tracking the ground and follows them directly.
	private static readonly float AirLimitUp = Pixels(56.0f);
	private static readonly float AirLimitDown = Pixels(40.0f);

	[Export] public NodePath TargetPath { get; set; }

	private Character _target;
	private float _lookAhead;
	private float _groundY;

	public override void _Ready()
	{
		_target = GetNodeOrNull<Character>(TargetPath);

		if (_target == null)
		{
			GD.PushError($"PlayerCamera has no Character at '{TargetPath}'.");
			SetPhysicsProcess(false);
			return;
		}

		_groundY = _target.CameraFocus.Y - RestingLift;
		GlobalPosition = new Vector2(_target.CameraFocus.X, _groundY);
		ResetSmoothing();
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		Vector2 focus = _target.CameraFocus;
		Vector2 aim = GlobalPosition;

		aim.X = FollowHorizontally(aim.X, focus.X, dt);
		aim.Y = FollowVertically(aim.Y, focus.Y, dt);

		// The player moves in whole pixels, so the camera does too. Landing between
		// pixels makes the sprite shimmer against the tiles behind it.
		GlobalPosition = new Vector2(WholePixels(aim.X), WholePixels(aim.Y));
	}

	private static float WholePixels(float distance) => Mathf.Round(distance / PixelScale) * PixelScale;

	private float FollowHorizontally(float cameraX, float focusX, float dt)
	{
		float velocityX = _target.Velocity.X;
		float speedRatio = Mathf.Min(Mathf.Abs(velocityX) / _target.TopSpeed, 1.0f);

		float lead = speedRatio < LookAheadStartRatio
			? 0.0f
			: Mathf.Sign(velocityX) * Mathf.Max(LookAhead * speedRatio, LookAheadMin);

		_lookAhead = Mathf.MoveToward(_lookAhead, lead, LookAheadRate * dt);

		// Clamping instead of easing means that once the player reaches the edge of
		// the dead zone the camera moves at exactly their speed, so it can't fall
		// behind however fast they run.
		float aim = focusX + _lookAhead;
		return Mathf.Clamp(cameraX, aim - DeadZoneX, aim + DeadZoneX);
	}

	private float FollowVertically(float cameraY, float focusY, float dt)
	{
		if (_target.IsOnFloor())
		{
			_groundY = focusY - RestingLift;
		}

		if (Mathf.Abs(cameraY - _groundY) > DeadZoneY)
		{
			cameraY = Mathf.MoveToward(cameraY, _groundY, GroundFollowSpeed * dt);
		}

		return Mathf.Clamp(cameraY, focusY - AirLimitDown, focusY + AirLimitUp);
	}
}
