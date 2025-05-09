using System;
using System.Collections.Generic;
using Godot;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Environment = System.Environment;
using Vector2 = Godot.Vector2;

namespace KinectProject;

public partial class KinectReceiver : Control
{
	[ExportGroup("Server Settings")] [Export]
	private string _serverDomain = Environment.MachineName;

	[Export] private int _serverPort = 9000;

	public class AudioFileSoundStrategy
	{
		private AudioStreamPlayer2D _player;

		public void Setup(AudioStreamPlayer2D player, Vector2 position, string fileName)
		{
			_player = player;
			_player.Position = position;
			var audioStream = ResourceLoader.Load<AudioStream>(fileName);
			if (audioStream == null)
			{
				GD.PrintErr($"Failed to load audio file: {fileName}");
				return;
			}

			_player.MaxPolyphony = 2;
			_player.Stream = audioStream;
		}

		public void UpdatePitch(float pitch) => _player.PitchScale = pitch;
		public void UpdateVolume(float volume) => _player.VolumeDb = volume;
		public void Play() => _player.Play();
		public void Stop() => _player.Stop();
	}

	public class Joint
	{
		public Vector2 Position { get; set; }
		public int TrackingState { get; set; }
		public bool IsTracked => TrackingState == 2;
	}

	public partial class HandCone : Area2D
	{
		private const float MaxRotationChangeDegrees = 20F;
		private const float MovementThreshold = 1F;
		private const float ConeAngleDegrees = 30F;
		private const float ConeLength = 850F;
		private static readonly Color ConeColor = new(.3F, .3F, .3F, .1F);

		private readonly CollisionPolygon2D _collisionPolygon = new();
		public readonly Polygon2D VisualPolygon = new();
		public Joint HandJoint { get; set; }

		private Vector2 _direction = Vector2.Up;
		private Vector2 _previousPosition;
		private float _currentRotation;
		private readonly float _coneAngleRadians = Mathf.DegToRad(ConeAngleDegrees);

		public override void _Ready()
		{
			ZIndex = 1;
			Monitoring = true;
			Monitorable = true;
			CollisionLayer = 0;
			CollisionMask = 1;

			VisualPolygon.Color = ConeColor;

			AddChild(_collisionPolygon);
			AddChild(VisualPolygon);

			BodyEntered += OnBodyEntered;
			BodyExited += OnBodyExited;
		}

		private static void OnBodyEntered(Node body)
		{
			if (body is Person person)
			{
				person.SetMouthRadius();
			}
		}

		private static void OnBodyExited(Node body)
		{
			if (body is Person person)
			{
				person.ResetMouthRadius();
			}
		}

		public override void _PhysicsProcess(double delta)
		{
			if (HandJoint?.IsTracked != true)
			{
				return;
			}

			UpdateConeShape();
		}

		private void UpdateConeShape()
		{
			Position = HandJoint.Position + new Vector2(0f, -41f);

			var normalizedDirection = _direction.Normalized();
			var perpendicular = new Vector2(-normalizedDirection.Y, normalizedDirection.X);
			var halfAngle = _coneAngleRadians / 2f;
			var edge1 = (normalizedDirection * Mathf.Cos(halfAngle) + perpendicular * Mathf.Sin(halfAngle)) * ConeLength;
			var edge2 = (normalizedDirection * Mathf.Cos(halfAngle) - perpendicular * Mathf.Sin(halfAngle)) * ConeLength;
			var points = new[] { Vector2.Zero, edge1, edge2 };

			_collisionPolygon.Polygon = points;
			VisualPolygon.Polygon = points;

			var movementDirection = Position - _previousPosition;
			_previousPosition = Position;

			if (movementDirection.Length() < MovementThreshold)
			{
				return;
			}

			var rotationChange = movementDirection.X switch
			{
				< 0 => -1f,
				> 0 => 1f,
				_ => 0f
			};

			_currentRotation = Mathf.Clamp(_currentRotation + rotationChange, -MaxRotationChangeDegrees, MaxRotationChangeDegrees);
			_collisionPolygon.RotationDegrees = _currentRotation;
			VisualPolygon.RotationDegrees = _currentRotation;
		}
	}

	public partial class Stage : Node2D
	{
		private readonly Color _gridColor = new(.3F, .3F, .3F, .15F);
		private readonly int[] _peoplePerRow = [10, 8, 6, 4];
		private float _cellGridSize;

