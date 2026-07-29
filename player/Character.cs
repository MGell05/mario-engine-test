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

	private static readonly float PMeterChargeSpeed = PerFrame(2.1875f);

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

	// Takeoff runs $B0 standing up to $9F at full speed.
	private static readonly float JumpVelocity = -PerFrame(5.0f);
	private static readonly float RunJumpVelocity = -PerFrame(6.0625f);

	// Holding the jump button halves gravity, rising or falling.
	private static readonly float Gravity = PerFrameSquared(0.375f);
	private static readonly float JumpHoldGravity = PerFrameSquared(0.1875f);
	private static readonly float MaxFallSpeed = PerFrame(4.0f);

	private const float PMeterFillTime = 56.0f / 60.0f;
	private const float PMeterDrainRate = 0.5f;
	private const float CoyoteTime = 6.0f / 60.0f;

	private static readonly float SnapDistance = 2.0f * PixelScale;

	private static readonly float GroundProbeLift = 1.0f * PixelScale;

	private const float FootHalfWidth = 7.0f;
	private const float FootHalfHeight = 11.5f;

	private AnimatedSprite2D _sprite;
	private float _pMeter;
	private float _coyoteTimer;
	private CollisionShape2D _collider;

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("Sprite2D");

		_collider = GetNode<CollisionShape2D>("CollisionShape2D");

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

		bool supported = IsOnFloor();

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
		FloorSnapLength = (!jumped && velocity.Y >= 0 && HasGroundBelow()) ? SnapDistance : 0.0f;

		MoveAndSlide();

		UpdateAnimation(Velocity, directionX);
	}

	// Checks under both corners of the feet, so a ledge only counts as left
	// behind once neither corner has anything under it.
	private bool HasGroundBelow()
	{
		Vector2 scale = GlobalScale;
		float footY = _collider.GlobalPosition.Y + FootHalfHeight * scale.Y;
		float footX = FootHalfWidth * scale.X;
		float centreX = _collider.GlobalPosition.X;

		return HasGroundAt(new Vector2(centreX - footX, footY))
			|| HasGroundAt(new Vector2(centreX + footX, footY));
	}

	private bool HasGroundAt(Vector2 point)
	{
		var query = new PhysicsRayQueryParameters2D
		{
			From = point - new Vector2(0, GroundProbeLift),
			To = point + new Vector2(0, SnapDistance),
			CollisionMask = CollisionMask,
			Exclude = [GetRid()],
		};

		return GetWorld2D().DirectSpaceState.IntersectRay(query).Count > 0;
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
			jumped = true;
			_coyoteTimer = 0.0f;

			// Takeoff scales with the speed carried into the jump.
			float speedRatio = Mathf.Clamp(Mathf.Abs(velocityX) / DashSpeed, 0.0f, 1.0f);
			return Mathf.Lerp(JumpVelocity, RunJumpVelocity, speedRatio);
		}

		float gravity = Input.IsActionPressed("ui_up") ? JumpHoldGravity : Gravity;
		return Mathf.Min(velocityY + gravity * dt, MaxFallSpeed);
	}

	private void UpdatePMeter(float dt, bool isRunning, float velocityX)
	{
		// Only charges at run speed; holding the button while walking doesn't.
		bool charging = isRunning && Mathf.Abs(velocityX) >= PMeterChargeSpeed;

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
		// the player turns to look into a skid.
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
