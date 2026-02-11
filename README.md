# Macro Re-Enabler

A system that monitors Minecraft instances and automatically re-enables a macro when certain events are detected in the game logs.

## System Components

This package contains two components that work together:

1. **Desktop Monitor** - A Windows WPF application that monitors Minecraft log files
2. **Minecraft Mod** - A Forge 1.8.9 mod that receives commands from the desktop app

---

## Building the Desktop Monitor

### Requirements
- Windows 10/11 (64-bit)
- .NET 8.0 SDK or later

### Build Steps

1. Open a command prompt or PowerShell in the `DesktopMonitor` directory

2. Build the application:
   ```
   dotnet build FishingReEnabler.csproj --configuration Release
   ```

3. The executable will be located at:
   ```
   bin\Release\net8.0-windows\FishingReEnabler.exe
   ```

4. Run the application by double-clicking `FishingReEnabler.exe` or running:
   ```
   dotnet run --configuration Release
   ```

### Configuration

On first run, configure:
- **PrismLauncher Path**: Path to your PrismLauncher instances folder
- **Key**: The key to send to Minecraft (default: G)
- **IPC Port**: Port for communication with the mod (default: 17653)

Click "Save Settings" to persist your configuration.

---

## Building the Minecraft Mod

### Requirements
- Java 8 JDK
- Minecraft 1.8.9 with Forge

### Build Steps

1. Open a command prompt or PowerShell in the `MinecraftMod` directory

2. On Windows, run:
   ```
   gradlew.bat build
   ```

   On Linux/Mac, run:
   ```
   ./gradlew build
   ```

3. The mod JAR will be located at:
   ```
   build\libs\MinecraftModIntegration-1.0.jar
   ```

4. Copy the JAR file to your Minecraft mods folder:
   ```
   .minecraft\mods\
   ```

### Configuration

1. Launch Minecraft 1.8.9 with Forge
2. Press **Right Shift** to open the mod configuration GUI
3. Set the **Instance ID** to match your instance name in PrismLauncher
4. Click "Save" to persist settings

---

## How It Works

### Event Detection

The desktop monitor watches Minecraft log files for these events:

1. **Generic Events** (hotspot/route failures)
   - First occurrence: Waits 10 seconds, then sends the configured key
   - Second occurrence within 30 seconds: Executes `/hub` → `/warp BAYOU` → sends key

2. **Blue Ringed Octopus** (3-step sequence detection)
   - Detects death by Blue Ringed Octopus
   - Immediately sends the configured key

3. **Hotspot Mismatch** (smart detection)
   - Detects repeated hotspot mismatches (5+ occurrences)
   - Presses 'W' key twice with 100ms delay

### Communication

- Desktop app runs a TCP server on port 17653
- Minecraft mod connects as a client
- When a second event occurs, the desktop app sends a command to the mod
- Mod executes the command sequence in-game
- Mod reports completion back to desktop app
- Desktop app then sends the configured key press

---

## Usage

1. Start the Desktop Monitor application
2. Launch Minecraft with the mod installed
3. The monitor will automatically discover running Minecraft instances
4. Enable monitoring for specific instances by checking the "Enabled" checkbox
5. The system will now watch for events and respond automatically

---

## Troubleshooting

### Desktop Monitor doesn't find instances
- Verify the PrismLauncher path is correct
- Click "Rescan Instances" to manually search for running instances
- Ensure Minecraft is actually running

### Mod doesn't connect
- Check that the IPC port matches in both the desktop app and mod config
- Ensure no firewall is blocking localhost connections on port 17653
- Verify the Instance ID in the mod matches the instance name

### Keys not being sent
- Make sure the instance is "Enabled" in the monitor
- Check that the window status shows "Found"
- Verify the IPC status shows "Connected"

---

## License

This software is provided as-is without warranty. Use at your own risk.