		public float ScreenWidth { get; private set; }
		public float ScreenHeight { get; private set; }

		public override void _Ready()
		{
			var viewportRect = GetViewport().GetVisibleRect();
			ScreenWidth = viewportRect.Size.X;
			ScreenHeight = viewportRect.Size.Y;
			OnStageResize(ScreenWidth, ScreenHeight);
			ZIndex = -1;
		}

		public void OnStageResize(float screenWidth, float screenHeight)
		{
			ScreenWidth = screenWidth;
			ScreenHeight = screenHeight;
			Position = new Vector2(ScreenWidth / 2F, ScreenHeight / 2F);
			_cellGridSize = Math.Min(ScreenWidth / 10F, ScreenHeight / 10F);
			PopulatePeople();
		}

		private void PopulatePeople()
		{
			foreach (var child in GetChildren()) child.QueueFree();

			var totalRows = _peoplePerRow.Length;
			var vOrigin = -((totalRows - 1) * _cellGridSize);

			for (var rowIndex = 0; rowIndex < totalRows; rowIndex++)
			{
				var peopleInRow = _peoplePerRow[rowIndex];
				var y = vOrigin + rowIndex * _cellGridSize;
				var rowWidth = (peopleInRow - 1F) * _cellGridSize;
				var x = -rowWidth / 2f;

				for (var i = 0; i < peopleInRow; i++)
				{
					AddChild(
						new Person
						{
							Position = new Vector2(x + i * _cellGridSize, y)
						}
					);
				}
			}
		}

		public override void _Draw()
		{
			
			for (var x = -ScreenWidth; x <= ScreenWidth; x += _cellGridSize)
				DrawLine(new Vector2(x, -ScreenHeight), new Vector2(x, ScreenHeight), _gridColor, -1, true);

			for (var y = -ScreenHeight; y <= ScreenHeight; y += _cellGridSize)
				DrawLine(new Vector2(-ScreenWidth, y), new Vector2(ScreenWidth, y), _gridColor, -1, true);
		}
	}

	public partial class Person : StaticBody2D
	{
		private const float MinMouthRadius = 3F;
		private const float MaxMouthRadius = 7F;
		private const float MaxSwayAngleDeg = 2F;
		private const float SwaySpeed = 5F;
		private const float MouthLerpSpeed = 7F;
		private const float MinLightEnergy = 1F;
		private const float MaxLightEnergy = 1.5F;
		private const float MinPitch = .1F;
		private const float MaxPitch = 12F;
		private const float MinVolumeDb = -24F;
		private const float MaxVolumeDb = -8F;

		private AudioFileSoundStrategy _soundStrategy;
		private string _currentAnimation = "idle";
		private string _targetAnimation = "idle";
		private AnimatedSprite2D _sprite;
		private Light2D _pointLight;

		private Vector2 _targetScale = new(1.2F, 1.2F);
		private float _currentMouthRadius;
		private float _targetMouthRadius;
		private float _animatedMouthRadius;
		private float _swayTime;
		private float _vibratoTime;
		private float _pitchTime;
		private float _pitchPhase;
		private string _spriteType;

		public override void _Ready()
		{
			_spriteType = GD.Randf() < 0.5F ? "Tie" : "Ribbon";
			CollisionLayer = 1;
			CollisionMask = 0;

			var player = new AudioStreamPlayer2D();
			_soundStrategy = new AudioFileSoundStrategy();

			var collisionShape = new CollisionShape2D
			{
				Shape = new CircleShape2D { Radius = 15F },
				Position = new Vector2(0F, -20F)
			};

			_sprite = new AnimatedSprite2D
			{
				Scale = _targetScale,
				TextureFilter = TextureFilterEnum.LinearWithMipmapsAnisotropic,
				Material = new CanvasItemMaterial
				{
					LightMode = CanvasItemMaterial.LightModeEnum.Normal
				}
			};

			_pointLight = new PointLight2D
			{
				Enabled = true,
				Visible = true,
				BlendMode = Light2D.BlendModeEnum.Mix,
				Texture = ResourceLoader.Load<Texture2D>("Sprites/Lights/light_smoothest.png"),
				Color = Colors.WhiteSmoke,
			};

			SetupSpriteFrames();
			_soundStrategy.Setup(player, Position, "Sounds/sine440hz.wav");
			_pitchPhase = (float)GD.RandRange(0F, 2F * Mathf.Pi);

			AddChild(_sprite);
			AddChild(_pointLight);
			AddChild(player);
			AddChild(collisionShape);
		}

