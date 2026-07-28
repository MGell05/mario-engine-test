using Godot;

public partial class Character : CharacterBody2D
{
	public const float Speed = 300.0f;
	public const float RunSpeed = 500.0f;
	public const float JumpVelocity = -400.0f;

	private AnimatedSprite2D _sprite;

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("Sprite2D");
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector2 velocity = Velocity;

		// Add the gravity.
		if (!IsOnFloor())
		{
			velocity += GetGravity() * (float)delta;
		}

		// Handle Jump (space/enter or the up key).
		if (Input.IsActionJustPressed("ui_up") && IsOnFloor())
		{
			velocity.Y = JumpVelocity;
		}

		// Hold the run button to move faster.
		bool isRunning = Input.IsActionPressed("run");
		float currentSpeed = isRunning ? RunSpeed : Speed;

		// Get the horizontal input direction and handle the movement/deceleration.
		// Read the X axis on its own so pressing up/down can't scale horizontal speed.
		float directionX = Input.GetAxis("ui_left", "ui_right");
		if (directionX != 0)
		{
			velocity.X = directionX * currentSpeed;
		}
		else
		{
			velocity.X = Mathf.MoveToward(Velocity.X, 0, currentSpeed);
		}

		Velocity = velocity;
		MoveAndSlide();

		UpdateAnimation(velocity);
	}

	private void UpdateAnimation(Vector2 velocity)
	{
		// Flip the sprite based on horizontal movement direction.
		// Only flip when actually moving so it keeps facing the last direction when idle.
		if (velocity.X != 0)
		{
			_sprite.FlipH = velocity.X < 0;
		}

		// Choose which animation to play.
		if (!IsOnFloor())
		{
			_sprite.Play("jumping");
		}
		else if (Mathf.Abs(velocity.X) > 1.0f)
		{
			_sprite.Play("running");
		}
		else
		{
			_sprite.Play("default");
		}
	}
}
