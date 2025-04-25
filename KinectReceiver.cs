using System;
using Godot;
using Microsoft.AspNet.SignalR.Client;
using System.Collections.Generic;
using Newtonsoft.Json;

public partial class KinectReceiver : Node2D
{
    [Export] private string serverDomain = System.Environment.MachineName;
    [Export] private int serverPort = 9000;
    [Export] private float jointRadius = 5F;
    [Export] private float headRadius = 20F; 
    [Export] private float textOffsetY = 4F; 
    [Export] private float textOffsetX = 4F; 
    private const int UPDATE_LOG_INTERVAL_MS = 1000;

    private readonly struct Bone
    {
        public readonly string displayName;
        public readonly string startJ;
        public readonly string endJ;
        public readonly Color color;

         public Bone(string start, string end, Color color, string displayName)
        {
            this.startJ = start;
            this.endJ = end;
            this.displayName = displayName;
            this.color = color;
        }
    }

     private readonly Bone[] skeletonBones = [
        // spine
        new Bone("SpineBase", "SpineMid", Colors.Blue, "Mid Spine"),
        new Bone("SpineMid", "Neck", Colors.Blue, "Neck"),
        new Bone("Neck", "Head", Colors.Blue, "Head"),
        
        // shoulders
        new Bone("SpineShoulder", "ShoulderLeft", Colors.Green, "Left Shoulder"),
        new Bone("SpineShoulder", "ShoulderRight", Colors.Yellow, "Right Shoulder"),
        
        // left-arm
        new Bone("ShoulderLeft", "ElbowLeft", Colors.Green, "Left Elbow"),
        new Bone("ElbowLeft", "WristLeft", Colors.Green, "Left Wrist"),
        new Bone("WristLeft", "HandLeft", Colors.Green, "Left Hand"), 
        
        // right-arm
        new Bone("ShoulderRight", "ElbowRight", Colors.Yellow, "Right Elbow"),
        new Bone("ElbowRight", "WristRight", Colors.Yellow, "Right Wrist"),
        new Bone("WristRight", "HandRight", Colors.Yellow, "Right Hand"),
        
        // left-leg
        new Bone("HipLeft", "KneeLeft", Colors.Cyan, "Left Knee"),
        new Bone("KneeLeft", "AnkleLeft", Colors.Cyan, "Left Ankle"),
        new Bone("AnkleLeft", "FootLeft", Colors.Cyan, "Left Foot"),
        
        // right-leg
        new Bone("HipRight", "KneeRight", Colors.Magenta, "Right Knee"),
        new Bone("KneeRight", "AnkleRight", Colors.Magenta, "Right Ankle"),
        new Bone("AnkleRight", "FootRight", Colors.Magenta, "Right Foot"),
        
        // hip
        new Bone("HipLeft", "HipRight", new Color(0.5f, 0.5f, 0), "Right Hip"),
        new Bone("HipRight", "HipLeft", new Color(0.5f, 0.5f, 0), "Left Hip")
    ];

    private readonly Dictionary<int, string> trackingStateNames = new(3)
    {
        { 0, "NotTracked" },
        { 1, "Inferred" },
        { 2, "Tracked" }
    };

    private readonly Dictionary<int, string> handStateNames = new(6)
    {
        { 0, "Unknown" },
        { 1, "NotTracked" },
        { 2, "Open" },
        { 3, "Closed" },
        { 4, "Lasso" },
        { -1, "None" }
    };

    private bool redraw;
    private bool showStates;
    private int handLeftState;
    private int handRightState;
    private IHubProxy hubProxy;
    private FontFile defaultFont; 
    private HubConnection connection;
    private readonly Dictionary<string, int> jointStates = new(32);
    private readonly Dictionary<string, Vector2> jointPositions = new(32);

    public override void _Ready()
    {
        this.defaultFont = new FontFile();
        this.defaultFont.FixedSize = 16;
        this.connection = new HubConnection($"http://{serverDomain}:{serverPort}/signalr");
        this.hubProxy = connection.CreateHubProxy("Kinect2Hub");
        setupCallbacks();
        startConnection();
    }

    private void setupCallbacks()
    {
        hubProxy.On<string, string>("OnBody", (bodyJson, projectionMappedPointsJson) =>
        {
            updateJointPositions(projectionMappedPointsJson);
            updateJointStates(bodyJson);
        });

        // hubProxy.On<string, string, ulong>("OnFace", (faceData, status, trackingId) =>
        // {
        // });

        // hubProxy.On<string, long, string>("OnBodies", (bodyIds, frame, userName) =>
        // {
        // });

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

    private void updateJointPositions(string json)
    {
        var projections = JsonConvert.DeserializeObject<Dictionary<string, List<float>>>(json);
        foreach (var joint in projections)
        {
            this.jointPositions[joint.Key] = new Vector2(joint.Value[0], joint.Value[1]);
        }
    }

     private void updateJointStates(string bodyJson)
    {
        var bodyData = JsonConvert.DeserializeObject<Dictionary<string, object>>(bodyJson);
        this.handLeftState = bodyData.TryGetValue("HandLeftState", out var leftState) ? Convert.ToInt32(leftState) : 0;
        this.handRightState = bodyData.TryGetValue("HandRightState", out var rightState) ? Convert.ToInt32(rightState) : 0;

        if (bodyData.TryGetValue("Joints", out var jointsObj))
        {
            var joints = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(jointsObj.ToString());
             foreach (var joint in joints)
            {
                if (joint.Value.TryGetValue("TrackingState", out var trackingState))
                {
                    this.jointStates[joint.Key] = Convert.ToInt32(trackingState);
                }
                else
                {
                    this.jointStates[joint.Key] = 0;
                }
            }
        }
    }

     public override void _Draw()
    {
        if (jointPositions.Count == 0) return;

        foreach (var joint in this.jointPositions)
        {
            float radius = joint.Key == "Head" ? headRadius : jointRadius;
            DrawCircle(joint.Value, radius, Colors.Red);
        }

        foreach (var bone in skeletonBones)
        {
            if (this.jointPositions.TryGetValue(bone.startJ, out var start) && this.jointPositions.TryGetValue(bone.endJ, out var end))
            {
                DrawLine(start, end, bone.color);
                if (this.showStates) {
                    string displayText;
                    if (bone.endJ == "HandLeft" || bone.endJ == "HandRight")
                    {
                        int handState = bone.endJ == "HandLeft" ? this.handLeftState : this.handRightState;
                        string handStateName = this.handStateNames.TryGetValue(handState, out var hName) ? hName : "None";
                        int trackingState = this.jointStates.TryGetValue(bone.endJ, out var tState) ? tState : 0;
                        string trackingStateName = this.trackingStateNames.TryGetValue(trackingState, out var tName) ? tName : "NotTracked";
                        displayText = $"{bone.displayName}: {handStateName} ({trackingStateName})";
                    }
                    else
                    {
                        int trackingState = this.jointStates.TryGetValue(bone.endJ, out var tState) ? tState : 0;
                        string trackingStateName = this.trackingStateNames.TryGetValue(trackingState, out var tName) ? tName : "NotTracked";
                        displayText = $"{bone.displayName}: {trackingStateName}";
                    }   

                    DrawString(defaultFont, end + new Vector2(textOffsetX, textOffsetY), displayText, HorizontalAlignment.Center, -1, 16, Colors.White);
                }
            }
        }
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public void _on_state_toggle_button_pressed()
    {
        this.showStates = !this.showStates;
    }

}