using System;
using System.Collections.Generic;
using Godot;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;

namespace KinectProject;

public partial class KinectReceiver : Node2D
{
    [ExportGroup("Server Settings")] 
    [Export] private string _serverDomain = System.Environment.MachineName;
    [Export] private int _serverPort = 9000;

    [ExportGroup("Visual Settings")] 
    [Export] private float _jointRadius = 6F;
    [Export] private float _headRadius = 15F;
    [Export] private float _bodyLineThickness = .5F;

    [ExportGroup("Audio Settings")] 
    [Export] private string _audioFile = "sine440hz.wav";
    [Export] private bool _useGeneratedSound;

    [ExportSubgroup("Generator Settings")] 
    [Export] private float _sampleRate = 44100F;
    [Export] private float _baseFrequency = 440.0f;

    [ExportSubgroup("Playback Settings")] 
    [Export] private float _minPitch = 0.01F;
    [Export] private float _maxPitch = 10F;
    [Export] private float _minVolume = -24F;
    [Export] private float _maxVolume = 24F;

    public interface ISoundStrategy
    {
        void Play();
        void Setup(AudioStreamPlayer2D player, Vector2 position, float sampleRate, string fileName);
        void UpdatePitch(float pitch, float baseFrequency);
        void UpdateVolume(float volumeDb);
        string GetPitch(float currentPitch, float baseFrequency);
    }

    public class GeneratedSoundStrategy : ISoundStrategy
    {
        private AudioStreamPlayer2D _player;
        private AudioStreamGeneratorPlayback _playback;
        private double _phase;
        private float _sampleRate;

        public void Setup(AudioStreamPlayer2D player, Vector2 position, float sampleRate, string fileName = "")
        {
            _player = player;
            _sampleRate = sampleRate;
            _player.Position = position;
            var generator = new AudioStreamGenerator { MixRate = sampleRate, BufferLength = 0.1F };
            _player.Stream = generator;
        }

        public void UpdatePitch(float pitch, float baseFrequency)
        {
            GenerateSound(baseFrequency * pitch);
        }

        public void UpdateVolume(float volumeDb)
        {
            _player.VolumeDb = volumeDb;
        }

        public void Play()
        {
            _player.Play();
            _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        }

        public string GetPitch(float currentPitch, float baseFrequency)
        {
            return $"Freq: {baseFrequency * currentPitch:F0} Hz";
        }

        private void GenerateSound(float frequency)
        {
            var increment = frequency / _sampleRate;
            var framesAvailable = _playback.GetFramesAvailable();
            if (framesAvailable == 0) return;
            for (var i = 0; i < framesAvailable; i++)
            {
                _playback.PushFrame(Vector2.One * (float)Mathf.Sin(_phase * Mathf.Tau));
                _phase = Mathf.PosMod(_phase + increment, 1.0);
            }
        }
    }

    public class AudioFileSoundStrategy : ISoundStrategy
    {
        private AudioStreamPlayer2D _player;

        public void Setup(AudioStreamPlayer2D player, Vector2 position, float sampleRate, string fileName)
        {
            _player = player;
            _player.Position = position;
            var audioStream = ResourceLoader.Load<AudioStream>(fileName);
            if (audioStream == null)
            {
                GD.PrintErr($"Failed to load audio file: {fileName}");
                return;
            }

            _player.Stream = audioStream;
        }

        public void UpdatePitch(float pitch, float baseFrequency)
        {
            _player.PitchScale = pitch;
        }

        public void UpdateVolume(float volumeDb)
        {
            _player.VolumeDb = volumeDb;
        }

        public void Play()
        {
            _player.Play();
        }

        public string GetPitch(float currentPitch, float baseFrequency)
        {
            return $"Pitch: {currentPitch:F2}";
        }
    }

    private class Joint
    {
        public Vector2 Position { get; set; } = Vector2.Zero;
        public int TrackingState { get; set; }
        public bool IsTracked => TrackingState == 2;
    }

    private class Bone(string start, string end, Color color, string displayName)
    {
        private string DisplayName { get; } = displayName;
        public string StartJointName { get; } = start;
        public string EndJointName { get; } = end;
        public Color Color { get; } = color;

        public string GetDisplayText(Joint joint)
        {
            var stateName = joint.TrackingState switch
            {
                0 => "NotTracked",
                1 => "Inferred",
                2 => "Tracked",
                _ => "Unknown"
            };
            return $"{DisplayName}: {stateName}";
        }
    }

