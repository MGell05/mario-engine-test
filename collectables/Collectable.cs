using Godot;

public partial class Collectable : Area2D
{
	[Signal]
	public delegate void PickedUpEventHandler();

	private AnimatedSprite2D _sprite;
	private CollisionShape2D _collider;
	private bool _taken;

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("Sprite2D");
		_collider = GetNode<CollisionShape2D>("CollisionShape2D");

		// Begin on a random frame, otherwise a row of them all bob in unison.
		_sprite.Play("idle");
		_sprite.Frame = (int)(GD.Randi() % (uint)_sprite.SpriteFrames.GetFrameCount("idle"));

		BodyEntered += OnBodyEntered;
		_sprite.AnimationFinished += OnAnimationFinished;
	}

	private void OnBodyEntered(Node2D body)
	{
		if (_taken || body is not Character)
		{
			return;
		}

		_taken = true;

		// Turning the shape off has to wait until the physics step that reported
		// this overlap has finished, so queue it rather than doing it now.
		_collider.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);

		// Play the burst and let it finish before the node removes itself.
		_sprite.Play("collected");
		EmitSignal(SignalName.PickedUp);
	}

	private void OnAnimationFinished()
	{
		if (_taken)
		{
			QueueFree();
		}
	}
}