		private void SetupSpriteFrames()
		{
			var spriteFrames = new SpriteFrames();
			var basePath = _spriteType == "Tie" ? "Sprites/Person/Tie/" : "Sprites/Person/Ribbon/";

			spriteFrames.AddAnimation("idle");
			spriteFrames.AddFrame("idle", ResourceLoader.Load<Texture2D>(basePath + "idle.png"));

			spriteFrames.AddAnimation("idle_blink");
			spriteFrames.AddFrame("idle_blink", ResourceLoader.Load<Texture2D>(basePath + "idle_blink.png"));

			spriteFrames.AddAnimation("arms_mid");
			spriteFrames.AddFrame("arms_mid", ResourceLoader.Load<Texture2D>(basePath + "arms_mid.png"));

			spriteFrames.AddAnimation("arms_high");
			spriteFrames.AddFrame("arms_high", ResourceLoader.Load<Texture2D>(basePath + "arms_high.png"));

			_sprite.SpriteFrames = spriteFrames;
			_sprite.Animation = "idle";
			_sprite.Play();
		}

		public override void _Process(double delta)
		{
			var deltaF = (float)delta;

			_currentMouthRadius = Mathf.Lerp(_currentMouthRadius, _targetMouthRadius, MouthLerpSpeed * deltaF);
			_sprite.Scale = _sprite.Scale.Lerp(_targetScale, MouthLerpSpeed * deltaF);
			
			
			UpdateBodyAnimation();
			if (_currentAnimation != _targetAnimation)
			{
				_sprite.Play(_currentAnimation = _targetAnimation);
			}

			if (_currentMouthRadius > 0.01F)
			{
				UpdateMouthAnimation(deltaF);
				UpdateSound();
			}
			else
			{
				ResetAnimation();
				_soundStrategy.Stop();
			}
		}

		private void UpdateBodyAnimation()
		{
			if (_currentMouthRadius <= 0.01F)
			{
				_targetAnimation = "idle";
				_pointLight.Energy = MinLightEnergy;
				return;
			}

			var normalizedRadius = (_currentMouthRadius - MinMouthRadius) / (MaxMouthRadius - MinMouthRadius);
			var basePitch = Mathf.Lerp(MinPitch, MaxPitch, normalizedRadius);
			var oscillation = Mathf.Sin(_pitchTime + _pitchPhase);
			var pitch = Mathf.Clamp(basePitch + oscillation * MaxPitch, MinPitch, MaxPitch);
			var normalizedPitch = (pitch - MinPitch) / (MaxPitch - MinPitch);
			_targetAnimation = normalizedPitch > .5F ? "arms_high" : "arms_mid";
		}

		private void UpdateMouthAnimation(float delta)
		{
			_pointLight.Energy = MaxLightEnergy;
			_swayTime += delta;
			_vibratoTime += delta;
			_pitchTime += delta;
			Rotation = Mathf.DegToRad(MaxSwayAngleDeg) * Mathf.Sin(_swayTime * SwaySpeed);
			var mouthPulse = Mathf.Sin(_vibratoTime * Mathf.Sin(_vibratoTime));
			_animatedMouthRadius = Mathf.Clamp(_currentMouthRadius + mouthPulse, MinMouthRadius, MaxMouthRadius);
			// var lightIntensity = Mathf.Lerp(MinLightEnergy, MaxLightEnergy, (_animatedMouthRadius - MinMouthRadius) / (MaxMouthRadius - MinMouthRadius));
			// _pointLight.Energy = lightIntensity;
		}

		private void UpdateSound()
		{
			if (_currentMouthRadius <= 0.01F)
			{
				_soundStrategy.Stop();
				return;
			}

			var normalizedRadius = (_currentMouthRadius - MinMouthRadius) / (MaxMouthRadius - MinMouthRadius);
			var basePitch = Mathf.Lerp(MinPitch, MaxPitch * .5F, normalizedRadius);
			var oscillation = Mathf.Sin(_pitchTime * 2F + _pitchPhase) * .5F;
			var pitch = Mathf.Clamp(basePitch + oscillation * MaxPitch * .5F, MinPitch, MaxPitch);
			var volume = Mathf.Lerp(MinVolumeDb, MaxVolumeDb, _animatedMouthRadius / MaxMouthRadius);
			_soundStrategy.UpdatePitch(pitch);
			_soundStrategy.UpdateVolume(volume);
			_soundStrategy.Play();
		}