    private class Theremin
    {
        public const float BodyWidth = 125F;
        public const float BodyHeight = 24F;
        public const float PitchAntennaHeight = 150F;
        public const float VolumeAntennaWidth = 80F;
        public const float AntennaThickness = 2.5F;
        public const float KnobRadius = 6F;
        
        public float CurrentPitch { get; set; } = 1F;
        public float CurrentVolume { get; set; }

        public Rect2 BodyRect { get; }
        public Color BodyColor { get; }
        public Vector2 Position { get; }
        public Color PitchColor { get; }
        public Color VolumeColor { get; }
        public Color BodyBorderColor { get; }
        
        public Vector2 PitchAntennaBase { get; }
        public Vector2 PitchAntennaEnd { get; }
        
        public Vector2 VolumeAntennaBase { get; }
        public Vector2 VolumeAntennaEnd { get; }
        
        public Vector2 PitchKnobPosition { get; set; }
        public Vector2 VolumeKnobPosition { get; set; }

        public Theremin(Vector2 position)
        {
            Position = position;
            PitchAntennaBase = new Vector2(Position.X + BodyWidth / 2F, Position.Y - BodyHeight / 2F);
            PitchAntennaEnd = new Vector2(Position.X + BodyWidth / 2F, Position.Y - BodyHeight / 2F - PitchAntennaHeight);
            VolumeAntennaBase = new Vector2(Position.X - BodyWidth / 2F - 1, Position.Y + 2);
            VolumeAntennaEnd = new Vector2(Position.X - BodyWidth / 2F - 1 - VolumeAntennaWidth, Position.Y + 2);
            BodyRect = new Rect2(Position - new Vector2(BodyWidth / 2F - 1F, BodyHeight / 2F - 3F), new Vector2(BodyWidth, BodyHeight));
            PitchColor = Colors.Green;
            VolumeColor = Colors.Red;
            BodyColor = Colors.DimGray;
            BodyBorderColor = Colors.Black;
        }
    }

    private readonly Bone[] _skeletonBones =
    [
        new("SpineBase", "SpineMid", Colors.Blue, "Mid Spine"),
        new("SpineMid", "Neck", Colors.Blue, "Neck"),
        new("Neck", "Head", Colors.Blue, "Head"),
        new("SpineShoulder", "ShoulderLeft", Colors.Green, "Left Shoulder"),
        new("SpineShoulder", "ShoulderRight", Colors.Yellow, "Right Shoulder"),
        new("ShoulderLeft", "ElbowLeft", Colors.Green, "Left Elbow"),
        new("ElbowLeft", "WristLeft", Colors.Green, "Left Wrist"),
        new("WristLeft", "HandLeft", Colors.Green, "Left Hand"),
        new("ShoulderRight", "ElbowRight", Colors.Yellow, "Right Elbow"),
        new("ElbowRight", "WristRight", Colors.Yellow, "Right Wrist"),
        new("WristRight", "HandRight", Colors.Yellow, "Right Hand"),
        new("HipLeft", "KneeLeft", Colors.Cyan, "Left Knee"),
        new("KneeLeft", "AnkleLeft", Colors.Cyan, "Left Ankle"),
        new("AnkleLeft", "FootLeft", Colors.Cyan, "Left Foot"),
        new("HipRight", "KneeRight", Colors.Magenta, "Right Knee"),
        new("KneeRight", "AnkleRight", Colors.Magenta, "Right Ankle"),
        new("AnkleRight", "FootRight", Colors.Magenta, "Right Foot"),
        new("HipLeft", "HipRight", Colors.PaleGoldenrod, "Right Hip"),
        new("HipRight", "HipLeft", Colors.PaleGoldenrod, "Left Hip")
    ];

    private bool _showStates;
    private Theremin _theremin;
    private IHubProxy _hubProxy;
    private Joint _leftHandJoint; 
    private Joint _rightHandJoint;
    private FontFile _defaultFont;
    private AudioStream _audioStream;
    private HubConnection _connection;
    private AudioStreamPlayer2D _player;
    private ISoundStrategy _soundStrategy;
    private readonly Dictionary<string, Joint> _joints = new(32);

