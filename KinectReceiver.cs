using System;
using System.Collections.Generic;
using System.Linq;
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

    public static class InstrumentResources
    {
        public enum Instrument
        {
            Sine,
            Trombone,
            Viola
        }

        public static readonly Dictionary<Instrument, string> AudioFiles = new()
        {
            { Instrument.Sine, "res://Sounds/sine.wav" },
            { Instrument.Trombone, "res://Sounds/trombone.wav" },
            { Instrument.Viola, "res://Sounds/viola.wav" }
        };
    }

    public class AudioFileSoundStrategy
    {
        private readonly AudioStreamPlayer2D _player;
        private string _currentFileName;

        public AudioFileSoundStrategy(AudioStreamPlayer2D player)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _player.MaxPolyphony = 2;
        }

        public void LoadAudioFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || _currentFileName == fileName)
                return;

            var audioStream = ResourceLoader.Load<AudioStream>(fileName);
            if (audioStream == null)
            {
                GD.PrintErr($"Failed to load audio file: {fileName}");
                return;
            }

            var wasPlaying = _player.Playing;

            _player.Stop();
            _player.Stream = audioStream;
            _currentFileName = fileName;

            if (wasPlaying) _player.Play();
        }

        public string GetCurrentAudioFile() => _currentFileName;
        public void UpdatePitch(float pitch) => _player.PitchScale = pitch;
        public void UpdateVolume(float volume) => _player.VolumeDb = volume;
        public void Play() => _player.Play();
        public void Stop() => _player.Stop();
    }


    public partial class Joint : Node2D
    {
        public Vector2 TargetPosition { get; set; }
        public int TrackingState { get; set; }
        public bool IsTracked => TrackingState == 2;
    }

    public partial class HandCone(Joint handJoint) : Area2D
    {
        private const float MaxRotationChangeDegrees = 20F;
        private const float MovementThreshold = 1F;
        private const float ConeAngleDegrees = 30F;
        private const float ConeLength = 850F;
        private static readonly Color ConeColor = new(.3F, .3F, .3F, .1F);

        private readonly CollisionPolygon2D _collisionPolygon = new();
        public readonly Polygon2D VisualPolygon = new();
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
            if (body is Worble worble)
            {
                worble.SetScale(worble.ActivatedScale);
            }
        }

        private static void OnBodyExited(Node body)
        {
            if (body is Worble worble)
            {
                worble.SetScale(worble.DefaultScale);
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            if (handJoint?.IsTracked != true)
            {
                return;
            }

            UpdateConeShape();
        }

        private void UpdateConeShape()
        {
            Position = handJoint.Position + new Vector2(0F, -41F);

            var normalizedDirection = _direction.Normalized();
            var perpendicular = new Vector2(-normalizedDirection.Y, normalizedDirection.X);
            var halfAngle = _coneAngleRadians / 2F;
            var edge1 = (normalizedDirection * Mathf.Cos(halfAngle) + perpendicular * Mathf.Sin(halfAngle)) *
                        ConeLength;
            var edge2 = (normalizedDirection * Mathf.Cos(halfAngle) - perpendicular * Mathf.Sin(halfAngle)) *
                        ConeLength;
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
                < 0 => -1F,
                > 0 => 1F,
                _ => 0F
            };

            _currentRotation = Mathf.Clamp(_currentRotation + rotationChange, -MaxRotationChangeDegrees,
                MaxRotationChangeDegrees);
            _collisionPolygon.RotationDegrees = _currentRotation;
            VisualPolygon.RotationDegrees = _currentRotation;
        }
    }

    public partial class Stage : Node2D
    {
        private readonly Color _gridColor = new(.3F, .3F, .3F, .15F);
        private readonly int[] _peoplePerRow = [10, 8, 6, 4];
        private int _cellGridSize;

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
            _cellGridSize = (int)Math.Max(ScreenWidth / 20, ScreenHeight / 20);
            PopulatePeople();
        }

        private void PopulatePeople()
        {
            foreach (var child in GetChildren()) child.QueueFree();

            var totalRows = _peoplePerRow.Length;
            var yOrigin = -(totalRows * _cellGridSize) / 2F;

            for (var rowIndex = 0; rowIndex < totalRows; rowIndex++)
            {
                var peopleInRow = _peoplePerRow[rowIndex];
                var rowWidth = (peopleInRow - 1F) * _cellGridSize;

                var x = -rowWidth / 2F;
                var y = yOrigin + rowIndex * _cellGridSize;

                for (var i = 0; i < peopleInRow; i++)
                {
                    AddChild(
                        new Worble
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


    public partial class Worble : StaticBody2D
    {
        private const float MaxSwayAngleDeg = 2F;
        private const float SwaySpeed = 5F;
        private const float MouthLerpSpeed = 7F;
        private const float MinLightEnergy = 1F;
        private const float MaxLightEnergy = 1.5F;
        private const float MinPitch = 0.1F;
        private const float MaxPitch = 12F;
        private const float MinVolumeDb = -24F;
        private const float MaxVolumeDb = -8F;
        private const float XScale = .5F;
        private const float YScale = .5F;
        public readonly Vector2 DefaultScale = new(1.2F, 1.2F);
        public readonly Vector2 ActivatedScale = new(1.2F, 1.5F);
        private Vector2 _targetScale = new(1.2F, 1.2F);

        private readonly AudioFileSoundStrategy _soundStrategy;
        private readonly AnimatedSprite2D _sprite;
        private readonly Light2D _pointLight;
        private readonly string _spriteType;
        private string _currentAnimation = "idle";
        private float _swayTime;

        public Worble()
        {
            _spriteType = GD.Randf() < 0.5F ? "Tie" : "Ribbon";
            _sprite = CreateSprite();
            _pointLight = CreatePointLight();
            var player = new AudioStreamPlayer2D();
            AddChild(player);
            _soundStrategy = new AudioFileSoundStrategy(player);
        }

        public override void _Ready()
        {
            CollisionLayer = 1;
            CollisionMask = 0;
            AddChild(_sprite);
            AddChild(_pointLight);
            AddChild(CreateCollisionShape());
            SetupSpriteFrames();
            _soundStrategy.LoadAudioFile(InstrumentResources.AudioFiles[InstrumentResources.Instrument.Trombone]);
        }

        private AnimatedSprite2D CreateSprite()
        {
            return new AnimatedSprite2D
            {
                Scale = DefaultScale,
                TextureFilter = TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Normal }
            };
        }

        private static PointLight2D CreatePointLight()
        {
            return new PointLight2D
            {
                Enabled = true,
                Visible = true,
                BlendMode = Light2D.BlendModeEnum.Mix,
                Texture = ResourceLoader.Load<Texture2D>("res://Sprites/Lights/light_smoothest.png"),
                Color = Colors.WhiteSmoke
            };
        }

        private static CollisionShape2D CreateCollisionShape()
        {
            return new CollisionShape2D
            {
                Shape = new CircleShape2D { Radius = 15f },
                Position = new Vector2(0f, -20f)
            };
        }

        private void SetupSpriteFrames()
        {
            var spriteFrames = new SpriteFrames();
            var basePath = $"res://Sprites/Worble/{_spriteType}/";
            AddAnimation(spriteFrames, "idle", $"{basePath}idle.png");
            AddAnimation(spriteFrames, "idle_blink", $"{basePath}idle_blink.png");
            AddAnimation(spriteFrames, "arms_mid", $"{basePath}arms_mid.png");
            AddAnimation(spriteFrames, "arms_high", $"{basePath}arms_high.png");
            _sprite.SpriteFrames = spriteFrames;
            _sprite.Animation = "idle";
        }

        private static void AddAnimation(SpriteFrames spriteFrames, string animationName, string texturePath)
        {
            spriteFrames.AddAnimation(animationName);
            spriteFrames.AddFrame(animationName, ResourceLoader.Load<Texture2D>(texturePath));
        }

        public override void _Process(double delta)
        {
            var deltaF = (float)delta;

            _sprite.Scale = _sprite.Scale.Lerp(_targetScale, MouthLerpSpeed * deltaF);

            if (Math.Abs(_sprite.Scale.X - DefaultScale.X) < .1F)
            {
                _sprite.Play(_currentAnimation = "idle");
                _pointLight.Energy = MinLightEnergy;
                _swayTime = 0F;
                Rotation = 0F;
                _soundStrategy.Stop();
            }
            else
            {
                if (GetTree().Root.GetChild(0).GetChild(0) is not Node2D leftHandJoint) return;
                _pointLight.Energy = MaxLightEnergy;
                _swayTime += deltaF;
                Rotation = Mathf.DegToRad(MaxSwayAngleDeg) * Mathf.Sin(_swayTime * SwaySpeed);
                var normalizedYPosition =
                    Mathf.Clamp(leftHandJoint.Position.Y / (((Stage)GetParent()).ScreenHeight * YScale), 0F, 1F);
                var normalizedXPosition =
                    Mathf.Clamp(leftHandJoint.Position.X / (((Stage)GetParent()).ScreenWidth * XScale), 0F, 1F);
                var pitch = Mathf.Lerp(MaxPitch, MinPitch, normalizedYPosition);
                var normalizedPitch = (pitch - MinPitch) / (MaxPitch - MinPitch);
                var volume = Mathf.Lerp(MinVolumeDb, MaxVolumeDb, normalizedXPosition);
                _soundStrategy.UpdatePitch(pitch);
                _soundStrategy.UpdateVolume(volume);
                _soundStrategy.Play();
                _sprite.Play(_currentAnimation = normalizedPitch > .5F ? "arms_high" : "arms_mid");
            }
        }

        public new void SetScale(Vector2 scale)
        {
            _targetScale = scale;
        }

        private void ChangeInstrument(InstrumentResources.Instrument instrument)
        {
            if (InstrumentResources.AudioFiles.TryGetValue(instrument, out var file))
                _soundStrategy.LoadAudioFile(file);
        }

        public InstrumentResources.Instrument? GetCurrentInstrument()
        {
            var currentFile = _soundStrategy.GetCurrentAudioFile();
            return InstrumentResources.AudioFiles
                .FirstOrDefault(x => x.Value == currentFile)
                .Key;
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
    private Vector2 _previousRightHandTargetPosition;
    private const float KinectDepthCameraWidth = 512F;
    private const float KinectDepthCameraHeight = 424F;
    private const float HandLerpSpeed = 15F;
    private const float PanLerpSpeed = .25F;
    private const float MaxPanDeviation = 10F;
    private const float PanSpeed = 0.1F;
    private const float HandRadius = 40F;

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
        _rightHandCone = new HandCone(_rightHandJoint);

        var vignetteLayer = new CanvasLayer { Layer = -1 };
        vignetteLayer.AddChild(_vignetteColorRect = new ColorRect
        {
            Material = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>("Shaders/vignette.gdshader") },
            Size = GetViewport().GetVisibleRect().Size
        });

        AddChild(_leftHandJoint);
        AddChild(_rightHandJoint);
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
            _leftHandJoint.TargetPosition =
                CalculateHandPosition(leftHandPositions, spineBase, scaleX, scaleY, sensitivity);
        }

        if (projections.TryGetValue("HandRight", out var rightHandPositions) && rightHandPositions.Count >= 2)
        {
            _rightHandJoint.TargetPosition =
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
        if (_leftHandJoint.IsTracked)
        {
            DrawCircle(_leftHandJoint.Position, HandRadius, Colors.Gray);
            DrawCircle(_leftHandJoint.Position, HandRadius, Colors.DimGray, false, 1F, true);
        }

        if (_rightHandJoint.IsTracked)
        {
            DrawCircle(_rightHandJoint.Position, HandRadius, Colors.Gray);
            DrawCircle(_rightHandJoint.Position, HandRadius, Colors.DimGray, false, 1F, true);
            var stickDirection = new Vector2(0, -1).Rotated(_rightHandCone.VisualPolygon.Rotation).Normalized();
            var stickStartPoint = _rightHandJoint.Position + stickDirection * HandRadius;
            var stickEndPoint = stickStartPoint + stickDirection * 150F;
            DrawLine(stickStartPoint, stickEndPoint, Colors.DimGray, 4F, true);
        }
    }

    public override void _Process(double delta)
    {
        QueueRedraw();

        if (_leftHandJoint.TargetPosition != Vector2.Zero)
            _leftHandJoint.Position =
                _leftHandJoint.Position.Lerp(_leftHandJoint.TargetPosition, (float)(HandLerpSpeed * delta));

        if (_rightHandJoint.TargetPosition != Vector2.Zero)
            _rightHandJoint.Position =
                _rightHandJoint.Position.Lerp(_rightHandJoint.TargetPosition, (float)(HandLerpSpeed * delta));

        var rightHandSpeed = _rightHandJoint.TargetPosition - _previousRightHandTargetPosition;
        _previousRightHandTargetPosition = _rightHandJoint.TargetPosition;

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