		private void ResetAnimation()
		{
			_swayTime = 0F;
			_vibratoTime = 0F;
			Rotation = 0F;
			_animatedMouthRadius = 0F;
			_targetAnimation = "idle";
		}

		public void SetMouthRadius()
		{
			_targetMouthRadius = (float)GD.RandRange(MinMouthRadius, MaxMouthRadius);
			_targetScale = new Vector2(1.3F, 1.3F);
		}

		public void ResetMouthRadius()
		{
			_targetMouthRadius = 0F;
			_targetScale = new Vector2(1.2F, 1.2F);
		}
	}

	private Stage _stage;
	private Joint _leftHandJoint;
	private Joint _rightHandJoint;
	private HandCone _rightHandCone;
	private FontFile _defaultFont;
	private HubConnection _connection;
	private IHubProxy _hubProxy;
	private ColorRect _vignetteColorRect;
	private Vector2 _leftHandTargetPosition;
	private Vector2 _rightHandTargetPosition;
	private Vector2 _previousRightHandTargetPosition;
	private const float KinectDepthCameraWidth = 512F;
	private const float KinectDepthCameraHeight = 424F;
	private const float HandLerpSpeed = 15F;
	private const float PanLerpSpeed = .25F;
	private const float MaxPanDeviation = 10F;
	private const float PanSpeed = 0.1F;

