using Godot;
using Microsoft.AspNet.SignalR.Client;
using System.Collections.Generic;
using Newtonsoft.Json;

public partial class KinectReceiver : Node2D
{
    [ExportGroup("Server Settings")]
    [Export] private string serverDomain = System.Environment.MachineName;
    [Export] private int serverPort = 9000;

    [ExportGroup("Visual Settings")]
    [Export] private float jointRadius = 8F;
    [Export] private float headRadius = 20F;
    [Export] private float bodyLineThickness = 1F;

    [ExportGroup("Audio Settings")]
    [Export] private string audioFile = "sine440hz.wav";
    [Export] private float minPitch = 0.01F;
    [Export] private float maxPitch = 10F;
    [Export] private float minVolume = -24F;
    [Export] private float maxVolume = 24F;

    private class Joint
    {
        public string Name { get; }
        public Vector2 Position { get; set; }
        public int TrackingState { get; set; }
        public bool IsTracked => TrackingState == 2;
        public Joint(string name)
        {
            Name = name;
            Position = Vector2.Zero;
            TrackingState = 0;
        }
    }

    private class Bone
    {
        public string DisplayName { get; }
        public string StartJointName { get; }
        public string EndJointName { get; }
        public Color Color { get; }

        public Bone(string start, string end, Color color, string displayName)
        {
            StartJointName = start;
            EndJointName = end;
            DisplayName = displayName;
            Color = color;
        }

        public string GetDisplayText(Dictionary<string, Joint> joints, Dictionary<int, string> trackingStateNames)
        {
            if (!joints.TryGetValue(EndJointName, out var endJoint))
                return $"{DisplayName}: Unknown";

            string stateName = trackingStateNames.TryGetValue(endJoint.TrackingState, out var name) ? name : "Not Tracked";
            return $"{DisplayName}: {stateName}";
        }
    }

    private class Theremin
    {
        public Vector2 Position { get; set; }
        public float BodyWidth { get; } = 125F;
        public float BodyHeight { get; } = 24F;
        public float PitchAntennaHeight { get; } = 150F;
        public float VolumeAntennaWidth { get; } = 80F;
        public float AntennaThickness { get; } = 2.5F;
        public float KnobRadius { get; } = 6F;
        public Vector2 PitchKnobPosition { get; set; }
        public Vector2 VolumeKnobPosition { get; set; }
        public float CurrentPitch { get; set; }
        public float CurrentVolume { get; set; }
        public Color PitchColor { get; } = Colors.Green;
        public Color VolumeColor { get; } = Colors.Red;
        public Color BodyColor { get; } = Colors.DimGray;
        public Color BodyBorderColor { get; } = Colors.Black;
        public Rect2 BodyRect => new Rect2(Position - new Vector2(BodyWidth / 2F - 1F, BodyHeight / 2F - 3F), new Vector2(BodyWidth, BodyHeight));
        public Vector2 PitchAntennaBase => new Vector2(Position.X + BodyWidth / 2F, Position.Y - BodyHeight / 2F);
        public Vector2 PitchAntennaEnd => new Vector2(PitchAntennaBase.X, PitchAntennaBase.Y - PitchAntennaHeight);
        public Vector2 VolumeAntennaBase => new Vector2(Position.X - BodyWidth / 2F - 1, Position.Y + 2);
        public Vector2 VolumeAntennaEnd => new Vector2(VolumeAntennaBase.X - VolumeAntennaWidth, VolumeAntennaBase.Y);
        
        public Theremin(Vector2 position)
        {
            Position = position;
            CurrentPitch = 1F;
            CurrentVolume = 0F;
        }
    }

