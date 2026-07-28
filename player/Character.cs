using Godot;

public partial class Character : CharacterBody2D
{
	// Attempting to recreate Super Mario World physics in Godot, with a few tweaks to make it feel better.
	private const float PixelScale = 3.0f;
	private const float Fps = 60.0f;

	private static float PerFrame(float pixelsPerFrame) => pixelsPerFrame * Fps * PixelScale;
	private static float PerFrameSquared(float pixelsPerFrame) => pixelsPerFrame * Fps * Fps * PixelScale;

	// $14 walk, $24 run, $30 sprint on a full P-meter.
	private static readonly float WalkSpeed = PerFrame(1.25f);
	private static readonly float RunSpeed = PerFrame(2.25f);
	private static readonly float DashSpeed = PerFrame(3.0f);

	private static readonly float WalkAcceleration = PerFrameSquared(0.09375f);
	private static readonly float DashAcceleration = PerFrameSquared(0.09375f);
	private static readonly float SkidDeceleration = PerFrameSquared(0.125f);

	private static readonly float Friction = PerFrameSquared(0.0625f);
	private static readonly float AirFriction = PerFrameSquared(0.0078125f);
	private static readonly float DuckFriction = PerFrameSquared(0.015625f);

	private static readonly float AirAcceleration = PerFrameSquared(0.0625f);
	private static readonly float AirSkidDeceleration = PerFrameSquared(0.09375f);

	// Wider than one frame's acceleration, so being at the speed cap can't flip
	// between accelerating and braking on alternating frames.
	private const float SpeedEpsilon = 1.0f;

	// $B0, or $A8 off a dash.
	private static readonly float JumpVelocity = -PerFrame(5.0f);
	private static readonly float RunJumpVelocity = -PerFrame(5.5f);

	// Holding jump halves gravity on the way up
	private static readonly float Gravity = PerFrameSquared(0.1875f);
	private static readonly float JumpHoldGravity = PerFrameSquared(0.09375f);
	private static readonly float MaxFallSpeed = PerFrame(4.0f);

	private const float PMeterFillTime = 112.0f / 60.0f;
	private const float PMeterDrainRate = 2.0f;
	private const float CoyoteTime = 6.0f / 60.0f;

	private static readonly float SnapDistance = 2.0f * PixelScale;

	private AnimatedSprite2D _sprite;
	private float _pMeter;
	private bool _jumpHeld;
	private float _coyoteTimer;

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("Sprite2D");

		FloorSnapLength = SnapDistance;
		FloorStopOnSlope = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		Vector2 velocity = Velocity;

		bool isRunning = Input.IsActionPressed("run");
		bool isDucking = Input.IsActionPressed("ui_down");
		float directionX = Input.GetAxis("ui_left", "ui_right");

		UpdatePMeter(dt, isRunning, velocity.X);

		// Dash speed only unlocks on a full P-meter.
		bool isDashing = isRunning && Mathf.IsEqualApprox(_pMeter, 1.0f);
		float maxSpeed = isDashing ? DashSpeed : (isRunning ? RunSpeed : WalkSpeed);

		bool supported = IsOnFloor() && HasGroundBelow();

		if (supported)
		{
			_coyoteTimer = CoyoteTime;
		}
		else
		{
			_coyoteTimer = Mathf.Max(_coyoteTimer - dt, 0.0f);
		}

		bool jumped = false;
		velocity.X = ApplyHorizontalMovement(velocity.X, directionX, maxSpeed, isDashing, dt, supported, isDucking);
		velocity.Y = ApplyVerticalMovement(velocity.Y, velocity.X, dt, ref jumped);

		Velocity = velocity;

		// Snapping holds us to slopes, but left on it also re-grabs a ledge we
		// just stepped off, since one frame of gravity moves us less than a pixel.
		FloorSnapLength = (!jumped && velocity.Y >= 0 && supported) ? SnapDistance : 0.0f;

		MoveAndSlide();