    public override void _Ready()
    {
        _defaultFont = new FontFile { FixedSize = 16 };
        _connection = new HubConnection($"http://{_serverDomain}:{_serverPort}/signalr");
        _hubProxy = _connection.CreateHubProxy("Kinect2Hub");
        var viewportRect = GetViewport().GetVisibleRect();
        _theremin = new Theremin(new Vector2(viewportRect.Size.X / 2, viewportRect.Size.Y / 2));
        SetupCallbacks();
        StartConnection();
        SetupAudioPlayer();
    }

    private void SetupCallbacks()
    {
        _hubProxy.On<string, string>("OnBody", (states, positions) =>
        {
            UpdateJointPositions(positions);
            UpdateJointStates(states);
            UpdateThereminControls();
        });

        _connection.Closed += () => GD.PrintErr("SignalR connection closed...");
        _connection.Error += (exception) => GD.PrintErr($"SignalR connection error: {exception.Message}");
    }

    private void StartConnection()
    {
        _connection.Start().ContinueWith(task =>
        {
            GD.Print(task.IsFaulted ? $"SignalR connection error: {task.Exception?.GetBaseException().Message}" : "Connected to SignalR...");
        });
    }

    private void SetupAudioPlayer()
    {
        _player = new AudioStreamPlayer2D();
        _soundStrategy = _useGeneratedSound ? new GeneratedSoundStrategy() : new AudioFileSoundStrategy();
        _soundStrategy.Setup(_player, _theremin.Position, _sampleRate, _audioFile);
        AddChild(_player);
        _soundStrategy.Play();
    }

    private void UpdateJointPositions(string positionsJson)
    {
        var projections = JsonConvert.DeserializeObject<Dictionary<string, List<float>>>(positionsJson);
        if (projections.Count == 0) return;
        var spineBasePosition = projections.TryGetValue("SpineBase", out var spineBaseCoordinates) ? new Vector2(spineBaseCoordinates[0], spineBaseCoordinates[1]) : Vector2.Zero;
        var offsetX = spineBasePosition.X - _theremin.Position.X;
        var offsetY = spineBasePosition.Y - _theremin.Position.Y;
        foreach (var (jointName, entry) in projections)
        {
            if (!_joints.TryGetValue(jointName, out var joint))
            {
                joint = new Joint();
                
                switch (jointName)
                {
                    case "HandLeft":
                        _leftHandJoint = joint;
                        break;
                    case "HandRight":
                        _rightHandJoint = joint;
                        break;
                }
                
                _joints[jointName] = joint;
            }
            joint.Position = new Vector2(entry[0] - offsetX, entry[1] - offsetY);
        }
    }

    private void UpdateJointStates(string statesJson)
    {
        var bodyData = JsonConvert.DeserializeObject<Dictionary<string, object>>(statesJson);
        if (!bodyData.TryGetValue("Joints", out var jointsObj)) return;
        var jointsData = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(jointsObj.ToString());
        foreach (var (jointKey, value) in jointsData)
        {
            if (!value.TryGetValue("TrackingState", out var trackingState)) continue;
            if (int.TryParse(trackingState.ToString(), out var state))
            {
                _joints[jointKey].TrackingState = state;
            }
        }
    }

    private void UpdateThereminControls()
    {
        UpdatePitchControl();
        UpdateVolumeControl();
    }

    private void UpdatePitchControl()
    {
        if (_rightHandJoint is { IsTracked: true })
        {
            var pitchAntennaBaseY = _theremin.Position.Y - Theremin.BodyHeight / 2F;
            var distanceFromBase = pitchAntennaBaseY - _rightHandJoint.Position.Y;
            var normalizedDistance = Mathf.Clamp(distanceFromBase / Theremin.PitchAntennaHeight, 0F, 1F);
            _theremin.CurrentPitch = _minPitch + normalizedDistance * (_maxPitch - _minPitch);
            _soundStrategy.UpdatePitch(_theremin.CurrentPitch, _baseFrequency);
            _theremin.PitchKnobPosition = new Vector2(_theremin.Position.X + Theremin.BodyWidth / 2F, pitchAntennaBaseY - normalizedDistance * Theremin.PitchAntennaHeight);
        }
        else
        {
            _theremin.CurrentPitch = _minPitch;
            _soundStrategy.UpdatePitch(_minPitch, _baseFrequency);
            _theremin.PitchKnobPosition = new Vector2(_theremin.Position.X + Theremin.BodyWidth / 2F, _theremin.Position.Y - Theremin.BodyHeight / 2F);
        }
    }

