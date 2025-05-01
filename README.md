# Kinect Setup

## Prerequisites

- Kinect
- Cake Websocket

## Setup

1. **Connect Kinect to Your Computer**
   - This should be self-explanatory.
   - If you don't have the Kinect SDK, download and install it from the following link:
	 - [Kinect SDK](https://learn.microsoft.com/en-us/previous-versions/windows/kinect/dn799271(v=ieb.10)?redirectedfrom=MSDN)

2. **Run Cake23 As Administrator**
   - Run `cake23.exe`.

3. **Connect to Kinect**
   - In Cake, click the **Connect** button.
   - Wait for the message indicating that the Kinect is "Up and Running."

4. **Start the Server**
   - Once Kinect is connected, click the **Host** button in Cake.

5. **Launch Godot**
   - Keep Cake running unless you no longer need it.
   - Open your Godot project.
   - The Kinect should automatically connect to Godot as long as the server in Cake is still running. No additional configuration should be needed in Godot.

## If Kinect Disconnects

1. **Reconnect the Kinect** to your computer.
2. **Restart the Process**:
   - Just to be safe, close Cake, and restart the process by clicking **Connect** first, then **Host**.

**Important**: Always remember: **Connect** first, then **Host**.