	public override void _Ready()
	{
		_defaultFont = new FontFile
		{
			FixedSize = 16
		};
		_connection = new HubConnection($"http://{_serverDomain}:{_serverPort}/signalr");
		_hubProxy = _connection.CreateHubProxy("Kinect2Hub");
		GetTree().Root.SizeChanged += OnResize;
		SetupCallbacks();
		StartConnection();
		_stage = new Stage();
		_leftHandJoint = new Joint();
		_rightHandJoint = new Joint();
		_rightHandCone = new HandCone();
		_rightHandCone.HandJoint = _rightHandJoint;
		var vignetteLayer = new CanvasLayer { Layer = -1 };
		vignetteLayer.AddChild(_vignetteColorRect = new ColorRect
		{
			Material = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>("Shaders/vignette.gdshader") },
			Size = GetViewport().GetVisibleRect().Size,
		});
		AddChild(_stage);
		AddChild(_rightHandCone);
		AddChild(vignetteLayer);
	}

	private void SetupCallbacks()
	{
		_hubProxy.On<string, string>("OnBody", UpdateHandData);
		_connection.Closed += () => GD.PrintErr("SignalR connection closed...");
		_connection.Error += exception => GD.PrintErr($"SignalR connection error: {exception.Message}");
	}

	private void StartConnection()
	{
		_connection.Start().ContinueWith(task =>
		{
			GD.Print(task.IsFaulted
				? $"SignalR connection error: {task.Exception?.GetBaseException().Message}"
				: "Connected to SignalR...");
		});
	}

	private void UpdateHandData(string states, string positions)
	{
		UpdateHandPositions(positions);
		UpdateHandStates(states);
	}

	private void OnResize()
	{
		var viewportRect = GetViewport().GetVisibleRect();
		_vignetteColorRect.Size = viewportRect.Size;
		_stage.OnStageResize(viewportRect.Size.X, viewportRect.Size.Y);
	}

	private void UpdateHandPositions(string positionsJson)
	{
		var projections = JsonConvert.DeserializeObject<Dictionary<string, List<float>>>(positionsJson);
		if (projections.Count == 0) return;

		const float sensitivity = 1.5f;
		var spineBase = projections.TryGetValue("SpineBase", out var spineBaseCoordinates)
			? new Vector2(spineBaseCoordinates[0], spineBaseCoordinates[1])
			: Vector2.Zero;
		var scaleX = _stage.ScreenWidth / KinectDepthCameraWidth;
		var scaleY = _stage.ScreenHeight / KinectDepthCameraHeight;

		if (projections.TryGetValue("HandLeft", out var leftHandPositions) && leftHandPositions.Count >= 2)
		{
			_leftHandTargetPosition = CalculateHandPosition(leftHandPositions, spineBase, scaleX, scaleY, sensitivity);
		}

		if (projections.TryGetValue("HandRight", out var rightHandPositions) && rightHandPositions.Count >= 2)
		{
			_rightHandTargetPosition =
				CalculateHandPosition(rightHandPositions, spineBase, scaleX, scaleY, sensitivity);
		}
	}

	private Vector2 CalculateHandPosition(List<float> handPositions, Vector2 spineBase, float scaleX,
		float scaleY,
		float sensitivity)
	{
		var relativeX = (handPositions[0] - spineBase.X) * sensitivity;
		var relativeY = (spineBase.Y - handPositions[1]) * sensitivity;
		var scaledX = relativeX * scaleX + _stage.ScreenWidth / 2F;
		var scaledY = _stage.ScreenHeight - relativeY * scaleY;
		return new Vector2(
			Mathf.Clamp(scaledX, 0, _stage.ScreenWidth),
			Mathf.Clamp(scaledY, 0, _stage.ScreenHeight)
		);
	}

	private void UpdateHandStates(string statesJson)
	{
		var bodyData = JsonConvert.DeserializeObject<Dictionary<string, object>>(statesJson);
		if (!bodyData.TryGetValue("Joints", out var jointsObj)) return;
		var jointsData = ((JObject)jointsObj).ToObject<Dictionary<string, Dictionary<string, object>>>();
		if (jointsData == null) return;

		if (jointsData.TryGetValue("HandLeft", out var leftHandStates) &&
			leftHandStates.TryGetValue("TrackingState", out var leftHandState) &&
			int.TryParse(leftHandState.ToString(), out var leftHandStateInt))
		{
			_leftHandJoint.TrackingState = leftHandStateInt;
		}

		if (jointsData.TryGetValue("HandRight", out var rightHandStates) &&
			rightHandStates.TryGetValue("TrackingState", out var rightHandState) &&
			int.TryParse(rightHandState.ToString(), out var rightHandStateInt))
		{
			_rightHandJoint.TrackingState = rightHandStateInt;
		}
	}

	public override void _Draw()
	{
		const float handRadius = 40F;

		if (_leftHandJoint.IsTracked)
		{
			DrawCircle(_leftHandJoint.Position, handRadius, Colors.Gray);
			DrawCircle(_leftHandJoint.Position, handRadius, Colors.DimGray, false, 1F, true);
		}

		if (_rightHandJoint.IsTracked)
		{
			DrawCircle(_rightHandJoint.Position, handRadius, Colors.Gray);
			DrawCircle(_rightHandJoint.Position, handRadius, Colors.DimGray, false, 1F, true);
			var stickDirection = new Vector2(0, -1).Rotated(_rightHandCone.VisualPolygon.Rotation).Normalized();
			var stickStartPoint = _rightHandJoint.Position + stickDirection * handRadius;
			var stickEndPoint = stickStartPoint + stickDirection * 150F;
			DrawLine(stickStartPoint, stickEndPoint, Colors.DimGray, 4F, true);
		}
	}

	public override void _Process(double delta)
	{
		QueueRedraw();

		if (_leftHandTargetPosition != Vector2.Zero)
			_leftHandJoint.Position =
				_leftHandJoint.Position.Lerp(_leftHandTargetPosition, (float)(HandLerpSpeed * delta));

		if (_rightHandTargetPosition != Vector2.Zero)
			_rightHandJoint.Position =
				_rightHandJoint.Position.Lerp(_rightHandTargetPosition, (float)(HandLerpSpeed * delta));

		var rightHandSpeed = _rightHandTargetPosition - _previousRightHandTargetPosition;
		_previousRightHandTargetPosition = _rightHandTargetPosition;

		var stageMovement = new Vector2(-rightHandSpeed.X * PanSpeed, -rightHandSpeed.Y * PanSpeed);
		var newStagePosition = _stage.Position + stageMovement;

		newStagePosition = new Vector2(
			Mathf.Clamp(newStagePosition.X, _stage.Position.X - MaxPanDeviation,
				_stage.Position.X + MaxPanDeviation),
			Mathf.Clamp(newStagePosition.Y, _stage.Position.Y - MaxPanDeviation,
				_stage.Position.Y + MaxPanDeviation)
		);

		_stage.Position = _stage.Position.Lerp(newStagePosition, PanLerpSpeed);
	}
}