    private void UpdateVolumeControl()
    {
        if (_leftHandJoint is { IsTracked: true })
        {
            var volumeAntennaBaseX = _theremin.Position.X - Theremin.BodyWidth / 2F;
            var distanceFromBase = volumeAntennaBaseX - _leftHandJoint.Position.X;
            var normalizedDistance = Mathf.Clamp(distanceFromBase / Theremin.VolumeAntennaWidth, 0F, 1F);
            _theremin.CurrentVolume = _minVolume + normalizedDistance * (_maxVolume - _minVolume);
            _soundStrategy.UpdateVolume(_theremin.CurrentVolume);
            _theremin.VolumeKnobPosition = new Vector2(volumeAntennaBaseX - normalizedDistance * Theremin.VolumeAntennaWidth, _theremin.Position.Y);
        }
        else
        {
            _soundStrategy.UpdateVolume(_minVolume);
            _theremin.VolumeKnobPosition = new Vector2(_theremin.Position.X - Theremin.BodyWidth / 2F, _theremin.Position.Y);
        }
    }

    public override void _Draw()
    {
        DrawTheremin();

        if (_joints.Count == 0) return;

        foreach (var joint in _joints)
        {
            if (!joint.Value.IsTracked) continue;
            var radius = joint.Key == "Head" ? _headRadius : _jointRadius;
            DrawCircle(joint.Value.Position, radius, Colors.Red, true, -1F, true);
        }

        foreach (var bone in _skeletonBones)
        {
            if (!_joints.TryGetValue(bone.StartJointName, out var startJoint) || !_joints.TryGetValue(bone.EndJointName, out var endJoint)) continue;
            if (!startJoint.IsTracked || !endJoint.IsTracked) continue;
            DrawLine(startJoint.Position, endJoint.Position, bone.Color, _bodyLineThickness, true);
            if (_showStates)
            {
                DrawString(_defaultFont, endJoint.Position, bone.GetDisplayText(endJoint), HorizontalAlignment.Center, -1F, 16, Colors.White);
            }
        }

        var offsetVec = new Vector2(0, 40);
        if (_leftHandJoint is { IsTracked: true })
        {
            DrawString(_defaultFont, _leftHandJoint.Position + offsetVec, $"Vol: {_player.VolumeDb:F1} dB", HorizontalAlignment.Left, -1F, 16, Colors.White);
        }
        if (_rightHandJoint is { IsTracked: true })
        {
            DrawString(_defaultFont, _rightHandJoint.Position + offsetVec, _soundStrategy.GetPitch(_theremin.CurrentPitch, _baseFrequency), HorizontalAlignment.Left, -1F, 16, Colors.White);
        }
    }

    private void DrawTheremin()
    {
        //body
        DrawRect(_theremin.BodyRect, _theremin.BodyColor, true, -1F, true);
        DrawRect(_theremin.BodyRect, _theremin.BodyBorderColor, false, 1.5F, true);

        //antennas
        DrawLine(_theremin.PitchAntennaBase, _theremin.PitchAntennaEnd, _theremin.PitchColor, Theremin.AntennaThickness, true);
        DrawLine(_theremin.VolumeAntennaBase, _theremin.VolumeAntennaEnd, _theremin.VolumeColor, Theremin.AntennaThickness, true);

        //pitch-knob
        var pitchScale = _rightHandJoint is { IsTracked: true } ? 1.5F : 1F;
        var pitchRadius = Theremin.KnobRadius * pitchScale;
        DrawCircle(_theremin.PitchKnobPosition, pitchRadius, _theremin.PitchColor, true, -1F, true);
        DrawCircle(_theremin.PitchKnobPosition, pitchRadius, Colors.Black, false, 1F, true);

        //volume-knob
        var volumeScale = _leftHandJoint is { IsTracked: true } ? 1.5F : 1F;
        var volumeRadius = Theremin.KnobRadius * volumeScale;
        DrawCircle(_theremin.VolumeKnobPosition, volumeRadius, _theremin.VolumeColor, true, -1F, true);
        DrawCircle(_theremin.VolumeKnobPosition, volumeRadius, Colors.Black, false, 1F, true);
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    private void _on_state_toggle_button_pressed()
    {
        _showStates = !_showStates;
    }
}