		UpdateAnimation(Velocity, directionX);
	}

	// Looks for solid ground just below the feet, using the body's own collider.
	// IsOnFloor() can't answer this on its own. Once snapping has pulled us
	// back onto a ledge we already walked off, it reports true again.
	private bool HasGroundBelow()
	{
		return TestMove(GlobalTransform, new Vector2(0, SnapDistance));
	}

	private float ApplyHorizontalMovement(float velocityX, float directionX, float maxSpeed, bool isDashing, float dt, bool onFloor, bool isDucking)
	{
		if (directionX == 0 || (isDucking && onFloor))
		{
			// Ducking ignores steering and slides; the air barely drags at all.
			float stopRate = !onFloor ? AirFriction : (isDucking ? DuckFriction : Friction);
			return Mathf.MoveToward(velocityX, 0, stopRate * dt);
		}

		float acceleration;
		if (!onFloor)
		{
			// Turning mid-air still gets the harder brake.
			acceleration = velocityX != 0 && Mathf.Sign(directionX) != Mathf.Sign(velocityX)
				? AirSkidDeceleration
				: AirAcceleration;
		}
		else if (velocityX != 0 && Mathf.Sign(directionX) != Mathf.Sign(velocityX))
		{
			acceleration = SkidDeceleration;
		}
		else
		{
			acceleration = isDashing ? DashAcceleration : WalkAcceleration;
		}

		float target = directionX * maxSpeed;

		// The epsilon stops float noise flipping between accelerating and
		// braking on alternating frames at top speed.
		if (Mathf.Abs(velocityX) > maxSpeed + SpeedEpsilon && Mathf.Sign(velocityX) == Mathf.Sign(directionX))
		{
			return Mathf.MoveToward(velocityX, target, Friction * dt);
		}

		return Mathf.MoveToward(velocityX, target, acceleration * dt);
	}

	private float ApplyVerticalMovement(float velocityY, float velocityX, float dt, ref bool jumped)
	{
		if (Input.IsActionJustPressed("ui_up") && _coyoteTimer > 0.0f)
		{
			_jumpHeld = true;
			jumped = true;
			_coyoteTimer = 0.0f;

			// Takeoff scales with the speed carried into the jump.
			float speedRatio = Mathf.Clamp(Mathf.Abs(velocityX) / DashSpeed, 0.0f, 1.0f);
			return Mathf.Lerp(JumpVelocity, RunJumpVelocity, speedRatio);
		}

		// Releasing mid-rise restores full gravity and can't be re-acquired.
		if (!Input.IsActionPressed("ui_up") || velocityY >= 0)
		{
			_jumpHeld = false;
		}

		float gravity = _jumpHeld ? JumpHoldGravity : Gravity;
		return Mathf.Min(velocityY + gravity * dt, MaxFallSpeed);
	}

	private void UpdatePMeter(float dt, bool isRunning, float velocityX)
	{
		// Only charges at run speed; holding the button while walking doesn't.
		bool charging = isRunning && Mathf.Abs(velocityX) >= RunSpeed - 1.0f;

		if (charging)
		{
			_pMeter = Mathf.Min(_pMeter + dt / PMeterFillTime, 1.0f);
		}
		else
		{
			_pMeter = Mathf.Max(_pMeter - dt / PMeterFillTime * PMeterDrainRate, 0.0f);
		}
	}

	private void UpdateAnimation(Vector2 velocity, float directionX)
	{
		// Face where you're steering, not where momentum is carrying you, so
		// Mario turns to look into a skid.
		if (directionX != 0)
		{
			_sprite.FlipH = directionX < 0;
		}
		else if (velocity.X != 0)
		{
			_sprite.FlipH = velocity.X < 0;
		}

		if (!IsOnFloor())
		{
			_sprite.Play("jumping");
		}
		else if (Mathf.Abs(velocity.X) > 1.0f)
		{
			_sprite.Play("running");
			_sprite.SpeedScale = Mathf.Lerp(0.6f, 1.6f, Mathf.Abs(velocity.X) / DashSpeed);
		}
		else
		{
			_sprite.Play("default");
			_sprite.SpeedScale = 1.0f;
		}
	}
}