    private readonly Bone[] skeletonBones = [
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

    private readonly Dictionary<int, string> trackingStateNames = new()
    {
        { 0, "Not Tracked" }, { 1, "Inferred" }, { 2, "Tracked" }
    };

    private bool showStates;
    private Theremin theremin;
    private IHubProxy hubProxy;
    private FontFile defaultFont;
    private HubConnection connection;
    private AudioStream audioStream;
    private AudioStreamPlayer2D player;
    private readonly Dictionary<string, Joint> joints = new(32);

    public override void _Ready()
    {
        this.defaultFont = new FontFile { FixedSize = 16 };
        this.connection = new HubConnection($"http://{serverDomain}:{serverPort}/signalr");
        this.hubProxy = connection.CreateHubProxy("Kinect2Hub");
        var viewportRect = GetViewport().GetVisibleRect();
        this.theremin = new Theremin(new Vector2(viewportRect.Size.X / 2, viewportRect.Size.Y / 2));
        setupCallbacks();
        startConnection();
        setupAudioPlayer();
    }

    private void setupCallbacks()
    {
        this.hubProxy.On<string, string>("OnBody", (bodyJson, projectionMappedPointsJson) =>
        {
            updateJointPositions(projectionMappedPointsJson);
            updateJointStates(bodyJson);
            updateThereminControls();
        });

        this.connection.Closed += () => GD.PrintErr("SignalR connection closed...");
        this.connection.Error += (exception) => GD.PrintErr($"SignalR connection error: {exception.Message}");
    }

    private void startConnection()
    {
        this.connection.Start().ContinueWith(task =>
        {
            GD.Print(task.IsFaulted ? $"SignalR connection error: {task.Exception?.GetBaseException().Message}" : "Connected to SignalR...");
        });
    }

    private void setupAudioPlayer()
    {
        this.player = new AudioStreamPlayer2D();
        this.audioStream = ResourceLoader.Load<AudioStream>(audioFile);
        if (this.audioStream == null)
        {
            GD.PrintErr($"Failed to load audio file: {audioFile}");
            return;
        }

        GD.Print($"Loaded: {audioFile}");
        this.player.Stream = audioStream;
        this.player.VolumeDb = minVolume;
        this.player.PitchScale = minPitch;
        this.player.Position = theremin.Position;
        this.player.Autoplay = true;
        AddChild(this.player);
    }

    private void updateJointPositions(string json)
    {
        var projections = JsonConvert.DeserializeObject<Dictionary<string, List<float>>>(json);
        Vector2 spineBasePosition = projections.TryGetValue("SpineBase", out var spineBaseCoordinates) ? new Vector2(spineBaseCoordinates[0], spineBaseCoordinates[1]) : Vector2.Zero;
        float offsetX = spineBasePosition.X - this.theremin.Position.X;
        float offsetY = spineBasePosition.Y - this.theremin.Position.Y;

        foreach (var projection in projections)
        {
            string jointName = projection.Key;
            if (!joints.ContainsKey(jointName))
            {
                joints[jointName] = new Joint(jointName);
            }
            joints[jointName].Position = new Vector2(projection.Value[0] - offsetX, projection.Value[1] - offsetY);
        }
    }

    private void updateJointStates(string bodyJson)
    {
        var bodyData = JsonConvert.DeserializeObject<Dictionary<string, object>>(bodyJson);
        if (bodyData.TryGetValue("Joints", out var jointsObj))
        {
            var jointsData = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(jointsObj.ToString());
            foreach (var jointData in jointsData)
            {
                string jointKey = jointData.Key;
                if (jointData.Value.TryGetValue("TrackingState", out var trackingState))
                {
                    if (int.TryParse(trackingState.ToString(), out int state))
                    {
                        joints[jointKey].TrackingState = state;
                    }
                }
            }
        }
    }
    private void updateThereminControls()
    {
        updatePitchControl();
        updateVolumeControl();
    }

    private void updatePitchControl()
    {
        if (joints.TryGetValue("HandRight", out var rightHandJoint) && rightHandJoint.IsTracked)
        {
            float pitchAntennaBaseY = theremin.Position.Y - theremin.BodyHeight / 2F;
            float distanceFromBase = pitchAntennaBaseY - rightHandJoint.Position.Y;
            float normalizedDistance = Mathf.Clamp(distanceFromBase / theremin.PitchAntennaHeight, 0F, 1F);
            this.theremin.CurrentPitch = minPitch + normalizedDistance * (maxPitch - minPitch);
            this.player.PitchScale = theremin.CurrentPitch;
            this.theremin.PitchKnobPosition = new Vector2(theremin.Position.X + theremin.BodyWidth / 2F, pitchAntennaBaseY - normalizedDistance * theremin.PitchAntennaHeight);
        }
        else
        {
            this.theremin.CurrentPitch = minPitch;
            this.player.PitchScale = minPitch;
            this.theremin.PitchKnobPosition = new Vector2(theremin.Position.X + theremin.BodyWidth / 2F, theremin.Position.Y - theremin.BodyHeight / 2F);
        }
    }

    private void updateVolumeControl()
    {
        if (joints.TryGetValue("HandLeft", out var leftHandJoint) && leftHandJoint.IsTracked)
        {
            float volumeAntennaBaseX = theremin.Position.X - theremin.BodyWidth / 2F;
            float distanceFromBase = volumeAntennaBaseX - leftHandJoint.Position.X;
            float normalizedDistance = Mathf.Clamp(distanceFromBase / theremin.VolumeAntennaWidth, 0F, 1F);
            this.theremin.CurrentVolume = minVolume + normalizedDistance * (maxVolume - minVolume);
            this.player.VolumeDb = theremin.CurrentVolume;
            this.theremin.VolumeKnobPosition = new Vector2(volumeAntennaBaseX - normalizedDistance * theremin.VolumeAntennaWidth, theremin.Position.Y);
        }
        else
        {
            this.player.VolumeDb = minVolume;
            this.theremin.VolumeKnobPosition = new Vector2(theremin.Position.X - theremin.BodyWidth / 2, theremin.Position.Y);
        }
    }

    public override void _Draw()
    {
        drawTheremin();

        if (this.joints.Count == 0) return;

        foreach (var joint in joints)
        {
            float radius = joint.Key == "Head" ? headRadius : jointRadius;
            DrawCircle(joint.Value.Position, radius, Colors.Red, true, -1F, true);
        }

        foreach (var bone in skeletonBones)
        {
            if (this.joints.TryGetValue(bone.StartJointName, out var startJoint) && this.joints.TryGetValue(bone.EndJointName, out var endJoint))
            {
                DrawLine(startJoint.Position, endJoint.Position, bone.Color, bodyLineThickness, true);
                if (showStates)
                {
                    string displayText = bone.GetDisplayText(joints, trackingStateNames);
                    DrawString(defaultFont, endJoint.Position, displayText, HorizontalAlignment.Center, -1F, 16, Colors.White);
                }
            }
        }

        Vector2 offsetVec = new Vector2(0, 40);
        if (joints.TryGetValue("HandLeft", out var leftHandJoint) && leftHandJoint.IsTracked)
        {
            DrawString(defaultFont, leftHandJoint.Position + offsetVec, $"Vol: {player.VolumeDb:F1} dB", HorizontalAlignment.Left, -1F, 16, Colors.White);
        }

        if (joints.TryGetValue("HandRight", out var rightHandJoint) && rightHandJoint.IsTracked)
        {
            DrawString(defaultFont, rightHandJoint.Position + offsetVec, $"Pitch: {player.PitchScale:F2}", HorizontalAlignment.Left, -1F, 16, Colors.White);
        }
    }

    private void drawTheremin()
    {
        //body
        DrawRect(theremin.BodyRect, theremin.BodyColor, true, -1F, true);
        DrawRect(theremin.BodyRect, theremin.BodyBorderColor, false, 1.5F, true);

        //antennas
        DrawLine(theremin.PitchAntennaBase, theremin.PitchAntennaEnd, theremin.PitchColor, theremin.AntennaThickness, true);
        DrawLine(theremin.VolumeAntennaBase, theremin.VolumeAntennaEnd, theremin.VolumeColor, theremin.AntennaThickness, true);

        bool rightHandTracked = joints.TryGetValue("HandRight", out var rightHandJoint) && rightHandJoint.IsTracked;
        bool leftHandTracked = joints.TryGetValue("HandLeft", out var leftHandJoint) && leftHandJoint.IsTracked;

        //pitch-knob
        float pitchScale = rightHandTracked ? 1.5F : 1F;
        float pitchRadius = theremin.KnobRadius * pitchScale;
        DrawCircle(theremin.PitchKnobPosition, pitchRadius, theremin.PitchColor, true, -1F, true);
        DrawCircle(theremin.PitchKnobPosition, pitchRadius, Colors.Black, false, 1F, true);

        //volume-knob
        float volumeScale = leftHandTracked ? 1.5F : 1F;
        float volumeRadius = theremin.KnobRadius * volumeScale;
        DrawCircle(theremin.VolumeKnobPosition, volumeRadius, theremin.VolumeColor, true, -1F, true);
        DrawCircle(theremin.VolumeKnobPosition, volumeRadius, Colors.Black, false, 1F, true);
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public void _on_state_toggle_button_pressed()
    {
        showStates = !showStates;
    }
}